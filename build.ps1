[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$SkipTests
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
# development server and deployed application offer matching Windows and Linux client downloads.
Publish-SieveClient -RuntimeIdentifier "win-x64" -OutputDirectory "SIQS.UI/download/windows-x64"
Publish-SieveClient -RuntimeIdentifier "linux-x64" -OutputDirectory "SIQS.UI/download/linux-x64"

if (!$SkipTests) {
    Invoke-Dotnet @("test", "--solution", "SIQS.slnx", "-c", $Configuration, "--no-build", "--no-restore")
}

# UI publishing copies the self-contained distributed sieve client into the deployed application.
Invoke-Dotnet @("publish", "SIQS.UI/SIQS.UI.csproj", "-c", $Configuration, "--no-restore", "-p:SkipSieveClientPublish=true")
