# Automated Release Workflow

## Overview

GitHub Actions automatically creates releases when you push a git tag with the format `v*` (e.g., `v1.0.0`, `v1.2.3`).

The workflow:
1. Builds Docker images for fermm-server
2. Publishes to Docker Hub
3. Creates a GitHub Release with release notes and docker-compose examples

## How to Create a Release

### Step 1: Tag the commit

```bash
git tag v1.0.0
```

Or with an annotated tag (recommended):
```bash
git tag -a v1.0.0 -m "Release version 1.0.0"
```

### Step 2: Push the tag

```bash
git push origin v1.0.0
```

Or push all tags:
```bash
git push origin --tags
```

### What Happens Automatically

1. **GitHub Actions triggered** - The `docker-publish.yml` workflow starts
2. **Docker build** - Builds fermm-server image
3. **Docker push** - Publishes to Docker Hub at:
   - `popox15/fermm-server:v1.0.0`
   - `popox15/fermm-server:latest`
4. **GitHub Release created** with:
   - Docker pull commands
   - docker-compose examples
   - Quick start guide
   - Auto-generated changelog

## Example Release

When you push `v1.0.0`, GitHub Release will include:

```
## FERMM v1.0.0 Release

### Docker Images

**fermm-server:**
```bash
docker pull popox15/fermm-server:v1.0.0
docker pull popox15/fermm-server:latest
```

### Quick Start
```bash
git clone https://github.com/pgwiz/fermm.git
cd fermm
sudo docker-compose -f docker-compose.ubuntu-alt.yml up -d
```

[+ auto-generated changelog]
```

## Version Naming Convention

Follow semantic versioning:
- `v1.0.0` - Initial release
- `v1.0.1` - Patch (bug fixes)
- `v1.1.0` - Minor (new features)
- `v2.0.0` - Major (breaking changes)

## Viewing Releases

Releases are visible at:
- GitHub: https://github.com/pgwiz/fermm/releases
- Docker Hub: https://hub.docker.com/r/popox15/fermm-server/tags

## Manual Workflow Dispatch

You can also manually trigger the workflow without pushing a tag:
1. Go to GitHub repo → Actions → "Build and Publish Docker Image"
2. Click "Run workflow"
3. This builds and pushes but does NOT create a release (releases require tags)

## Important

- **Docker credentials required**: Ensure `DOCKER_USERNAME` and `DOCKER_PASSWORD` are set in GitHub Secrets
- **Permissions**: Workflow needs `contents: write` to create releases (already configured)
- **Tags only trigger releases**: Regular pushes to `main` only build and push Docker images
