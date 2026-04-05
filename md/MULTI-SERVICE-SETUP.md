# Multi-Service Domain Setup

Host multiple services on different backend ports through a single nginx reverse proxy.

## Overview

With FERMM's modular domain configuration, you can:
- Run multiple services (different ports internally)
- Route them through nginx (single external ports 8080/8443)
- Each service gets its own domain name
- Share SSL certificates and nginx infrastructure

## Architecture

```
External (Internet)
    ↓
nginx (8080/8443) on fermm-nginx container
    ↓
    ├─→ app.example.com → proxy_pass http://localhost:8000 (Your App 1)
    ├─→ api.example.com → proxy_pass http://localhost:8001 (Your App 2)
    └─→ admin.example.com → proxy_pass http://localhost:8002 (Your App 3)
```

Internal services:
- Port 8000: FERMM Server (default)
- Port 8001: Your App 2
- Port 8002: Your App 3
- etc.

## Quick Setup Example

### 1. Setup first domain (FERMM Server)

```bash
cd ~/fermm
sudo bash scripts/quick-domain.sh

Enter domain: fermm.example.com
Enter backend port (default: 8000): 8000
```

Creates: `conf.d/fermm.example.com.conf` with `proxy_pass http://localhost:8000`

### 2. Setup second domain (Another service)

```bash
sudo bash scripts/quick-domain.sh

Enter domain: app.example.com
Enter backend port (default: 8000): 8001
```

Creates: `conf.d/app.example.com.conf` with `proxy_pass http://localhost:8001`

### 3. Setup third domain (Yet another service)

```bash
sudo bash scripts/quick-domain.sh

Enter domain: api.example.com
Enter backend port (default: 8000): 8002
```

Creates: `conf.d/api.example.com.conf` with `proxy_pass http://localhost:8002`

## DNS & Firewall

For all domains, point to your server's IP:

```bash
# In your DNS provider:
fermm.example.com  A  203.0.113.1
app.example.com    A  203.0.113.1
api.example.com    A  203.0.113.1
```

External ports exposed:
- **8080** (HTTP) → nginx
- **8443** (HTTPS) → nginx

Internal ports (not exposed):
- **8000** (FERMM Server)
- **8001** (App 2)
- **8002** (App 3)

## Using SSL with Multiple Domains

### Option 1: Separate certificates per domain

```bash
# Domain 1 with SSL
sudo bash scripts/quick-domain.sh
Enter domain: fermm.example.com
Enter backend port: 8000
[Script detects no cert, offers to create one with Let's Encrypt]

# Domain 2 with SSL
sudo bash scripts/quick-domain.sh
Enter domain: app.example.com
Enter backend port: 8001
[Script creates separate certificate for app.example.com]
```

Each domain gets its own certificate in `/etc/letsencrypt/live/{domain}/`

### Option 2: Single wildcard certificate

If you prefer one certificate for all subdomains:

```bash
# Install certificate once
sudo bash scripts/quick-domain.sh
Enter domain: example.com
[Set up with wildcard: *.example.com + example.com]

# Then add subdomains (they'll find the certificate)
sudo bash scripts/quick-domain.sh
Enter domain: app.example.com
[Detects *.example.com certificate]

sudo bash scripts/quick-domain.sh
Enter domain: api.example.com
[Detects *.example.com certificate]
```

## Config File Example

Generated config files are in `conf.d/`:

### `conf.d/fermm.example.com.conf`
```nginx
server {
    listen 8080;
    listen 8443 ssl http2;
    server_name fermm.example.com www.fermm.example.com;
    
    ssl_certificate /etc/letsencrypt/live/fermm.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/fermm.example.com/privkey.pem;
    
    location / {
        proxy_pass http://localhost:8000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

### `conf.d/app.example.com.conf`
```nginx
server {
    listen 8080;
    listen 8443 ssl http2;
    server_name app.example.com www.app.example.com;
    
    ssl_certificate /etc/letsencrypt/live/app.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/app.example.com/privkey.pem;
    
    location / {
        proxy_pass http://localhost:8001;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

## Running Your Services

Inside the docker network, services connect to `localhost`:

```bash
# Service 1 (port 8000)
docker run -p 8000:3000 myapp1

# Service 2 (port 8001)
docker run -p 8001:3000 myapp2

# Service 3 (port 8002)
docker run -p 8002:3000 myapp3
```

Or in docker-compose.yml:

```yaml
services:
  myapp1:
    image: myapp:latest
    ports:
      - "8000:3000"
  
  myapp2:
    image: otherapp:latest
    ports:
      - "8001:3000"
  
  myapp3:
    image: thirdapp:latest
    ports:
      - "8002:3000"
  
  nginx:
    image: nginx:alpine
    ports:
      - "8080:80"
      - "8443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf
      - ./conf.d:/etc/nginx/conf.d
      - /etc/letsencrypt:/etc/letsencrypt
```

## Manual Domain Configuration

To manually add a domain without the script:

1. Create `conf.d/yourdomain.conf`:
```bash
cat > conf.d/yourdomain.conf << 'EOF'
server {
    listen 8080;
    server_name yourdomain.com www.yourdomain.com;
    
    location / {
        proxy_pass http://localhost:9000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
EOF
```

2. Restart nginx:
```bash
docker-compose -f docker-compose.ubuntu.yml restart fermm-nginx
```

3. (Optional) Setup SSL:
```bash
# Using cert-manager script
sudo bash ~/scripts/cert-manager.sh

# Or certbot directly
sudo certbot certonly --standalone -d yourdomain.com
```

## Troubleshooting

### Domain not accessible

1. Check nginx is running:
   ```bash
   docker ps | grep nginx
   ```

2. Check config syntax:
   ```bash
   docker exec fermm-nginx nginx -t
   ```

3. Check logs:
   ```bash
   docker logs fermm-nginx
   ```

### Certificate not found

1. List installed certificates:
   ```bash
   sudo ls -la /etc/letsencrypt/live/
   ```

2. For debugging:
   ```bash
   # Check cert expiry
   sudo openssl x509 -in /etc/letsencrypt/live/yourdomain.com/fullchain.pem -noout -dates
   
   # Check certificate details
   sudo openssl x509 -in /etc/letsencrypt/live/yourdomain.com/fullchain.pem -noout -text
   ```

### Backend not responding

1. Check if backend port is open:
   ```bash
   netstat -tuln | grep 8001
   ```

2. Test connection:
   ```bash
   curl -v http://localhost:8001
   ```

3. Check docker network:
   ```bash
   docker network ls
   docker network inspect fermm_default
   ```

## Summary

- **One nginx**: Handles external ports 8080/8443
- **Multiple domains**: Each routed to different backend port
- **Multiple services**: Run independently on ports 8000, 8001, 8002, etc.
- **SSL**: Each domain can have its own certificate
- **Simple management**: Use quick-domain.sh to add new domains

Perfect for multi-tenant or microservices architectures!
