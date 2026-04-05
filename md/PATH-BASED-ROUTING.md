# Path-Based Service Routing

Host multiple services on the same domain using different URL paths instead of different domains.

## Overview

Instead of:
- app.example.com → Service 1
- api.example.com → Service 2

You can use:
- example.com/ → Service 1
- example.com/1 → Service 2
- example.com/2 → Service 3
- example.com/api → Service 4

**Benefits:**
- ✅ Single SSL certificate for one domain
- ✅ Single DNS entry
- ✅ Cleaner URLs
- ✅ Reuse same domain
- ✅ Easy to manage related services
- ✅ Perfect for multi-tenant with tenant ID in path

## Architecture

```
External (Internet)
         ↓
   rmm.bware.systems:8080
         ↓
    Nginx Server Block (one domain)
    ├─ location / → port 8000
    ├─ location /1 → port 8001
    ├─ location /2 → port 8002
    ├─ location /admin → port 8003
    └─ location /api → port 8004
         ↓
   Different Backend Services
```

## Quick Setup

### Step 1: Create Base Domain Config

```bash
cd ~/fermm
sudo bash scripts/quick-domain.sh

Enter domain: rmm.bware.systems
Enter backend port (default: 8000): 8000
```

This creates `conf.d/rmm.bware.systems.conf` with a default root location pointing to port 8000.

### Step 2: Add Service Paths

```bash
# Add /1 path pointing to port 8001
sudo bash scripts/add-service-path.sh

Enter domain: rmm.bware.systems
Enter path: /1
Enter backend port: 8001
✓ Added path: /1
✓ Nginx reloaded

# Add /2 path pointing to port 8002
sudo bash scripts/add-service-path.sh

Enter domain: rmm.bware.systems
Enter path: /2
Enter backend port: 8002
✓ Added path: /2
✓ Nginx reloaded

# Add /admin path pointing to port 9000
sudo bash scripts/add-service-path.sh

Enter domain: rmm.bware.systems
Enter path: /admin
Enter backend port: 9000
✓ Added path: /admin
✓ Nginx reloaded
```

### Result

Generated `conf.d/rmm.bware.systems.conf`:

```nginx
server {
    listen 8080;
    listen 8443 ssl http2;
    server_name rmm.bware.systems www.rmm.bware.systems;
    
    ssl_certificate /etc/letsencrypt/live/rmm.bware.systems/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/rmm.bware.systems/privkey.pem;
    
    # Root path - default service
    location / {
        proxy_pass http://localhost:8000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
    
    # Service path: /1
    location /1 {
        proxy_pass http://localhost:8001;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Script-Name /1;
    }
    
    # Service path: /2
    location /2 {
        proxy_pass http://localhost:8002;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Script-Name /2;
    }
    
    # Service path: /admin
    location /admin {
        proxy_pass http://localhost:9000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Script-Name /admin;
    }
}
```

### Access Services

```
http://rmm.bware.systems:8080/       → localhost:8000
http://rmm.bware.systems:8080/1      → localhost:8001
http://rmm.bware.systems:8080/2      → localhost:8002
http://rmm.bware.systems:8080/admin  → localhost:9000
```

## Use Cases

### 1. Multi-Tenant with Tenant ID

```bash
# Create base config
sudo bash scripts/quick-domain.sh
Enter domain: tenant.example.com
Enter backend port: 9000  # Default/admin tenant

# Add tenant 1
sudo bash scripts/add-service-path.sh
Enter domain: tenant.example.com
Enter path: /tenant/1
Enter backend port: 9001

# Add tenant 2
sudo bash scripts/add-service-path.sh
Enter domain: tenant.example.com
Enter path: /tenant/2
Enter backend port: 9002
```

Access as:
- http://tenant.example.com/ - Admin panel
- http://tenant.example.com/tenant/1 - Tenant 1
- http://tenant.example.com/tenant/2 - Tenant 2

**Your service receives header:** `X-Script-Name: /tenant/1`

### 2. API Versioning

```bash
# v1 API
sudo bash scripts/add-service-path.sh
Enter domain: api.example.com
Enter path: /v1
Enter backend port: 8001

# v2 API
sudo bash scripts/add-service-path.sh
Enter domain: api.example.com
Enter path: /v2
Enter backend port: 8002
```

### 3. Service Separation

```bash
# Web UI
sudo bash scripts/quick-domain.sh
Enter domain: myapp.example.com
Enter backend port: 8000

# API backend
sudo bash scripts/add-service-path.sh
Enter domain: myapp.example.com
Enter path: /api
Enter backend port: 8001

# Admin panel
sudo bash scripts/add-service-path.sh
Enter domain: myapp.example.com
Enter path: /admin
Enter backend port: 8002

# Status/health
sudo bash scripts/add-service-path.sh
Enter domain: myapp.example.com
Enter path: /status
Enter backend port: 8003
```

