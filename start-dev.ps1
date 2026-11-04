#Requires -Version 5.1
<#
.SYNOPSIS
    One-command development startup for EducationHub.

    Starts:
      1. the ASP.NET Core backend API      -> http://localhost:57735  (Swagger: https://localhost:57734/swagger)
      2. the Angular admin dev server      -> http://localhost:4200   (hot reload, /api + /health proxied)
    opens both URLs in the browser, then supervises both processes until you press Ctrl+C.

.NOTES
    Preconditions: .NET SDK 10, Node.js >= 20, SQL Server reachable (EducationHub_Dev).
    Startup output is redirected to log files; if a service fails to come up the relevant log
    tail is printed here - failures are never swallowed.
#>
[CmdletBinding()]
param(
    # Skip opening the two browser tabs (useful for automated runs).
    [switch]$SkipBrowserOpen,

    # Seconds to wait for each service to become healthy.
    [int]$StartupTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
Set-Location -LiteralPath $root

$stamp     = Get-Date -Format 'yyyyMMdd-HHmmss'
$temp      = [IO.Path]::GetTempPath()
$backendLog  = Join-Path $temp "educationhub-backend-$stamp.log"
$frontendLog = Join-Path $temp "educationhub-frontend-$stamp.log"

$backendProc  = $null
$frontendProc = $null

function Write-Step($msg) { Write-Host "[start-dev] $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "[start-dev] $msg" -ForegroundColor Green }
function Write-Fail($msg) { Write-Host "[start-dev] ERROR: $msg" -ForegroundColor Red }

function Stop-Tree($proc) {
    if ($null -ne $proc -and -not $proc.HasExited) {
        taskkill.exe /PID $proc.Id /T /F 2>&1 | Out-Null
    }
}

# Returns the listening PIDs bound to the given local port (any protocol/stack).
function Get-PortPids([int]$Port) {
    $found = @()
    foreach ($line in (netstat -ano | Select-String 'LISTENING')) {
        $cols = @($line.ToString().Trim() -split '\s+' | Where-Object { $_ })
        if ($cols.Count -ge 5 -and $cols[3] -eq 'LISTENING' -and $cols[1].EndsWith(":$Port")) {
            $found += [int]$cols[4]
        }
    }
    return @($found | Sort-Object -Unique)
}

function Assert-PortFree([int]$Port) {
    $pids = Get-PortPids $Port
    if ($pids.Count -gt 0) {
        $names = ($pids | ForEach-Object {
            $p = Get-Process -Id $_ -ErrorAction SilentlyContinue
            if ($p) { "PID $_ ($($p.ProcessName))" } else { "PID $_" }
        }) -join ', '
        throw "Port $Port is already in use by $names. Stop that process first, then re-run start-dev."
    }
}

function Test-UrlOk([string]$Url) {
    try {
        $null = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function Show-LogTail([string]$LogFile) {
    Write-Fail "---- tail of $LogFile ----"
    if (Test-Path -LiteralPath $LogFile) {
        Get-Content -LiteralPath $LogFile -Tail 40 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    } else {
        Write-Host "  (log file was not created)" -ForegroundColor Red
    }
    Write-Fail "--------------------"
}

function Wait-Until([string]$Url, $Proc, [string]$Name, [string]$LogFile) {
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($null -ne $Proc -and $Proc.HasExited) {
            Show-LogTail $LogFile
            throw "$Name exited during startup (exit code $($Proc.ExitCode))."
        }
        if (Test-UrlOk $Url) { return }
        Start-Sleep -Milliseconds 1000
    }
    Show-LogTail $LogFile
    throw "$Name did not answer $Url within $StartupTimeoutSeconds seconds."
}

$exitCode = 0
try {
    # ------------------------------------------------------------ preflight
    Write-Step "Preflight checks..."

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet was not found on PATH. Install the .NET SDK 10 (see global.json)."
    }

    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw "npm was not found on PATH. Install Node.js 20+ and ensure npm is on PATH."
    }

    Assert-PortFree 57735
    Assert-PortFree 57734
    Assert-PortFree 4200

    $adminDir = Join-Path $root 'admin'
    if (-not (Test-Path (Join-Path $adminDir 'node_modules\@angular\cli'))) {
        Write-Step "admin\node_modules missing - running npm ci (lockfile preserved)..."
        Push-Location $adminDir
        try {
            & npm ci
            if ($LASTEXITCODE -ne 0) {
                throw "npm ci failed (exit $LASTEXITCODE). If the npm cache is compromised on this machine, set `$env:npm_config_cache to a temp directory and retry - see LOCAL_RUN_AND_TEST_STATUS.md."
            }
        } finally { Pop-Location }
    }

    # ------------------------------------------------------------ backend
    Write-Step "Starting backend API (dotnet run, launch profile 'EducationHub.API')..."
    $backendArgs = "/c dotnet run --project `"$root\src\EducationHub.API\EducationHub.API.csproj`" --launch-profile EducationHub.API > `"$backendLog`" 2>&1"
    $backendProc = Start-Process -FilePath 'cmd.exe' -ArgumentList $backendArgs `
        -PassThru -WindowStyle Minimized

    Write-Step "Waiting for http://localhost:57735/health/ready (timeout ${StartupTimeoutSeconds}s)..."
    Wait-Until 'http://localhost:57735/health/ready' $backendProc 'Backend' $backendLog
    Write-Ok "Backend is healthy."

    # ------------------------------------------------------------ frontend
    Write-Step "Starting Angular admin dev server (ng serve --host 127.0.0.1 --port 4200)..."
    $frontendArgs = "/c npm start -- --host 127.0.0.1 --port 4200 > `"$frontendLog`" 2>&1"
    $frontendProc = Start-Process -FilePath 'cmd.exe' -ArgumentList $frontendArgs `
        -WorkingDirectory $adminDir -PassThru -WindowStyle Minimized

    Write-Step "Waiting for http://127.0.0.1:4200/ (timeout ${StartupTimeoutSeconds}s)..."
    Wait-Until 'http://127.0.0.1:4200/' $frontendProc 'Angular dev server' $frontendLog
    Write-Ok "Angular dev server is up."

    # ------------------------------------------------------------ browsers
    if (-not $SkipBrowserOpen) {
        Write-Step "Opening Swagger UI and the admin client..."
        Start-Process 'https://localhost:57734/swagger'
        Start-Process 'http://127.0.0.1:4200'
    }

    # ------------------------------------------------------------ supervise
    Write-Ok "Both services are running."
    Write-Host ""
    Write-Host "  Backend API   : http://localhost:57735        (Swagger UI: https://localhost:57734/swagger)" -ForegroundColor White
    Write-Host "  Angular admin : http://127.0.0.1:4200" -ForegroundColor White
    Write-Host "  Backend log   : $backendLog" -ForegroundColor DarkGray
    Write-Host "  Frontend log  : $frontendLog" -ForegroundColor DarkGray
    Write-Host "  Press Ctrl+C to stop both services." -ForegroundColor Yellow
    Write-Host ""

    while ($true) {
        Start-Sleep -Seconds 2
        if ($backendProc.HasExited) {
            Show-LogTail $backendLog
            throw "Backend exited unexpectedly (exit code $($backendProc.ExitCode))."
        }
        if ($frontendProc.HasExited) {
            Show-LogTail $frontendLog
            throw "Angular dev server exited unexpectedly (exit code $($frontendProc.ExitCode))."
        }
    }
} catch {
    Write-Fail $_.Exception.Message
    $exitCode = 1
} finally {
    # Runs on Ctrl+C too: both process trees are torn down together.
    Stop-Tree $frontendProc
    Stop-Tree $backendProc
    Write-Step "Stopped."
}

exit $exitCode
