using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IntegratedModManager.Core.Configuration;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The dashboard: one card per repository plus the current repository's status for the strip
/// at the top. The window keeps the cards in step with its repository list and localizes text.
/// </summary>
/// <summary>What the dashboard asks the window to do; <see cref="DashboardActionRequest.Argument"/> names a repository or preset.</summary>
public enum DashboardAction
{
    /// <summary>Argument: the repository id.</summary>
    SwitchRepository,

    /// <summary>Argument: the preset id, or null for a blank repository.</summary>
    NewRepository,

    EditRepository,
    OpenRepository,
    PickSource,
    PickTarget,
    PickLauncher,
    OpenSource,
    OpenTarget,
    OpenLauncher,
    BridgeOptions,
    BrowseOnline,
    CheckModUpdates,
    RunLauncher,

    /// <summary>The getting-started tips were closed or finished; the window saves that.</summary>
    GettingStartedTipsClosed
}

public sealed record DashboardActionRequest(DashboardAction Action, string? Argument = null);

/// <summary>One entry of the "New repository" menu: a blank repository (no preset) or a game preset.</summary>
public sealed record NewRepositoryChoice(string? PresetId, string Label, string Glyph, string? ToolTip);

public sealed partial class DashboardViewModel : ObservableObject
{
    private string _currentStatusText = string.Empty;
    private string _currentSummary = string.Empty;
    private bool _currentIsReady;

    private string _charactersEmptyText = string.Empty;

    public ObservableCollection<RepositoryCardViewModel> Repositories { get; } = [];

    /// <summary>The current repository's character folders, from the background scan.</summary>
    public ObservableCollection<CharacterChipViewModel> Characters { get; } = [];

    /// <summary>Why the overview is empty (no source folder, no characters yet, or still counting).</summary>
    public string CharactersEmptyText
    {
        get => _charactersEmptyText;
        set
        {
            if (SetProperty(ref _charactersEmptyText, value)) OnPropertyChanged(nameof(HasCharactersEmptyText));
        }
    }

    public bool HasCharactersEmptyText => !string.IsNullOrEmpty(_charactersEmptyText);

    /// <summary>
    /// Whether the overview lists any characters. An empty grid is collapsed: left visible, its scroll
    /// viewer sat in the row sized by the empty text and showed and hid its scroll bar in turn until
    /// the layout gave up (a layout cycle when returning to the dashboard).
    /// </summary>
    public bool HasCharacters => Characters.Count > 0;

    public void SetCharacters(IEnumerable<CharacterChipViewModel> characters, string emptyText)
    {
        Characters.Clear();
        foreach (CharacterChipViewModel character in characters)
        {
            Characters.Add(character);
        }
        CharactersEmptyText = Characters.Count == 0 ? emptyText : string.Empty;
        OnPropertyChanged(nameof(HasCharacters));
    }

    public string CurrentStatusText { get => _currentStatusText; set => SetProperty(ref _currentStatusText, value); }

    public string CurrentSummary { get => _currentSummary; set => SetProperty(ref _currentSummary, value); }

    public bool CurrentIsReady
    {
        get => _currentIsReady;
        set
        {
            if (SetProperty(ref _currentIsReady, value)) OnPropertyChanged(nameof(CurrentNeedsSetup));
        }
    }

    public bool CurrentNeedsSetup => !_currentIsReady;

    // Stage J4: what the page shows besides the cards, and what it asks the window to do.
    private string _currentRepositoryName = string.Empty;
    private bool _canEditRepository;
    private bool _canOpenRepository;
    private bool _bridgeVisible;
    private bool _bridgeEnabled = true;
    private string _bridgeLabel = string.Empty;
    private IReadOnlyList<NewRepositoryChoice> _newRepositoryChoices = [];

    /// <summary>Raised when the user asks for something only the window can do.</summary>
    public event EventHandler<DashboardActionRequest>? ActionRequested;

    /// <summary>Raised after the window refreshed the cards and choices; the page relabels its own controls.</summary>
    public event EventHandler? Refreshed;

    public void Request(DashboardAction action, string? argument = null) => ActionRequested?.Invoke(this, new DashboardActionRequest(action, argument));

    public void NotifyRefreshed() => Refreshed?.Invoke(this, EventArgs.Empty);

    private GettingStartedTips? _gettingStarted;

    /// <summary>
    /// The getting-started tips to show (stage J4-b), or null. The window sets them once no startup
    /// dialog is open; the page walks them and asks for <see cref="DashboardAction.GettingStartedTipsClosed"/>.
    /// </summary>
    public GettingStartedTips? GettingStarted
    {
        get => _gettingStarted;
        set => SetProperty(ref _gettingStarted, value);
    }

    /// <summary>The current repository's name; empty when none is selected.</summary>
    public string CurrentRepositoryName { get => _currentRepositoryName; set => SetProperty(ref _currentRepositoryName, value ?? string.Empty); }

    public bool CanEditRepository { get => _canEditRepository; set => SetProperty(ref _canEditRepository, value); }

    public bool CanOpenRepository { get => _canOpenRepository; set => SetProperty(ref _canOpenRepository, value); }

    /// <summary>The Endfield launch settings button, shown for Endfield repositories (EndfieldBridgeService).</summary>
    public bool BridgeVisible { get => _bridgeVisible; set => SetProperty(ref _bridgeVisible, value); }

    public bool BridgeEnabled { get => _bridgeEnabled; set => SetProperty(ref _bridgeEnabled, value); }

    public string BridgeLabel { get => _bridgeLabel; set => SetProperty(ref _bridgeLabel, value ?? string.Empty); }

    public IReadOnlyList<NewRepositoryChoice> NewRepositoryChoices => _newRepositoryChoices;

    public void SetNewRepositoryChoices(IReadOnlyList<NewRepositoryChoice> choices)
    {
        _newRepositoryChoices = choices ?? [];
        OnPropertyChanged(nameof(NewRepositoryChoices));
    }

    /// <summary>Reuses the card for an id that is already shown, so a refresh does not rebuild the list.</summary>
    public RepositoryCardViewModel GetOrAdd(string id, int index, Func<RepositoryCardViewModel> create)
    {
        int existing = -1;
        for (int i = 0; i < Repositories.Count; i++)
        {
            if (Repositories[i].Id == id)
            {
                existing = i;
                break;
            }
        }

        if (existing < 0)
        {
            RepositoryCardViewModel card = create();
            Repositories.Insert(Math.Min(index, Repositories.Count), card);
            return card;
        }

        RepositoryCardViewModel found = Repositories[existing];
        int target = Math.Min(index, Repositories.Count - 1);
        if (existing != target)
        {
            Repositories.Move(existing, target);
        }
        return found;
    }

    /// <summary>Drops cards for repositories that no longer exist.</summary>
    public void RemoveExcept(IReadOnlyCollection<string> ids)
    {
        for (int i = Repositories.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(Repositories[i].Id)) Repositories.RemoveAt(i);
        }
    }
}
