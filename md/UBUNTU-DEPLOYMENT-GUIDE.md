# FERMM Ubuntu Server Deployment Guide

**Objective**: Deploy FERMM API server on Ubuntu with PostgreSQL database using Docker

---

## Prerequisites

Ensure you have on your Ubuntu server:
- Docker (v20.10+)
- Docker Compose (v2.0+)
- 4GB+ RAM, 10GB+ disk space

**Check versions**:
```bash
docker --version
docker-compose --version
```

---

## Step 1: Prepare Environment

```bash
# Create project directory
mkdir -p ~/fermm
cd ~/fermm

# Create .env file with secure credentials
cat > .env << 'EOF'
POSTGRES_PASSWORD=your_secure_db_password_here
JWT_SECRET=your_jwt_secret_key_here_min_32_chars_long
ADMIN_USERNAME=admin
ADMIN_PASSWORD=your_secure_admin_password
EOF

# Secure the .env file
chmod 600 .env
```

---

## Step 2: Get docker-compose.yml

**Option A: Copy from Windows machine**
```bash
# On Windows (PowerShell):
scp docker-compose.prod.yml ubuntu@your-server:/home/ubuntu/fermm/docker-compose.yml

# On Ubuntu:
cd ~/fermm
```

**Option B: Create docker-compose.yml on Ubuntu**

Create `~/fermm/docker-compose.yml`:
```yaml
version: '3.8'

services:
  fermm-server:
    image: popox15/fermm-server:latest
    container_name: fermm-server
    restart: unless-stopped
    ports:
      - "8000:8000"
    environment:
      - FERMM_DATABASE_URL=postgresql+asyncpg://fermm:${POSTGRES_PASSWORD:-fermm}@fermm-postgres:5432/fermm
      - FERMM_JWT_SECRET=${JWT_SECRET:-change-me-in-production}
      - FERMM_ADMIN_USERNAME=${ADMIN_USERNAME:-admin}
      - FERMM_ADMIN_PASSWORD=${ADMIN_PASSWORD:-admin}
    depends_on:
      fermm-postgres:
        condition: service_healthy
    networks:
      - fermm-network

  fermm-postgres:
    image: postgres:16-alpine
    container_name: fermm-postgres
    restart: unless-stopped
    environment:
      - POSTGRES_USER=fermm
      - POSTGRES_PASSWORD=${POSTGRES_PASSWORD:-fermm}
      - POSTGRES_DB=fermm
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U fermm"]
      interval: 5s
      timeout: 5s
      retries: 5
    networks:
      - fermm-network

networks:
  fermm-network:
    driver: bridge

volumes:
  postgres_data:
```

---

## Step 3: Start Services

```bash
cd ~/fermm

# Pull latest images
sudo docker-compose pull

# Start all services
sudo docker-compose up -d

# Check status
sudo docker-compose ps
```

**Expected output**:
```
NAME              IMAGE                           STATUS
fermm-server      popox15/fermm-server:latest     Up (healthy)
fermm-postgres    postgres:16-alpine              Up (healthy)
```

---

## Step 4: Verify Deployment

```bash
# Check API is responding
curl http://localhost:8000

# Check database connection
sudo docker-compose logs fermm-server | grep -i "database\|connected"

# View all logs
sudo docker-compose logs -f
```

---

## What's Inside the Image

The `popox15/fermm-server:latest` image contains:

✅ **Included**:
- FastAPI Python backend
- All API routes (`/api/devices/`, `/api/devices/{id}/overlay/*`)
- WebSocket server for agent↔dashboard messaging
- Overlay endpoints (spawn, close, message relay)
- All dependencies (FastAPI, uvicorn, psycopg2, etc.)

❌ **NOT Included** (separate container):
- PostgreSQL database (provided as `fermm-postgres` container)

---

## API Endpoints

Once running, your API is available at: `http://your-server:8000`

**Key endpoints**:
- `GET /` - Health check
- `POST /api/devices/{device_id}/overlay/spawn` - Spawn overlay
- `POST /api/devices/{device_id}/overlay/close` - Close overlay
- `POST /api/devices/{device_id}/overlay/message` - Send message
- `WS /ws/devices/{device_id}` - WebSocket for agent connection

---

## Connecting Your Windows Agent

On your Windows machine with `fermm-agent.exe`:

```bash
# Set server URL
set FERMM_SERVER_URL=http://your-ubuntu-ip:8000

# Run agent
fermm-agent.exe
```

Agent will:
1. Connect to WebSocket at `ws://your-ubuntu-ip:8000/ws/devices/{device_id}`
2. Register device with server
3. Allow overlay control from dashboard

---

## Troubleshooting

### Container fails to start
```bash
# View error logs
sudo docker-compose logs fermm-server

# Restart services
sudo docker-compose restart
```

### Database connection error
```bash
# Check database is ready
sudo docker-compose logs fermm-postgres

# Verify env variables
cat .env
```

### Port already in use
```bash
# Change port in docker-compose.yml
# Change: ports: - "8000:8000"
# To: ports: - "9000:8000" (for example)

sudo docker-compose restart fermm-server
```

---

## Maintenance

### View logs
```bash
sudo docker-compose logs -f fermm-server
```

### Stop services
```bash
sudo docker-compose stop
```

### Restart services
```bash
sudo docker-compose restart
```

### Clean up (WARNING: deletes data)
```bash
sudo docker-compose down -v
```

### Update image
```bash
sudo docker-compose pull
sudo docker-compose up -d
```

---

## Security Notes

1. **Change default passwords** in `.env` file
2. **Use strong JWT_SECRET** (32+ characters, random)
3. **Restrict API access** with firewall rules
4. **Use HTTPS** in production (configure reverse proxy)
5. **Keep `.env` file private** (chmod 600)

---

## Production Recommendations

1. Use reverse proxy (Nginx/Caddy) for HTTPS/SSL
2. Set resource limits in docker-compose.yml
3. Enable monitoring and logging
4. Regular backups of `postgres_data` volume
5. Use secrets manager for credentials (not .env file)

---

## Next Steps

1. Deploy agent binary on Windows devices
2. Agents will auto-register with server
3. Use dashboard to spawn overlays
4. Monitor agent connections in API logs

---

**Status**: Ready for production deployment  
**API Health**: http://your-server:8000 (test endpoint)  
**Database**: PostgreSQL 16-Alpine (persistent volume)  
**Registry**: popox15/fermm-server on Docker Hub
