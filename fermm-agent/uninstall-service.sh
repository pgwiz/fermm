#!/bin/bash

# FERMM Agent Service Uninstallation Script for Linux
# Run with sudo

echo "FERMM Agent Service Uninstaller (Linux)"
echo "======================================="

# Check for root privileges
if [ "$EUID" -ne 0 ]; then
    echo "✗ This script must be run as root (use sudo)"
    exit 1
fi

echo "✓ Root privileges confirmed"
echo ""

# Stop and disable the service
echo "Stopping and disabling FERMM Agent service..."

systemctl stop fermm-agent 2>/dev/null
systemctl disable fermm-agent 2>/dev/null

# Remove service file
SERVICE_FILE="/etc/systemd/system/fermm-agent.service"
if [ -f "$SERVICE_FILE" ]; then
    rm "$SERVICE_FILE"
    echo "✓ Removed service file"
fi

# Reload systemd
systemctl daemon-reload
echo "✓ Systemd daemon reloaded"

# Get the agent directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Ask about configuration cleanup
read -p "Remove configuration files? (y/N): " CLEANUP
if [[ "$CLEANUP" =~ ^[Yy]$ ]]; then
    if [ -f "$SCRIPT_DIR/config.dat" ]; then
        rm "$SCRIPT_DIR/config.dat"
        echo "✓ Removed config.dat"
    fi
    if [ -d "$SCRIPT_DIR/logs" ]; then
        rm -rf "$SCRIPT_DIR/logs"
        echo "✓ Removed logs directory"
    fi
    if [ -d "$SCRIPT_DIR/keylogs" ]; then
        read -p "Remove keylog files? (y/N): " REMOVE_LOGS
        if [[ "$REMOVE_LOGS" =~ ^[Yy]$ ]]; then
            rm -rf "$SCRIPT_DIR/keylogs"
            echo "✓ Removed keylogs directory"
        fi
    fi
fi

# Ask about user removal
read -p "Remove fermm user account? (y/N): " REMOVE_USER
if [[ "$REMOVE_USER" =~ ^[Yy]$ ]]; then
    userdel fermm 2>/dev/null
    echo "✓ Removed fermm user"
fi

echo ""
echo "Uninstallation completed!"