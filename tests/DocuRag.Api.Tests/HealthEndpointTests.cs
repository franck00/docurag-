using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DocuRag.Api.Tests;

/// <summary>
/// Smoke test : démarre réellement l'API (tout le câblage DI/host) et vérifie
/// l'endpoint /health, qui ne dépend d'aucun service Azure. Prouve que l'application
/// boote sans erreur de configuration ou de résolution de dépendances.
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
    }
}
