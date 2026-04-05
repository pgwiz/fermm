# FERMM Domain Setup Guide

Complete guide to setting up your domain with FERMM.

## Quick Start

```bash
cd ~/fermm
git pull
sudo bash scripts/setup-domain.sh
```

The script will:
1. ✓ Check prerequisites (Docker, docker-compose)
2. ✓ Detect existing domains (Docker nginx.conf + system nginx)
3. ? Prompt for domain selection or new domain entry
4. ? Configure nginx reverse proxy
5. ? Optional: Setup SSL with Let's Encrypt
6. ✓ Restart services
7. ✓ Verify setup

---

## Features

### Domain Detection

The setup script automatically detects:

1. **Docker domains** — From your `./nginx.conf`
   - FERMM-managed domains
   - Previously configured domains

2. **System domains** — From `/etc/nginx/sites-enabled/`
   - Existing nginx configurations
   - Already running domains

**Example**:
```
✓ Currently configured domains:
  1. fermm.pgwiz.cloud   (Docker nginx.conf)
  2. rmm.bware.systems   (System /etc/nginx/sites-enabled)

Select domain (number) or press Enter to add new domain: _
```

### Interactive Setup

- **Select existing domain** — Type `1` or `2` to reuse existing domain
- **Add new domain** — Press Enter to add a brand new domain
- **Custom ports** — Choose 80/443 or alternate ports (8080/8443)
- **SSL setup** — Optional Let's Encrypt certificate

### Testing & Debugging

Test domain detection without full setup:

```bash
sudo bash scripts/setup-domain.sh --test
```

Output shows:
- Docker nginx.conf path
- System nginx path  
- All detected domains
- No configuration changes

Detailed debug test:

```bash
sudo bash scripts/test-domain-detection.sh
```

Shows step-by-step parsing of:
- Docker nginx.conf
- Each system nginx file
- Extracted domain names

---

## Setup Workflow

### Step 1: Check Prerequisites

```bash
cd ~/fermm
sudo bash scripts/setup-domain.sh
```

**Output**:
```
✓ Running with sudo
✓ docker-compose is installed
✓ Docker daemon is running
✓ nginx.conf found
✓ docker-compose file found
```

If any fail, fix them first:
- Must run with `sudo`
- Install docker-compose: `sudo apt install docker-compose`
- Start Docker: `sudo systemctl start docker`

### Step 2: Select Domain

If domains are detected:
```
✓ Currently configured domains:
  1. rmm.bware.systems

Select domain (number) or press Enter to add new domain: 1
```

For new domain, press Enter and enter:
```
Enter your domain name (e.g., fermm.example.com): api.mycompany.com
```

### Step 3: Configure Ports

```
Use non-standard ports? (y/n, recommended: n): n
```

- **No (recommended)** — Uses standard ports 80 (HTTP) and 443 (HTTPS)
- **Yes** — Use alternate ports like 8080 and 8443 (if 80/443 in use)

### Step 4: SSL Configuration (Optional)

```
Setup SSL with Let's Encrypt? (y/n): y
Enter email for Let's Encrypt SSL: admin@example.com
```

- **Requires**: Domain must be accessible from internet
- **Port 80** must be open to the internet
- **Automatic**: Certificates auto-renew every 90 days

### Step 5: Review & Confirm

Script shows summary:
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
→ Generating Configuration
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✓ nginx.conf generated
✓ Docker Compose configuration updated
✓ Services started successfully
```

### Step 6: Verify Access

```
✓ Your FERMM server is now accessible at http://rmm.bware.systems
```

Access in browser:
- **HTTP**: `http://your-domain.com`
- **API**: `http://your-domain.com/api/`
- **WebSocket**: `http://your-domain.com/ws`

---

## Troubleshooting

### Domain Not Detected

If your domain isn't showing in the list:

```bash
sudo bash scripts/test-domain-detection.sh
```

Check output for:
1. Is Docker nginx.conf being read?
2. Is system nginx directory found?
3. What files exist in `/etc/nginx/sites-enabled/`?

### Port 80/443 Already in Use

Use alternative ports:

