namespace IntegratedModManager.Core.Online;

public static class OnlineBrowsePolicy
{
    public static string CategoryPageUrl(string categoryId, int page, int pageSize, int authorId = 0)
    {
        if (!long.TryParse(categoryId, out long category) || category <= 0)
            throw new ArgumentException("A positive category ID is required.", nameof(categoryId));
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (authorId < 0) throw new ArgumentOutOfRangeException(nameof(authorId));
        return $"https://gamebanana.com/apiv11/Mod/Index?_aFilters%5BGeneric_Category%5D={category}"
            + (authorId > 0 ? $"&_aFilters%5BGeneric_Submitter%5D={authorId}" : string.Empty)
            + $"&_nPerpage={Math.Clamp(pageSize, 1, 50)}&_nPage={page}";
    }
}

public enum OnlineDuplicateKind { None, SameFile, OtherFile, UnknownFile }

public static class OnlineDuplicatePolicy
{
    // A website entry can contain several variants. Names and byte lengths are not file identities.
    public static OnlineDuplicateKind Classify(string? requestedFileId, IEnumerable<string?> installedFileIds)
    {
        string[] installed = installedFileIds.Select(id => id?.Trim() ?? string.Empty).ToArray();
        if (installed.Length == 0) return OnlineDuplicateKind.None;
        string requested = requestedFileId?.Trim() ?? string.Empty;
        if (requested.Length == 0) return OnlineDuplicateKind.UnknownFile;
        if (installed.Contains(requested, StringComparer.Ordinal)) return OnlineDuplicateKind.SameFile;
        return installed.Any(id => id.Length == 0) ? OnlineDuplicateKind.UnknownFile : OnlineDuplicateKind.OtherFile;
    }
}
