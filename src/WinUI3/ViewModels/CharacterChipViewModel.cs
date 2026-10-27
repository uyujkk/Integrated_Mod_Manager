using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModFolderCopier.WinUI;

/// <summary>One character folder in the dashboard's character overview; opening it shows the character in the Mod workspace.</summary>
public sealed partial class CharacterChipViewModel : ObservableObject
{
    private readonly Func<string, Task> _open;

    public CharacterChipViewModel(string name, string path, string detail, Func<string, Task> open)
    {
        Name = name;
        Path = path;
        Detail = detail;
        _open = open;
    }

    public string Name { get; }

    public string Path { get; }

    /// <summary>The Mod count, localized by the window.</summary>
    public string Detail { get; }

    // Shown in the circle until the workspace has loaded the character's avatar image.
    public string Initial => Name.Length == 0 ? "?"
        : char.IsHighSurrogate(Name[0]) && Name.Length > 1 ? Name[..2]
        : Name[..1].ToUpperInvariant();

    /// <summary>The avatar the Mod workspace loaded for this folder, when it has one.</summary>
    public Microsoft.UI.Xaml.Media.ImageSource? Avatar
    {
        get => _avatar;
        set
        {
            if (SetProperty(ref _avatar, value)) OnPropertyChanged(nameof(HasAvatar));
        }
    }

    public bool HasAvatar => _avatar is not null;

    private Microsoft.UI.Xaml.Media.ImageSource? _avatar;

    [RelayCommand]
    private Task OpenAsync() => _open(Path);
}
