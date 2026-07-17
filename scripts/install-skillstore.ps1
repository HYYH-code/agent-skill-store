#Requires -Version 5.1
<#
.SYNOPSIS
    Install the skillstore CLI on Windows.
.PARAMETER Version
    Release version or tag. Defaults to latest.
.PARAMETER InstallDir
    Installation directory. Defaults to LocalAppData\Programs\skillstore.
.PARAMETER Repository
    GitHub repository in owner/name form.
#>
param(
    [string]$Version = "latest",
    [string]$InstallDir = "",
    [string]$Repository = $(if ($env:AGENT_SKILL_STORE_REPOSITORY) { $env:AGENT_SKILL_STORE_REPOSITORY } else { "agent-skill-store/agent-skill-store" })
)

$ErrorActionPreference = "Stop"
$binaryName = "skillstore"
$rid = "win-x64"

if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\skillstore"
}

function Resolve-ReleaseTag {
    if ($script:Version -ne "latest") {
        return $(if ($script:Version.StartsWith("v")) { $script:Version } else { "v$($script:Version)" })
    }

    $response = Invoke-WebRequest -Uri "https://api.github.com/repos/$Repository/releases/latest" -UseBasicParsing
    $release = $response.Content | ConvertFrom-Json
    if (-not $release.tag_name) {
        throw "Could not determine the latest release tag."
    }
    return [string]$release.tag_name
}

$tag = Resolve-ReleaseTag
$normalizedVersion = $tag.TrimStart('v')
$archiveName = "agent-skill-store-$normalizedVersion-$rid.zip"
$releaseBaseUrl = "https://github.com/$Repository/releases/download/$tag"
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
try {
    Write-Host "Installing $binaryName $normalizedVersion for $rid..."
    Invoke-WebRequest -Uri "$releaseBaseUrl/$archiveName" -OutFile (Join-Path $tempDir $archiveName) -UseBasicParsing
    Invoke-WebRequest -Uri "$releaseBaseUrl/SHA256SUMS" -OutFile (Join-Path $tempDir "SHA256SUMS") -UseBasicParsing

    $expectedLine = Get-Content -LiteralPath (Join-Path $tempDir "SHA256SUMS") |
        Where-Object { $_ -match "\s\*?$([Regex]::Escape($archiveName))$" } |
        Select-Object -First 1
    if (-not $expectedLine) {
        throw "SHA256SUMS does not contain $archiveName."
    }
    $expected = ($expectedLine -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $tempDir $archiveName)).Hash.ToLowerInvariant()
    if ($expected -ne $actual) {
        throw "Checksum mismatch for $archiveName."
    }

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Expand-Archive -LiteralPath (Join-Path $tempDir $archiveName) -DestinationPath $InstallDir -Force
    $installedBinary = Join-Path $InstallDir "$binaryName.exe"
    if (-not (Test-Path -LiteralPath $installedBinary)) {
        throw "Archive does not contain $binaryName.exe."
    }

    $userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
    if ($userPath -notlike "*$InstallDir*") {
        $newPath = if ($userPath) { "$userPath;$InstallDir" } else { $InstallDir }
        [Environment]::SetEnvironmentVariable("PATH", $newPath, "User")
        $env:PATH = "$env:PATH;$InstallDir"
    }

    Write-Host "Installed $binaryName to $installedBinary"
    Write-Host "Run '$binaryName --version' to verify."
}
finally {
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
