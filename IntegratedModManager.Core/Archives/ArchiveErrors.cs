namespace IntegratedModManager.Core;

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
/// <see cref="Exception.Data"/> and existing exception-type handlers still apply.
/// </summary>
public static class ArchiveErrors
{
    private const string KindKey = "IntegratedModManager.ArchiveErrorKind";
    private const string DetailKey = "IntegratedModManager.ArchiveErrorDetail";

    public static InvalidDataException Create(ArchiveErrorKind kind, string message, string? detail = null, Exception? inner = null)
    {
        return Tag(new InvalidDataException(message, inner), kind, detail);
    }

    public static TException Tag<TException>(TException exception, ArchiveErrorKind kind, string? detail = null)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.Data[KindKey] = kind;
        if (detail is not null)
        {
            exception.Data[DetailKey] = detail;
        }

        return exception;
    }

    public static bool Is(Exception exception, ArchiveErrorKind kind)
    {
        return KindOf(exception) == kind;
    }

    public static ArchiveErrorKind? KindOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[KindKey] is ArchiveErrorKind kind ? kind : null;
    }

    public static string? DetailOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[DetailKey] as string;
    }
}
