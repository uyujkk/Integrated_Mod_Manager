using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

// Once per feature set, after startup, a short dialog lists the new features and where to find them.
// The marker is the feature set the text describes, not the build version, so development builds and
// later patch versions show it once. Update WhatsNewFeatureSet and the texts for the next set.
public sealed partial class MainWindow
{
    private const string WhatsNewFeatureSet = "4.5";
    private const string StateSlotTestingGuide = "State-Slots-Hot-Apply-Testing.zh-CN.md";
    private string? _whatsNewSeenVersion;

    private async Task ShowWhatsNewOnceAsync()
    {
        if (string.Equals(_whatsNewSeenVersion, WhatsNewFeatureSet, StringComparison.Ordinal))
        {
            return;
        }

        // WinUI allows one ContentDialog at a time: wait for startup dialogs (for example the
        // Endfield launch settings) to close first.
        await Task.Delay(1500);
        for (int attempt = 0; attempt < 300 && IsContentDialogOpen(); attempt++)
        {
            await Task.Delay(1000);
        }
        if (_onlineWindowClosed || IsContentDialogOpen())
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = T("WhatsNew.Title", WhatsNewFeatureSet),
            Content = new ScrollViewer
            {
                MaxHeight = Math.Max(240, RootGrid.ActualHeight - 260),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock { Text = T("WhatsNew.Body"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            },
            PrimaryButtonText = T("WhatsNew.OpenGuide"),
            CloseButtonText = T("WhatsNew.Close"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = RootGrid.ActualTheme,
            XamlRoot = RootGrid.XamlRoot
        };
        _whatsNewSeenVersion = WhatsNewFeatureSet;
        SaveShellConfig();
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            OpenStateSlotTestingGuide();
        }
    }

    private bool IsContentDialogOpen() => RootGrid.XamlRoot is not null
        && VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot).Any(popup => popup.Child is ContentDialog);

    // The release package ships docs\guides beside the launcher; a source checkout has it at the root.
    private void OpenStateSlotTestingGuide()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "..", "docs", "guides", StateSlotTestingGuide),
            Path.Combine(AppContext.BaseDirectory, "docs", "guides", StateSlotTestingGuide)
        ];
        string? guide = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        string target = guide ?? $"{GitHubRepositoryUrl}/blob/main/docs/guides/{StateSlotTestingGuide}";
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception)
        {
            LogApplicationIssue("Open state slot testing guide", exception);
            StatusTextBlock.Text = target;
        }
    }
}
