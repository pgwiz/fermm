# FERMM Agent Service Installation

This guide covers installing the FERMM Agent as a background service on Windows and Linux systems.

## Windows Service Installation

### Prerequisites
- Administrator privileges
- FERMM Server running and accessible
- fermm-agent.exe built and present in this directory

### Installation

#### Option 1: Automated Installation Script
1. Right-click on `install-service.bat`
2. Select "Run as administrator"
3. Enter your FERMM server URL when prompted
4. Follow the prompts

#### Option 2: Manual Installation
```cmd
# Run as Administrator
fermm-agent -s http://your-server:8000    # Configure server
fermm-agent install                       # Install service
fermm-agent start-service                 # Start service
```

### Service Management
```cmd
# Using Windows commands
net start FERMMAgent      # Start service
net stop FERMMAgent       # Stop service
sc query FERMMAgent       # Check status

# Using agent commands
fermm-agent start-service
fermm-agent stop-service
fermm-agent status
```

### Uninstallation
1. Right-click on `uninstall-service.bat`
2. Select "Run as administrator"
3. Follow the prompts

Or manually:
```cmd
fermm-agent uninstall
```

### Logs
- **Event Log**: Application Log → "FERMM Agent" source
- **File Log**: `logs/fermm-agent.log` (fallback)

## Linux Service Installation (systemd)

### Prerequisites
- Root privileges (sudo)
- FERMM Server running and accessible
- fermm-agent binary built and present in this directory

### Installation

#### Option 1: Automated Installation Script
```bash
chmod +x install-service.sh
sudo ./install-service.sh
```

#### Option 2: Manual Installation
```bash
# Configure server (as fermm user)
sudo -u fermm ./fermm-agent -s http://your-server:8000

# Create and start service
sudo ./fermm-agent install  # Creates systemd service file
sudo systemctl daemon-reload
sudo systemctl enable fermm-agent
sudo systemctl start fermm-agent
```

### Service Management
```bash
systemctl start fermm-agent      # Start service
systemctl stop fermm-agent       # Stop service  
systemctl status fermm-agent     # Check status
systemctl restart fermm-agent    # Restart service
systemctl disable fermm-agent    # Disable auto-start

# View logs
journalctl -u fermm-agent -f     # Follow logs
journalctl -u fermm-agent --since "1 hour ago"
```

### Uninstallation
```bash
chmod +x uninstall-service.sh
sudo ./uninstall-service.sh
```

Or manually:
```bash
sudo systemctl stop fermm-agent
sudo systemctl disable fermm-agent
sudo rm /etc/systemd/system/fermm-agent.service
sudo systemctl daemon-reload
```

### Security Configuration
The Linux service runs as the `fermm` user with restricted permissions:
- No new privileges
- Protected system and home directories
- Write access only to `/var/log`, `/tmp`, and agent directory

## Configuration Files

### config.dat
Stores server configuration:
```json
{
  "ServerUrl": "http://192.168.1.100:8000",
  "ConfirmUrl": "https://your-vercel-app.vercel.app",
  "LastUpdated": "2024-01-15T10:30:00.000Z"
}
```

### Directory Structure
```
fermm-agent/
├── fermm-agent.exe/.          # Agent executable
├── config.dat                 # Server configuration
├── private_rsa.key            # RSA private key for config encryption
├── logs/                      # Service log files
├── keylogs/                   # Hourly keylog files
├── install-service.bat/.sh    # Installation scripts
└── uninstall-service.bat/.sh  # Uninstallation scripts
```

## Features Available as Service

When running as a service, all agent features are available:
- ✅ Remote shell execution
- ✅ Process management
- ✅ File browser
- ✅ Screenshot capture
- ✅ System information
- ✅ GOD Mode navigation (Windows)
- ✅ **Keylogger with hourly file rotation**

### Hourly Keylogger Files
The service automatically creates hourly keylog files:
- Location: `keylogs/keylog_YYYY-MM-DD_HH.txt`
- Format: `timestamp\tkey\twindow_title`
- Rotation: New file every hour
- Management: Use keylogger commands `list` and `get:filename`

## Troubleshooting

### Windows
- **Installation fails**: Ensure running as Administrator
- **Service won't start**: Check Event Log for errors
- **Connection issues**: Verify server URL in config.dat

### Linux  
- **Permission denied**: Ensure script is executable (`chmod +x`)
- **Service fails**: Check logs with `journalctl -u fermm-agent`
- **Network issues**: Verify firewall allows outbound connections

### Common Issues
- **Server unreachable**: Test server URL manually
- **Config not saved**: Check write permissions to agent directory
- **Keylogger not working**: Ensure service has necessary permissions for input monitoring

## Support

For issues or questions:
1. Check service logs first
2. Verify server connectivity
3. Review configuration files
4. Test agent in console mode before service installation