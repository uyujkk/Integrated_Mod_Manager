using System.Text;

namespace IntegratedModManager.Core;

public sealed record OnlineMetadataCacheEntry(GameBananaDetails Value, DateTimeOffset CachedAtUtc);

public interface IOnlineMetadataCache
{
    Task<OnlineMetadataCacheEntry?> ReadAsync(int itemId, CancellationToken cancellationToken);
    Task WriteAsync(int itemId, OnlineMetadataCacheEntry entry, CancellationToken cancellationToken);
}

public sealed class GameBananaMetadataService
{
    public const int DefaultMaxBytes = 4 * 1024 * 1024;
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(3);
    private const string DetailsFields = "name,description,text,screenshots,Preview().sSubFeedImageUrl()";
    private const string CardFields = "name,Category().name,catid,RootCategory().name,likes,views,downloads,Owner().name,mdate,Preview().sSubFeedImageUrl(),Url().sProfileUrl(),Url().sDownloadUrl(),Files().aFiles(),Updates().bSubmissionHasUpdates()";
    private readonly HttpClient _client;
    private readonly IOnlineMetadataCache? _cache;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _requestTimeout;
    private readonly int _maxBytes;
    private readonly int _memoryCapacity;
    private readonly Action<string, Exception>? _diagnostics;
    private readonly object _sync = new();
    private readonly Dictionary<int, OnlineMetadataCacheEntry> _memory = [];

    public GameBananaMetadataService(HttpClient client, IOnlineMetadataCache? cache = null,
        TimeProvider? clock = null, TimeSpan? requestTimeout = null, int maxBytes = DefaultMaxBytes,
        int memoryCapacity = 128, Action<string, Exception>? diagnostics = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cache = cache;
        _clock = clock ?? TimeProvider.System;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (_requestTimeout <= TimeSpan.Zero || _requestTimeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        if (maxBytes <= 0 || maxBytes > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (memoryCapacity <= 0 || memoryCapacity > 4096) throw new ArgumentOutOfRangeException(nameof(memoryCapacity));
        _maxBytes = maxBytes;
        _memoryCapacity = memoryCapacity;
        _diagnostics = diagnostics;
    }

    public async Task<GameBananaDetails> GetDetailsAsync(int itemId, CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        ValidateId(itemId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!forceRefresh)
        {
            lock (_sync)
                if (_memory.TryGetValue(itemId, out OnlineMetadataCacheEntry? memory) && IsFresh(memory)) return memory.Value.Copy();
            OnlineMetadataCacheEntry? disk = null;
            if (_cache is not null)
            {
                try { disk = await _cache.ReadAsync(itemId, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _diagnostics?.Invoke("Metadata cache read", ex); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (disk is not null && IsFresh(disk))
            {
                Remember(itemId, disk);
                return disk.Value.Copy();
            }
        }

        string json = await FetchAsync(itemId, DetailsFields, cancellationToken).ConfigureAwait(false);
        GameBananaDetails details = GameBananaMetadataParser.ParseDetails(json);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = new OnlineMetadataCacheEntry(details, _clock.GetUtcNow());
        if (_cache is not null)
        {
            try { await _cache.WriteAsync(itemId, entry, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { _diagnostics?.Invoke("Metadata cache write", ex); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        Remember(itemId, entry);
        return details.Copy();
    }

    // File lists and update timestamps must remain fresh; no details-cache shortcut here.
    public async Task<GameBananaModMetadata> GetModAsync(int itemId, CancellationToken cancellationToken = default)
    {
        ValidateId(itemId);
        cancellationToken.ThrowIfCancellationRequested();
        string json = await FetchAsync(itemId, CardFields, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return GameBananaMetadataParser.ParseMod(json, itemId);
    }

    private async Task<string> FetchAsync(int itemId, string fields, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        try
        {
            string requestUrl = $"https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={itemId}&fields={Uri.EscapeDataString(fields)}&return_keys=true&format=json_min";
            using HttpResponseMessage response = await _client.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > _maxBytes) throw new InvalidDataException("The metadata response exceeds the size limit.");
            using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            return await ReadBoundedJsonAsync(stream, _maxBytes, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The metadata request timed out.", ex);
        }
    }

    public static async Task<string> ReadBoundedJsonAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxBytes <= 0 || maxBytes > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxBytes + 1 - (int)bytes.Length)), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (bytes.Length + read > maxBytes) throw new InvalidDataException("The metadata response exceeds the size limit.");
            bytes.Write(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        string json = new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
        return json.TrimStart('\uFEFF');
    }

    private bool IsFresh(OnlineMetadataCacheEntry entry)
    {
        TimeSpan age = _clock.GetUtcNow() - entry.CachedAtUtc;
        return entry.Value is not null && entry.Value.IsValid && age >= TimeSpan.Zero && age <= CacheLifetime;
    }

    private void Remember(int itemId, OnlineMetadataCacheEntry entry)
    {
        lock (_sync)
        {
            _memory[itemId] = entry with { Value = entry.Value.Copy() };
            while (_memory.Count > _memoryCapacity || _memory.Values.Sum(value =>
                       2L * (value.Value.Summary.Length + value.Value.Description.Length
                           + value.Value.ImageUrls.Sum(url => (long)url.Length))) > 16 * 1024 * 1024)
                _memory.Remove(_memory.OrderBy(pair => pair.Value.CachedAtUtc).First().Key);
        }
    }

    private static void ValidateId(int itemId)
    {
        if (itemId <= 0) throw new ArgumentOutOfRangeException(nameof(itemId));
    }
}
