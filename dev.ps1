<#
.SYNOPSIS
    Starts the BugTracker API and the Vue dev server together.

.DESCRIPTION
    Launches the .NET API on http://localhost:5174 in a background job and the Vite
    dev server on http://localhost:5173 in the foreground. Vite proxies /api to the
    API, so the browser stays on a single origin.

    Press Ctrl+C to stop the dev server; the API job is stopped on the way out.

.EXAMPLE
    ./dev.ps1
#>
[CmdletBinding()]
param(
    [int] $ApiPort = 5174,
    [int] $ClientPort = 5173
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "Starting BugTracker API on http://localhost:$ApiPort ..." -ForegroundColor Cyan

$api = Start-Job -Name 'bugtracker-api' -ScriptBlock {
    param($apiRoot, $port)
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = "http://localhost:$port"
    Set-Location $apiRoot
    dotnet run --no-launch-profile
} -ArgumentList (Join-Path $root 'server/BugTracker.Api'), $ApiPort

try {
    # Wait for the API to answer before the client starts proxying to it.
    $deadline = (Get-Date).AddSeconds(90)
    do {
        Start-Sleep -Milliseconds 700
        $ready = try {
            (Invoke-WebRequest -Uri "http://localhost:$ApiPort/health" -TimeoutSec 3 -UseBasicParsing).StatusCode -eq 200
        } catch { $false }
    } while (-not $ready -and (Get-Date) -lt $deadline)

    if (-not $ready) {
        Receive-Job -Job $api
        throw "The API did not become healthy on port $ApiPort."
    }

    Write-Host "API is healthy. Swagger: http://localhost:$ApiPort/swagger" -ForegroundColor Green

    $clientPath = Join-Path $root 'client'
    if (-not (Test-Path (Join-Path $clientPath 'node_modules'))) {
        Write-Host 'Installing client dependencies...' -ForegroundColor Cyan
        Push-Location $clientPath
        npm install
        Pop-Location
    }

    Write-Host "Starting the Vue client on http://localhost:$ClientPort ..." -ForegroundColor Cyan
    $env:VITE_API_PROXY_TARGET = "http://localhost:$ApiPort"
    Push-Location $clientPath
    npm run dev -- --port $ClientPort
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    Write-Host 'Stopping the API...' -ForegroundColor Cyan
    Stop-Job -Job $api -ErrorAction SilentlyContinue
    Remove-Job -Job $api -Force -ErrorAction SilentlyContinue
}
