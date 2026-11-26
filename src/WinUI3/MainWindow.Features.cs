using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Users said guidance was unclear and many features were hard to find in second-level or right-click menus, and a
// step-by-step popup at first start was not wanted. So the window helps without interrupting:
// - the search (Ctrl+K) lists every feature and where it is, and "takes you there" (FeatureCatalog, Spotlight);
// - the dashboard's "First steps" card ticks off a few first tasks as they are done anywhere in the app;
// - each page shows one short hint the first time, closed for good with its X;
// - the title bar's help button opens the user guide for the page on screen.
public sealed partial class MainWindow
{
    private Spotlight? _spotlight;

    // Picked in the search: go to the page, make it ready, and point at the place. Places that need a selection first
    // (a character, an open Mod) say where they are instead.
    private async void RunFeature(string id)
    {
        try
        {
            FeatureEntry? feature = FeatureCatalog.Build(T).FirstOrDefault(entry => entry.Id == id);
            if (feature is null) return;
            if (feature.Prepare == "getstarted") ShowGettingStarted();
            if (feature.Section is PrimarySection section && _shell.CurrentSection != section) NavigateToPrimarySection(section);
            switch (feature.Prepare)
            {
                case "updates:mods": _services.Updates.View = UpdatesPageView.Mods; break;
                case "updates:downloads": _services.Updates.View = UpdatesPageView.Downloads; break;
                case "updates:recovery": _services.Updates.View = UpdatesPageView.Recovery; break;
                case "workspace:presets": EnsureWorkspacePage().SetRepositoryWorkspaceViewV4(RepositoryWorkspaceView.Presets); break;
            }
            _spotlight ??= new Spotlight(RootGrid);
            bool shown = feature.Target is { } target && await _spotlight.ShowAsync(target, feature.Container, feature.Title);
            if (!shown) ShowAppNotification(T("Features.WhereNotice", feature.Title, feature.Where), InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("RunFeature", ex);
        }
    }

    private void OnCommandPaletteOpened()
    {
        if (_shell.SearchUsed) return;
        _shell.SearchUsed = true;
        SaveShellConfig();
        RefreshGettingStarted();
    }

    // --- The dashboard's "First steps" card ---

    private GettingStartedProgress CurrentGettingStartedProgress()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        RepositoryContentSummary? summary = repository is null ? null : GetKnownRepositorySummary(repository);
        var characters = _services.Workspace.Characters.All;
        bool hasMods = summary is { ModCount: > 0 } || characters.Any(character => character.Children.Count > 0);
        bool deployed = _services.Install.CopyRecordsSnapshot().Count > 0
            || characters.Any(character => character.Children.Any(mod => mod.IsDeployed));
        return new GettingStartedProgress(repository is not null && IsRepositoryReady(repository), hasMods, deployed,
            _shell.SearchUsed, _shell.ShortcutsVisited);
    }

    private void RefreshGettingStarted()
    {
        _services.Dashboard.GettingStartedProgress = CurrentGettingStartedProgress();
        _services.Dashboard.GettingStarted = _shell.GettingStarted.IsPending ? _shell.GettingStarted : null;
    }

    private void ShowGettingStarted()
    {
        _shell.GettingStarted.Show();
        SaveShellConfig();
        RefreshGettingStarted();
    }

    private void OnGettingStartedClosed()
    {
        _shell.GettingStarted.Close();
        RefreshGettingStarted();
        SaveShellConfig();
    }

    private void MarkShortcutsVisited()
    {
        if (_shell.ShortcutsVisited) return;
        _shell.ShortcutsVisited = true;
        RefreshGettingStarted();
    }

    // --- One hint per page, the first time ---

    private static string? PageHintId(PrimarySection section) => section switch
    {
        PrimarySection.Repository => "repository",
        PrimarySection.Online => "online",
        PrimarySection.Updates => "updates",
        PrimarySection.Shortcuts => "shortcuts",
        _ => null
    };

    private void RefreshPageHint()
    {
        if (PageHintInfoBar is null) return;
        string? id = PageHintId(_shell.CurrentSection);
        if (id is null || _shell.ClosedPageHints.Contains(id))
        {
            PageHintInfoBar.IsOpen = false;
            PageHintInfoBar.Visibility = Visibility.Collapsed;
            return;
        }
        PageHintInfoBar.Tag = id;
        PageHintInfoBar.Title = T("PageHints.Title");
        PageHintInfoBar.Message = id switch
        {
            "repository" => T("PageHints.Repository"),
            "online" => T("PageHints.Online"),
            "updates" => T("PageHints.Updates"),
            _ => T("PageHints.Shortcuts")
        };
        if (PageHintInfoBar.ActionButton is not Button all)
        {
            all = new Button();
            AutomationProperties.SetAutomationId(all, "PageHintAllFeaturesButton");
            all.Click += (_, _) => OpenCommandPalette();
            PageHintInfoBar.ActionButton = all;
        }
        all.Content = T("PageHints.AllFeatures");
        PageHintInfoBar.Visibility = Visibility.Visible;
        PageHintInfoBar.IsOpen = true;
    }

    // Only the hint's own X closes it for good; switching pages closes it for now.
    private void OnPageHintClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        // Closed takes no room, also in the margin under it.
        sender.Visibility = Visibility.Collapsed;
        if (args.Reason != InfoBarCloseReason.CloseButton || sender.Tag is not string id) return;
        _shell.ClosedPageHints.Add(id);
        SaveShellConfig();
    }

    // --- Help for the page on screen ---

    private const string GuidesUrl = AppUpdateService.GitHubRepositoryUrl + "/blob/main/docs/guides/";

    private string HelpUrl(PrimarySection section)
    {
        bool chinese = _services.Language.IsChinese;
        string page = section switch
        {
            PrimarySection.Dashboard => chinese ? "快速上手" : "Quick-Start",
            PrimarySection.Online or PrimarySection.Updates => chinese ? "浏览与下载" : "Browse-and-Download",
            PrimarySection.Shortcuts => chinese ? "快捷键与浮窗" : "Shortcut-Panel",
            _ => chinese ? "用户手册" : "User-Guide"
        };
        return GuidesUrl + (chinese ? "zh-CN/" : "en/") + Uri.EscapeDataString(page) + ".md";
    }

    private async void OnTitleBarHelpClicked(object sender, RoutedEventArgs e)
        => await RunUiActionAsync("Open help", () => OpenExternalUrlAsync(HelpUrl(_shell.CurrentSection), T("Common.OpenPageFailed")));

    private void ApplyHelpLanguage()
    {
        string name = T("Help.ThisPage");
        ToolTipService.SetToolTip(TitleBarHelpButton, name);
        AutomationProperties.SetName(TitleBarHelpButton, name);
        RefreshPageHint();
    }
}
