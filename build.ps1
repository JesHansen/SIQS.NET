[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$SkipTests,

    # Command-line release targets. Windows and Linux x64 are the distributable defaults.
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

# Use a readable artifact folder name while retaining .NET runtime identifiers for publishing.
function Get-PlatformSlug {
    param([Parameter(Mandatory)][string]$RuntimeIdentifier)

    if ($RuntimeIdentifier -eq "win-x64") { return "windows-x64" }
    return $RuntimeIdentifier
}

function Publish-Qs {
    param(
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][string]$OutputDirectory
    )

    if (Test-Path $OutputDirectory) {
        Remove-Item $OutputDirectory -Recurse -Force
    }

    Invoke-Dotnet @("restore", "QS/QS.csproj", "-r", $RuntimeIdentifier)
    Invoke-Dotnet @(
        "publish", "QS/QS.csproj", "-c", $Configuration,
        "-r", $RuntimeIdentifier, "--self-contained", "true",
        "-p:PublishSingleFile=true", "-p:DebugType=None", "-p:DebugSymbols=false",
        "-p:GenerateDocumentationFile=false",
        "-o", $OutputDirectory, "--no-restore"
    )
}

Invoke-Dotnet @("clean", "SIQS.slnx", "-c", $Configuration)
Invoke-Dotnet @("restore", "SIQS.slnx")
Invoke-Dotnet @("build", "SIQS.slnx", "-c", $Configuration, "--no-restore")

if (!$SkipTests) {
    Invoke-Dotnet @("test", "--solution", "SIQS.slnx", "-c", $Configuration, "--no-build", "--no-restore")
}

foreach ($runtime in $Runtimes) {
    Publish-Qs -RuntimeIdentifier $runtime -OutputDirectory "artifacts/$(Get-PlatformSlug $runtime)"
}
