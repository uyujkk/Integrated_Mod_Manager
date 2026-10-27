using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

/// <summary>
/// What the online page is showing: the query (character, author, search, sort), the server
/// page, the loaded Mods and the status lines. The window still runs the requests and builds
/// the cards; it reads and writes this state instead of keeping it in its own fields.
/// </summary>
public sealed partial class OnlinePageViewModel : ObservableObject
{
    private readonly OnlinePaging _paging = new();
    private string _characterFilter = string.Empty;
    private string _searchText = string.Empty;
    private OnlineSortMode _sortMode = OnlineSortMode.Hotness;
    private OnlineCardLayoutMode _cardLayoutMode = OnlineCardLayoutMode.List;
    private int _authorId;
    private string _authorName = string.Empty;
    private int _authorWorkFilter;

    /// <summary>Every Mod on the current server page, before the local filters.</summary>
    public List<OnlineModCard> Mods { get; } = [];

    /// <summary>The filtered and sorted Mods the list and grid views show.</summary>
    public ObservableCollection<OnlineModCard> VisibleMods { get; } = [];

    public List<OnlineCharacterOption> CharacterOptions { get; } = [];

    /// <summary>Character names seen on loaded pages, kept across pages of one repository.</summary>
    public HashSet<string> KnownCharacters { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int CurrentPage
    {
        get => _paging.CurrentPage;
        set
        {
            if (_paging.CurrentPage == value) return;
            _paging.CurrentPage = value;
            OnPropertyChanged();
            RaisePagingChanged();
        }
    }

    public int TotalPages => _paging.TotalPages;

    public int TotalCount => _paging.TotalCount;

    public bool HasPreviousPage => _paging.HasPrevious;

    public bool HasNextPage => _paging.HasNext;

    public string CharacterFilter { get => _characterFilter; set => SetProperty(ref _characterFilter, value); }

    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }

    public OnlineSortMode SortMode { get => _sortMode; set => SetProperty(ref _sortMode, value); }

    public OnlineCardLayoutMode CardLayoutMode { get => _cardLayoutMode; set => SetProperty(ref _cardLayoutMode, value); }

    /// <summary>Browsing one author's works across all pages; 0 when not browsing an author.</summary>
    public int AuthorId { get => _authorId; set => SetProperty(ref _authorId, value); }

    public string AuthorName { get => _authorName; set => SetProperty(ref _authorName, value); }

    /// <summary>0 all works, 1 installed only, 2 saved only.</summary>
    public int AuthorWorkFilter { get => _authorWorkFilter; set => SetProperty(ref _authorWorkFilter, value); }

    // Status lines keep both languages so a language switch can show them again without a reload.
    public string? StatusZh { get; private set; }

    public string? StatusEn { get; private set; }

    public string? CacheStatusZh { get; private set; }

    public string? CacheStatusEn { get; private set; }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusZh) && !string.IsNullOrWhiteSpace(StatusEn);

    public bool HasCacheStatus => !string.IsNullOrWhiteSpace(CacheStatusZh) && !string.IsNullOrWhiteSpace(CacheStatusEn);

    public void SetStatus(string zh, string en)
    {
        StatusZh = zh;
        StatusEn = en;
    }

    public void SetCacheStatus(string? zh, string? en)
    {
        CacheStatusZh = zh;
        CacheStatusEn = en;
    }

    public void ApplyPageResult(int totalCount, int totalPages)
    {
        _paging.ApplyResult(totalCount, totalPages);
        RaisePagingChanged();
    }

    /// <summary>Totals are unknown again; the page number stays.</summary>
    public void ClearTotals()
    {
        _paging.ClearTotals();
        RaisePagingChanged();
    }

    /// <summary>Back to the first page with unknown totals.</summary>
    public void ResetPaging()
    {
        _paging.Reset();
        OnPropertyChanged(nameof(CurrentPage));
        RaisePagingChanged();
    }

    private void RaisePagingChanged()
    {
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
    }
}
