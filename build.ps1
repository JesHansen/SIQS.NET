[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$SkipTests,

    # Sieve clients to publish. The two x64 targets are the default because they are what most
    # volunteer machines run and because each extra runtime adds a self-contained publish to the
    # build. The sieve's AVX2 kernels have scalar fallbacks, so the arm64 and macOS targets build
    # and run — they are simply not published unless asked for.
    [ValidateSet("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string[]]$Runtimes = @("win-x64", "linux-x64")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    Write-Host "> dotnet $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

# The download URL and folder use "windows-x64" where the .NET RID is "win-x64"; every other
# platform's slug and RID are already the same string. SieveClientCatalog holds the other half of
# this mapping, and the two must agree or the UI will advertise a file the endpoint cannot find.
function Get-PlatformSlug {
    param([Parameter(Mandatory)][string]$RuntimeIdentifier)

    if ($RuntimeIdentifier -eq "win-x64") { return "windows-x64" }
    return $RuntimeIdentifier
}

function Publish-SieveClient {
    param(
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][string]$OutputDirectory
    )

    Invoke-Dotnet @("restore", "QS.SieveClient/QS.SieveClient.csproj", "-r", $RuntimeIdentifier)
    Invoke-Dotnet @(
        "publish", "QS.SieveClient/QS.SieveClient.csproj", "-c", $Configuration,
        "-r", $RuntimeIdentifier, "--self-contained", "true",
        "-p:PublishSingleFile=true", "-p:DebugType=None", "-p:DebugSymbols=false",
        "-o", $OutputDirectory, "--no-restore"
    )
}

Invoke-Dotnet @("clean", "SIQS.slnx", "-c", $Configuration)
Invoke-Dotnet @("restore", "SIQS.slnx")
Invoke-Dotnet @("build", "SIQS.slnx", "-c", $Configuration, "--no-restore")

# The UI endpoint serves these executables directly. Publish them before the UI package so that the
# development server and the deployed application offer the same set of client downloads.
foreach ($runtime in $Runtimes) {
    Publish-SieveClient -RuntimeIdentifier $runtime -OutputDirectory "SIQS.UI/download/$(Get-PlatformSlug $runtime)"
}

if (!$SkipTests) {
    Invoke-Dotnet @("test", "--solution", "SIQS.slnx", "-c", $Configuration, "--no-build", "--no-restore")
}

# UI publishing copies the self-contained distributed sieve client into the deployed application.
Invoke-Dotnet @("publish", "SIQS.UI/SIQS.UI.csproj", "-c", $Configuration, "--no-restore", "-p:SkipSieveClientPublish=true")
