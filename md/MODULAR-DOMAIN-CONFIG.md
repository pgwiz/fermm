# Improved Domain Setup Strategy

## Problem with Current `setup-domain.sh`

1. **grep errors**: `Unmatched [` - Happens when trying to match port patterns
2. **sed errors**: `unterminated s` - When replacing multi-line content or special chars in domain names
3. **Complex single file**: Tries to manage one giant nginx.conf with sed replacement

## Solution: Modular Domain Configuration

### New Approach

Instead of generating one monolithic nginx.conf, use a **modular architecture**:

1. **Base nginx.conf** (minimal, never changes)
   - Global settings, upstream, rate limiting
   - `include conf.d/*.conf;` directive

2. **Domain-specific files** in `conf.d/`
   - One file per domain: `conf.d/rmm.bware.systems.conf`
   - Each domain config is standalone
   - Easy to edit, add, or remove domains

### Why This Works

✅ No multi-line sed replacements
✅ No grep bracket escaping issues  
✅ Simple port updates (single sed on docker-compose)
✅ Multiple domains coexist naturally
✅ User can manually edit domain configs anytime

### Usage

```bash
sudo bash scripts/quick-domain-setup.sh
```

Workflow:
1. Detect existing domains
2. Ask user to select or enter new
3. Check if `conf.d/{domain}.conf` exists
4. Ask to overwrite if needed
5. Generate clean config file
6. Restart services

### Example File Structure

```
fermm/
├── nginx.conf                    (base: never generated)
├── conf.d/
│   ├── rmm.bware.systems.conf   (for port 80/443)
│   └── fermm.pgwiz.cloud.conf   (for port 80/443)
└── docker-compose.ubuntu.yml    (ports 8080:80, 8443:443)
```

### Base nginx.conf Content

```nginx
user nginx;
worker_processes auto;

http {
    upstream fermm_backend {
        server fermm-server:8000;
    }
    
    limit_req_zone ...
    
    # Include all domain configs
    include conf.d/*.conf;
}
```

### Domain Config Example

```nginx
# conf.d/rmm.bware.systems.conf
server {
    listen 80;
    server_name rmm.bware.systems www.rmm.bware.systems;
    
    location / {
        proxy_pass http://fermm_backend;
        ...
    }
}
```

### Implementation Steps

1. User runs: `sudo bash scripts/quick-domain-setup.sh`
2. Script generates minimal `nginx.conf` (if missing)
3. Script creates/updates `conf.d/{domain}.conf`
4. Updates docker-compose ports with simple sed
5. Restarts services

### Benefits Over Old Approach

| Old | New |
|-----|-----|
| One giant nginx.conf | Multiple domain files in conf.d/ |
| Complex sed replacements | Simple file writes |
| Overwrite entire config | Ask before changing domain |
| grep/sed escaping errors | No escaping needed |
| Hard to manage multiple domains | Easy domain coexistence |

## Next Step

When running on Ubuntu, use:
```bash
sudo bash scripts/quick-domain-setup.sh
```

This script handles the modular approach automatically.
