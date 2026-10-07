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

<!--expanded-->
## Using the client inside an enterprise .NET application

Large .NET applications have habits, and a library should fit them instead of fighting them. That means dependency injection, configuration binding, structured logging and cancellation tokens. The client accepts an `HttpClient` in its constructor so the platform can manage handlers, proxies and lifetimes for you. Register it once and inject it wherever it is needed.

A typed options class keeps the key out of code:

```csharp
public sealed class CookielessOptions
{
    public string Key { get; set; } = "";
}

builder.Services.Configure<CookielessOptions>(builder.Configuration.GetSection("Cookieless"));
builder.Services.AddHttpClient("cookieless");
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<CookielessOptions>>().Value;
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("cookieless");
    return new CookielessAudiencesClient(options.Key, http);
});
```

In production the key should come from a secret store such as a managed vault, not from `appsettings.json`. Configuration binding lets the same code run unchanged on a laptop and in a cluster.

## Caching with the memory cache

Audience profiles change slowly, so a cache is the easiest way to cut both latency and cost. `IMemoryCache` is enough for a single instance, and a distributed cache works for a fleet. Wrap the client in a small service:

```csharp
public sealed class AudienceService
{
    private readonly CookielessAudiencesClient _client;
    private readonly IMemoryCache _cache;

    public AudienceService(CookielessAudiencesClient client, IMemoryCache cache)
    {
        _client = client;
        _cache = cache;
    }

    public Task<JsonObject> GetAsync(string url, CancellationToken ct) =>
        _cache.GetOrCreateAsync(url.Trim().ToLowerInvariant(), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(14);
            return await _client.SegmentAsync(url, ct: ct);
        })!;
}
```

Fourteen days is a sensible default for editorial pages. Evergreen reference pages can live far longer, while a news front page may deserve a shorter life.

## A background worker for large lists

Long lists do not belong in a web request. Use a hosted service that reads URLs from a queue, calls the client and writes results to a store. A bounded channel gives you back pressure without extra libraries:

```csharp
public sealed class SegmentWorker : BackgroundService
{
    private readonly Channel<string> _queue;
    private readonly CookielessAudiencesClient _client;
    private readonly ILogger<SegmentWorker> _log;

    public SegmentWorker(Channel<string> queue, CookielessAudiencesClient client, ILogger<SegmentWorker> log)
    {
        _queue = queue; _client = client; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await foreach (var url in _queue.Reader.ReadAllAsync(stop))
        {
            try { await Save(url, await _client.SegmentAsync(url, ct: stop)); }
            catch (CookielessAudiencesException ex) { _log.LogWarning("{Url} failed with {Status}", url, ex.Status); }
        }
    }
}
```

Run several workers if you need more throughput, and keep the total below the limits of your plan.

## Logging and metrics

Write one structured log entry per call with the URL host, the status and the elapsed time. Do not log full responses at information level, because they are large and add noise. Count outcomes by status code. A steady trickle of 410 is normal, since some pages have no content. A sudden wave of 403 means the key or the credits need attention. Emit those counters through your usual metrics library and alert on the ones that call for human action.

## Testing without a network

The client accepts any `HttpClient`, so a fake `HttpMessageHandler` is all you need. Return a canned body with `"status": 200` for the happy path, and bodies with other statuses to check your error handling. Keep one integration test that calls the public vocabularies method. It requires no key and costs nothing, and it will tell you if the service has changed shape.

## How audience data fits the wider business

An audience profile is useful to more people than the team that wired it up. Planners read the personas. Finance reads the credit counts. Privacy teams read the fact that no cookies or identifiers are involved. The [ad inventory curation feature page](https://www.cookielessaudiences.com/features/ad-inventory-curation.php) shows how coded audience attributes turn a list of domains into a package a buyer can understand.

If your company also builds software for recruiters, the [French schema reference of the resume parser](https://www.resumereaderapi.com/api-v2-fr.php) is a good example of how an API can offer localized field names without changing values. If your corporate development group screens acquisition targets, the page on [family office deal sourcing](https://www.acquisitionuniverse.com/for/family-offices.php) describes how long tail companies are found when databases stop returning new names.

## Troubleshooting

**`CookielessAudiencesException` with status 403.** The key is not active or the credits are gone. Check the account page before debugging code.

**`JsonException` while parsing.** A proxy or captive portal returned HTML. Inspect the raw response in a debugger.

**Slow calls.** Pages differ in how long they take to load. Raise the timeout on your `HttpClient` for batch jobs, and keep user facing calls on a shorter leash.

**Different results on two machines.** Compare `vocab_version`. Results should match when the version and the page match.

<!--extra-->
## Release checklist for your integration

Before you ship, confirm six things. The key lives in a secret store and never in source control. The `HttpClient` comes from the factory. Every call receives a cancellation token. Failures are logged with the status and the host but never with the full response. Answers are cached by normalized URL with a lifetime that suits your content. And a dashboard shows credits used and the count of each error status, so a rising 403 count wakes someone up before a customer notices.

One more habit is worth adopting early. Keep a small console tool in the solution that segments a single URL and prints the whole response. When a user asks why a page was described a certain way, a developer can answer in a minute by running it, and that shortens every conversation about data quality.

<!--further-->
## Further reading and practical notes

The [official .NET documentation](https://learn.microsoft.com/en-us/dotnet/) covers `HttpClient`, dependency injection, the options pattern, hosted services and `System.Text.Json`, all of which appear in the examples above.

A few practical notes help when you move from a prototype to a service. Reuse the `HttpClient` through the factory so that sockets and DNS changes are handled for you, and never create a new one per call. Keep the timeout on the `HttpClient` generous for batch work and short for user facing calls, and make that difference explicit with two named clients. Pass the cancellation token from the request all the way to the client call, so that an abandoned request stops costing credits. Finally, treat the `JsonObject` as an edge type. Map it into your own records near the boundary, and let the rest of the application work with types that have names and meaning.

For the audience side, remember that the answer describes the page and its predictable readers. It is not a statement about the person who happens to be on your site at the moment. That is why no consent flow is needed, and also why the data should be used for planning and analysis, not for decisions about an individual.

## Support

Report client problems on the repository. For questions about plans, limits and the service itself, write to info@alpha-quantum.com.

## FAQ

**Is it thread safe?** Yes. Calls are independent.

**Can I run it on .NET Framework?** The package targets .NET 8.

**Pricing?** See [ABM account profiling](https://www.cookielessaudiences.com/use-cases/abm-account-profiling.php) for a use case and the plans page for tiers.

MIT license. info@alpha-quantum.com
