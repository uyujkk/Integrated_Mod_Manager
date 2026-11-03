using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Which captured values go into a state slot, and the loader's own save interval, which decides how soon
// d3dx_user.ini holds the values to capture.
public sealed partial class MainWindow
{
    private bool _keepMenuStateVariables;
    private Dictionary<string, int> _loaderAutoSaveOriginals = new(StringComparer.OrdinalIgnoreCase);

    private void AddSlotCaptureSettings(StackPanel pane)
    {
        var keep = new ToggleSwitch
        {
            OnContent = T("Common.On2"), OffContent = T("Common.Off2"),
            Header = T("SlotCapture.KeepMenuState"), IsOn = _keepMenuStateVariables
        };
        ToolTipService.SetToolTip(keep, T("SlotCapture.KeepMenuStateHint"));
        keep.Toggled += (_, _) =>
        {
            _keepMenuStateVariables = keep.IsOn;
            SaveShellConfig();
        };
        pane.Children.Add(keep);
        pane.Children.Add(CreateLoaderAutoSaveSetting());
    }

    // Main window: the user sees which menu-state values would be left out and can keep any of them.
    // Returns null when the user cancels.
    private async Task<IReadOnlyList<ModPersistentValue>?> ChooseSlotValuesAsync(IReadOnlyList<ModPersistentValue> values)
    {
        if (_keepMenuStateVariables) return values;
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
        if (_keepMenuStateVariables) return (values, 0);
        var (kept, transient) = TransientVariablePolicy.Split(values);
        return (kept, transient.Count);
    }

    private string? LoaderSettingsPath(WorkspaceRepository? repository)
    {
        string? userIni = repository?.PersistentUserIniPath is { Length: > 0 } configured ? configured : TryGetDefaultPersistentIniPath(repository);
        string? folder = string.IsNullOrWhiteSpace(userIni) ? null : Path.GetDirectoryName(userIni);
        return folder is null ? null : Path.Combine(folder, "d3dx.ini");
    }

    private FrameworkElement CreateLoaderAutoSaveSetting()
    {
        var picker = new ComboBox { Header = T("SlotCapture.AutoSaveInterval"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionTextStyle"] };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(picker);
        panel.Children.Add(note);
        bool filling = false;
        void Fill()
        {
            filling = true;
            picker.Items.Clear();
            string? path = LoaderSettingsPath(GetSelectedRepository());
            int? current = null;
            try { current = path is not null && File.Exists(path) ? LoaderAutoSaveSetting.Read(File.ReadAllText(path)) : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogApplicationIssue("Read loader auto-save interval", ex); }
            picker.IsEnabled = current is not null;
            note.Text = current is null ? T("SlotCapture.AutoSaveUnavailable") : T("SlotCapture.AutoSaveNote", path!);
            if (current is null) { filling = false; return; }
            foreach (int seconds in LoaderAutoSaveSetting.Choices.Append(current.Value).Distinct().Order())
                picker.Items.Add(new ComboBoxItem { Content = T("SlotCapture.Seconds", seconds), Tag = seconds });
            if (_loaderAutoSaveOriginals.TryGetValue(path!, out int original) && original != current)
                picker.Items.Add(new ComboBoxItem { Content = T("SlotCapture.RestoreOriginal", original), Tag = original });
            picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().First(item => (int)item.Tag == current);
            filling = false;
        }
        picker.Loaded += (_, _) => Fill();
        picker.SelectionChanged += async (_, _) =>
        {
            if (filling || picker.SelectedItem is not ComboBoxItem { Tag: int seconds }) return;
            await RunUiActionAsync("Change loader auto-save interval", async () =>
            {
                string? path = LoaderSettingsPath(GetSelectedRepository());
                if (path is null || !File.Exists(path)) { Fill(); return; }
                string text = await File.ReadAllTextAsync(path);
                int? current = LoaderAutoSaveSetting.Read(text);
                if (current == seconds) return;
                if (current is null || LoaderAutoSaveSetting.Write(text, seconds) is not { } updated
                    || !await ShowConfirmAsync(T("SlotCapture.ConfirmAutoSave", path, current, seconds), T("SlotCapture.AutoSaveInterval")))
                {
                    Fill();
                    return;
                }
                string backup = $"{path}.imm-autosave-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                File.Copy(path, backup);
                await File.WriteAllTextAsync(path, updated, new System.Text.UTF8Encoding(false));
                _loaderAutoSaveOriginals.TryAdd(path, current.Value);
                if (_loaderAutoSaveOriginals[path] == seconds) _loaderAutoSaveOriginals.Remove(path);
                SaveShellConfig();
                StatusTextBlock.Text = T("SlotCapture.AutoSaveChanged", seconds, backup);
                Fill();
            });
        };
        return panel;
    }
}
