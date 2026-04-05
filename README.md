# FERMM — Fast Execution Remote Management Module

Self-hosted, multiplatform Remote Management system. Your own private RMM.

## Components

- **Agent** — Lightweight C# .NET 8 binary (Windows, Linux, macOS)
- **Server** — FastAPI broker on your VPS
- **Dashboard** — React SPA for control

## Quick Start

### Deploy Server (Docker)

**On your VPS/Ubuntu server:**

```bash
# Pull latest image from Docker Hub
docker pull popox15/fermm-server:latest

# Create project directory
mkdir -p ~/fermm
cd ~/fermm

# Create .env
cat > .env << 'EOF'
POSTGRES_PASSWORD=secure_password_here
JWT_SECRET=your_jwt_secret_min_32_chars
ADMIN_USERNAME=admin
ADMIN_PASSWORD=secure_password_here
EOF

chmod 600 .env

# Create docker-compose.yml (see UBUNTU-DEPLOYMENT-GUIDE.md)
# Then start:
docker-compose up -d
```

See [UBUNTU-DEPLOYMENT-GUIDE.md](UBUNTU-DEPLOYMENT-GUIDE.md) for detailed instructions.

### Install Agent

**Windows:**
```powershell
# Download
Invoke-WebRequest -Uri "https://github.com/pgwiz/fermm/releases/latest/download/fermm-agent.exe" `
  -OutFile "C:\fermm-agent.exe"

# Configure
[Environment]::SetEnvironmentVariable("FERMM_SERVER_URL", "https://your-server.com", "Machine")
[Environment]::SetEnvironmentVariable("FERMM_TOKEN", "your-device-token", "Machine")

# Install service
sc.exe create FERMMAgent binPath= "C:\fermm-agent.exe" start= auto
sc.exe start FERMMAgent
```

**Linux:**
```bash
wget https://github.com/pgwiz/fermm/releases/latest/download/fermm-agent -O /usr/local/bin/fermm-agent
chmod +x /usr/local/bin/fermm-agent

cat > /etc/systemd/system/fermm-agent.service << EOF
[Unit]
Description=FERMM Agent
After=network.target

[Service]
Type=simple
ExecStart=/usr/local/bin/fermm-agent
Restart=always
Environment=FERMM_SERVER_URL=https://your-server.com
Environment=FERMM_TOKEN=your-device-token

[Install]
WantedBy=multi-user.target
EOF

systemctl enable --now fermm-agent
```

## Features

- ✅ Remote shell execution (live streaming)
- ✅ Screenshot capture
- ✅ Process manager (list/kill)
- ✅ File browser & transfer
- ✅ System info collection
- ✅ Hybrid WebSocket/polling resilience
- ✅ **Phase 6: Top-Level Overlay** (non-recordable chatboard)
  - Spawn overlay from dashboard
  - Real-time message sync between agent and dashboard
  - Hotkey support (Ctrl+Shift+Space)
  - Always on top (HWND_TOPMOST)
  - Not captured by screenshot tools

See [PHASE-6-OVERLAY-COMPLETE.md](PHASE-6-OVERLAY-COMPLETE.md) for overlay details.

## Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `FERMM_SERVER_URL` | ✅ | Server URL |
| `FERMM_TOKEN` | ✅ | Device auth token |
| `FERMM_DEVICE_ID` | ❌ | Auto-generated if not set |
| `FERMM_POLL_INTERVAL_SECONDS` | ❌ | Default: 15 |

## Architecture

```
Dashboard (React) ←→ Server (FastAPI) ←→ Agents (.NET 8)
                          ↓
                     PostgreSQL
```

Agents connect outbound only — no firewall ports needed on devices.

## License

Private
