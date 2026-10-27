using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The dashboard: one card per repository plus the current repository's status for the strip
/// at the top. The window keeps the cards in step with its repository list and localizes text.
/// </summary>
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

    public void SetCharacters(IEnumerable<CharacterChipViewModel> characters, string emptyText)
    {
        Characters.Clear();
        foreach (CharacterChipViewModel character in characters)
        {
            Characters.Add(character);
        }
        CharactersEmptyText = Characters.Count == 0 ? emptyText : string.Empty;
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
