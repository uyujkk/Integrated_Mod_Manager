using System.Text.RegularExpressions;

namespace IntegratedModManager.Core;

public static class UpdateChecksumParser
{
    public static string ParseSha256(string checksumText, string packageFileName, string checksumFileName)
    {
        ArgumentNullException.ThrowIfNull(checksumText);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksumFileName);

        foreach (string rawLine in checksumText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            Match common = Regex.Match(line, "^(?<hash>[0-9a-fA-F]{64})\\s+[*]?(?<name>.+?)\\s*$");
            if (common.Success
                && string.Equals(Path.GetFileName(common.Groups["name"].Value.Trim()), packageFileName, StringComparison.OrdinalIgnoreCase))
            {
                return common.Groups["hash"].Value;
            }

            Match bsd = Regex.Match(line, "^SHA256\\s*\\((?<name>.+)\\)\\s*=\\s*(?<hash>[0-9a-fA-F]{64})$", RegexOptions.IgnoreCase);
            if (bsd.Success
                && string.Equals(Path.GetFileName(bsd.Groups["name"].Value.Trim()), packageFileName, StringComparison.OrdinalIgnoreCase))
            {
                return bsd.Groups["hash"].Value;
            }
        }

        Match singleHash = Regex.Match(checksumText.Trim(), "^[0-9a-fA-F]{64}$");
        if (singleHash.Success
            && string.Equals(checksumFileName, packageFileName + ".sha256", StringComparison.OrdinalIgnoreCase))
        {
            return singleHash.Value;
        }

        throw new InvalidDataException("The checksum file does not contain a SHA-256 entry for " + packageFileName + ".");
    }
}
