using System.Globalization;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core;

namespace ModFolderCopier.WinUI;

/// <summary>New raw cache namespace; old UI-derived cache entries remain untouched.</summary>
public sealed class OnlineMetadataCacheAdapter(AppDataStore store, string directory,
    Action<string, Exception>? diagnostics = null) : IOnlineMetadataCache
{
    private const string Kind = "online-details-raw-v1";

    public async Task<OnlineMetadataCacheEntry?> ReadAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string key = Key(itemId);
        CachedValue<GameBananaDetails>? cached = store.TryReadCache<GameBananaDetails>(Kind, key, GameBananaMetadataService.CacheLifetime);
        if (cached?.Value.IsValid == true) return new(cached.Value, cached.CachedAtUtc);
        foreach (string kind in new[] { Kind, "online-details" })
        {
            if (kind != Kind)
            {
                cached = store.TryReadCache<GameBananaDetails>(kind, key, GameBananaMetadataService.CacheLifetime);
                if (cached?.Value.IsValid == true) return new(cached.Value, cached.CachedAtUtc);
            }
            string file = Path.Combine(directory, kind == Kind ? $"metadata-mod-{key}.json" : $"mod-{key}.json");
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length > GameBananaMetadataService.DefaultMaxBytes) continue;
                DateTimeOffset stamp = new(info.LastWriteTimeUtc);
                if (DateTimeOffset.UtcNow - stamp > GameBananaMetadataService.CacheLifetime) continue;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                string json = await GameBananaMetadataService.ReadBoundedJsonAsync(stream, GameBananaMetadataService.DefaultMaxBytes, cancellationToken);
                GameBananaDetails? value = JsonSerializer.Deserialize<GameBananaDetails>(json);
                if (value?.IsValid == true) return new(value, stamp);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { diagnostics?.Invoke("Metadata file cache read", ex); }
        }
        return null;
    }

    public async Task WriteAsync(int itemId, OnlineMetadataCacheEntry entry, CancellationToken cancellationToken)
    {
        string key = Key(itemId);
        if (!entry.Value.IsValid) throw new ArgumentException("Invalid metadata cache entry.", nameof(entry));
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry.Value));
        if (bytes.Length > GameBananaMetadataService.DefaultMaxBytes)
            throw new InvalidDataException("The metadata cache exceeds the size limit.");
        string? partial = null;
        bool ownsPartial = false;
        try
        {
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, $"metadata-mod-{key}.json");
            partial = Path.Combine(directory, $".metadata-{Guid.NewGuid():N}.download");
            using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                ownsPartial = true;
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, file, overwrite: true);
            partial = null;
            File.SetLastWriteTimeUtc(file, entry.CachedAtUtc.UtcDateTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { diagnostics?.Invoke("Metadata file cache write", ex); }
        finally
        {
            if (ownsPartial && partial is not null)
                try { File.Delete(partial); }
                catch (Exception ex) { diagnostics?.Invoke("Metadata cache partial cleanup", ex); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        store.WriteCache(Kind, key, entry.Value, entry.CachedAtUtc);
    }

    private static string Key(int itemId) => itemId > 0 ? itemId.ToString(CultureInfo.InvariantCulture)
        : throw new ArgumentOutOfRangeException(nameof(itemId));
}
