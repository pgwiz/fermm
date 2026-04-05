# FERMM Domain Routing Setup - Complete

## Summary

Successfully configured domain routing for `rmm.bware.systems` with both HTTP and HTTPS support. The domain now properly serves the FERMM Dashboard at both standard ports.

## Architecture

### System-Level Nginx (Ports 80/443)
- Installed by `cert-manager.sh` 
- Holds the Let's Encrypt SSL certificate
- Acts as reverse proxy to Docker container

### Docker Nginx (Ports 8080/8443)  
- Runs inside Docker container
- Serves dashboard and proxies to FastAPI backend
- Routes based on domain-specific configs in `/etc/nginx/conf.d/`

### Routing Flow
```
HTTPS Request to rmm.bware.systems:443
    ↓
System Nginx (Port 443)
    ↓
Docker Nginx (Port 8080 via proxy_pass)
    ↓
Domain Config (conf.d/rmm.bware.systems.conf)
    ↓
FastAPI Backend (fermm-server:8000)
    ↓
FERMM Dashboard
```

## Configuration Files

### 1. System Nginx Proxy (`/etc/nginx/sites-available/rmm.bware.systems`)
- HTTP on port 80 → Docker port 8080
- HTTPS on port 443 → Docker port 8080
- Certificate: `/etc/letsencrypt/live/rmm.bware.systems/`

**Source file in repo:** `scripts/rmm.bware.systems.nginx.conf`

### 2. Docker Nginx Main Config (`nginx.conf`)
```
- Include directive for domain configs: include /etc/nginx/conf.d/*.conf;
- Default catch-all server block: DISABLED (commented out)
- Allows domain-specific configs to take priority
```

### 3. Domain-Specific Config (`conf.d/rmm.bware.systems.conf`)
```
- Listens on port 80 (internal Docker port)
- Server name: rmm.bware.systems, www.rmm.bware.systems
- Proxies to: fermm-server:8000
- Headers: Host, X-Real-IP, X-Forwarded-For, X-Forwarded-Proto
```

## Setup Steps (If Repeating)

### On Server
```bash
# 1. Copy system nginx config
sudo cp scripts/rmm.bware.systems.nginx.conf /etc/nginx/sites-available/rmm.bware.systems

# 2. Enable the config and disable default
sudo ln -sf /etc/nginx/sites-available/rmm.bware.systems /etc/nginx/sites-enabled/rmm.bware.systems
sudo rm /etc/nginx/sites-enabled/default

# 3. Test and reload
sudo nginx -t
sudo systemctl reload nginx

# 4. Verify
curl https://rmm.bware.systems/
```

## Verification

### HTTP
```bash
curl http://rmm.bware.systems/
# Returns: FERMM Dashboard HTML
```

### HTTPS
```bash
curl https://rmm.bware.systems/
# Returns: FERMM Dashboard HTML
```

### API Docs
```bash
curl https://rmm.bware.systems/docs
# Returns: Swagger UI with API documentation
```

## Per-Domain Backend Ports

To add new domains or change backend ports, use the `quick-domain.sh` script:

```bash
sudo bash scripts/quick-domain.sh
```

This script will:
1. Check for existing certificates
2. Prompt for domain name
3. Prompt for backend port
4. Generate nginx config in `conf.d/`
5. Automatically reload Docker nginx

## Path-Based Routing

To add multiple services on the same domain (e.g., /api1, /api2):

```bash
sudo bash scripts/add-service-path.sh
```

## Issues & Solutions

### Problem: Apache/default page served on HTTPS
**Root Cause:** System nginx was running on port 443 with default config
**Solution:** Created proxy config to route to Docker nginx on port 8080

### Problem: Domain config not loading
**Root Cause:** Missing `include /etc/nginx/conf.d/*.conf;` in Docker nginx.conf
**Solution:** Added include directive to main nginx.conf

### Problem: Default catch-all preventing domain routing
**Root Cause:** Wildcard `server_name _` in default block was matching all requests
**Solution:** Commented out default server block, using only domain-specific configs

## Environment

- **Server:** Ubuntu 20.04 LTS at 129.151.173.195
- **Domain:** rmm.bware.systems
- **DNS:** Resolves to 129.151.173.195
- **Certificate:** Let's Encrypt (valid until June 28, 2026)
- **Docker Compose File:** docker-compose.ubuntu-alt.yml (uses ports 8080/8443)

## Next Steps

1. ✅ HTTP/HTTPS routing working
2. ⏳ Test agent connections via real domain
3. ⏳ Multi-domain setup (additional services)
4. ⏳ Path-based routing for multi-tenant scenarios
