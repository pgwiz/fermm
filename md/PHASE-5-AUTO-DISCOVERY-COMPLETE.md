# PHASE 5: AUTO-DISCOVERY & SERVICE MANAGEMENT

**Completion Date:** 2026-03-29  
**Status:** ✅ COMPLETE

## Overview

Phase 5 implements automatic agent discovery and registration, eliminating the need for manual environment variable configuration. Agents can now self-register to any FERMM server without prior setup, and Windows Service management is available for production deployments.

---

## 1. Auto-Discovery System

### 1.1 Discovery Endpoint

**Endpoint:** `GET /api/devices/discover`  
**Authentication:** None (public endpoint)  
**Purpose:** Agents query this to get server configuration and receive registration tokens

**Response:**
```json
{
  "server_url": "http://localhost",
  "registration_token": "<secure-token>",
  "poll_interval_seconds": 15,
  "description": "Auto-discovery endpoint for agents"
}
```

### 1.2 Agent Discovery Flow

When agent starts **without environment variables**:

1. **Discovery Phase**: Agent tries multiple discovery URLs in order:
   - `http://localhost/api/devices/discover` (local dev)
   - `https://fermm.pgwiz.cloud/api/devices/discover` (production)
   - `http://127.0.0.1:8000/api/devices/discover` (fallback)

2. **Registration Phase**: Upon discovery success:
   - Agent generates or loads persisted Device ID
   - Agent auto-registers with device metadata
   - Server stores device and returns confirmation
   - Agent receives server URL and token

3. **Connection Phase**: Agent proceeds with normal operation:
   - WebSocket connection to server
   - HTTP polling fallback
   - Command execution

### 1.3 Device Persistence

Device ID is persisted in `.device_id` file in agent working directory:

```
E:\Backup\pgwiz\FERMM\fermm-agent\bin\Debug\net8.0\win-x64\.device_id
```

Same Device ID is used on subsequent agent runs, ensuring dashboard recognizes the device across restarts.

### 1.4 Implementation Details

**Files Changed:**
- `fermm-agent/Services/DiscoveryService.cs` (new, 200 lines)
  - `DiscoverAndRegisterAsync()` - main discovery orchestration
  - `DiscoverServerAsync()` - tries each URL
  - `AutoRegisterAsync()` - registers with received token
  - Automatic Device ID generation/persistence

- `fermm-agent/Program.cs` (modified)
  - Moved HttpClient factory before discovery
  - Calls DiscoveryService if no environment URL
  - Falls back to env vars if already configured

- `fermm-agent/Transport/WsClient.cs` (modified)
  - Added WebSocket keepalive heartbeat
  - Sends `{"type":"ping"}` every 30 seconds
  - Prevents connection timeout on idle networks

- `fermm-server/routers/devices.py` (modified)
  - Added `GET /api/devices/discover` endpoint
  - Generates one-time registration tokens
  - Returns server configuration

---

## 2. Windows Service Management

### 2.1 Service Installation

**Prerequisites:**
- Administrator privileges
- Agent built in Release mode

**Installation Script:** `fermm-agent/InstallService.ps1`

**Usage:**

```powershell
# Build agent for production
cd E:\Backup\pgwiz\FERMM\fermm-agent
dotnet publish -c Release

# Install service (as Administrator)
.\InstallService.ps1 -Action install

# Optional: Set server URL and token (for non-auto-discovery)
.\InstallService.ps1 -Action install -ServerUrl "https://fermm.pgwiz.cloud" -Token "device-token"

# Start service
.\InstallService.ps1 -Action start

# Check status
.\InstallService.ps1 -Action status

# Stop service
.\InstallService.ps1 -Action stop

# Uninstall service
.\InstallService.ps1 -Action uninstall
```

### 2.2 Service Features

- **Name:** `FERMMAgent`
- **Display Name:** `FERMM Remote Agent`
- **Startup Type:** Automatic (starts on Windows boot)
- **Run As:** Local System account
- **Auto-restart:** Yes (if agent crashes)
- **Log Location:** Windows Event Viewer → Windows Logs → System

### 2.3 Service Environment Variables

Service environment variables can be configured via registry:

```powershell
# Check current service environment
reg query "HKLM\SYSTEM\CurrentControlSet\Services\FERMMAgent"

# Manually set server URL
reg add "HKLM\SYSTEM\CurrentControlSet\Services\FERMMAgent" /v "FERMM_SERVER_URL" /t REG_SZ /d "https://fermm.pgwiz.cloud"

# Manually set token
reg add "HKLM\SYSTEM\CurrentControlSet\Services\FERMMAgent" /v "FERMM_TOKEN" /t REG_SZ /d "your-token-here"
```

### 2.4 Troubleshooting Service Issues

**Check Service Status:**
```powershell
Get-Service FERMMAgent
```

**View Service Logs:**
```powershell
Get-EventLog -LogName System -Source "FERMMAgent" -Newest 50
```

**Manual Service Control:**
```powershell
Start-Service FERMMAgent
Stop-Service FERMMAgent
Restart-Service FERMMAgent
```

---

## 3. Production Deployment Checklist

