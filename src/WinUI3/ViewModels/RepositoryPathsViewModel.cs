using CommunityToolkit.Mvvm.ComponentModel;
using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The current repository's paths as the user edits them: the Mod folder, the game's Mods folder, the
/// launcher, and copy or link deployment (stage J4, docs/development/UI-Structure-Plan.md). The
/// dashboard's path boxes bind to these; the rest of the window reads them here instead of from the boxes.
/// </summary>
public sealed partial class RepositoryPathsViewModel : ObservableObject
{
    private string _source = string.Empty;
    private string _target = string.Empty;
    private string _launcher = string.Empty;
    private bool _useDirectoryLinks;
    private bool _arePathsEditable = true;
    private bool _isLinkDeploymentEditable = true;

    /// <summary>The repository's Mod folder.</summary>
    public string Source
    {
        get => _source;
        set
        {
            if (SetProperty(ref _source, value ?? string.Empty)) OnPropertyChanged(nameof(SourceStatus));
        }
    }

    /// <summary>The game's Mods folder that Mods are deployed into.</summary>
    public string Target
    {
        get => _target;
        set
        {
            if (SetProperty(ref _target, value ?? string.Empty)) OnPropertyChanged(nameof(TargetStatus));
        }
    }

    /// <summary>The launcher to run; optional.</summary>
    public string Launcher
    {
        get => _launcher;
        set
        {
            if (!SetProperty(ref _launcher, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(LauncherStatus));
            OnPropertyChanged(nameof(CanRunLauncher));
        }
    }

    /// <summary>Deploy with directory junctions instead of copies.</summary>
    public bool UseDirectoryLinks
    {
        get => _useDirectoryLinks;
        set => SetProperty(ref _useDirectoryLinks, value);
    }

    /// <summary>False while a combination preset is being applied.</summary>
    public bool ArePathsEditable
    {
        get => _arePathsEditable;
        set => SetProperty(ref _arePathsEditable, value);
    }

    /// <summary>False while Mods are being deployed.</summary>
    public bool IsLinkDeploymentEditable
    {
        get => _isLinkDeploymentEditable;
        set => SetProperty(ref _isLinkDeploymentEditable, value);
    }

    public PathFieldStatus SourceStatus => PathFieldStatuses.Evaluate(_source, expectFile: false);

    public PathFieldStatus TargetStatus => PathFieldStatuses.Evaluate(_target, expectFile: false);

    public PathFieldStatus LauncherStatus => PathFieldStatuses.Evaluate(_launcher, expectFile: true);

    public bool CanRunLauncher => LauncherStatus == PathFieldStatus.Ready;

    /// <summary>How many of the three paths point at something that exists.</summary>
    public int ReadyCount => new[] { SourceStatus, TargetStatus, LauncherStatus }.Count(status => status == PathFieldStatus.Ready);

    /// <summary>How many of the two required paths (Mod folder, game folder) are empty or missing; the launcher is optional.</summary>
    public int MissingRequiredCount => new[] { SourceStatus, TargetStatus }.Count(status => status != PathFieldStatus.Ready);

    /// <summary>The paths as typed, without surrounding spaces.</summary>
    public string TrimmedSource => _source.Trim();

    public string TrimmedTarget => _target.Trim();

    public string TrimmedLauncher => _launcher.Trim();
}
