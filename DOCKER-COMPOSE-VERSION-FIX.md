# Docker Compose Version Fix

The errors you received were due to docker-compose version incompatibility.

**Error 1**:
```
ERROR: Version in "./docker-compose.ubuntu.yml" is unsupported.
```
Fixed by downgrading from version 3.8 to 2.2

**Error 2**:
```
Additional properties are not allowed ('start_period' was unexpected)
```
Fixed by removing `start_period` from healthchecks (not supported in 2.2)

---

## What to Do Now

### Step 1: Pull Latest Changes

```bash
cd ~/fermm
git pull origin main
```

### Step 2: Start Docker (if not running)

```bash
sudo systemctl start docker
```

Or verify it's running:

```bash
sudo systemctl status docker
```

### Step 3: Try Again

```bash
sudo docker-compose -f docker-compose.ubuntu.yml up -d
```

Or use the standard compose file:

```bash
sudo docker-compose up -d
```

### Step 4: Verify

```bash
sudo docker-compose ps
```

Expected output:
```
NAME              IMAGE                           STATUS
fermm-server      popox15/fermm-server:latest     Up
fermm-postgres    postgres:16-alpine              Up
fermm-nginx       nginx:alpine                    Up
fermm-certbot     certbot/certbot:latest          Up
```

---

## What Changed

### Change 1: Version Compatibility

**Before** (unsupported):
```yaml
version: '3.8'
```

**After** (compatible):
```yaml
version: '2.2'
```

Version 2.2 works with Docker Compose 1.29+

### Change 2: Healthcheck Syntax

**Before** (unsupported):
```yaml
healthcheck:
  test: ...
  interval: 10s
  timeout: 5s
  retries: 3
  start_period: 30s  # ❌ Not in 2.2
```

**After** (compatible):
```yaml
healthcheck:
  test: ...
  interval: 10s
  timeout: 5s
  retries: 3  # ✅ All supported in 2.2
```

The `start_period` keyword was introduced in Docker Compose 3.4 and is not available in 2.2.

---

## If Still Getting an Error

### Check Docker is Running

```bash
sudo systemctl status docker
```

If stopped, start it:

```bash
sudo systemctl start docker
```

### Check Docker Compose Version

```bash
docker-compose --version
```

Should be 1.29 or newer. Update if needed:

```bash
sudo apt update
sudo apt install docker-compose
```

### Check .env File Exists

```bash
ls -la ~/.env
cat ~/.env
```

Should have all 6 variables:
- `POSTGRES_PASSWORD`
- `JWT_SECRET`
- `ADMIN_USERNAME`
- `ADMIN_PASSWORD`
- `LOG_LEVEL`
- `DEBUG`

If missing, copy from example:

```bash
cp .env.example .env
# Then edit with your values:
nano .env
```

---

## GitHub Commits

Latest fixes have been pushed:
- Commit: 7ed2282 (healthcheck syntax fix)
- Commit: e5b2a9e (version fix guide)
- Commit: 8b2fb18 (version downgrade)
- Branch: main
- URL: https://github.com/pgwiz/fermm

---

**Status**: ✅ All syntax issues fixed  
**Next Steps**:
1. `git pull origin main`
2. `sudo systemctl start docker` (ensure Docker is running)
3. `sudo docker-compose up -d`
4. `sudo docker-compose ps` (verify all containers running)

Let me know if it works!
