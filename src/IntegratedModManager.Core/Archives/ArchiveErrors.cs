using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Archives;

public enum ArchiveErrorKind
{
    /// <summary>The extracted archive has no content other than tool/OS clutter.</summary>
    Empty,

    /// <summary>An archive listing declares a symbolic, hard or copy link entry.</summary>
    LinkEntry,

    /// <summary>The archive needs a password; extraction never prompts for one.</summary>
    Encrypted,

    /// <summary>A ZIP archive contains only directory entries.</summary>
    NoExtractableFiles,

    /// <summary>A ZIP entry is stored as a Unix symbolic link.</summary>
    ZipSymbolicLink,

    /// <summary>A Windows tar listing shows a link entry.</summary>
    TarLinkEntry,

    /// <summary>The format needs 7-Zip or Bandizip and neither was found.</summary>
    ToolUnavailable,

    /// <summary>7-Zip could not list the archive (damaged or header-encrypted). Detail: tool output.</summary>
    SevenZipListingFailed,

    /// <summary>7-Zip exited with an error while extracting. Detail: tool output, if any.</summary>
    SevenZipExtractionFailed,

    /// <summary>Windows tar exited with an error while extracting. Detail: tool output, if any.</summary>
    TarExtractionFailed,

    /// <summary>The Bandizip fallback failed. Detail: the underlying failure message.</summary>
    BandizipFailed,

    /// <summary>An archive tool exceeded its time limit. Detail: the tool's file name.</summary>
    ToolTimeout
}

/// <summary>
/// Tags archive failures with a kind so callers can localize them without matching
/// message text. <see cref="InvalidDataException"/> is sealed, so the kind travels in
/// <see cref="Exception.Data"/> (see <see cref="ErrorTags"/>) and existing
/// exception-type handlers still apply.
/// </summary>
public static class ArchiveErrors
{
    public static InvalidDataException Create(ArchiveErrorKind kind, string message, string? detail = null, Exception? inner = null)
    {
        return Tag(new InvalidDataException(message, inner), kind, detail);
    }

    public static TException Tag<TException>(TException exception, ArchiveErrorKind kind, string? detail = null)
        where TException : Exception
        => ErrorTags.Tag(exception, kind, detail);

    public static bool Is(Exception exception, ArchiveErrorKind kind) => KindOf(exception) == kind;

    public static ArchiveErrorKind? KindOf(Exception exception) => ErrorTags.KindOf<ArchiveErrorKind>(exception);

    public static string? DetailOf(Exception exception) => ErrorTags.DetailOf(exception);
}
