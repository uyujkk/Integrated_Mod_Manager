using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

public sealed class FirstLevelFolderItem : INotifyPropertyChanged
{
    private ImageSource? _avatarImageSource;

    public FirstLevelFolderItem(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Path { get; }

    public List<SecondLevelFolderItem> Children { get; } = [];

    public int ChildrenCount => Children.Count;

    // Shown until the avatar image loads, or when the folder has none.
    public string AvatarInitial => Name.Length == 0 ? "?"
        : char.IsHighSurrogate(Name[0]) && Name.Length > 1 ? Name[..2]
        : Name[..1].ToUpperInvariant();

    public ImageSource? AvatarImageSource
    {
        get => _avatarImageSource;
        set
        {
            if (ReferenceEquals(_avatarImageSource, value)) return;
            _avatarImageSource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarImageSource)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarPlaceholderVisibility)));
        }
    }

    public Visibility AvatarPlaceholderVisibility => AvatarImageSource is null ? Visibility.Visible : Visibility.Collapsed;
}
