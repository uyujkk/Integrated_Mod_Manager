using IntegratedModManager.Core.Presets;

namespace ModFolderCopier.WinUI;

// Which captured values go into a state slot, and the loader's own save interval, which decides how soon
// d3dx_user.ini holds the values to capture.
public sealed partial class MainWindow
{

    // Main window: the user sees which menu-state values would be left out and can keep any of them.
    // Returns null when the user cancels.
    private async Task<IReadOnlyList<ModPersistentValue>?> ChooseSlotValuesAsync(IReadOnlyList<ModPersistentValue> values)
    {
        if (_shortcuts.KeepMenuStateVariables) return values;
        var (kept, transient) = TransientVariablePolicy.Split(values);
        if (transient.Count == 0) return values;
        if (await MenuStateDialog.ChooseAsync(_services, transient, kept.Count, Math.Max(160, RootGrid.ActualHeight - 360)) is not { } keepAnyway) return null;
        return values.Where(value => !transient.Contains(value) || keepAnyway.Contains(value)).ToArray();
    }

}
