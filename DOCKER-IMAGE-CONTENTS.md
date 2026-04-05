# What's Included in popox15/fermm-server Docker Image

**Short Answer**: Yes, the API is included. The database is not (but easily added with docker-compose).

---

## Image Contents

### ✅ INCLUDED in `popox15/fermm-server:latest`

**FastAPI Backend**:
- Python 3.12 runtime
- FastAPI framework + Uvicorn server
- All Python dependencies (psycopg2, uvicorn, pydantic, etc.)
- Source code (main.py, routers, handlers, utils)

**API Routes**:
- Device registration and management
- Agent WebSocket endpoint (`/ws/devices/{device_id}`)
- Overlay control endpoints:
  - `POST /api/devices/{device_id}/overlay/spawn`
  - `POST /api/devices/{device_id}/overlay/close`
  - `POST /api/devices/{device_id}/overlay/message`
- All other FERMM API routes

**Features**:
- WebSocket relay (agent ↔ dashboard messaging)
- Real-time overlay synchronization
- JWT authentication
- Device discovery and registration
- Logging and error handling

**Size**: 302 MB (Alpine Linux base, optimized)

---

### ❌ NOT INCLUDED (separate container)

**PostgreSQL Database**:
- Database engine
- Data persistence
- User management

**Why Separate?**:
- Better scalability (separate database server)
- Database management independent of API
- Easy data persistence (Docker volume)
- Can run on different machines

---

## How They Connect

### Your Docker Network Architecture

```
┌─────────────────────────────────────┐
│         Docker Network              │
│      (fermm-network)                │
│                                     │
│  ┌──────────────┐  ┌────────────┐ │
│  │ fermm-server │  │   postgres │ │
│  │  (API)       │──│ (Database) │ │
│  │  Port 8000   │  │ Port 5432  │ │
│  └──────────────┘  └────────────┘ │
│                                     │
└─────────────────────────────────────┘
         ↓
    External Port 8000
```

---

## Quick Start on Ubuntu

**1. Create directory and .env**:
```bash
mkdir -p ~/fermm
cd ~/fermm

cat > .env << 'EOF'
POSTGRES_PASSWORD=secure_password
JWT_SECRET=your_jwt_secret_here
ADMIN_USERNAME=admin
ADMIN_PASSWORD=secure_password
EOF

chmod 600 .env
```

**2. Create docker-compose.yml** (see UBUNTU-DEPLOYMENT-GUIDE.md)

**3. Run**:
```bash
sudo docker-compose pull
sudo docker-compose up -d
```

**4. Verify**:
```bash
sudo docker-compose ps
curl http://localhost:8000
```

---

## What Happens When You Run It

### Container Startup Sequence

1. **postgres container starts first**
   - Initializes database (fermm_db)
   - Creates default user (fermm)
   - Waits until healthy (pg_isready passes)

2. **fermm-server container starts** (waits for postgres healthy)
   - Runs: `uvicorn main:app --host 0.0.0.0 --port 8000`
   - Connects to PostgreSQL at `fermm-postgres:5432`
   - Opens WebSocket server at `:8000/ws/*`
   - Exposes REST API at `:8000/api/*`

3. **Both ready** (typically 5-10 seconds)
   - API responds to requests
   - Agents can connect via WebSocket

---

## Key Environment Variables

**Set in `.env` file** (sourced by docker-compose):

| Variable | Purpose | Default |
|----------|---------|---------|
| `POSTGRES_PASSWORD` | DB password | fermm |
| `JWT_SECRET` | Auth token secret | change-me |
| `ADMIN_USERNAME` | Default admin user | admin |
| `ADMIN_PASSWORD` | Default admin password | admin |

---

## How Agent Connects

**On your Windows machine** (where fermm-agent.exe runs):

```bash
# Set server URL
set FERMM_SERVER_URL=http://your-ubuntu-ip:8000

# Run agent
fermm-agent.exe
```

Agent will:
1. Resolve `FERMM_SERVER_URL`
2. Connect to WebSocket: `ws://your-ubuntu-ip:8000/ws/devices/{device_id}`
3. Register with server (device_id, hostname, OS info)
4. Receive commands from dashboard
5. Execute overlay commands via Named Pipe IPC

---

## Common Commands

```bash
cd ~/fermm

# View logs
sudo docker-compose logs -f fermm-server

# Check status
sudo docker-compose ps

# Restart
sudo docker-compose restart

# Stop
sudo docker-compose stop

# Full cleanup (WARNING: deletes database!)
sudo docker-compose down -v
```

---

## Database Persistence

The `postgres_data` Docker volume persists across restarts:

```bash
# Data is stored in:
docker volume inspect fermm_postgres_data

# To backup:
sudo docker-compose exec fermm-postgres pg_dump -U fermm fermm > backup.sql

# To restore:
sudo docker-compose exec -T fermm-postgres psql -U fermm fermm < backup.sql
```

---

## Production Checklist

- [ ] Change POSTGRES_PASSWORD in .env
- [ ] Change JWT_SECRET in .env (32+ chars, random)
- [ ] Change ADMIN_PASSWORD in .env
- [ ] Set firewall rules (restrict access to port 8000 if needed)
- [ ] Configure reverse proxy (Nginx/Caddy) for HTTPS
- [ ] Set up SSL certificates (Let's Encrypt)
- [ ] Enable monitoring/logging
- [ ] Schedule database backups
- [ ] Test agent connection from Windows

---

## Troubleshooting

**Container won't start?**
```bash
sudo docker-compose logs fermm-server
```

**Database connection error?**
- Verify .env file has correct POSTGRES_PASSWORD
- Wait 30 seconds for postgres to initialize
- Check `sudo docker-compose logs fermm-postgres`

**Agent can't connect?**
- Verify Ubuntu IP: `hostname -I`
- Test API: `curl http://ubuntu-ip:8000`
- Check firewall: `sudo ufw status`
- Verify URL in agent: `set FERMM_SERVER_URL=http://ubuntu-ip:8000`

---

**Next Step**: Follow UBUNTU-DEPLOYMENT-GUIDE.md to deploy on your server!
