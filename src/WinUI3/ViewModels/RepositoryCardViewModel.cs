using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModFolderCopier.WinUI;

/// <summary>What the card can ask the window to do with its repository, by repository id.</summary>
public sealed record RepositoryCardActions(
    Action<string> SwitchTo,
    Action<string> Manage,
    Func<string, Task> Edit,
    Action<string> OpenWiki,
    Func<string, Task> Delete);

/// <summary>
/// One repository on the dashboard. The window fills the localized text and the folder counts
/// (which arrive later from a background scan); the card binds to these properties and commands.
/// </summary>
public sealed partial class RepositoryCardViewModel : ObservableObject
{
    private readonly RepositoryCardActions _actions;
    private string _name = string.Empty;
    private string _subtitle = string.Empty;
    private string _summary = string.Empty;
    private string _statusText = string.Empty;
    private bool _isReady;
    private bool _isCurrent;
    private bool _canDelete;
    private bool _hasWiki;
    private RepositoryCardLabels _labels = RepositoryCardLabels.Empty;

    public RepositoryCardViewModel(string id, RepositoryCardActions actions)
    {
        Id = id;
        _actions = actions;
    }

    public string Id { get; }

    public string Name { get => _name; set => SetProperty(ref _name, value); }

    /// <summary>Game and online category, e.g. "Arknights: Endfield · GameBanana 42770".</summary>
    public string Subtitle { get => _subtitle; set => SetProperty(ref _subtitle, value); }

    /// <summary>Character and Mod counts, or a hint while they are still being counted.</summary>
    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }

    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public bool IsReady
    {
        get => _isReady;
        set
        {
            if (SetProperty(ref _isReady, value)) OnPropertyChanged(nameof(NeedsSetup));
        }
    }

    public bool NeedsSetup => !_isReady;

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (SetProperty(ref _isCurrent, value)) OnPropertyChanged(nameof(IsOther));
        }
    }

    public bool IsOther => !_isCurrent;

    public bool CanDelete
    {
        get => _canDelete;
        set
        {
            if (SetProperty(ref _canDelete, value)) DeleteCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasWiki
    {
        get => _hasWiki;
        set
        {
            if (SetProperty(ref _hasWiki, value)) OpenWikiCommand.NotifyCanExecuteChanged();
        }
    }

    public RepositoryCardLabels Labels { get => _labels; set => SetProperty(ref _labels, value); }

    [RelayCommand]
    private void SwitchTo() => _actions.SwitchTo(Id);

    [RelayCommand]
    private void Manage() => _actions.Manage(Id);

    [RelayCommand]
    private Task EditAsync() => _actions.Edit(Id);

    [RelayCommand(CanExecute = nameof(HasWiki))]
    private void OpenWiki() => _actions.OpenWiki(Id);

    // The last repository cannot be deleted, so the menu item disables itself.
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => _actions.Delete(Id);
}

/// <summary>Button and menu text shared by every card, localized by the window.</summary>
public sealed record RepositoryCardLabels(string SwitchTo, string Manage, string More, string Edit, string OpenWiki, string Delete, string Current)
{
    public static RepositoryCardLabels Empty { get; } = new("", "", "", "", "", "", "");
}
