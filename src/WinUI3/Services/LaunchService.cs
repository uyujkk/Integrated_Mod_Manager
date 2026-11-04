using System.Diagnostics;

namespace ModFolderCopier.WinUI;

/// <summary>Starts the repository's loader or launcher.</summary>
internal sealed class LaunchService
{
    /// <summary>Starts a loader from its own folder, as double-clicking it would.</summary>
    public void StartLoader(string loader) => Process.Start(new ProcessStartInfo(loader)
    {
        WorkingDirectory = Path.GetDirectoryName(loader) ?? AppContext.BaseDirectory,
        UseShellExecute = true
    });

    /// <summary>Starts a launcher with arguments, such as XXMI with the importer to open.</summary>
    public void StartWithArguments(string loader, string arguments, string workingDirectory)
        => Process.Start(new ProcessStartInfo(loader, arguments) { WorkingDirectory = workingDirectory, UseShellExecute = true });
}