```
Use non-standard ports? (y/n, recommended: n): y
HTTP port (default 80): 8080
HTTPS port (default 443): 8443
```

Or find what's using the port:

```bash
sudo lsof -i :80
```

Then stop that service:

```bash
sudo systemctl stop apache2  # if Apache is running
sudo systemctl stop nginx    # if system nginx is running
```

### SSL Certificate Setup Failed

If Let's Encrypt setup fails:

1. Verify domain points to your server IP
2. Verify port 80 is accessible from internet
3. Try again manually later: 
   ```bash
   sudo bash scripts/setup-domain.sh
   ```

### Docker Services Won't Start

Check logs:

```bash
sudo docker-compose -f docker-compose.ubuntu.yml logs fermm-server
```

Common issues:
- Port already in use → Use alternate ports
- .env missing variables → Run `cp .env.example .env` and fill it
- Database won't start → Check `docker-compose ps`

---

## Architecture

The domain setup creates a reverse proxy flow:

```
Internet Traffic (port 80/443)
         ↓
    Nginx Container (reverse proxy)
         ↓
    FERMM Backend (port 8000)
         ↓
    Python API Server
    WebSocket Server
    Database Connections
```

### Routing

| URL Path | Routes To | Purpose |
|----------|-----------|---------|
| `/` | FERMM API | Dashboard, HTML assets |
| `/api/*` | FERMM API | REST endpoints |
| `/ws` | FERMM API | WebSocket for real-time updates |
| `/health` | FERMM API | Health check endpoint |
| `/.well-known/acme-challenge/` | Certbot | SSL certificate validation |

### Files Modified

- `nginx.conf` — Reverse proxy configuration
- `docker-compose.yml` — Port mappings updated
- `/etc/letsencrypt/` — SSL certificates (if enabled)

---

## Managing Multiple Domains

After first domain setup, you can add more:

```bash
sudo bash scripts/setup-domain.sh
```

Next time you run it:
```
✓ Currently configured domains:
  1. rmm.bware.systems
  2. fermm.pgwiz.cloud

Select domain (number) or press Enter to add new domain: (Enter)
```

Press Enter to add a new domain. The nginx.conf will be updated with additional server blocks.

---

## Security Best Practices

1. **Always use HTTPS** — Let your domain point to port 443 (SSL)
2. **Strong .env secrets** — Use random JWT_SECRET and ADMIN_PASSWORD
3. **Firewall rules** — Only expose ports 80 and 443
4. **Keep certificates updated** — Certbot auto-renewal runs daily
5. **Regular backups** — Backup your docker volumes:
   ```bash
   sudo docker-compose exec fermm-postgres pg_dump -U fermm fermm > backup.sql
   ```

---

## Advanced: Manual Configuration

If you prefer to manually configure:

1. **Skip script** — Don't run setup-domain.sh
2. **Edit nginx.conf** — Add your server blocks manually
3. **Update docker-compose.yml** — Change port mappings
4. **Restart services**:
   ```bash
   sudo docker-compose -f docker-compose.ubuntu.yml down
   sudo docker-compose -f docker-compose.ubuntu.yml up -d
   ```

Template nginx server block:

```nginx
server {
    listen 80;
    server_name your-domain.com;
    
    location / {
        proxy_pass http://fermm-server:8000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## Next Steps

After domain setup:

1. ✅ Access FERMM dashboard at your domain
2. ✅ Create admin account
3. ✅ Configure agents and RDP targets
4. ✅ Setup overlay spawning
5. ✅ Monitor WebSocket connections

See `/README.md` and `setup.md` for next steps.

---

## Support

For issues:

1. Check logs:
   ```bash
   sudo docker-compose logs -f
   ```

2. Run debug test:
   ```bash
   sudo bash scripts/test-domain-detection.sh
   ```

3. Verify nginx syntax:
   ```bash
   sudo nginx -t
   ```

4. Review DOCKER-COMPOSE-VERSION-FIX.md and PORT-CONFLICT-FIX.md

---

**Last Updated**: April 2026  
**Script Version**: 1.0  
**Tested On**: Ubuntu 18.04, 20.04, 22.04+
