# FERMM Mini Installer - Quick Start Guide

## One-Liner Installation

### Windows (PowerShell)
```powershell
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest -Uri https://rmm.bware.systems/mini -OutFile $env:TEMP\installer.exe
& $env:TEMP\installer.exe /silent
```

### Linux/Mac (wget)
```bash
wget https://rmm.bware.systems/mini -O /tmp/installer.exe
chmod +x /tmp/installer.exe
/tmp/installer.exe /silent
```

---

## Installation Options

### Silent (No UI)
```batch
FermmMiniInstaller.exe /silent
```
✓ No windows, no dialogs, only logs

### Interactive (Show Progress)
```batch
FermmMiniInstaller.exe
```
✓ System tray notifications, balloon tips, summary

### With Custom Log
```batch
FermmMiniInstaller.exe /log=C:\my-install.log
```

---

## What Gets Installed

**Location**: `%APPDATA%\Local\Microlens\`

```
Microlens/
├── fermm-agent.exe         (9.2 MB - main agent)
├── FermmMiniInstaller.exe  (0.23 MB - self-updater)
├── config.json             (metadata)
└── logs/
    └── install-*.log       (timestamped logs)
```

**Registry**: HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```
Microlens = C:\Users\{user}\AppData\Local\Microlens\fermm-agent.exe
```

---

## Verification

### Check Installation
```powershell
# Verify files exist
Get-ChildItem $env:APPDATA\Local\Microlens\

# Check registry auto-start
Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name Microlens

# Check logs
Get-ChildItem $env:APPDATA\Local\Microlens\logs\ | Sort-Object LastWriteTime -Desc | Select-Object -First 3
```

### Check Agent Running
```powershell
# Task Manager: Look for fermm-agent.exe
Get-Process fermm-agent -ErrorAction SilentlyContinue | Select-Object Name, CPU, Memory
```

---

## Troubleshooting

### Download Failed
```
Error: 404 Not Found
```
→ Check server is running: `curl https://rmm.bware.systems/mini`

### Installation Failed
```
Check logs: %APPDATA%\Local\Microlens\logs\install-*.log
```

Common issues:
- Insufficient disk space (need ~10 MB)
- Network timeout (try again or check internet)
- Antivirus blocking (whitelist `%APPDATA%\Local\Microlens\`)

### Agent Won't Start
```powershell
# Check if exe exists
Test-Path $env:APPDATA\Local\Microlens\fermm-agent.exe

# Check Windows Event Viewer for errors
Get-WinEvent -LogName Application | Where-Object {$_.ProviderName -like "*Microlens*"}
```

---

## File Sizes

| Component | Size |
|-----------|------|
| Mini Installer | 0.23 MB |
| Agent Executable | 9.2 MB |
| Config + Logs | ~100 KB |
| **Total Disk Usage** | **~9.5 MB** |

---

## Update Detection

The installer automatically checks for updates:

1. **On every launch**, queries: `https://linkify-ten-sable.vercel.app/api/env`
2. **Looks for**: `UPDATE_DATE=dd/mm/yy` (e.g., `05/04/26`)
3. **If newer**: Downloads fresh agent automatically
4. **If same/older**: Skips install

To force update:
```batch
# Delete config.json so installer thinks it's fresh
del %APPDATA%\Local\Microlens\config.json
FermmMiniInstaller.exe /silent
```

---

## Manual Install (Advanced)

If you don't want to use the mini installer:

1. **Download agent directly**:
   ```bash
   wget https://rmm.bware.systems/xs -O fermm-agent.exe
   ```

2. **Create directory**:
   ```powershell
   mkdir $env:APPDATA\Local\Microlens
   ```

3. **Copy files**:
   ```powershell
   Copy-Item fermm-agent.exe $env:APPDATA\Local\Microlens\
   ```

4. **Add to startup**:
   ```powershell
   New-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
     -Name "Microlens" `
     -Value "$env:APPDATA\Local\Microlens\fermm-agent.exe"
   ```

5. **Run agent**:
   ```powershell
   & "$env:APPDATA\Local\Microlens\fermm-agent.exe"
   ```

---

## Enterprise Deployment

### Group Policy (Active Directory)
1. Copy `FermmMiniInstaller.exe` to shared network drive
2. Create GPO script:
   ```batch
   \\server\share\FermmMiniInstaller.exe /silent /log=%APPDATA%\Local\Microlens\logs\gpo-install.log
   ```
3. Deploy to OUs as Startup Script

### Batch Deployment
```powershell
$machines = @("PC001", "PC002", "PC003")
foreach ($machine in $machines) {
    $session = New-PSSession -ComputerName $machine
    Invoke-Command -Session $session -ScriptBlock {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri https://rmm.bware.systems/mini -OutFile $env:TEMP\installer.exe
        & $env:TEMP\installer.exe /silent
    }
    Remove-PSSession $session
}
```

### Automated Weekly Updates
```powershell
# Create Windows Task Scheduler job
$action = New-ScheduledTaskAction `
  -Execute "$env:APPDATA\Local\Microlens\FermmMiniInstaller.exe" `
  -Argument "/silent"

$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Wednesday -At 3AM

Register-ScheduledTask `
  -Action $action `
  -Trigger $trigger `
  -TaskName "Microlens Weekly Update" `
  -Description "Check for FERMM agent updates"
```

---

## Logging

All operations logged to: `%APPDATA%\Local\Microlens\logs\`

Example log output:
```
[14:23:45] FERMM Mini Installer Started
[14:23:45] Silent Mode: true
[14:23:46] Checking for updates from Vercel...
[14:23:47] Update available: 05/04/26
[14:23:48] Starting agent download...
[14:23:58] Download complete: C:\Users\user\AppData\Local\Microlens\fermm-agent.exe
[14:24:00] Installation complete
[14:24:01] Registry Run key set
[14:24:02] Installation finished successfully
```

---

## FAQs

**Q: Will it work if .NET isn't installed?**
A: The installer assumes .NET 8 runtime is installed (Windows 10+, or server with runtime). If missing, it will error.

**Q: Can I uninstall it?**
A: Not yet. For now, manually delete:
  - `%APPDATA%\Local\Microlens\`
  - Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Microlens`

**Q: Does it require admin?**
A: No. Installs to user's AppData which doesn't need elevation.

**Q: How do I see the real-time progress?**
A: Don't use `/silent` flag:
```batch
FermmMiniInstaller.exe
```
Shows system tray balloon tips and summary.

**Q: What if Vercel is down?**
A: Installer still proceeds with local install (failsafe mode).

**Q: Can I run multiple installers at once?**
A: Yes, each gets a timestamped log file.

---

## Support

For issues:
1. **Check logs**: `%APPDATA%\Local\Microlens\logs\`
2. **Enable verbose**: Run without `/silent` to see real-time status
3. **Report bugs**: GitHub Issues with log contents

