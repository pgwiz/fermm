# FERMM — Full Orchestration Plan
**Fast Execution Remote Management Module**
`Rev 1.0 · 2026 · JetBrains Rider / Rider + PyCharm`

---

## Table of Contents

1. [What is FERMM](#1-what-is-fermm)
2. [Core Pillars](#2-core-pillars)
3. [System Architecture](#3-system-architecture)
4. [Agent Design](#4-agent-design)
5. [Server Layer](#5-server-layer)
6. [Communication Model](#6-communication-model)
7. [API Surface](#7-api-surface)
8. [Feature Modules](#8-feature-modules)
9. [Tech Stack](#9-tech-stack)
10. [Repo Structure](#10-repo-structure)
11. [Build Phases](#11-build-phases)
12. [Security Model](#12-security-model)
13. [Deploy Strategy](#13-deploy-strategy)
14. [Roadmap](#14-roadmap)

---

## 1. What is FERMM

FERMM is a **self-hosted, multiplatform Remote Management system** — your own private RMM built on infrastructure you own and control.

The system has three parts:

- **Agent** — a lightweight C# .NET 8 binary that runs silently on each device (Windows, Linux, macOS). No installer, no dependencies on the target machine, no UI.
- **Broker Server** — a FastAPI application running on your VPS (`pgwiz.cloud` or any server). Accepts commands from the dashboard, routes them to agents, persists results.
- **Dashboard** — a React SPA served by the server. Control all devices from any browser.

FERMM is **standalone** — it does not depend on any other system. It has its own Postgres database, its own Docker compose stack, and its own authentication layer.

---

## 2. Core Pillars

### Zero Friction Install
Single self-contained binary per platform. Drop it on a machine, set two environment variables (`FERMM_SERVER_URL` and `FERMM_TOKEN`), run it. No .NET runtime required on the target, no package managers, no admin portals.

### Hybrid Resilience
WebSocket push is the primary channel — sub-100ms command dispatch. If the WS drops (network change, sleep/wake, NAT timeout), the agent silently switches to HTTP polling every 15 seconds and keeps working. It reconnects the WebSocket in the background and switches back automatically.

### Private by Design
Your server, your data, your keys. JWT authentication on all endpoints, per-device tokens, TLS-only communication. No third-party cloud sees your device data.

### Modular Handlers
Each capability (shell, files, processes, screenshot) is an isolated handler class. Adding a new feature means writing one new handler — the dispatch system picks it up automatically.

---

## 3. System Architecture

```
┌─────────────────────┐         HTTPS / WSS          ┌──────────────────────────┐
│   Web Dashboard     │ ◄──────────────────────────► │   FERMM Server           │
│   (React SPA)       │                               │   (FastAPI on pgwiz.cloud│
│                     │                               │    or any VPS)           │
└─────────────────────┘                               └────────────┬─────────────┘
                                                                   │
                                                         HTTPS / WSS (agent-initiated)
                                                                   │
                              ┌────────────────────────────────────┴──────────────────────┐
                              │                                                            │
                    ┌─────────▼──────────┐                                    ┌───────────▼─────────┐
                    │  Windows Agent     │                                    │  Linux Agent         │
                    │  fermm-agent.exe   │                                    │  fermm-agent         │
                    │  (Windows Service) │                                    │  (systemd unit)      │
                    └────────────────────┘                                    └─────────────────────┘
                                                          +
                                                ┌─────────────────────┐
                                                │  macOS Agent        │
                                                │  fermm-agent        │
                                                │  (LaunchAgent)      │
                                                └─────────────────────┘
```

**Key design decisions:**

- Agents are **outbound-only** — they initiate connections to the server. No inbound ports need to be opened on device firewalls or home routers. Works behind NAT.
- The server is the **only public-facing endpoint**. All communication flows through it.
- The dashboard has **no direct connection to agents** — it talks only to the server.

---

## 4. Agent Design

The agent is a C# .NET 8 `Worker Service` — a headless background process with no UI. Compiled as a single self-contained binary for each target platform.

### Internal Structure

```
AgentService (BackgroundService)
│
├── Startup
│   ├── Read config (env vars / appsettings.json)
│   ├── Register device with server (POST /api/devices/register)
│   └── Start main loop
│
├── Main Loop
│   ├── [PRIMARY] Connect WebSocket → RunWebSocketLoop()
│   └── [FALLBACK] On WS failure → RunPollingLoop()
│                   + background WS reconnect attempt
│
├── Command Dispatcher
│   └── HandleCommand(AgentCommand cmd) → switch on cmd.Type
│       ├── "shell"       → ShellHandler.Execute()
│       ├── "screenshot"  → ScreenshotHandler.Capture()
│       ├── "processes"   → ProcessHandler.List()
│       ├── "kill"        → ProcessHandler.Kill(pid)
│       ├── "ls"          → FileHandler.ListDir(path)
│       ├── "upload"      → FileHandler.UploadToServer()
│       ├── "download"    → FileHandler.DownloadFromPath()
│       └── "sysinfo"     → SysInfoHandler.Collect()
│
└── Result Transport
    ├── WS stream (real-time output lines)
    └── POST /api/devices/{id}/results (polling mode)
```

### Platform Behaviour

| Platform | Shell | Service | Screenshot |
|----------|-------|---------|------------|
| Windows | `cmd.exe /c` | Windows Service (`sc create`) | `Graphics.CopyFromScreen` |
| Linux | `/bin/bash -c` | systemd unit | `scrot` or `gnome-screenshot` |
| macOS | `/bin/zsh -c` | LaunchAgent plist | `screencapture -x` |

### Key Files

| File | Purpose |
|------|---------|
| `AgentService.cs` | Core `BackgroundService` loop — WS + poll hybrid |
| `WsClient.cs` | WebSocket connection lifecycle + reconnect policy |
| `PollClient.cs` | HTTP polling fallback — GET pending, POST results |
| `CommandDispatcher.cs` | Routes `AgentCommand` to the correct handler |
| `AgentConfig.cs` | Config model — loaded from env vars or `appsettings.json` |
| `Models/AgentCommand.cs` | `record AgentCommand(string CommandId, string Type, string? Payload)` |

---

## 5. Server Layer

The FERMM server is a FastAPI application. It acts as the **command broker** — it does not execute anything itself, it routes.

### Responsibilities

- Accept commands from the dashboard
- Push commands to online agents via WebSocket
- Queue commands for offline agents (polling fallback)
- Receive and persist command results
- Serve the React dashboard as static files
- Handle file transfer between dashboard and agents

### Internal Structure

```
Nginx (TLS termination)
    │
    ▼
FastAPI App
    ├── Middleware: JWT auth, CORS, rate limiting
    ├── routers/auth.py       — token issue + verify
    ├── routers/devices.py    — register, list, detail, deregister
    ├── routers/commands.py   — dispatch, pending poll, results
    ├── routers/files.py      — chunked upload/download
    └── routers/ws.py         — agent WS + dashboard WS
    │
    ├── ws_manager.py         — ConnectionManager (device_id → WebSocket map)
    └── database.py           — asyncpg + SQLAlchemy 2.0 async engine
    │
    ▼
PostgreSQL
    ├── devices          — registered devices, last_seen, online status
    ├── command_queue    — queued commands for offline agents
    └── command_results  — persisted results + audit log
```

### Database Schema (simplified)

```sql
-- Devices
CREATE TABLE devices (
    id          TEXT PRIMARY KEY,       -- UUID, set by agent on first run
    hostname    TEXT NOT NULL,
    os          TEXT NOT NULL,
    arch        TEXT,
    ip          TEXT,
    token_hash  TEXT NOT NULL,          -- hashed device token
    online      BOOLEAN DEFAULT false,
    last_seen   TIMESTAMPTZ,
    registered_at TIMESTAMPTZ DEFAULT NOW()
);

-- Command Queue (for polling fallback)
CREATE TABLE command_queue (
    id          TEXT PRIMARY KEY,
    device_id   TEXT REFERENCES devices(id),
    payload     JSONB NOT NULL,
    created_at  TIMESTAMPTZ DEFAULT NOW()
);

-- Results (audit log + async result retrieval)
CREATE TABLE command_results (
    id          TEXT PRIMARY KEY,
    command_id  TEXT NOT NULL,
    device_id   TEXT REFERENCES devices(id),
    result      JSONB NOT NULL,
    created_at  TIMESTAMPTZ DEFAULT NOW()
);
```

---

## 6. Communication Model

### Primary — WebSocket Push

```
Dashboard                Server                    Agent
   │                        │                         │
   │  POST /command         │                         │
   │ ─────────────────────► │                         │
   │                        │  ws.send(cmd JSON)      │
   │                        │ ───────────────────────►│
   │                        │                         │ Execute
   │                        │  ws.send(output lines)  │
   │                        │ ◄───────────────────────│
   │  dashboard WS stream   │                         │
   │ ◄───────────────────── │                         │
```

- Agent connects once on boot: `wss://fermm.pgwiz.cloud/ws/agent/{device_id}`
- Dashboard gets its own WS: `wss://fermm.pgwiz.cloud/ws/dashboard/{session_id}`
- Server fans out agent output to the relevant dashboard session
- Shell output streams **line by line** — real-time terminal experience in `xterm.js`
- Heartbeat ping every 30 seconds to keep the connection alive through NAT

### Fallback — HTTP Polling

Activates automatically when the WS drops:

```
Agent                           Server
  │                                │
  │  GET /api/devices/{id}/pending │   (every 15s)
  │ ─────────────────────────────► │
  │  [ list of queued commands ]   │
  │ ◄───────────────────────────── │
  │                                │
  │  Execute commands locally      │
  │                                │
  │  POST /api/devices/{id}/results│
  │ ─────────────────────────────► │
  │  { ok }                        │
  │ ◄───────────────────────────── │
```

- Polling interval: 15s normal → 30s → 60s with exponential backoff on errors
- WS reconnect attempted in background on a separate timer
- Agent switches back to WS automatically on successful reconnect — no restart needed

### Command JSON Format

```json
{
  "command_id": "uuid-v4",
  "type": "shell",
  "payload": "ls -la /var/log",
  "timeout_seconds": 30
}
```

### Result JSON Format

```json
{
  "command_id": "uuid-v4",
  "device_id": "device-uuid",
  "type": "shell",
  "exit_code": 0,
  "output": ["line1", "line2"],
  "error": null,
  "duration_ms": 142,
  "timestamp": "2026-01-01T00:00:00Z"
}
```

---

## 7. API Surface

All endpoints require `Authorization: Bearer <token>` header.

### Auth

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/auth/token` | Issue dashboard JWT (username + password) |
| `POST` | `/api/auth/refresh` | Refresh expired JWT |

### Devices

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/devices/register` | Agent self-registration, upserts device record |
| `GET` | `/api/devices` | List all devices with online status |
| `GET` | `/api/devices/{id}` | Single device detail — hostname, OS, last seen |
| `DELETE` | `/api/devices/{id}` | Deregister device, revoke its token |
| `GET` | `/api/devices/{id}/sysinfo` | Latest collected system info |

### Commands

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/devices/{id}/command` | Dispatch command — WS push or queue fallback |
| `GET` | `/api/devices/{id}/pending` | Agent polls for queued commands |
| `POST` | `/api/devices/{id}/results` | Agent POSTs command result |
| `GET` | `/api/commands/{cmd_id}/result` | Dashboard polls for async result |
| `GET` | `/api/devices/{id}/history` | Command history for a device |

### Files

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/devices/{id}/upload` | Push file from dashboard to agent (chunked multipart) |
| `GET` | `/api/devices/{id}/download` | Pull file from agent through server to dashboard |

### WebSockets

| Channel | Path | Description |
|---------|------|-------------|
| `WS` | `/ws/agent/{device_id}` | Agent persistent control channel |
| `WS` | `/ws/dashboard/{session_id}` | Dashboard — receives live agent output streams |

---

## 8. Feature Modules

### Shell Execution
- Spawn `cmd.exe /c` on Windows, `/bin/bash -c` on Linux, `/bin/zsh -c` on macOS
- Redirect stdout + stderr with `BeginOutputReadLine()` / `BeginErrorReadLine()`
- Each output line is sent as a WS frame immediately — live streaming, no buffering
- `xterm.js` on the dashboard renders output in a real terminal emulator
- Interactive shell sessions (PTY) are a Phase 5 enhancement

### Screenshot Capture
- **Windows**: `Graphics.CopyFromScreen` captures all monitors combined into one PNG
- **Linux**: Calls `scrot {tmp}.png` or `gnome-screenshot -f {tmp}.png` via shell, reads the file, deletes it
- **macOS**: `screencapture -x {tmp}.png` (silent, no sound)
- Result is base64-encoded PNG sent back as a command result
- Dashboard renders inline, with timestamp and download button

### Process Manager
- `Process.GetProcesses()` returns all running processes cross-platform
- CPU% requires `PerformanceCounter` on Windows or reading `/proc/{pid}/stat` on Linux
- Kill: `Process.GetProcessById(pid).Kill()` — confirms PID exists before kill
- Response is a JSON array of `{ pid, name, cpuPercent, memoryMb, status }`

### File Browser
- `DirectoryInfo(path).GetFileSystemInfos()` for directory listing
- Response: `{ dirs: [...], files: [{ name, size, modified }] }`
- Upload: dashboard sends `POST /api/devices/{id}/upload` with multipart form → server streams to agent via WS command with base64 chunks → agent writes to path
- Download: agent reads file, base64 encodes, sends back as result payload (chunked for large files)

### System Info
- Hostname, OS version, architecture, uptime
- Total/used/free RAM
- Disk usage per mount point
- Network interfaces + IP addresses
- Sent on registration and cached server-side, refreshable on demand

---

## 9. Tech Stack

### Agent — C# .NET 8

| Package | Purpose |
|---------|---------|
| `Microsoft.Extensions.Hosting` | Worker Service host + DI container |
| `Microsoft.Extensions.Http` | `HttpClient` factory for poll requests |
| `System.Net.WebSockets` | Built-in WS client — no extra packages |
| `System.Drawing.Common` | Windows-only, screenshot via GDI+ |
| `Microsoft.Extensions.Hosting.WindowsServices` | Windows Service registration |
| `Microsoft.Extensions.Hosting.Systemd` | Linux systemd integration |

**Why C# .NET 8:** Worker Service pattern maps perfectly to a background agent. `dotnet publish --self-contained` produces a single binary with zero dependencies on the target. Excellent async support, great cross-platform stdlib, and JetBrains Rider has first-class .NET support.

### Server — Python / FastAPI

| Package | Purpose |
|---------|---------|
| `fastapi` | Async web framework, WebSocket support built-in |
| `uvicorn[standard]` | ASGI server |
| `asyncpg` | Async Postgres driver |
| `sqlalchemy[asyncio]` | ORM + async session |
| `alembic` | Schema migrations |
| `python-jose[cryptography]` | JWT creation + verification |
| `passlib[bcrypt]` | Password + token hashing |
| `slowapi` | Rate limiting middleware |
| `python-multipart` | File upload support |

### Dashboard — React

| Package | Purpose |
|---------|---------|
| `react` + `vite` | SPA framework + dev server |
| `xterm.js` | Full terminal emulator — renders live shell output |
| `@xterm/addon-fit` | Auto-resize terminal to container |
| `@tanstack/react-query` | Server state — device list, results polling |
| `zustand` | Client state — active device, WS status, UI |
| `tailwindcss` | Utility-first styling |
| `lucide-react` | Icon set |

### Infrastructure

| Tool | Purpose |
|------|---------|
| Docker + Docker Compose | Single `compose up` deploys everything |
| PostgreSQL 16 | Primary data store |
| Nginx | TLS termination (Let's Encrypt), reverse proxy, static files |
| GitHub Actions | CI — builds all 3 agent binaries on push to `main` |

### JetBrains Setup

| Tool | Used For |
|------|---------|
| **Rider** | C# agent development — `fermm-agent/` |
| **PyCharm** | FastAPI server development — `fermm-server/` |
| **WebStorm** (or Rider) | React dashboard — `fermm-dashboard/` |

Rider handles the `.csproj` natively, including the cross-compile publish profiles. Set up **Run Configurations** in Rider for each publish target:

```
Name: Publish Windows
Command: dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

Name: Publish Linux
Command: dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

Name: Publish macOS
Command: dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

---

## 10. Repo Structure

Single monorepo — three independently deployable packages.

```
fermm/
├── fermm-agent/                    # C# .NET 8 Worker Service
│   ├── fermm-agent.csproj
│   ├── Program.cs                  # IHost bootstrap
│   ├── AgentService.cs             # Core BackgroundService loop
│   ├── CommandDispatcher.cs        # Routes commands to handlers
│   ├── AgentConfig.cs              # Config model
│   ├── Handlers/
│   │   ├── ShellHandler.cs
│   │   ├── ScreenshotHandler.cs
│   │   ├── ProcessHandler.cs
│   │   ├── FileHandler.cs
│   │   └── SysInfoHandler.cs
│   ├── Transport/
│   │   ├── WsClient.cs             # WebSocket connection + reconnect
│   │   └── PollClient.cs           # HTTP polling fallback
│   ├── Models/
│   │   ├── AgentCommand.cs
│   │   └── CommandResult.cs
│   ├── appsettings.json            # Default config (override with env vars)
│   └── dist/                       # Build output (gitignored)
│       ├── windows/                # fermm-agent.exe
│       ├── linux/                  # fermm-agent
│       └── macos/                  # fermm-agent
│
├── fermm-server/                   # FastAPI broker
│   ├── main.py                     # FastAPI app + lifespan
│   ├── database.py                 # asyncpg engine + session
│   ├── auth.py                     # JWT logic
│   ├── ws_manager.py               # ConnectionManager class
│   ├── routers/
│   │   ├── auth.py
│   │   ├── devices.py
│   │   ├── commands.py
│   │   ├── files.py
│   │   └── ws.py
│   ├── models/
│   │   ├── schemas.py              # Pydantic models
│   │   └── db.py                   # SQLAlchemy ORM models
│   ├── migrations/                 # Alembic migration files
│   │   └── env.py
│   ├── requirements.txt
│   └── Dockerfile
│
├── fermm-dashboard/                # React + Vite SPA
│   ├── src/
│   │   ├── App.tsx
│   │   ├── main.tsx
│   │   ├── api/                    # API client functions
│   │   │   ├── devices.ts
│   │   │   ├── commands.ts
│   │   │   └── ws.ts
│   │   ├── pages/
│   │   │   ├── DeviceGrid.tsx
│   │   │   ├── Terminal.tsx
│   │   │   ├── FileBrowser.tsx
│   │   │   ├── Processes.tsx
│   │   │   └── Screenshot.tsx
│   │   ├── hooks/
│   │   │   ├── useDeviceWs.ts
│   │   │   └── useCommands.ts
│   │   └── store/
│   │       └── appStore.ts         # Zustand store
│   ├── package.json
│   └── vite.config.ts
│
├── docker-compose.yml              # Full stack deploy
├── nginx.conf                      # Reverse proxy config
├── .github/
│   └── workflows/
│       └── build-agent.yml         # CI — builds all 3 binaries
└── README.md
```

---

## 11. Build Phases

### Phase 1 — Foundation (1–2 weeks)
*Goal: reliable two-way communication confirmed end-to-end. No features yet.*

- [ ] Scaffold C# Worker Service: `dotnet new worker -n fermm-agent` in Rider
- [ ] Implement `AgentService.cs` — WS connect + poll fallback loop
- [ ] Implement `WsClient.cs` — connect, receive, send, reconnect with backoff
- [ ] Implement `PollClient.cs` — GET pending, POST results
- [ ] Scaffold FastAPI server with `/register`, `/pending`, `/results`, `/ws/agent/{id}`
- [ ] Create Postgres schema — `devices`, `command_queue`, `command_results` tables
- [ ] Implement `ConnectionManager` — device_id → WebSocket map
- [ ] JWT middleware — issue token, verify on all routes
- [ ] Docker Compose with server + Postgres + Nginx
- [ ] Deploy server to pgwiz.cloud, compile agent for Windows, confirm handshake end-to-end
- [ ] **Checkpoint:** agent registers → WS connects → send a test ping → receive pong → verify in Postgres

### Phase 2 — Core Feature Modules (2–3 weeks)
*Goal: all 4 primary features working, testable via Postman/curl.*

- [ ] `ShellHandler.cs` — cross-platform shell, live stdout/stderr streaming over WS
- [ ] `ScreenshotHandler.cs` — Windows GDI+, Linux scrot, macOS screencapture
- [ ] `ProcessHandler.cs` — list + kill, CPU/RAM stats per process
- [ ] `FileHandler.cs` — directory listing, upload (dashboard→device), download (device→dashboard)
- [ ] `SysInfoHandler.cs` — hostname, OS, RAM, disk, network interfaces
- [ ] Wire all handlers into `CommandDispatcher`
- [ ] File transfer endpoints on server — `POST /upload`, `GET /download`
- [ ] Compile and test on Windows dev machine AND Linux server
- [ ] **Checkpoint:** Postman can send each command type, results come back correctly on both platforms

### Phase 3 — React Dashboard (2–3 weeks)
*Goal: fully usable system with a real UI.*

- [ ] Vite + React 18 + Tailwind scaffold — `fermm-dashboard/`
- [ ] Device grid page — online/offline badges, last seen, OS icon, quick actions
- [ ] Dashboard WebSocket — `GET /ws/dashboard/{session_id}`, receive live output
- [ ] Terminal panel — `xterm.js` integration, command input, live output rendering
- [ ] File browser — directory tree, upload dropzone, download + delete
- [ ] Process manager — sortable table, kill button with PID confirm, refresh
- [ ] Screenshot viewer — capture button, inline PNG render, download
- [ ] Auth — login page, JWT stored in memory (not localStorage), auto-refresh
- [ ] Serve dashboard as static files from FastAPI (`app.mount("/", StaticFiles(...))`)
- [ ] **Checkpoint:** Full end-to-end workflow — login → select device → run shell → see live output

### Phase 4 — Hardening + Advanced (ongoing)
*Goal: production-ready. System is already usable at Phase 3.*

- [ ] Log tail module — `tail -f` equivalent, streams file lines over WS continuously
- [ ] Scheduled commands — cron-like scheduler per device, configured from dashboard
- [ ] Multi-device broadcast — dispatch same command to all selected devices simultaneously
- [ ] Command history UI — searchable audit log with timestamps, filterable by device/type
- [ ] Clipboard sync — push text to device clipboard, read device clipboard
- [ ] Port scanner module — list open ports + listening services per device
- [ ] GitHub Actions CI — auto-build all 3 binaries on push to `main`, attach to release
- [ ] Alembic migrations setup — structured schema versioning

### Phase 5 — Vision Features (backlog)
*Ideas for later — system is complete without these.*

- [ ] Interactive PTY shell (proper terminal — tab completion, arrow keys, Ctrl+C)
- [ ] Screen recording — not just screenshots, video capture over time
- [ ] VPN/tunnel — peer-to-peer device-to-device tunnel via server relay
- [ ] Webhook triggers — fire HTTP webhooks on device events (offline, disk full, etc.)
- [ ] Ollama/GPU management — start/stop/monitor Ollama on your Windows machine remotely
- [ ] Lab mode — monitor multiple machines simultaneously (TVET lab use case)

---

## 12. Security Model

### Threat Model
FERMM is personal infrastructure. The primary threat is **unauthorized external access** — not insider threat. The security layer is pragmatic and proportional.

### Authentication

**Dashboard users** authenticate with username + password → receive a short-lived JWT (1 hour). JWT is stored in memory only (not `localStorage`) and refreshed automatically.

**Agents** use a **per-device token** — a long-lived secret generated at registration and stored hashed in Postgres. The agent sends it on the `Authorization` header for every request and on the initial WebSocket upgrade.

If a device is decommissioned or compromised, revoke only its token — all other devices are unaffected.

### Transport Security

- Nginx terminates TLS with a Let's Encrypt certificate on `fermm.pgwiz.cloud`
- HTTP → HTTPS redirect enforced at Nginx level
- WebSockets use WSS only — `wss://` never `ws://`
- No plaintext communication at any point in the system

### Network Exposure

- Agents are **outbound-only**. They initiate connections to the server.
- No ports need to be opened on device firewalls, home routers, or corporate NAT
- Only the server (pgwiz.cloud) has a public-facing port (443)

### Rate Limiting

`slowapi` applies rate limits on sensitive endpoints:

| Endpoint | Limit |
|----------|-------|
| `POST /api/auth/token` | 10 req/min per IP |
| `POST /api/devices/register` | 5 req/min per IP |
| `POST /api/devices/{id}/command` | 60 req/min per token |

### Audit Log

Every command dispatched and every result received is persisted to `command_results` in Postgres with a timestamp and device ID. Full audit trail, queryable from the dashboard history page.

---

## 13. Deploy Strategy

### Server (once, on your VPS)

```bash
# On pgwiz.cloud (or any Ubuntu server)
git clone https://github.com/pgwiz/fermm.git
cd fermm

# Configure environment
cp .env.example .env
# Edit .env: set POSTGRES_PASSWORD, JWT_SECRET, FERMM_ADMIN_PASSWORD

# Deploy
docker compose up -d

# Run migrations
docker compose exec fermm-server alembic upgrade head

# Dashboard is now live at https://fermm.pgwiz.cloud
```

### Agent — Windows

```powershell
# Download from GitHub Releases
Invoke-WebRequest -Uri "https://github.com/pgwiz/fermm/releases/latest/download/fermm-agent-win-x64.exe" `
  -OutFile "C:\fermm-agent.exe"

# Set environment variables (persist across reboots)
[System.Environment]::SetEnvironmentVariable("FERMM_SERVER_URL", "https://fermm.pgwiz.cloud", "Machine")
[System.Environment]::SetEnvironmentVariable("FERMM_TOKEN", "your-device-token", "Machine")

# Register and start as Windows Service (run as Administrator)
sc.exe create FERMMAgent binPath= "C:\fermm-agent.exe" start= auto
sc.exe start FERMMAgent

# View logs
Get-EventLog -LogName Application -Source FERMMAgent -Newest 50
```

### Agent — Linux

```bash
# Download from GitHub Releases
wget https://github.com/pgwiz/fermm/releases/latest/download/fermm-agent-linux-x64 -O /usr/local/bin/fermm-agent
chmod +x /usr/local/bin/fermm-agent

# Create systemd unit
cat > /etc/systemd/system/fermm-agent.service << EOF
[Unit]
Description=FERMM Remote Management Agent
After=network.target

[Service]
Type=simple
ExecStart=/usr/local/bin/fermm-agent
Restart=always
RestartSec=10
Environment=FERMM_SERVER_URL=https://fermm.pgwiz.cloud
Environment=FERMM_TOKEN=your-device-token

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable fermm-agent
systemctl start fermm-agent

# View logs
journalctl -u fermm-agent -f
```

### Agent — macOS

```bash
# Download
curl -L https://github.com/pgwiz/fermm/releases/latest/download/fermm-agent-osx-arm64 \
  -o /usr/local/bin/fermm-agent
chmod +x /usr/local/bin/fermm-agent

# Create LaunchAgent plist
cat > ~/Library/LaunchAgents/cloud.pgwiz.fermm.plist << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>cloud.pgwiz.fermm</string>
  <key>ProgramArguments</key>
  <array><string>/usr/local/bin/fermm-agent</string></array>
  <key>EnvironmentVariables</key>
  <dict>
    <key>FERMM_SERVER_URL</key><string>https://fermm.pgwiz.cloud</string>
    <key>FERMM_TOKEN</key><string>your-device-token</string>
  </dict>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
</dict>
</plist>
EOF

launchctl load ~/Library/LaunchAgents/cloud.pgwiz.fermm.plist
```

### GitHub Actions CI — Auto-Build Binaries

```yaml
# .github/workflows/build-agent.yml
name: Build Agent Binaries

on:
  push:
    branches: [main]
    tags: ['v*']

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Build Windows
        run: |
          dotnet publish fermm-agent -c Release -r win-x64 \
            --self-contained true -p:PublishSingleFile=true \
            -o dist/windows

      - name: Build Linux
        run: |
          dotnet publish fermm-agent -c Release -r linux-x64 \
            --self-contained true -p:PublishSingleFile=true \
            -o dist/linux

      - name: Build macOS
        run: |
          dotnet publish fermm-agent -c Release -r osx-arm64 \
            --self-contained true -p:PublishSingleFile=true \
            -o dist/macos

      - name: Create Release
        if: startsWith(github.ref, 'refs/tags/')
        uses: softprops/action-gh-release@v1
        with:
          files: |
            dist/windows/fermm-agent.exe
            dist/linux/fermm-agent
            dist/macos/fermm-agent
```

---

## 14. Roadmap

### 🔴 Now — Phase 1–3

| Area | Item |
|------|------|
| Agent | WS + poll hybrid comms |
| Agent | Remote shell execution (streaming) |
| Agent | Screenshot capture (all platforms) |
| Agent | Process list + kill |
| Agent | File browser + transfer |
| Agent | System info collection |
| Server | JWT auth + per-device tokens |
| Server | Command queue + WS dispatch |
| Server | File transfer endpoints |
| Dashboard | Device grid with status |
| Dashboard | `xterm.js` live terminal |
| Dashboard | File browser UI |
| Dashboard | Process manager UI |
| Dashboard | Screenshot viewer |
| Infra | Docker Compose full stack |
| Infra | Nginx + Let's Encrypt TLS |

### 🟡 Soon — Phase 4

| Area | Item |
|------|------|
| Agent | Log tail — `tail -f` over WS |
| Agent | Clipboard sync |
| Server | Scheduled commands (cron per device) |
| Server | Multi-device broadcast |
| Dashboard | Command history + audit log UI |
| Dashboard | Webhook triggers on events |
| Infra | GitHub Actions CI — auto-build binaries |
| Infra | Alembic migrations |

### 🟢 Later — Phase 5+

| Area | Item |
|------|------|
| Agent | Port scanner module |
| Agent | Interactive PTY shell |
| Agent | Ollama/GPU management (remote start/stop) |
| Agent | Screen recording |
| Server | Device groups + tags |
| Server | VPN/tunnel relay between devices |
| Dashboard | Dark/light theme toggle |
| Dashboard | Multi-device split view |
| Mobile | PWA dashboard |

### 🔵 Someday — Vision

| Area | Item |
|------|------|
| Agent | Keyboard/mouse injection (full remote control) |
| Agent | Hardware sensor monitoring (temps, fan speeds) |
| Server | Kubernetes deploy for fleet scale |
| Lab | Multi-machine simultaneous monitoring (TVET lab management) |

---

## Quick Reference — Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `FERMM_SERVER_URL` | ✅ | Base URL of the FERMM server, e.g. `https://fermm.pgwiz.cloud` |
| `FERMM_TOKEN` | ✅ | Per-device auth token, issued from server at registration |
| `FERMM_DEVICE_ID` | ❌ | Optional. If not set, auto-generated UUID on first run and persisted |
| `FERMM_POLL_INTERVAL_SECONDS` | ❌ | Default: 15. Polling interval in fallback mode |
| `FERMM_LOG_LEVEL` | ❌ | Default: `Information`. Options: `Debug`, `Information`, `Warning`, `Error` |

---

*Start with Phase 1. First command: `dotnet new worker -n fermm-agent` in Rider.*
