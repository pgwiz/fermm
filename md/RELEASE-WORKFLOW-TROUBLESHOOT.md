# GitHub Release Workflow - 403 Error Fix

## Problem
GitHub Actions workflow fails with: `⚠️ GitHub release failed with status: 403`

## Root Cause
The workflow token doesn't have write permissions to create releases.

## Solutions

### Solution 1: Explicit Token (Already Applied ✅)
Updated `docker-publish.yml` to include:
```yaml
permissions:
  contents: write
  packages: write

steps:
  - name: Create Release
    uses: softprops/action-gh-release@v1
    with:
      token: ${{ secrets.GITHUB_TOKEN }}  # Explicit token
      generate_release_notes: true
```

### Solution 2: GitHub Repo Settings
If Solution 1 doesn't work, check your repository settings:

1. Go to: **Settings → Actions → General**
2. Under "Workflow permissions", select:
   - ✅ "Read and write permissions"
   - ✅ "Allow GitHub Actions to create and approve pull requests"
3. Click **Save**

### Solution 3: Personal Access Token (Optional)
If you want to use a custom token instead of GITHUB_TOKEN:

1. Create a Personal Access Token (PAT):
   - Go to: **Settings → Developer settings → Personal access tokens**
   - Create new token with `public_repo` or `repo` scope
   - Copy the token

2. Add to repo secrets:
   - Go to: **Settings → Secrets and variables → Actions**
   - Create new secret called `RELEASE_TOKEN`
   - Paste the token value

3. Update workflow:
   ```yaml
   - name: Create Release
     uses: softprops/action-gh-release@v1
     with:
       token: ${{ secrets.RELEASE_TOKEN }}
       generate_release_notes: true
   ```

## After Fixing

Try creating a release again:
```bash
git tag v1.0.0
git push origin v1.0.0
```

Then check GitHub Actions → "Build and Publish Docker Image" workflow for status.

## Debugging

1. Check workflow logs: **Actions → "Build and Publish Docker Image" → click run**
2. Look for "Create Release" step output
3. If still failing, check the error message in the logs

## Alternative: Manual Release Creation

If automated release still fails, you can create releases manually:
1. Go to: **Releases → Draft a new release**
2. Choose tag (e.g., v1.0.0)
3. Add title and description
4. Click "Publish release"

This is a backup option while troubleshooting permissions.
