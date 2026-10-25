using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

public sealed class SecondLevelFolderItem : System.ComponentModel.INotifyPropertyChanged
{
    private string _state = string.Empty;
    private string _coverPlaceholder = string.Empty;
    private ImageSource? _coverImageSource;
    private GridLength _coverPreviewHeight = new(140);
    private Thickness _coverSelectionThickness = new(0);
    private double _coverCardWidth = 236;
    private double _coverCardHeight = 240;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public SecondLevelFolderItem(string path, List<string> files, string state)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Files = files;
        State = state;
    }

    public string Name { get; }

    public string Path { get; }

    public List<string> Files { get; }

    public string State
    {
        get => _state;
        set { if (_state == value) return; _state = value; Changed(nameof(State)); }
    }

    public ImageSource? CoverImageSource
    {
        get => _coverImageSource;
        set { _coverImageSource = value; Changed(nameof(CoverImageSource)); Changed(nameof(CoverPlaceholderVisibility)); }
    }

    public Visibility CoverPlaceholderVisibility => CoverImageSource is null ? Visibility.Visible : Visibility.Collapsed;

    public GridLength CoverPreviewHeight
    {
        get => _coverPreviewHeight;
        set { if (_coverPreviewHeight == value) return; _coverPreviewHeight = value; Changed(nameof(CoverPreviewHeight)); }
    }

    public Thickness CoverSelectionThickness
    {
        get => _coverSelectionThickness;
        set { if (_coverSelectionThickness == value) return; _coverSelectionThickness = value; Changed(nameof(CoverSelectionThickness)); }
    }

    public double CoverCardWidth
    {
        get => _coverCardWidth;
        set { if (Math.Abs(_coverCardWidth - value) < .5) return; _coverCardWidth = value; Changed(nameof(CoverCardWidth)); }
    }

    public double CoverCardHeight
    {
        get => _coverCardHeight;
        set { if (Math.Abs(_coverCardHeight - value) < .5) return; _coverCardHeight = value; Changed(nameof(CoverCardHeight)); }
    }

    public string CoverPlaceholder
    {
        get => _coverPlaceholder;
        set { if (_coverPlaceholder == value) return; _coverPlaceholder = value; Changed(nameof(CoverPlaceholder)); }
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public int FilesCount => Files.Count;
}
