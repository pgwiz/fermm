# FERMM — Fast Execution Remote Management Module

Self-hosted, multiplatform Remote Management system. Your own private RMM.

## Components

- **Agent** — Lightweight C# .NET 8 binary (Windows, Linux, macOS)
- **Server** — FastAPI broker on your VPS
- **Dashboard** — React SPA for control

## Quick Start

**New?** Follow these 3 steps:

1. **Read**: [setup.md](setup.md) — 5-minute first-run guide
2. **Deploy**: `docker-compose up -d`
3. **Access**: http://your-server-ip

For detailed documentation, see the [md/](md/) folder.

### Configure Domain (Port 80/443)

```bash
# Option 1: If port 80/443 available
sudo bash scripts/setup-domain.sh

# Option 2: If port 80/443 in use (system nginx) OR script fails
# Follow manual guide: md/MANUAL-DOMAIN-SETUP.md
# (More reliable, no script escaping issues)
```

### Deploy Server (Docker)

**On your VPS/Ubuntu server:**

```bash
# Clone repository
git clone https://github.com/pgwiz/fermm.git
cd fermm

# Copy .env.example and edit with your secrets
cp .env.example .env
nano .env  # Change passwords!

# Start all services
docker-compose up -d

# Verify
docker-compose ps
curl http://localhost:8000
```

**See** [setup.md](setup.md) **for 5-minute setup** or [md/UBUNTU-DEPLOYMENT-GUIDE.md](md/UBUNTU-DEPLOYMENT-GUIDE.md) **for detailed instructions.**

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

See [md/PHASE-6-OVERLAY-COMPLETE.md](md/PHASE-6-OVERLAY-COMPLETE.md) for overlay details.

## Documentation

All guides are in the [md/](md/) folder:
- **[setup.md](setup.md)** — Quick 5-minute setup (start here!)
- [md/UBUNTU-DEPLOYMENT-GUIDE.md](md/UBUNTU-DEPLOYMENT-GUIDE.md) — Detailed server setup
- [md/DOCKER-IMAGE-CONTENTS.md](md/DOCKER-IMAGE-CONTENTS.md) — What's in the Docker image
- [md/DEPLOYMENT-CHECKLIST.md](md/DEPLOYMENT-CHECKLIST.md) — Testing & go/no-go
- [md/PHASE-6-OVERLAY-COMPLETE.md](md/PHASE-6-OVERLAY-COMPLETE.md) — Overlay architecture
- [md/INDEX.md](md/INDEX.md) — Documentation index

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
