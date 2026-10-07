# AlphaQuantum.CookielessAudiences for .NET

Get the audience behind any web page from C#. The client sends a URL to the Cookieless Audiences API and returns demographics, interests, purchase intent, B2B context and personas as `JsonObject`. No cookies are used and no personal data is involved.

Targets .NET 8. One dependency-free class.

## Install

```bash
dotnet add package AlphaQuantum.CookielessAudiences
```

## First request

```csharp
using AlphaQuantum.CookielessAudiences;

using var client = new CookielessAudiencesClient(Environment.GetEnvironmentVariable("COOKIELESS_KEY")!);
var page = await client.SegmentAsync("https://example.com/blog");

Console.WriteLine(page["audience_type"]);
Console.WriteLine(string.Join(", ", CookielessAudiencesClient.LabelsFor(page)));
```

## Methods

| Method | Result |
|---|---|
| `SegmentAsync(url, structured: true)` | audience profile in the coded v2 shape |
| `CategorizeAsync(url, confidence, rootFallback)` | IAB v3 and v2 categories |
| `CategorizeTextAsync(text)` | IAB categories for text |
| `VocabulariesAsync()` | static, all allowed values, no key |
| `LabelsFor(result)` | static, readable names for codes |

All calls accept a `CancellationToken`.

## Exceptions

```csharp
try
{
    await client.SegmentAsync(url);
}
catch (CookielessAudiencesException ex) when (ex.Status == 403)
{
    logger.LogWarning("Key inactive or credits used up");
}
catch (CookielessAudiencesException ex) when (ex.Status is 410 or 411)
{
    logger.LogInformation("Page unreadable, skipping {Url}", url);
}
```

`ex.Body` keeps the full JSON in case you need it for a support request.

## Dependency injection

Pass a shared `HttpClient` so sockets are pooled:

```csharp
builder.Services.AddHttpClient<CookielessAudiencesClient>();
builder.Services.AddSingleton(sp =>
    new CookielessAudiencesClient(builder.Configuration["Cookieless:Key"]!, sp.GetRequiredService<IHttpClientFactory>().CreateClient()));
```

## Process a list in parallel

```csharp
var urls = File.ReadLines("sites.txt").ToList();
var results = new ConcurrentDictionary<string, string>();

await Parallel.ForEachAsync(urls, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (url, ct) =>
{
    try
    {
        var page = await client.SegmentAsync(url, ct: ct);
        results[url] = page["audience_type"]?.ToString() ?? "unknown";
    }
    catch (CookielessAudiencesException ex)
    {
        results[url] = $"error {ex.Status}";
    }
});
```

## Publisher side

If you sell advertising, the profile helps you tell buyers who reads your pages. The [industry page for DSPs](https://www.cookielessaudiences.com/industries/dsps.php) shows how buy-side platforms use the same fields when they choose supply.

## Why a library and not raw HTTP

The audience endpoint wants form-encoded bodies while most JSON clients assume JSON. The IAB endpoint uses a different path for text. And the HTTP status is not the signal. This class hides those three quirks.

## FAQ

**Is it thread safe?** Yes. Calls are independent.

**Can I run it on .NET Framework?** The package targets .NET 8.

**Pricing?** See [ABM account profiling](https://www.cookielessaudiences.com/use-cases/abm-account-profiling.php) for a use case and the plans page for tiers.

MIT license. info@alpha-quantum.com
