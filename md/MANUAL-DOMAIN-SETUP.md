# Quick Domain Setup (Manual)

## Problem with Automated Script

If `setup-domain.sh` fails with grep/sed errors, use this **manual** approach.

## Step-by-Step Manual Setup

### 1. Create Base nginx.conf

On your Ubuntu server:

```bash
cd ~/fermm
mkdir -p conf.d
```

Create `nginx.conf`:

```bash
cat > nginx.conf <<'EOF'
user nginx;
worker_processes auto;
error_log /var/log/nginx/error.log warn;
pid /var/run/nginx.pid;

events {
    worker_connections 1024;
}

http {
    include /etc/nginx/mime.types;
    default_type application/octet-stream;

    log_format main '$remote_addr - $remote_user [$time_local] "$request" '
                    '$status $body_bytes_sent "$http_referer" '
                    '"$http_user_agent" "$http_x_forwarded_for"';

    access_log /var/log/nginx/access.log main;

    sendfile on;
    tcp_nopush on;
    tcp_nodelay on;
    keepalive_timeout 65;
    types_hash_max_size 2048;
    client_max_body_size 20M;

    gzip on;
    gzip_vary on;
    gzip_proxied any;
    gzip_comp_level 6;
    gzip_types text/plain text/css text/xml text/javascript application/json application/javascript;

    limit_req_zone $binary_remote_addr zone=general:10m rate=10r/s;
    limit_req_zone $binary_remote_addr zone=api:10m rate=30r/s;

    upstream fermm_backend {
        server fermm-server:8000;
    }

    include conf.d/*.conf;
}
EOF
```

### 2. Create Domain Config File

For your domain (e.g., `rmm.bware.systems`):

```bash
cat > conf.d/rmm.bware.systems.conf <<'EOF'
server {
    listen 80;
    server_name rmm.bware.systems www.rmm.bware.systems;

    location /.well-known/acme-challenge/ {
        root /var/www/certbot;
    }

    location / {
        limit_req zone=general burst=20 nodelay;
        proxy_pass http://fermm_backend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }

    location /api/ {
        limit_req zone=api burst=50 nodelay;
        proxy_pass http://fermm_backend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /ws {
        proxy_pass http://fermm_backend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }

    location /health {
        access_log off;
        proxy_pass http://fermm_backend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
    }

    location ~ /\. {
        deny all;
        access_log off;
        log_not_found off;
    }
}
EOF
```

### 3. Update docker-compose Ports (if needed)

If port 80/443 are in use (by system nginx), update ports:

```bash
# Edit docker-compose.ubuntu.yml
sudo nano docker-compose.ubuntu.yml
```

Change:
```yaml
ports:
  - "80:80"      →  "8080:80"
  - "443:443"    →  "8443:443"
```

### 4. Restart Services

```bash
# Stop old services
docker-compose -f docker-compose.ubuntu.yml down

# Start with new config
docker-compose -f docker-compose.ubuntu.yml up -d

# Verify
docker-compose ps
curl http://localhost:8000/health
```

### 5. Test Nginx Config

```bash
# Verify config syntax
docker exec fermm-nginx nginx -t

# Expected: "syntax is ok"
```

### 6. Access Dashboard

Open browser:
- **HTTP**: http://rmm.bware.systems:8080
- **Health check**: http://rmm.bware.systems:8080/health

## Adding More Domains

Create another file in `conf.d/`:

```bash
cat > conf.d/fermm.pgwiz.cloud.conf <<'EOF'
server {
    listen 8080;
    server_name fermm.pgwiz.cloud www.fermm.pgwiz.cloud;
    
    location / {
        proxy_pass http://fermm_backend;
        ...
    }
}
EOF
```

Then:
```bash
docker exec fermm-nginx nginx -t
docker-compose restart fermm-nginx
```

## SSL Setup

For each domain with HTTPS:

```bash
# Using cert-manager.sh
sudo bash /path/to/cert-manager.sh -d rmm.bware.systems

# Then add HTTPS block to conf.d/rmm.bware.systems.conf
```

Append to config file:

```nginx
server {
    listen 8443 ssl http2;
    server_name rmm.bware.systems www.rmm.bware.systems;

    ssl_certificate /etc/letsencrypt/live/rmm.bware.systems/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/rmm.bware.systems/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;

    location / {
        proxy_pass http://fermm_backend;
        ...
    }
}
```

## Troubleshooting

**Port 80/443 in use?**
```bash
sudo lsof -i :80
sudo lsof -i :443
```

**Nginx config error?**
```bash
docker exec fermm-nginx nginx -t
docker logs fermm-nginx
```

**Services not starting?**
```bash
docker-compose logs fermm-server
docker-compose logs fermm-nginx
```

## Summary

| Task | Command |
|------|---------|
| Create base config | `cat > nginx.conf <<EOF ... EOF` |
| Add domain | `cat > conf.d/{domain}.conf <<EOF ... EOF` |
| Update ports | `nano docker-compose.ubuntu.yml` |
| Restart services | `docker-compose down && docker-compose up -d` |
| Verify config | `docker exec fermm-nginx nginx -t` |
| Add SSL | Use cert-manager.sh script |

This manual approach **avoids all sed/grep escaping issues** and works reliably!