## Managing Services

### List Services on Domain

```bash
grep "location" conf.d/rmm.bware.systems.conf
```

Output:
```
    location / {
    location /1 {
    location /2 {
    location /admin {
```

### View Service Configuration

```bash
cat conf.d/rmm.bware.systems.conf | grep -A 5 "location /1"
```

### Remove a Service Path

Edit `conf.d/rmm.bware.systems.conf` and delete the location block:

```bash
sudo nano conf.d/rmm.bware.systems.conf

# Remove the location /2 { ... } block

# Then reload:
docker exec fermm-nginx nginx -s reload
```

### Modify Backend Port

Edit the `proxy_pass` line:

```bash
sudo sed -i 's|proxy_pass http://localhost:8002;|proxy_pass http://localhost:8050;|' \
  conf.d/rmm.bware.systems.conf

docker exec fermm-nginx nginx -s reload
```

## Running Services

Run different services on different ports:

```bash
# Service on port 8000 (root)
docker run -p 8000:3000 service1 &

# Service on port 8001 (/1)
docker run -p 8001:3000 service2 &

# Service on port 8002 (/2)
docker run -p 8002:3000 service3 &

# Service on port 9000 (/admin)
docker run -p 9000:3000 admin-panel &
```

Or in `docker-compose.yml`:

```yaml
services:
  service1:
    image: myservice:latest
    ports:
      - "8000:3000"
  
  service2:
    image: myservice:latest
    ports:
      - "8001:3000"
  
  service3:
    image: myservice:latest
    ports:
      - "8002:3000"
  
  admin:
    image: admin-panel:latest
    ports:
      - "9000:3000"
  
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

## Passing Path to Backend

Services receive the original path in headers:

- **X-Script-Name**: The path prefix (e.g., `/1`, `/api`)
- **X-Forwarded-For**: Client IP
- **X-Forwarded-Proto**: Protocol (http/https)

### Python Example

```python
from flask import Flask, request

app = Flask(__name__)

@app.route('/')
def home():
    script_name = request.headers.get('X-Script-Name', '')
    # script_name = '/1' when accessed as example.com:8080/1
    return f"Service running at path: {script_name}"

if __name__ == '__main__':
    app.run(port=8001)
```

### Node.js Example

```javascript
const express = require('express');
const app = express();

app.get('/', (req, res) => {
  const scriptName = req.get('X-Script-Name') || '';
  // scriptName = '/2' when accessed as example.com:8080/2
  res.send(`Service running at path: ${scriptName}`);
});

app.listen(8002);
```

## Path Rewriting

If your backend needs the path rewritten:

Edit `conf.d/domain.conf` and change the location block:

```nginx
# Before: backend receives full path
location /api {
    proxy_pass http://localhost:8001;
}

# After: backend receives rewritten path (removes /api prefix)
location /api/ {
    proxy_pass http://localhost:8001/;
}
```

Note the trailing slashes:
- `location /api { proxy_pass http://localhost:8001; }` → `/api/users` → `http://localhost:8001/api/users`
- `location /api/ { proxy_pass http://localhost:8001/; }` → `/api/users` → `http://localhost:8001/users`

## Troubleshooting

### Service not responding

1. Check config:
   ```bash
   grep "location /1" conf.d/rmm.bware.systems.conf
   ```

2. Verify nginx syntax:
   ```bash
   docker exec fermm-nginx nginx -t
   ```

3. Test backend port:
   ```bash
   curl -v http://localhost:8001
   ```

### Path not found (404)

- Make sure location block exists in config
- Reload nginx after config change: `docker exec fermm-nginx nginx -s reload`
- Check backend service is running on correct port

### Certificate/SSL issues

All paths share the same certificate (one domain = one cert). If paths are on different domains, each needs its own certificate.

## Comparison: Paths vs Domains

| Feature | Paths | Domains |
|---------|-------|---------|
| **URL** | example.com/1 | app1.example.com |
| **Certificate** | One shared | One per domain (or wildcard) |
| **DNS** | One entry | Multiple entries |
| **Setup** | quick-domain.sh + add-service-path.sh | quick-domain.sh multiple times |
| **Best for** | Multi-tenant, related services | Independent apps |

## Summary

- **One domain**: `rmm.bware.systems`
- **One certificate**: `/etc/letsencrypt/live/rmm.bware.systems/`
- **Multiple paths**: `/`, `/1`, `/2`, `/admin`, `/api`, etc.
- **Multiple backends**: ports 8000, 8001, 8002, 8003, 8004
- **Easy management**: `add-service-path.sh` script
- **Perfect for**: Multi-tenant, microservices, API versioning

Perfect for reusing the same domain with many services! 🚀
