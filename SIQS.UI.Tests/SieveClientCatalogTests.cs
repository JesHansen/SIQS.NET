using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using SIQS.UI.Services;

namespace SIQS.UI.Tests;

public sealed class SieveClientCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "siqs-client-catalog", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Runtime_identifiers_map_to_documented_platform_slugs_and_file_names()
    {
        var mappings = SieveClientCatalog.Known
            .Select(client => (client.RuntimeIdentifier, client.Platform, client.FileName))
            .ToArray();

        Assert.Equal(
            [
                ("win-x64", "windows-x64", "qs-sieve-client.exe"),
                ("linux-x64", "linux-x64", "qs-sieve-client"),
                ("linux-arm64", "linux-arm64", "qs-sieve-client"),
                ("osx-x64", "osx-x64", "qs-sieve-client"),
                ("osx-arm64", "osx-arm64", "qs-sieve-client"),
            ],
            mappings);
    }

    [Fact]
    public void Published_discovery_uses_the_same_slug_paths_advertised_by_endpoints()
    {
        var catalog = new SieveClientCatalog(new TestEnvironment(_root));
        var linux = SieveClientCatalog.Find("LINUX-ARM64")!;
        Directory.CreateDirectory(Path.GetDirectoryName(catalog.PathTo(linux))!);
        File.WriteAllText(catalog.PathTo(linux), "test-client");

        Assert.True(catalog.IsPublished(linux));
        Assert.Equal(linux, Assert.Single(catalog.Published));
        Assert.EndsWith(
            Path.Combine("download", "linux-arm64", "qs-sieve-client"),
            catalog.PathTo(linux),
            StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SIQS.UI.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(contentRoot, "wwwroot");
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
