@echo off
REM FERMM Agent Service Installation Script for Windows
REM Run as Administrator

echo FERMM Agent Service Installer
echo =============================

REM Check for administrator privileges
net session >nul 2>&1
if %errorLevel% == 0 (
    echo ✓ Administrator privileges confirmed
) else (
    echo ✗ This script must be run as Administrator
    echo   Right-click and select "Run as administrator"
    pause
    exit /b 1
)

REM Get the directory where this script is located
set "SCRIPT_DIR=%~dp0"
set "AGENT_EXE=%SCRIPT_DIR%fermm-agent.exe"

REM Check if the agent executable exists
if not exist "%AGENT_EXE%" (
    echo ✗ fermm-agent.exe not found in script directory
    echo   Expected location: %AGENT_EXE%
    pause
    exit /b 1
)

echo.
echo Agent executable found: %AGENT_EXE%
echo.

REM Prompt for server configuration
echo Server Configuration
echo -------------------
set /p SERVER_URL="Enter server URL (e.g., http://192.168.1.100:8000): "

if "%SERVER_URL%"=="" (
    echo ✗ Server URL is required
    pause
    exit /b 1
)

REM Test server connection and save configuration
echo Testing server connection...
"%AGENT_EXE%" -s "%SERVER_URL%"

if %errorLevel% neq 0 (
    echo ✗ Failed to connect to server or save configuration
    pause
    exit /b 1
)

echo ✓ Server configuration saved

REM Install the service
echo.
echo Installing Windows Service...
"%AGENT_EXE%" install

if %errorLevel% neq 0 (
    echo ✗ Service installation failed
    pause
    exit /b 1
)

REM Start the service
echo.
echo Starting FERMM Agent service...
"%AGENT_EXE%" start-service

if %errorLevel% neq 0 (
    echo ✗ Failed to start service
    echo   You can manually start it later with: net start FERMMAgent
) else (
    echo ✓ Service started successfully
)

echo.
echo Installation completed!
echo.
echo Service Management Commands:
echo   Start:   net start FERMMAgent
echo   Stop:    net stop FERMMAgent
echo   Status:  sc query FERMMAgent
echo.
echo   Or use the agent commands:
echo   Start:   fermm-agent start-service
echo   Stop:    fermm-agent stop-service
echo   Status:  fermm-agent status
echo.
pause