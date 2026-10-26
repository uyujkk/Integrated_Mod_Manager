namespace IntegratedModManager.Core.Infrastructure;

/// <summary>
/// Attaches a typed error kind (and optional detail text) to an exception through
/// <see cref="Exception.Data"/>, so the UI can localize Core failures without matching
/// message text and without changing the exception type callers already handle.
/// Each kind enum gets its own key, so kinds from different areas never collide.
/// </summary>
public static class ErrorTags
{
    private const string DetailKey = "IntegratedModManager.ErrorDetail";

    public static TException Tag<TException, TKind>(TException exception, TKind kind, string? detail = null)
        where TException : Exception
        where TKind : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.Data[KindKey<TKind>()] = kind;
        if (detail is not null)
        {
            exception.Data[DetailKey] = detail;
        }

        return exception;
    }

    public static TKind? KindOf<TKind>(Exception exception)
        where TKind : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[KindKey<TKind>()] is TKind kind ? kind : null;
    }

    public static string? DetailOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[DetailKey] as string;
    }

    private static string KindKey<TKind>() => "IntegratedModManager.ErrorKind." + typeof(TKind).FullName;
}
