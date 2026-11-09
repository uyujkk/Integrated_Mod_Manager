using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ModFolderCopier.WinUI;

/// <summary>What restoring a combination preset gives the Restore preview's answer (moved from the window in stage J8).</summary>
internal enum CombinationRestoreChoice { Cancel, Restore, TakeOver }

/// <summary>One row of a preview list.</summary>
public sealed record CombinationPreviewRow(string Title, string Detail, string ValueText);

/// <summary>
/// The preview before a combination preset is restored: Mods, parameters and checks, with "take over" for external
/// folders (moved from the window in stage J8).
/// </summary>
internal static class CombinationRestoreDialog
{
    public static async Task<CombinationRestoreChoice> ConfirmAsync(AppServices services, string name, CombinationRestorePreview preview,
        UIElement? savedLook, double windowWidth, double windowHeight)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        CombinationRestoreChoice choice = CombinationRestoreChoice.Cancel;
        var closed = new CheckBox { Content = T("Common.IHaveFullyClosedTheGame") };
        var panel = new Grid { RowSpacing = 12, MinWidth = 320,
            Height = Math.Clamp(windowHeight - 260, 280, 580) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var runtime = preview.Runtime;
        string summary = preview.CanRestore
            ? T("CombinationPresets.ModsEnableRemove", name, preview.Deployment!.DesiredSources.Count, preview.Deployment.InstallSources.Count, preview.Deployment.RemoveTargets.Count)
            : T("CombinationPresets.IssuesMustBeResolvedRestoreHas", name, preview.Issues.Count);
        summary += runtime is null ? T("CombinationPresets.NoParameterWritePlan")
            : T("CombinationPresets.ParametersChangeAddUnchanged", runtime.ChangedCount, runtime.AddedCount, runtime.UnchangedCount);
        var heading = new StackPanel { Spacing = 8 };
        if (savedLook is not null) heading.Children.Add(savedLook);
        heading.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(heading);
        // Issue #107: same-named folders the manager cannot recognise are kept; the user may take them over.
        IReadOnlyList<string> unowned = preview.Deployment?.Unowned ?? [];
        Button? takeOver = null;
        if (unowned.Count > 0)
        {
            takeOver = new Button { Content = T("CombinationPresets.TakeOverExternalFolders", unowned.Count) };
            ToolTipService.SetToolTip(takeOver, T("CombinationPresets.TakeOverExternalFoldersHint"));
            heading.Children.Add(new TextBlock { Text = T("CombinationPresets.ExternalFoldersFound", unowned.Count), TextWrapping = TextWrapping.Wrap });
            heading.Children.Add(takeOver);
        }
        var tabs = new TabView { IsAddTabButtonVisible = false, CanReorderTabs = false, CanDragTabs = false };
        Grid.SetRow(tabs, 1);
        panel.Children.Add(tabs);
        // The Mods grouped by what the restore does with them (J8): added, removed, kept, skipped.
        CombinationPreviewRow Row(CombinationModPreview mod) => new(mod.ModRelativePath, mod.Path, ActionText(services, mod.Action));
        PreviewGroup Group(Func<int, string> header, params CombinationModAction[] actions)
        {
            var rows = preview.Mods.Where(mod => actions.Contains(mod.Action)).Select(Row).ToArray();
            return new PreviewGroup(header(rows.Length), rows);
        }
        tabs.TabItems.Add(new TabViewItem
        {
            Header = T("CombinationPresets.Mods", preview.Mods.Count), IsClosable = false,
            Content = preview.Mods.Count == 0
                ? new TextBlock { Text = T("CombinationPresets.NoItems"), Margin = new Thickness(8) }
                : DialogLayout.GroupedPreview(
                [
                    Group(n => T("Dialogs.PreviewAdded", n), CombinationModAction.Install),
                    Group(n => T("Dialogs.PreviewRemoved", n), CombinationModAction.Remove),
                    Group(n => T("Dialogs.PreviewKept", n), CombinationModAction.Keep, CombinationModAction.PreserveExternal, CombinationModAction.UnownedCopy),
                    Group(n => T("Dialogs.PreviewSkipped", n), CombinationModAction.Pending, CombinationModAction.Unavailable)
                ], DialogLayout.PreviewRowTemplate())
        });
        AddTab(T("CombinationPresets.Parameters", preview.Parameters.Count), preview.Parameters
            .OrderBy(item => item.Change.Kind).Select(item => new CombinationPreviewRow(
                item.ModRelativePath + " · " + item.Change.Saved.Variable,
                item.Change.Saved.RelativeIniPath + " · " + item.Change.Saved.RuntimeKey,
                (item.Change.PreviousValue ?? T("CombinationPresets.NotRecorded")) + " → " + item.Change.Saved.Value
                    + (item.Change.Kind == ModPersistentChangeKind.Unchanged ? T("CombinationPresets.Unchanged") : ""))));
        var checks = preview.Issues.Select(issue => new CombinationPreviewRow(
            IssueText(services, issue.Code), issue.Subject, T("CombinationPresets.RestoreBlocked"))).ToList();
        checks.Add(new(T("CombinationPresets.ProtectionScope"), T("CombinationPresets.OnlyRecognizedDeploymentsAreManagedExternal"), ""));
        checks.Add(new(T("CombinationPresets.ParametersAndBackups"), runtime?.Edit.Path ?? T("CombinationPresets.ThisPreviewHasNoParameterWrite"),
            T("CombinationPresets.WriteSelectedKeysOnlyBackUp")));
        AddTab(T("CombinationPresets.Checks", preview.Issues.Count), checks);
        if (!preview.CanRestore) tabs.SelectedIndex = 2;
        closed.IsEnabled = preview.CanRestore;
        Grid.SetRow(closed, 2);
        panel.Children.Add(closed);
        var dialog = new ContentDialog
        {
            Title = T("CombinationPresets.RestorePreview"),
            Content = panel,
            PrimaryButtonText = T("CombinationPresets.BackUpAndRestore"), CloseButtonText = T("Common.Cancel"),
            IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Close
        };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Clamp(windowWidth - 100, 360, 960);
        closed.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = preview.CanRestore;
        closed.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        if (takeOver is not null)
            takeOver.Click += (_, _) =>
            {
                choice = CombinationRestoreChoice.TakeOver;
                dialog.Hide();
            };
        if (await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary) choice = CombinationRestoreChoice.Restore;
        return choice;

        void AddTab(string header, IEnumerable<CombinationPreviewRow> rows)
        {
            var items = rows.ToArray();
            if (items.Length == 0) items = [new(T("CombinationPresets.NoItems"), "", "")];
            // ListView virtualizes rows; long presets scroll here, not the whole page.
            var list = new ListView { ItemsSource = items, SelectionMode = ListViewSelectionMode.None,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <Border Padding="8,10" BorderThickness="0,0,0,1" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
                    <StackPanel Spacing="4">
                      <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextWrapping="Wrap"/>
                      <TextBlock Text="{Binding ValueText}" TextWrapping="Wrap"/>
                      <TextBlock Text="{Binding Detail}" FontSize="12" Opacity="0.7" TextWrapping="Wrap"/>
                    </StackPanel>
                  </Border>
                </DataTemplate>
                """) };
            tabs.TabItems.Add(new TabViewItem { Header = header, IsClosable = false, Content = list });
        }
    }

    private static string ActionText(AppServices services, CombinationModAction action)
    {
        string T(string key) => services.Language.T(key);
        return action switch
        {
            CombinationModAction.Install => T("CombinationPresets.EnableRedeploy"),
            CombinationModAction.Keep => T("CombinationPresets.KeepEnabled"),
            CombinationModAction.Remove => T("CombinationPresets.RemoveDeploymentKeepRepositorySource"),
            CombinationModAction.PreserveExternal => T("CombinationPresets.ExternalUnrecognizedFolderKeepUntouched"),
            CombinationModAction.UnownedCopy => T("CombinationPresets.UnownedCopyKept"),
            CombinationModAction.Pending => T("CombinationPresets.DeploymentUncheckedResolveBlockingIssuesFirst"),
            _ => T("CombinationPresets.Unavailable")
        };
    }

    private static string IssueText(AppServices services, CombinationPreviewIssueCode code)
    {
        string T(string key) => services.Language.T(key);
        return code switch
        {
            CombinationPreviewIssueCode.SourceUnavailable => T("CombinationPresets.RepositoryUnavailableCheckItsPath"),
            CombinationPreviewIssueCode.TargetUnavailable => T("CombinationPresets.DeploymentFolderUnavailableCheckItsPath"),
            CombinationPreviewIssueCode.UnsafeRoots => T("CombinationPresets.UseSeparateRepositoryDeploymentRootsNot"),
            CombinationPreviewIssueCode.UnsafeModPath => T("CombinationPresets.InvalidModPathSaveThePreset"),
            CombinationPreviewIssueCode.MissingMod => T("CombinationPresets.SavedModMissingRestoreItsFolder"),
            CombinationPreviewIssueCode.DisabledMod => T("CombinationPresets.ModFolderDisabledCheckItAnd"),
            CombinationPreviewIssueCode.LinkedSource => T("CombinationPresets.LinkedSourceModItsIdentityCannot"),
            CombinationPreviewIssueCode.DuplicateMod => T("CombinationPresets.DuplicateModInPresetSaveIt"),
            CombinationPreviewIssueCode.AmbiguousTargetName => T("CombinationPresets.MultipleModsShareADeploymentName"),
            CombinationPreviewIssueCode.SnapshotMismatch => T("CombinationPresets.ModListAndStateSnapshotsDiffer"),
            CombinationPreviewIssueCode.ParameterMismatch => T("CombinationPresets.ModOrDeployedCopyDeclarationsChanged"),
            CombinationPreviewIssueCode.LoaderStateInvalid => T("CombinationPresets.CannotSafelyReadLoaderStateCheck"),
            CombinationPreviewIssueCode.DeploymentBlocked => T("CombinationPresets.DeploymentBlockedCheckAmbiguousNamesFiles"),
            CombinationPreviewIssueCode.UnownedCopyInTheWay => T("CombinationPresets.UnownedCopyInTheWay"),
            _ => T("CombinationPresets.ReadFailedCheckPermissionsAndRetry")
        };
    }
}
