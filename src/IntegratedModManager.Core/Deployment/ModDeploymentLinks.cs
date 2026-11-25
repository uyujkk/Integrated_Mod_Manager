namespace IntegratedModManager.Core.Deployment;

public static class ModDeploymentLinks
{
    /// <summary>
    /// True only when <paramref name="deployed"/> is a directory link to <paramref name="source"/>, as stored or through
    /// links in either path (see <see cref="DirectoryLinkDeployment.PointsTo"/>).
    /// </summary>
    public static bool IsLinkTo(string deployed, string source)
    {
        try
        {
            return DirectoryLinkDeployment.PointsTo(deployed, source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
