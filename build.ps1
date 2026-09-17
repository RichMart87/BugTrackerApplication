<#
.SYNOPSIS
    Produces a single deployable: the Vue client built into the API's wwwroot.

.DESCRIPTION
    1. npm install + vite build in ./client
    2. Copies client/dist into server/BugTracker.Api/wwwroot
    3. dotnet publish of the API

    The published API then serves the SPA and the JSON endpoints from one origin,
    so no CORS configuration is needed in production.

.EXAMPLE
    ./build.ps1 -Configuration Release -Output ./publish
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Output = './publish'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$clientPath = Join-Path $root 'client'
$apiPath = Join-Path $root 'server/BugTracker.Api'
$wwwroot = Join-Path $apiPath 'wwwroot'

# $ErrorActionPreference does not apply to native commands, so a failing npm or
# dotnet call would otherwise sail past and the script would report success.
function Invoke-Checked {
    param([scriptblock] $Command, [string] $What)

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed with exit code $LASTEXITCODE."
    }
}

Write-Host '==> Building the Vue client' -ForegroundColor Cyan
Push-Location $clientPath
try {
    if (Test-Path 'package-lock.json') {
        Invoke-Checked { npm ci } 'npm ci'
    } else {
        Invoke-Checked { npm install } 'npm install'
    }
    Invoke-Checked { npm run build } 'vite build'
}
finally {
    Pop-Location
}

Write-Host '==> Copying the client build into wwwroot' -ForegroundColor Cyan
if (Test-Path $wwwroot) {
    # Keep .gitkeep so the folder survives a clean checkout.
    Get-ChildItem -Path $wwwroot -Exclude '.gitkeep' | Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $wwwroot | Out-Null
}
Copy-Item -Path (Join-Path $clientPath 'dist/*') -Destination $wwwroot -Recurse -Force

Write-Host '==> Publishing the API' -ForegroundColor Cyan
Invoke-Checked { dotnet publish $apiPath -c $Configuration -o $Output } 'dotnet publish'

Write-Host ''
Write-Host "Done. Run it with: dotnet $(Join-Path $Output 'BugTracker.Api.dll')" -ForegroundColor Green
