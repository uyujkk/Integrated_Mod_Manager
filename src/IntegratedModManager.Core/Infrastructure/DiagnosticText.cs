using System.Text.RegularExpressions;

namespace IntegratedModManager.Core.Infrastructure;

/// <summary>
/// Text for the error log and diagnostic reports (stage J finish, step 8a: moved from the window unchanged): local paths,
/// secrets in "key=value" pairs and secret query parameters are replaced, so a report can be shared.
/// </summary>
public static class DiagnosticText
{
    public static string Sanitize(string text)
    {
        text = Regex.Replace(text, @"(?i)(?:[a-z]:\\|\\\\)[^\r\n]+", "<private-path>");
        text = Regex.Replace(text, @"(?i)(token|authorization|password|secret|api[_-]?key)\s*[:=]\s*\S+", "$1=<redacted>");
        text = Regex.Replace(text, @"(?i)([?&](?:token|key|auth|signature)=)[^&\s]+", "$1<redacted>");
        return text;
    }
}
