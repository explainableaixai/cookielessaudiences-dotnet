using System.Text.Json.Nodes;
using AlphaQuantum.CookielessAudiences;
using Xunit;

public class LabelsTests
{
    [Fact]
    public void LabelsResolveAndKeepUnknownCodes()
    {
        var r = (JsonObject)JsonNode.Parse("""{"interests":{"tier1":["INT.a"]},"purchase_intent":{"codes":["PI.b","PI.c"]},"labels":{"INT.a":"A","PI.b":"B"}}""")!;
        Assert.Equal(new[] { "A", "B", "PI.c" }, CookielessAudiencesClient.LabelsFor(r));
    }

    [Fact]
    public void ErrorCarriesStatus()
    {
        var e = new CookielessAudiencesException(403, "x");
        Assert.Equal(403, e.Status);
    }
}
