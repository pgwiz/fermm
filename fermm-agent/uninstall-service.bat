@echo off
REM FERMM Agent Service Uninstallation Script for Windows
REM Run as Administrator

echo FERMM Agent Service Uninstaller
echo ================================

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
echo Stopping and removing FERMM Agent service...

REM Uninstall the service
"%AGENT_EXE%" uninstall

if %errorLevel% neq 0 (
    echo ✗ Service uninstallation failed
    pause
    exit /b 1
)

echo ✓ Service uninstalled successfully
echo.

REM Ask about configuration cleanup
set /p CLEANUP="Remove configuration files? (y/N): "
if /I "%CLEANUP%"=="y" (
    if exist "%SCRIPT_DIR%config.dat" (
        del "%SCRIPT_DIR%config.dat"
        echo ✓ Removed config.dat
    )
    if exist "%SCRIPT_DIR%logs" (
        rmdir /s /q "%SCRIPT_DIR%logs"
        echo ✓ Removed logs directory
    )
    if exist "%SCRIPT_DIR%keylogs" (
        set /p REMOVE_LOGS="Remove keylog files? (y/N): "
        if /I "!REMOVE_LOGS!"=="y" (
            rmdir /s /q "%SCRIPT_DIR%keylogs"
            echo ✓ Removed keylogs directory
        )
    )
)

echo.
echo Uninstallation completed!
pause