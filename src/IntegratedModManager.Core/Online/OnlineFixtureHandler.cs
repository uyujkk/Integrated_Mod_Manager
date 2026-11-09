using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IntegratedModManager.Core.Online;

/// <summary>
/// The app's HTTP client. Normally a plain <see cref="HttpClient"/> as always; with the test-only environment variable
/// <see cref="FixturesVariable"/> set to a folder, every request is answered from the synthetic samples in that folder
/// and nothing goes to the network (stage J6: screenshots and UI tests of the Online page).
/// </summary>
public static class OnlineHttpClientFactory
{
    /// <summary>Test fixture mode, for screenshots and UI tests only: the folder of synthetic online samples.</summary>
    public const string FixturesVariable = "IMM_ONLINE_FIXTURES";

    /// <summary>The fixture handler for <paramref name="fixturesFolder"/>, or null (the normal HTTP stack) when it is not set.</summary>
    public static OnlineFixtureHandler? FixtureHandler(string? fixturesFolder, Action<string>? log = null)
        => string.IsNullOrWhiteSpace(fixturesFolder) ? null : new OnlineFixtureHandler(fixturesFolder, log);

    public static HttpClient Create(string userAgent, string? fixturesFolder, Action<string>? log = null)
    {
        OnlineFixtureHandler? fixtures = FixtureHandler(fixturesFolder, log);
        var client = fixtures is null ? new HttpClient() : new HttpClient(fixtures);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }
}

/// <summary>
/// Answers requests from a folder of synthetic samples listed in its <c>fixtures.json</c>
/// (<c>{"responses":[{"prefix":"https://…","file":"…","contentType":"…"}]}</c>; the longest matching prefix wins).
/// A request without a sample fails; nothing is ever sent to the network, and the folder is only read.
/// </summary>
public sealed class OnlineFixtureHandler : HttpMessageHandler
{
    public const string ModeName = "Online test fixture mode (测试样例模式)";

    private readonly string _folder;
    private readonly Action<string>? _log;
    private readonly IReadOnlyList<Fixture> _fixtures;

    public OnlineFixtureHandler(string folder, Action<string>? log = null)
    {
        _folder = Path.GetFullPath(folder);
        _log = log;
        _fixtures = Load(_folder);
    }

    /// <summary>The requests answered or refused so far, for tests.</summary>
    public IReadOnlyCollection<string> Requests => _requests;
    private readonly List<string> _requests = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
        lock (_requests) _requests.Add(url);
        Fixture? fixture = _fixtures.Where(item => url.StartsWith(item.Prefix, StringComparison.Ordinal))
            .OrderByDescending(item => item.Prefix.Length).FirstOrDefault();
        string? path = fixture is null ? null : Path.GetFullPath(Path.Combine(_folder, fixture.File));
        if (fixture is null || path is null || !IsInside(path, _folder) || !File.Exists(path))
        {
            _log?.Invoke($"{ModeName}: no sample for {url}; the request fails and is not sent.");
            throw new HttpRequestException($"{ModeName}: no sample for {url}");
        }

        byte[] body = await File.ReadAllBytesAsync(path, cancellationToken);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(fixture.ContentType ?? GuessType(path));
        return response;
    }

    private static string GuessType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };

    private static bool IsInside(string path, string folder)
        => path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<Fixture> Load(string folder)
    {
        string index = Path.Combine(folder, "fixtures.json");
        if (!File.Exists(index)) return [];
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(index));
        if (!document.RootElement.TryGetProperty("responses", out JsonElement responses) || responses.ValueKind != JsonValueKind.Array) return [];
        return responses.EnumerateArray()
            .Select(item => new Fixture(
                item.TryGetProperty("prefix", out JsonElement prefix) ? prefix.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("file", out JsonElement file) ? file.GetString() ?? string.Empty : string.Empty,
                item.TryGetProperty("contentType", out JsonElement type) ? type.GetString() : null))
            .Where(item => item.Prefix.Length > 0 && item.File.Length > 0)
            .ToArray();
    }

    private sealed record Fixture(string Prefix, string File, string? ContentType);
}
