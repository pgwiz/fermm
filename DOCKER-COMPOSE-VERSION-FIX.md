# Docker Compose Version Fix

The error you received was due to the docker-compose version being too old to support version 3.8.

**Error**:
```
ERROR: Version in "./docker-compose.ubuntu.yml" is unsupported. 
You might be seeing this error because you're using the wrong Compose file version.
```

**Fix**: I've updated all docker-compose files to use version 2.2 (compatible with older Docker Compose).

---

## What to Do Now

### Step 1: Pull Latest Changes

```bash
cd ~/fermm
git pull origin main
```

### Step 2: Try Again

```bash
sudo docker-compose -f docker-compose.ubuntu.yml up -d
```

Or use the standard compose file:

```bash
sudo docker-compose up -d
```

### Step 3: Verify

```bash
sudo docker-compose ps
```

Expected output:
```
NAME              IMAGE                           STATUS
fermm-server      popox15/fermm-server:latest     Up
fermm-postgres    postgres:16-alpine              Up
fermm-nginx       nginx:alpine                    Up
```

---

## What Changed

**Before** (unsupported):
```yaml
version: '3.8'
```

**After** (compatible):
```yaml
version: '2.2'
```

Version 2.2 is widely supported on Docker Compose 1.29+ (works on older servers).

---

## If Still Getting an Error

Check your Docker Compose version:

```bash
docker-compose --version
```

Should be 1.29+ or newer. If older, update it:

```bash
sudo apt update
sudo apt install docker-compose
```

Or upgrade to Docker Desktop on your machine.

---

## GitHub Commit

The fix has been pushed:
- Commit: 8b2fb18
- Branch: main
- URL: https://github.com/pgwiz/fermm

---

**Status**: ✅ Fixed and pushed  
**Next**: Pull latest and run `docker-compose up -d`

Let me know if it works!
