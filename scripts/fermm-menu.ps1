param(
    [string]$BaseUrl = "https://rmm.bware.systems"
)

$MiniUrl = "$BaseUrl/mini"
$AgentUrl = "$BaseUrl/xs"
$InstallRoot = Join-Path $env:LOCALAPPDATA "Microlens"
$MiniPath = Join-Path $env:TEMP "FermmMiniInstaller.exe"
$AgentZip = Join-Path $env:TEMP "fermm-agent.zip"
$ServiceName = "FERMMAgent"

function Test-IsAdmin {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Show-Header {
    Clear-Host
    $adminLabel = if (Test-IsAdmin) { "ADMIN" } else { "STANDARD" }
    Write-Host "FERMM Setup Menu" -ForegroundColor Cyan
    Write-Host "Base URL: $BaseUrl"
    Write-Host "Mode: $adminLabel"
    Write-Host ""
}

function Invoke-Download {
    param(
        [Parameter(Mandatory=$true)][string]$Url,
        [Parameter(Mandatory=$true)][string]$Path
    )
    Write-Host "Downloading: $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
    Write-Host "Saved to: $Path"
}

function Show-LatestInstallLog {
    $logDir = Join-Path $InstallRoot "logs"
    if (-not (Test-Path $logDir)) {
        Write-Host "No log directory found."
        return
    }
    $latest = Get-ChildItem $logDir -Filter "install-*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $latest) {
        Write-Host "No installer logs found."
        return
    }
    Write-Host "`n=== Latest Install Log: $($latest.Name) ==="
    Get-Content $latest.FullName -Tail 20
}

function Run-MiniInstaller {
    param([switch]$Silent)
    try {
        Invoke-Download -Url $MiniUrl -Path $MiniPath
        $args = if ($Silent) { "/silent" } else { "" }
        Write-Host "Launching installer..."
        $proc = Start-Process -FilePath $MiniPath -ArgumentList $args -PassThru
        $proc.WaitForExit()
        Write-Host "Installer exit code: $($proc.ExitCode)"
        Show-LatestInstallLog
    }
    catch {
        Write-Host "Installer failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Get-ServiceState {
    try {
        $svc = Get-Service -Name $ServiceName -ErrorAction Stop
        return $svc.Status
    } catch {
        return "NotInstalled"
    }
}

function Stop-FermmService {
    try {
        Write-Host "Stopping service..."
        Stop-Service -Name $ServiceName -ErrorAction Stop
        Start-Sleep -Seconds 2
    } catch {
        Write-Host "Failed to stop service: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Start-FermmService {
    try {
        Write-Host "Starting service..."
        Start-Service -Name $ServiceName -ErrorAction Stop
    } catch {
        Write-Host "Failed to start service: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Update-AgentBundle {
    try {
        Write-Host "Updating agent bundle..."
        $state = Get-ServiceState
        if ($state -eq "Running") {
            if (-not (Test-IsAdmin)) {
                Write-Host "Service is running. Run as administrator to update." -ForegroundColor Yellow
                return
            }
            Stop-FermmService
        }

        Invoke-Download -Url $AgentUrl -Path $AgentZip
        if (-not (Test-Path $InstallRoot)) {
            New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
        }
        Expand-Archive -Path $AgentZip -DestinationPath $InstallRoot -Force
        Write-Host "Agent bundle extracted to $InstallRoot"

        if (Test-IsAdmin -and (Get-ServiceState -ne "NotInstalled")) {
            Start-FermmService
        } else {
            $agentExe = Join-Path $InstallRoot "fermm-agent.exe"
            if (Test-Path $agentExe) {
                Start-Process -FilePath $agentExe
                Write-Host "Launched agent manually."
            }
        }
    } catch {
        Write-Host "Update failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Test-ServerConnectivity {
    try {
        $resp = Invoke-WebRequest -Uri "$BaseUrl/api/devices/discover" -Method Get -UseBasicParsing
        Write-Host "Server reachable. Status: $($resp.StatusCode)"
    } catch {
        Write-Host "Server test failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Show-DeviceId {
    $idPath = Join-Path $InstallRoot ".device_id"
    if (Test-Path $idPath) {
        $deviceId = (Get-Content $idPath -ErrorAction SilentlyContinue).Trim()
        Write-Host "Device ID: $deviceId"
    } else {
        Write-Host "Device ID file not found."
    }
}

while ($true) {
    Show-Header
    Write-Host "1) Download /mini and run (interactive)"
    Write-Host "2) Download /mini and run (silent)"
    Write-Host "3) Update agent from /xs"
    Write-Host "4) Service status"
    Write-Host "5) Start service"
    Write-Host "6) Stop service"
    Write-Host "7) Open dashboard"
    Write-Host "8) Test server connectivity"
    Write-Host "9) Show Device ID"
    Write-Host "0) Exit"
    Write-Host ""

    $choice = Read-Host "Select option"
    switch ($choice) {
        "1" { Run-MiniInstaller }
        "2" { Run-MiniInstaller -Silent }
        "3" { Update-AgentBundle }
        "4" { Write-Host "Service state: $(Get-ServiceState)" }
        "5" { Start-FermmService }
        "6" { Stop-FermmService }
        "7" { Start-Process $BaseUrl }
        "8" { Test-ServerConnectivity }
        "9" { Show-DeviceId }
        "0" { break }
        default { Write-Host "Invalid option." -ForegroundColor Yellow }
    }

    Write-Host ""
    Read-Host "Press Enter to continue" | Out-Null
}