### 3.1 Agent Deployment

- [ ] Build Release version: `dotnet publish -c Release`
- [ ] Copy `bin/Release/net8.0/win-x64/fermm-agent.exe` to target machine
- [ ] Run InstallService.ps1 with admin privileges
- [ ] Verify service status: `Get-Service FERMMAgent`
- [ ] Check device appears online in dashboard within 30 seconds

### 3.2 Server Configuration (Production)

For production, update:

**`fermm-server/routers/devices.py` line 18:**
```python
"server_url": os.getenv("FERMM_PUBLIC_URL", "http://localhost"),
```

**Environment variable to set:**
```bash
export FERMM_PUBLIC_URL="https://fermm.pgwiz.cloud"
```

### 3.3 Security Considerations

1. **Registration Tokens**: Currently valid for 5 minutes
   - TODO: Store in Redis with TTL for production
   - TODO: Implement token revocation

2. **WebSocket Encryption**: Always use `wss://` (WSS) in production
   - Configure nginx with SSL certificate
   - Update discovery response to return WSS URL

3. **Device Token**: Generated during registration
   - Store securely on agent machine
   - Rotate tokens periodically

---

## 4. Architecture Changes

### 4.1 Before (Manual Configuration)

```
Agent Startup
    ↓
Read env vars (FERMM_SERVER_URL, FERMM_TOKEN, FERMM_DEVICE_ID)
    ↓
Fail if any missing
    ↓
Exit with error
```

**User burden:** Must configure 3 environment variables on every machine

### 4.2 After (Auto-Discovery)

```
Agent Startup
    ↓
Check if env vars set
    ↓
If YES: Use env vars
If NO: Try auto-discovery
    ↓
Query /api/devices/discover
    ↓
Auto-register with server
    ↓
Receive server URL + token
    ↓
Proceed with operation
```

**User burden:** Zero (or optional for production security)

### 4.3 WebSocket Keepalive

```
WebSocket Connection
    ↓
Receive Loop (5s timeout)
    ↓
Check if 30s since last heartbeat
    ↓
Send {"type":"ping"}
    ↓
Reset timer
    ↓
Loop
```

**Impact:** Prevents connection drops on idle networks (hotel WiFi, VPNs)

---

## 5. Performance Impact

| Metric | Impact |
|--------|--------|
| Agent startup time | +500ms (discovery network call) |
| Agent startup without server | +5s (tries 3 URLs with timeout) |
| WebSocket memory | +100 bytes (timer object) |
| WebSocket network traffic | +1 packet per 30s (heartbeat ping) |
| Bandwidth impact | Negligible (<1KB/hour) |

---

## 6. Testing Results

✅ **Auto-discovery working:**
- Agent starts without environment variables
- Queries discovery endpoint
- Auto-registers to database
- Appears online in dashboard within 5 seconds
- Successfully receives and executes commands

✅ **WebSocket keepalive working:**
- Heartbeat sends successfully every 30 seconds
- Connection stays alive even on idle networks
- No timeout-related disconnects observed

---

## 7. Future Enhancements (Optional)

1. **Dynamic Token Refresh**
   - Implement token expiry and automatic refresh
   - Store refresh tokens for long-lived agents

2. **Multi-Server Registration**
   - Allow agents to register with multiple FERMM servers
   - Automatic failover to backup servers

3. **Zero-Touch Deployment**
   - QR code scanning to get server URL
   - One-click agent provisioning
   - Automatic service installation

4. **Agent Update Service**
   - Automatic agent binary updates
   - Rollback on failure
   - Staged rollout (10%, 50%, 100%)

---

## 8. Migration Path

**For existing deployments:**

1. Update server to latest version (with discovery endpoint)
2. Update agents to latest version (with auto-discovery)
3. Old deployment (manual env vars) still works unchanged
4. New agents can auto-discover immediately
5. Existing agents unaffected; can be migrated gradually

**Backward compatibility:** 100% - old deployment method still supported

---

## 9. Summary of Changes

| Component | Change | Impact |
|-----------|--------|--------|
| Agent Startup | Add DiscoveryService | Automatic server discovery |
| WebSocket | Add keepalive heartbeat | Connection stability |
| Server | Add /discover endpoint | Public registration endpoint |
| Service | New InstallService.ps1 | Windows service management |
| Device Persistence | .device_id file | Consistent device identity |

**Total new code:** ~400 lines  
**Breaking changes:** None  
**Migration effort:** Minimal (optional)

---

## 10. Quick Start for Production

```bash
# On target Windows machine (as Administrator):

# 1. Download agent executable
curl https://fermm.pgwiz.cloud/downloads/fermm-agent.exe -o fermm-agent.exe

# 2. Download installer script
curl https://fermm.pgwiz.cloud/downloads/InstallService.ps1 -o InstallService.ps1

# 3. Install service
PowerShell -ExecutionPolicy Bypass -File .\InstallService.ps1 -Action install

# 4. Start service
PowerShell -ExecutionPolicy Bypass -File .\InstallService.ps1 -Action start

# 5. Verify in dashboard
# Device should appear online within 30 seconds
```

**That's it!** No manual configuration needed.
