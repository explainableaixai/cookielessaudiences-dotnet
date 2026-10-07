using System.Text.Json.Nodes;

namespace AlphaQuantum.CookielessAudiences;

/// <summary>Thrown when the JSON body carries a status other than 200.</summary>
public sealed class CookielessAudiencesException : Exception
{
    public int Status { get; }
    public JsonNode? Body { get; }

    public CookielessAudiencesException(int status, string message, JsonNode? body = null) : base($"[{status}] {message}")
    {
        Status = status;
        Body = body;
    }
}

public sealed class CookielessAudiencesClient : IDisposable
{
    private const string Base = "https://www.cookielessaudiences.com";
    private readonly string _apiKey;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    private static readonly Dictionary<int, string> StatusText = new()
    {
        [400] = "Bad request, check the parameters",
        [401] = "Invalid API key",
        [403] = "Key not active or monthly credits used up",
        [407] = "Missing data_type, must be url or text",
        [410] = "Not enough content in the page or text",
        [411] = "The URL content could not be fetched",
        [500] = "General error, check the request or contact support",
    };

    public CookielessAudiencesClient(string apiKey, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("An API key is required", nameof(apiKey));
        _apiKey = apiKey;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("cookielessaudiences-dotnet/1.0.0 (+https://www.cookielessaudiences.com)");
    }

    private async Task<JsonObject> PostAsync(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        using var res = await _http.PostAsync(Base + path, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var node = JsonNode.Parse(text) as JsonObject
            ?? throw new CookielessAudiencesException((int)res.StatusCode, "Response was not a JSON object");
        var status = node["status"]?.GetValue<int>() ?? 200;
        if (status != 200)
            throw new CookielessAudiencesException(status, StatusText.GetValueOrDefault(status, "API error"), node);
        return node;
    }

    /// <summary>Page-level audience segmentation. Set structured to false for the legacy free-text shape.</summary>
    public Task<JsonObject> SegmentAsync(string url, bool structured = true, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["query"] = url, ["api_key"] = _apiKey };
        if (structured) form["format"] = "structured";
        return PostAsync("/api/audience/segment.php", form, ct);
    }

    /// <summary>IAB content categorization of a URL.</summary>
    public Task<JsonObject> CategorizeAsync(string url, bool confidence = true, bool rootFallback = false, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["query"] = url, ["api_key"] = _apiKey, ["data_type"] = "url" };
        if (confidence) form["confidence"] = "1";
        if (rootFallback) form["use_domain_as_basis_of_categorization_for_insufficient_subdomain_content"] = "1";
        return PostAsync("/api/iab/iab_web_content_filtering.php", form, ct);
    }

    /// <summary>IAB content categorization of plain text.</summary>
    public Task<JsonObject> CategorizeTextAsync(string text, bool confidence = true, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["query"] = text, ["api_key"] = _apiKey, ["data_type"] = "text" };
        if (confidence) form["confidence"] = "1";
        return PostAsync("/api/iab/iab_content_filtering.php", form, ct);
    }

    /// <summary>Public vocabularies, no API key needed.</summary>
    public static async Task<JsonObject> VocabulariesAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var text = await http.GetStringAsync(Base + "/api/audience/filters.php", ct).ConfigureAwait(false);
        return (JsonObject)JsonNode.Parse(text)!;
    }

    /// <summary>Readable names for the INT.* and PI.* codes of a structured response.</summary>
    public static List<string> LabelsFor(JsonObject result)
    {
        var names = result["labels"] as JsonObject;
        var output = new List<string>();
        foreach (var group in new[] { "interests", "purchase_intent" })
        {
            if (result[group] is not JsonObject block) continue;
            foreach (var key in new[] { "tier1", "tier2", "codes" })
            {
                if (block[key] is not JsonArray list) continue;
                foreach (var item in list)
                {
                    var code = item?.GetValue<string>();
                    if (code is null) continue;
                    output.Add(names?[code]?.GetValue<string>() ?? code);
                }
            }
        }
        return output;
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
