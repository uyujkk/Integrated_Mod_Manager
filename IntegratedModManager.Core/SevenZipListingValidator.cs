namespace IntegratedModManager.Core;

/// <summary>
/// Validates the technical listing produced by <c>7z l -slt</c> before extraction.
/// Empty link properties are normal in recent RAR listings and must not be treated as links.
/// </summary>
public static class SevenZipListingValidator
{
    public static void Validate(string listing, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        bool readingEntries = false;
        foreach (string rawLine in listing.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("----------", StringComparison.Ordinal))
            {
                readingEntries = true;
                continue;
            }

            if (!readingEntries)
            {
                continue;
            }

            if (TryReadProperty(line, "Path", out string path))
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new InvalidDataException("The archive contains an entry with an empty path.");
                }

                PathSafety.ResolveInsideDirectory(
                    destinationDirectory,
                    path.Replace('/', Path.DirectorySeparatorChar));
                continue;
            }

            if ((TryReadProperty(line, "Symbolic Link", out string symbolicLink) && IsPopulated(symbolicLink))
                || (TryReadProperty(line, "Hard Link", out string hardLink) && IsPopulated(hardLink))
                || (TryReadProperty(line, "Copy Link", out string copyLink) && IsPopulated(copyLink))
                || (TryReadProperty(line, "Mode", out string mode) && IsUnixSymbolicLinkMode(mode)))
            {
                throw new InvalidDataException("The archive contains an unsupported link.");
            }
        }
    }

    private static bool TryReadProperty(string line, string propertyName, out string value)
    {
        value = string.Empty;
        if (!line.StartsWith(propertyName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> remainder = line.AsSpan(propertyName.Length).TrimStart();
        if (remainder.IsEmpty || remainder[0] != '=')
        {
            return false;
        }

        value = remainder[1..].Trim().ToString();
        return true;
    }

    private static bool IsPopulated(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, "-", StringComparison.Ordinal);
    }

    private static bool IsUnixSymbolicLinkMode(string mode)
    {
        string trimmed = mode.Trim();
        return trimmed.Length > 0 && char.ToLowerInvariant(trimmed[0]) == 'l';
    }
}
