# Port 80 Already in Use - Quick Fix

Your Ubuntu server has something already running on port 80.

## Quick Solution

Use the alternative docker-compose file with different ports:

```bash
cd ~/fermm
sudo docker-compose -f docker-compose.ubuntu-alt.yml down
sudo docker-compose -f docker-compose.ubuntu-alt.yml up -d
```

**Ports Used**:
- `8080` → HTTP (instead of 80)
- `8443` → HTTPS (instead of 443)
- `8000` → API (unchanged)

---

## Access Your Server

Once running, access FERMM at:
- **HTTP**: `http://your-server:8080`
- **API**: `http://your-server:8000`

---

## What's Using Port 80?

To identify what service is using port 80:

```bash
sudo lsof -i :80
```

Or:

```bash
sudo netstat -tlnp | grep :80
```

### If you want to use port 80, stop the conflicting service:

**Apache**:
```bash
sudo systemctl stop apache2
sudo systemctl disable apache2
```

**Nginx** (running on host):
```bash
sudo systemctl stop nginx
sudo systemctl disable nginx
```

**Other service**: Replace service name and restart FERMM

---

## Permanent Setup with Domain

After services are running on alternate ports, setup your domain:

```bash
sudo bash scripts/setup-domain.sh
```

The script will:
1. Prompt for your domain name
2. Configure nginx to route traffic
3. Optionally setup SSL with Let's Encrypt
4. Restart services on proper ports (80/443 if available, or custom)

---

## Files Reference

- **docker-compose.ubuntu.yml** - Standard (uses ports 80/443)
- **docker-compose.ubuntu-alt.yml** - Alternative (uses ports 8080/8443)
- **scripts/setup-domain.sh** - Interactive domain setup script

Use whichever matches your port availability.
