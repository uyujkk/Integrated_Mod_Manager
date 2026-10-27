using CommunityToolkit.Mvvm.ComponentModel;
using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Mod workspace's details summary: which Mod it describes, its name and its deployment
/// state. The window localizes the labels and colours the state from <see cref="Tone"/>;
/// deploying, linking and deleting stay in the window and are unchanged.
/// </summary>
public sealed partial class ModDetailsViewModel : ObservableObject
{
    private SecondLevelFolderItem? _mod;
    private string _name = string.Empty;
    private string _state = string.Empty;
    private ModStateTone _tone;

    public SecondLevelFolderItem? Mod { get => _mod; private set => SetProperty(ref _mod, value); }

    public string Name { get => _name; private set => SetProperty(ref _name, value); }

    public string State { get => _state; private set => SetProperty(ref _state, value); }

    public ModStateTone Tone { get => _tone; private set => SetProperty(ref _tone, value); }

    /// <summary>Describes this Mod, or shows the "not selected" label in both lines when there is none.</summary>
    public void Show(SecondLevelFolderItem? mod, string notSelectedLabel, ModStateTone tone)
    {
        Mod = mod;
        Name = mod?.Name ?? notSelectedLabel;
        State = mod?.State ?? notSelectedLabel;
        Tone = mod is null ? ModStateTone.Neutral : tone;
    }
}
