#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs or uninstalls FERMM Agent as a Windows Service
.DESCRIPTION
    Creates a Windows Service to run FERMM Agent in the background with auto-start capability
.PARAMETER Action
    "install" to create the service, "uninstall" to remove it, "start" to start, "stop" to stop
.PARAMETER ServerUrl
    FERMM server URL (optional, agent will auto-discover if not provided)
.PARAMETER Token
    Device authentication token (optional, not needed with auto-discovery)
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("install", "uninstall", "start", "stop", "status")]
    [string]$Action,
    
    [string]$ServerUrl,
    [string]$Token
)

$serviceName = "FERMMAgent"
$displayName = "FERMM Remote Agent"
$description = "Fast Execution Remote Management Module - Lightweight agent for device management"
$agentPath = Split-Path -Parent $PSCommandPath
$exePath = Join-Path $agentPath "bin\Release\net8.0\win-x64\fermm-agent.exe"

# Check if running as admin
$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script requires administrator privileges. Please run as Administrator."
    exit 1
}

function Install-FERMMService {
    Write-Host "Installing FERMM Agent service..." -ForegroundColor Cyan
    
    # Check if exe exists
    if (-not (Test-Path $exePath)) {
        Write-Error "Agent executable not found at $exePath`nPlease build the Release version first: dotnet publish -c Release"
        exit 1
    }
    
    # Check if service already exists
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service) {
        Write-Warning "Service '$serviceName' already exists. Removing old version..."
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        Remove-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    
    # Create service
    $params = @{
        Name = $serviceName
        DisplayName = $displayName
        Description = $description
        BinaryPathName = $exePath
        StartupType = "Automatic"
        ErrorAction = "Stop"
    }
    
    New-Service @params | Out-Null
    Write-Host "✓ Service created successfully" -ForegroundColor Green
    
    # Set environment variables if provided
    if ($ServerUrl -or $Token) {
        $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
        
        if ($ServerUrl) {
            Set-ItemProperty -Path $regPath -Name "FERMM_SERVER_URL" -Value $ServerUrl -Type String
            Write-Host "✓ Set FERMM_SERVER_URL=$ServerUrl" -ForegroundColor Green
        }
        
        if ($Token) {
            Set-ItemProperty -Path $regPath -Name "FERMM_TOKEN" -Value $Token -Type String
            Write-Host "✓ Set FERMM_TOKEN=****" -ForegroundColor Green
        }
    }
    
    Write-Host "`nService installation complete!" -ForegroundColor Green
    Write-Host "Run 'Get-Service $serviceName' to view, or use this script with '-Action start' to begin" -ForegroundColor Cyan
}

function Uninstall-FERMMService {
    Write-Host "Uninstalling FERMM Agent service..." -ForegroundColor Cyan
    
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Warning "Service '$serviceName' not found"
        return
    }
    
    # Stop service if running
    if ($service.Status -eq "Running") {
        Write-Host "Stopping service..." -ForegroundColor Yellow
        Stop-Service -Name $serviceName -Force
        Start-Sleep -Seconds 2
    }
    
    # Remove service
    Remove-Service -Name $serviceName -Force
    Write-Host "✓ Service uninstalled successfully" -ForegroundColor Green
}

function Start-FERMMService {
    Write-Host "Starting FERMM Agent service..." -ForegroundColor Cyan
    
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Error "Service '$serviceName' not found. Install it first with -Action install"
        exit 1
    }
    
    if ($service.Status -eq "Running") {
        Write-Warning "Service is already running"
        return
    }
    
    Start-Service -Name $serviceName
    Start-Sleep -Seconds 2
    
    $service = Get-Service -Name $serviceName
    if ($service.Status -eq "Running") {
        Write-Host "✓ Service started successfully" -ForegroundColor Green
    } else {
        Write-Error "Failed to start service"
        exit 1
    }
}

function Stop-FERMMService {
    Write-Host "Stopping FERMM Agent service..." -ForegroundColor Cyan
    
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Warning "Service '$serviceName' not found"
        return
    }
    
    if ($service.Status -eq "Stopped") {
        Write-Warning "Service is already stopped"
        return
    }
    
    Stop-Service -Name $serviceName -Force
    Write-Host "✓ Service stopped" -ForegroundColor Green
}

function Get-FERMMServiceStatus {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    
    if (-not $service) {
        Write-Host "Service '$serviceName' not found" -ForegroundColor Red
        return
    }
    
    Write-Host "`n=== FERMM Agent Service Status ===" -ForegroundColor Cyan
    Write-Host "Service Name: $($service.Name)"
    Write-Host "Display Name: $($service.DisplayName)"
    Write-Host "Status: $(if ($service.Status -eq 'Running') { Write-Host $service.Status -ForegroundColor Green -NoNewline } else { Write-Host $service.Status -ForegroundColor Yellow -NoNewline })"
    Write-Host ""
    Write-Host "Startup Type: $($service.StartupType)"
    Write-Host ""
}

# Main switch
switch ($Action) {
    "install" { Install-FERMMService }
    "uninstall" { Uninstall-FERMMService }
    "start" { Start-FERMMService }
    "stop" { Stop-FERMMService }
    "status" { Get-FERMMServiceStatus }
}
