#!/bin/bash

# FERMM Agent Service Installation Script for Linux
# Run with sudo

echo "FERMM Agent Service Installer (Linux)"
echo "====================================="

# Check for root privileges
if [ "$EUID" -ne 0 ]; then
    echo "✗ This script must be run as root (use sudo)"
    exit 1
fi

echo "✓ Root privileges confirmed"

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AGENT_EXE="$SCRIPT_DIR/fermm-agent"

# Check if the agent executable exists
if [ ! -f "$AGENT_EXE" ]; then
    echo "✗ fermm-agent executable not found in script directory"
    echo "  Expected location: $AGENT_EXE"
    exit 1
fi

echo ""
echo "Agent executable found: $AGENT_EXE"
echo ""

# Make executable
chmod +x "$AGENT_EXE"

# Server Configuration
echo "Server Configuration"
echo "-------------------"
read -p "Enter server URL (e.g., http://192.168.1.100:8000): " SERVER_URL

if [ -z "$SERVER_URL" ]; then
    echo "✗ Server URL is required"
    exit 1
fi

# Create fermm user if it doesn't exist
if ! id "fermm" &>/dev/null; then
    echo "Creating fermm user..."
    useradd -r -s /bin/false fermm
    echo "✓ Created fermm user"
fi

# Test server connection and save configuration
echo "Testing server connection..."
sudo -u fermm "$AGENT_EXE" -s "$SERVER_URL"

if [ $? -ne 0 ]; then
    echo "✗ Failed to connect to server or save configuration"
    exit 1
fi

echo "✓ Server configuration saved"

# Create systemd service
echo ""
echo "Creating systemd service..."

SERVICE_CONTENT="[Unit]
Description=FERMM Remote Management Agent
After=network.target

[Service]
Type=notify
ExecStart=$AGENT_EXE
Restart=always
RestartSec=5
User=fermm
Group=fermm
WorkingDirectory=$SCRIPT_DIR

# Security settings
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/log /tmp $SCRIPT_DIR

[Install]
WantedBy=multi-user.target
"

SERVICE_FILE="/etc/systemd/system/fermm-agent.service"

echo "$SERVICE_CONTENT" > "$SERVICE_FILE"
chown root:root "$SERVICE_FILE"
chmod 644 "$SERVICE_FILE"

echo "✓ Service file created: $SERVICE_FILE"

# Set ownership of agent directory
chown -R fermm:fermm "$SCRIPT_DIR"
echo "✓ Set ownership of agent directory to fermm user"

# Reload systemd and enable service
systemctl daemon-reload
echo "✓ Systemd daemon reloaded"

systemctl enable fermm-agent
echo "✓ Service enabled for auto-start"

# Start the service
echo ""
echo "Starting FERMM Agent service..."
systemctl start fermm-agent

if [ $? -eq 0 ]; then
    echo "✓ Service started successfully"
else
    echo "✗ Failed to start service"
    echo "  Check status with: systemctl status fermm-agent"
fi

echo ""
echo "Installation completed!"
echo ""
echo "Service Management Commands:"
echo "  Start:   systemctl start fermm-agent"
echo "  Stop:    systemctl stop fermm-agent"
echo "  Status:  systemctl status fermm-agent"
echo "  Logs:    journalctl -u fermm-agent -f"
echo "  Disable: systemctl disable fermm-agent"
echo ""