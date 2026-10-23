namespace IntegratedModManager.Core;

public enum ArchiveErrorKind
{
    /// <summary>The extracted archive has no content other than tool/OS clutter.</summary>
    Empty,

    /// <summary>An archive listing declares a symbolic, hard or copy link entry.</summary>
    LinkEntry,

    /// <summary>The archive needs a password; extraction never prompts for one.</summary>
    Encrypted
}

/// <summary>
/// Tags archive failures with a kind so callers can localize them without matching
/// message text. <see cref="InvalidDataException"/> is sealed, so the kind travels in
/// <see cref="Exception.Data"/> and existing InvalidDataException handlers still apply.
/// </summary>
public static class ArchiveErrors
{
    private const string KindKey = "IntegratedModManager.ArchiveErrorKind";

    public static InvalidDataException Create(ArchiveErrorKind kind, string message)
    {
        var exception = new InvalidDataException(message);
        exception.Data[KindKey] = kind;
        return exception;
    }

    public static bool Is(Exception exception, ArchiveErrorKind kind)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[KindKey] is ArchiveErrorKind actual && actual == kind;
    }
}
