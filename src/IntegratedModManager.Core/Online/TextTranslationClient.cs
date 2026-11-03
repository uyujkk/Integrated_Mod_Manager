using System.Text;
using System.Text.Json;

namespace IntegratedModManager.Core.Online;

/// <summary>
/// Machine translation of Mod descriptions and character names through the public translate endpoint.
/// Text that cannot be translated comes back unchanged.
/// </summary>
public static class TextTranslationClient
{
    public const int MaxChunkLength = 2500;

    /// <summary>Translates paragraph by paragraph in chunks the endpoint accepts, keeping blank lines between them.</summary>
    public static async Task<string> TranslateLongTextAsync(HttpClient client, string text, string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (string chunk in SplitForTranslation(text, MaxChunkLength))
        {
            string translatedChunk = await TranslateAsync(client, chunk, targetLanguage, cancellationToken);
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(translatedChunk.Trim());
        }

        return builder.ToString().Trim();
    }

    /// <summary>One request; the sentences of the reply joined back together.</summary>
    public static async Task<string> TranslateAsync(HttpClient client, string text, string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        string requestUrl = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLanguage)}&dt=t&q={Uri.EscapeDataString(text)}";
        using HttpResponseMessage response = await client.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array)
        {
            return text;
        }

        var builder = new StringBuilder();
        foreach (JsonElement sentence in root[0].EnumerateArray())
        {
            if (sentence.ValueKind == JsonValueKind.Array && sentence.GetArrayLength() > 0 && sentence[0].ValueKind == JsonValueKind.String)
            {
                builder.Append(sentence[0].GetString());
            }
        }

        return string.IsNullOrWhiteSpace(builder.ToString()) ? text : builder.ToString();
    }

    /// <summary>Paragraphs packed into chunks of at most <paramref name="maxChunkLength"/>; a longer paragraph is cut.</summary>
    public static List<string> SplitForTranslation(string text, int maxChunkLength)
    {
        List<string> chunks = [];
        string[] paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.None);
        var builder = new StringBuilder();

        foreach (string paragraph in paragraphs)
        {
            string current = paragraph.Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            if (builder.Length > 0 && builder.Length + current.Length + 2 > maxChunkLength)
            {
                chunks.Add(builder.ToString());
                builder.Clear();
            }

            if (current.Length > maxChunkLength)
            {
                if (builder.Length > 0)
                {
                    chunks.Add(builder.ToString());
                    builder.Clear();
                }

                for (int index = 0; index < current.Length; index += maxChunkLength)
                {
                    chunks.Add(current.Substring(index, Math.Min(maxChunkLength, current.Length - index)));
                }
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }
            builder.Append(current);
        }

        if (builder.Length > 0)
        {
            chunks.Add(builder.ToString());
        }

        return chunks.Count == 0 ? [text] : chunks;
    }
}
