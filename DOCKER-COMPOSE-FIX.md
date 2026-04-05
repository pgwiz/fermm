# Docker Compose Fix — Complete

The error you received was due to invalid environment variable syntax in the docker-compose file.

**Error**:
```
ERROR: Invalid interpolation format for "fermm-server" option in service "services": 
"FERMM_DATABASE_URL=postgresql+asyncpg://fermm:${POSTGRES_PASSWORD:-fermm}@postgres:5432/fermm"
```

**Cause**: Docker-compose doesn't support the `:-` default value syntax.

**Fix**: Updated docker-compose files (already pushed to GitHub).

---

## What to Do Now on Your Ubuntu Server

### Step 1: Pull Latest Code

```bash
cd ~/fermm
git pull origin main
```

### Step 2: Create .env File

```bash
cp .env.example .env
nano .env
```

Edit with your secure passwords:
```
POSTGRES_PASSWORD=your_secure_db_password
JWT_SECRET=your_secure_jwt_secret_min_32_chars
ADMIN_USERNAME=admin
ADMIN_PASSWORD=your_secure_admin_password
LOG_LEVEL=INFO
DEBUG=false
```

**Important**: Save the file (Ctrl+X, Y, Enter in nano)

### Step 3: Verify .env File

```bash
cat .env
```

Make sure all variables have values (not empty)

### Step 4: Start Services

```bash
sudo docker-compose up -d
```

### Step 5: Verify

```bash
sudo docker-compose ps
```

Expected:
```
NAME              IMAGE                           STATUS
fermm-server      popox15/fermm-server:latest     Up (healthy)
fermm-postgres    postgres:16-alpine              Up (healthy)
fermm-nginx       nginx:alpine                    Up
```

### Step 6: Test API

```bash
curl http://localhost:8000
```

Should return an API response (not an error)

---

## What Changed in the Fix

**Before** (broken):
```yaml
- FERMM_DATABASE_URL=postgresql+asyncpg://fermm:${POSTGRES_PASSWORD:-fermm}@postgres:5432/fermm
```

**After** (fixed):
```yaml
- FERMM_DATABASE_URL=postgresql+asyncpg://fermm:${POSTGRES_PASSWORD}@fermm-postgres:5432/fermm
```

**Why**: 
- Docker-compose requires ALL variables to be defined in `.env`
- Removed the `:-` default syntax (not supported)
- Changed `postgres` to `fermm-postgres` (correct container name)

---

## Quick Checklist

- [ ] Pulled latest code: `git pull origin main`
- [ ] Created .env: `cp .env.example .env`
- [ ] Edited .env with secure passwords
- [ ] Started services: `sudo docker-compose up -d`
- [ ] All containers healthy: `sudo docker-compose ps`
- [ ] API responding: `curl http://localhost:8000`

---

## If It Still Doesn't Work

**Check the .env file has all variables:**
```bash
grep -E "^[A-Z_]+=" .env
```

Should show:
```
POSTGRES_PASSWORD=...
JWT_SECRET=...
ADMIN_USERNAME=...
ADMIN_PASSWORD=...
LOG_LEVEL=...
DEBUG=...
```

**If any are missing**, add them:
```bash
cat >> .env << 'EOF'
LOG_LEVEL=INFO
DEBUG=false
EOF
```

**View logs for errors:**
```bash
sudo docker-compose logs fermm-server
```

---

## GitHub Commit

The fix has been pushed:
- Commit: 567b6da
- Branch: main
- URL: https://github.com/pgwiz/fermm

---

**Status**: ✅ Fixed and pushed to GitHub  
**Next**: Follow the 6 steps above on your Ubuntu server

Let me know once it's running!
