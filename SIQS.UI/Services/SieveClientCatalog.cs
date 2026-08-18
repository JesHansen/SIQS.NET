namespace SIQS.UI.Services;

/// <summary>One downloadable sieve-client build, named by the platform slug used in its download URL.</summary>
/// <param name="Platform">URL slug, e.g. <c>linux-arm64</c>.</param>
/// <param name="RuntimeIdentifier">The .NET RID <c>build-ui.ps1</c> publishes for this platform.</param>
/// <param name="FileName">The published executable's name.</param>
/// <param name="DisplayName">How the platform is written in the UI.</param>
public sealed record SieveClient(string Platform, string RuntimeIdentifier, string FileName, string DisplayName)
{
    /// <summary>Whether a worker has to <c>chmod +x</c> the file before running it.</summary>
    public bool NeedsExecutableBit => !FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The self-contained sieve clients this deployment can hand to a worker. The executables are not
/// in source control: <c>build-ui.ps1</c> (or a <c>dotnet publish</c> of the UI) writes them into
/// <c>download/</c> under the content root. A server started with <c>dotnet run</c> therefore has
/// none of them, so whether a client is actually present is a question the UI and the download
/// endpoint both have to ask rather than assume.
/// </summary>
public sealed class SieveClientCatalog(IWebHostEnvironment environment)
{
    private readonly string _downloadRoot = Path.Combine(environment.ContentRootPath, "download");

    /// <summary>
    /// Every platform the download endpoint knows about. The two x64 clients are what
    /// <c>build-ui.ps1</c> publishes by default; the rest are built on request with
    /// <c>build-ui.ps1 -Runtimes</c>, and are offered here only once they exist on disk.
    /// </summary>
    public static IReadOnlyList<SieveClient> Known { get; } =
    [
        new("windows-x64", "win-x64", "qs-sieve-client.exe", "Windows x64"),
        new("linux-x64", "linux-x64", "qs-sieve-client", "Linux x64"),
        new("linux-arm64", "linux-arm64", "qs-sieve-client", "Linux arm64"),
        new("osx-x64", "osx-x64", "qs-sieve-client", "macOS x64 (Intel)"),
        new("osx-arm64", "osx-arm64", "qs-sieve-client", "macOS arm64 (Apple silicon)"),
    ];

    /// <summary>The default platform served by the bare <c>/api/dist/client</c> URL.</summary>
    public static SieveClient Default => Known[0];

    public static SieveClient? Find(string platform)
        => Known.FirstOrDefault(client => string.Equals(client.Platform, platform, StringComparison.OrdinalIgnoreCase));

    /// <summary>The published executable's path, whether or not it exists.</summary>
    public string PathTo(SieveClient client)
        => Path.Combine(_downloadRoot, client.Platform, client.FileName);

    public bool IsPublished(SieveClient client) => File.Exists(PathTo(client));

    /// <summary>The clients this deployment can actually serve right now.</summary>
    public IReadOnlyList<SieveClient> Published => Known.Where(IsPublished).ToArray();
}
