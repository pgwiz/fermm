# FERMM First-Run Setup Guide

Welcome! This guide will get your FERMM server running in **5 minutes**.

---

## Prerequisites

You need:
- Ubuntu server (18.04+)
- Docker installed (`docker --version`)
- Docker Compose v2+ (`docker-compose --version`)
- 4GB+ RAM, 10GB+ disk space

**Don't have Docker?** Install it:
```bash
sudo apt update && sudo apt install -y docker.io docker-compose
sudo usermod -aG docker $USER
```

---

## Step 1: Clone Repository

```bash
git clone https://github.com/pgwiz/fermm.git
cd fermm
```

---

## Step 2: Create .env File

**Create `.env` with secure passwords:**

```bash
cat > .env << 'EOF'
# Database
POSTGRES_PASSWORD=secure_db_password_change_me_12345

# JWT Secret (must be 32+ characters, random)
JWT_SECRET=your_super_secure_jwt_secret_change_me_abcdef123456789

# Admin credentials
ADMIN_USERNAME=admin
ADMIN_PASSWORD=secure_admin_password_change_me

# Logging (optional)
LOG_LEVEL=INFO
DEBUG=false
EOF

# Secure the file
chmod 600 .env

# Verify
cat .env
```

**⚠️ IMPORTANT**: Change all passwords before production!

---

## Step 3: Start All Services

```bash
# Use the Ubuntu-specific compose file
sudo docker-compose -f docker-compose.ubuntu.yml up -d

# Or use the standard one (works the same)
sudo docker-compose up -d
```

---

## Step 4: Verify Services

**Check if all services are running:**

```bash
sudo docker-compose ps
```

Expected output:
```
NAME              IMAGE                           STATUS
fermm-server      popox15/fermm-server:latest     Up (healthy)
fermm-postgres    postgres:16-alpine              Up (healthy)
fermm-nginx       nginx:alpine                    Up
```

**Test the API:**

```bash
curl http://localhost:8000
```

You should see an API response (not an error).

---

## Step 5: Access Dashboard

Open your browser:
```
http://your-server-ip
```

**Login with:**
- Username: `admin` (from .env)
- Password: `your-admin-password` (from .env)

---

## Step 6: Deploy Agent on Windows

On your Windows machine, copy the agent binary:

```powershell
# Set server URL (IMPORTANT: use your Ubuntu server IP)
[Environment]::SetEnvironmentVariable(
  "FERMM_SERVER_URL",
  "http://your-ubuntu-server-ip:8000",
  "Machine"
)

# Run agent (get binary from GitHub releases or build)
C:\Program Files\FERMM\fermm-agent.exe
```

The agent will register itself in the dashboard.

---

## Common Issues & Fixes

### Services won't start
```bash
# View logs
sudo docker-compose logs fermm-server

# Restart everything
sudo docker-compose restart
```

### Can't connect to API
```bash
# Check if port 8000 is open
sudo ufw allow 8000/tcp

# Test connectivity
curl -v http://localhost:8000
```

### Database error
Wait 15 seconds for PostgreSQL to initialize:
```bash
# Check DB status
sudo docker-compose logs fermm-postgres
```

### Reset everything (WARNING: deletes data)
```bash
sudo docker-compose down -v
rm .env
# Then start from Step 2
```

---

## File Structure

```
fermm/
├── setup.md                    ← You are here
├── README.md                   ← Project overview
├── md/                         ← Documentation (read these)
│   ├── PHASE-6-OVERLAY-COMPLETE.md
│   ├── UBUNTU-DEPLOYMENT-GUIDE.md
│   └── [other guides]
├── docker-compose.yml          ← Standard compose
├── docker-compose.ubuntu.yml   ← Ubuntu-optimized compose
├── .env                        ← Your secrets (create this!)
├── fermm-agent/                ← Agent source code
├── fermm-server/               ← Server source code
├── fermm-dashboard/            ← Dashboard source code
└── scripts/                    ← Build & deployment scripts
```

---

## Next Steps

### 1. Explore Documentation
Read guides in the `md/` folder:
- `md/UBUNTU-DEPLOYMENT-GUIDE.md` — Detailed setup
- `md/PHASE-6-OVERLAY-COMPLETE.md` — Overlay feature
- `md/DEPLOYMENT-CHECKLIST.md` — Testing checklist

### 2. Test the Overlay Feature
1. In dashboard, navigate to a device
2. Click "Overlay" tab
3. Click "Spawn Overlay"
4. Overlay window appears on Windows machine
5. Type in dashboard → message syncs to overlay

### 3. Configure HTTPS (Production)
```bash
# Enable SSL profile
sudo docker-compose --profile ssl up -d

# OR manually with Let's Encrypt
sudo apt install certbot
certbot certonly --standalone -d your-domain.com
```

### 4. Backup Database
```bash
# Backup
sudo docker-compose exec fermm-postgres pg_dump -U fermm fermm > backup.sql

# Restore
sudo docker-compose exec -T fermm-postgres psql -U fermm fermm < backup.sql
```

### 5. Monitor Logs
```bash
# Real-time logs
sudo docker-compose logs -f fermm-server

# Last 50 lines
sudo docker-compose logs --tail=50 fermm-server
```

---

## Useful Commands

```bash
# Stop services
sudo docker-compose stop

# Restart services
sudo docker-compose restart

# View all logs
sudo docker-compose logs

# Update image
sudo docker-compose pull
sudo docker-compose up -d

# Clean up
sudo docker-compose down

# Check disk usage
docker system df
```

---

## Security Checklist

- [ ] Changed POSTGRES_PASSWORD in .env
- [ ] Changed JWT_SECRET in .env (32+ chars, random)
- [ ] Changed ADMIN_PASSWORD in .env
- [ ] Restricted port 8000 with firewall
- [ ] Enabled HTTPS (for production)
- [ ] Set up database backups
- [ ] Reviewed logs for errors
- [ ] Tested agent connection

---

## Getting Help

**Documentation** (in `md/` folder):
- Setup issues? → `md/UBUNTU-DEPLOYMENT-GUIDE.md`
- Technical details? → `md/PHASE-6-OVERLAY-COMPLETE.md`
- Testing? → `md/DEPLOYMENT-CHECKLIST.md`

**Quick commands**:
```bash
# Start fresh
cd ~/fermm
sudo docker-compose down -v
sudo docker-compose up -d

# Check status
sudo docker-compose ps

# View logs
sudo docker-compose logs -f

# Test API
curl http://localhost:8000
```

---

## What's Running

**3 Docker containers**:
1. **fermm-server** (FastAPI) — API on port 8000
2. **fermm-postgres** (PostgreSQL) — Database on port 5432
3. **fermm-nginx** (Nginx) — Web server on port 80/443

**All data persists** in Docker volumes (survives restarts)

---

## First-Time Checklist

- [ ] Docker installed
- [ ] Repository cloned
- [ ] `.env` file created with passwords
- [ ] `docker-compose up -d` ran successfully
- [ ] All services showing "healthy" status
- [ ] API responding to `curl` test
- [ ] Dashboard accessible in browser
- [ ] Admin login works

---

**Status**: Ready to use!

**Next**: Read `md/UBUNTU-DEPLOYMENT-GUIDE.md` for detailed instructions.

**Need help?** Check the `md/` folder for comprehensive guides.

---

*Last updated: April 5, 2026*
