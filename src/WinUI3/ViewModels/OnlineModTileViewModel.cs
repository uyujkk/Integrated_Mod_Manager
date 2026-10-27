using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Display state of one online grid tile. The window owns the actions and keeps these
/// properties current (download state, late metrics, language); the tile only binds to them.
/// </summary>
/// <remarks>
/// Properties use <see cref="ObservableObject.SetProperty{T}(ref T, T, string?)"/> directly:
/// field-based [ObservableProperty] is not WinRT/AOT-safe (MVVMTK0045), and the partial-property
/// form needs LangVersion preview.
/// </remarks>
public sealed partial class OnlineModTileViewModel : ObservableObject
{
    private readonly Func<Task> _openPage;
    private readonly Func<Task> _download;
    private string _characterLabel = string.Empty;
    private string _previewPlaceholder = string.Empty;
    private string _heatLabel = string.Empty;
    private string _hotnessText = string.Empty;
    private string _hotnessToolTip = string.Empty;
    private string _statusText = string.Empty;
    private string _statusToolTip = string.Empty;
    private string _likesText = string.Empty;
    private string _viewsText = string.Empty;
    private string _downloadsText = string.Empty;
    private string _statsToolTip = string.Empty;
    private string _openPageToolTip = string.Empty;
    private string _downloadToolTip = string.Empty;
    private string _downloadGlyph = "";
    private bool _canDownload;

    // Opening the details is the grid's item click (mouse or Enter), not a tile command.
    public OnlineModTileViewModel(OnlineModCard mod, Func<Task> openPage, Func<Task> download)
    {
        Mod = mod;
        _openPage = openPage;
        _download = download;
    }

    public OnlineModCard Mod { get; }

    public string Title => Mod.Title;

    public bool HasPreview => !string.IsNullOrWhiteSpace(Mod.PreviewUrl);

    public string CharacterLabel { get => _characterLabel; set => SetProperty(ref _characterLabel, value); }

    public string PreviewPlaceholder { get => _previewPlaceholder; set => SetProperty(ref _previewPlaceholder, value); }

    public string HeatLabel { get => _heatLabel; set => SetProperty(ref _heatLabel, value); }

    public string HotnessText { get => _hotnessText; set => SetProperty(ref _hotnessText, value); }

    public string HotnessToolTip { get => _hotnessToolTip; set => SetProperty(ref _hotnessToolTip, value); }

    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public string StatusToolTip { get => _statusToolTip; set => SetProperty(ref _statusToolTip, value); }

    public string LikesText { get => _likesText; set => SetProperty(ref _likesText, value); }

    public string ViewsText { get => _viewsText; set => SetProperty(ref _viewsText, value); }

    public string DownloadsText { get => _downloadsText; set => SetProperty(ref _downloadsText, value); }

    public string StatsToolTip { get => _statsToolTip; set => SetProperty(ref _statsToolTip, value); }

    public string OpenPageToolTip { get => _openPageToolTip; set => SetProperty(ref _openPageToolTip, value); }

    public string DownloadToolTip { get => _downloadToolTip; set => SetProperty(ref _downloadToolTip, value); }

    public string DownloadGlyph { get => _downloadGlyph; set => SetProperty(ref _downloadGlyph, value); }

    public bool CanDownload
    {
        get => _canDownload;
        set
        {
            if (SetProperty(ref _canDownload, value)) DownloadCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private Task OpenPageAsync() => _openPage();

    // While a download runs the command reports CanExecute = false, so the button disables itself.
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync() => _download();
}
