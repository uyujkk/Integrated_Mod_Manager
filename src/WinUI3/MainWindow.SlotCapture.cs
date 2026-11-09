using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Which captured values go into a state slot, and the loader's own save interval, which decides how soon
// d3dx_user.ini holds the values to capture.
public sealed partial class MainWindow
{
    private Dictionary<string, int> _loaderAutoSaveOriginals = new(StringComparer.OrdinalIgnoreCase);

    // Main window: the user sees which menu-state values would be left out and can keep any of them.
    // Returns null when the user cancels.
    private async Task<IReadOnlyList<ModPersistentValue>?> ChooseSlotValuesAsync(IReadOnlyList<ModPersistentValue> values)
    {
        if (_shortcuts.KeepMenuStateVariables) return values;
        var (kept, transient) = TransientVariablePolicy.Split(values);
        if (transient.Count == 0) return values;
        var checks = transient.Select(value => new CheckBox { Content = $"{value.Variable} = {value.Value}  ·  {value.RelativeIniPath}" }).ToArray();
        var list = new StackPanel { Spacing = 2 };
        foreach (CheckBox check in checks) list.Children.Add(check);
        var content = new StackPanel { Spacing = 10, MaxWidth = 560 };
        content.Children.Add(new TextBlock { Text = T("SlotCapture.MenuStateFound", transient.Count, kept.Count), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new ScrollViewer { Content = list, MaxHeight = Math.Max(160, RootGrid.ActualHeight - 360) });
        var dialog = new ContentDialog
        {
            Title = T("SlotCapture.MenuStateTitle"), Content = content, XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme,
            PrimaryButtonText = T("SlotCapture.Continue"), CloseButtonText = T("Common.Cancel"), DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        HashSet<ModPersistentValue> keepAnyway = [.. transient.Where((_, index) => checks[index].IsChecked == true)];
        return values.Where(value => !transient.Contains(value) || keepAnyway.Contains(value)).ToArray();
    }

    // The overlay cannot ask: it leaves menu state out and says how many.
    private (IReadOnlyList<ModPersistentValue> Values, int Left) FilterSlotValues(IReadOnlyList<ModPersistentValue> values)
    {
        if (_shortcuts.KeepMenuStateVariables) return (values, 0);
        var (kept, transient) = TransientVariablePolicy.Split(values);
        return (kept, transient.Count);
    }

    private string? LoaderSettingsPath(WorkspaceRepository? repository)
    {
        string? userIni = repository?.PersistentUserIniPath is { Length: > 0 } configured ? configured : TryGetDefaultPersistentIniPath(repository);
        string? folder = string.IsNullOrWhiteSpace(userIni) ? null : Path.GetDirectoryName(userIni);
        return folder is null ? null : Path.Combine(folder, "d3dx.ini");
    }

    // The loader's auto-save interval in its d3dx.ini, shown on the Shortcuts page with the intervals to choose.
    private void RefreshLoaderAutoSave()
    {
        string? path = LoaderSettingsPath(GetSelectedRepository());
        int? current = null;
        try { current = path is not null && File.Exists(path) ? LoaderAutoSaveSetting.Read(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogApplicationIssue("Read loader auto-save interval", ex); }
        _shortcuts.LoaderAutoSaveNote = current is null ? T("SlotCapture.AutoSaveUnavailable") : T("SlotCapture.AutoSaveNote", path!);
        if (current is null)
        {
            _shortcuts.LoaderAutoSaveChoices = [];
            _shortcuts.LoaderAutoSaveSeconds = null;
            return;
        }
        var choices = LoaderAutoSaveSetting.Choices.Append(current.Value).Distinct().Order()
            .Select(seconds => new LoaderAutoSaveChoice(seconds, T("SlotCapture.Seconds", seconds))).ToList();
        if (_loaderAutoSaveOriginals.TryGetValue(path!, out int original) && original != current)
            choices.Add(new LoaderAutoSaveChoice(original, T("SlotCapture.RestoreOriginal", original)));
        _shortcuts.LoaderAutoSaveChoices = choices;
        _shortcuts.LoaderAutoSaveSeconds = current;
    }

    // Writes the chosen interval into d3dx.ini after the user confirms, keeping a backup and the original value.
    private async Task SetLoaderAutoSaveAsync(int seconds)
    {
        string? path = LoaderSettingsPath(GetSelectedRepository());
        if (path is null || !File.Exists(path)) { RefreshLoaderAutoSave(); return; }
        string text = await File.ReadAllTextAsync(path);
        int? current = LoaderAutoSaveSetting.Read(text);
        if (current == seconds) return;
        if (current is null || LoaderAutoSaveSetting.Write(text, seconds) is not { } updated
            || !await ShowConfirmAsync(T("SlotCapture.ConfirmAutoSave", path, current, seconds), T("SlotCapture.AutoSaveInterval")))
        {
            RefreshLoaderAutoSave();
            return;
        }
        string backup = $"{path}.imm-autosave-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Copy(path, backup);
        await File.WriteAllTextAsync(path, updated, new System.Text.UTF8Encoding(false));
        _loaderAutoSaveOriginals.TryAdd(path, current.Value);
        if (_loaderAutoSaveOriginals[path] == seconds) _loaderAutoSaveOriginals.Remove(path);
        SaveShellConfig();
        StatusTextBlock.Text = T("SlotCapture.AutoSaveChanged", seconds, backup);
        RefreshLoaderAutoSave();
    }
}
