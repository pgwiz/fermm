# Windows EXE Size Optimization

## Problem
.NET executables can be quite large (often 100MB+) because they include the entire .NET runtime.

## Solutions Applied

### 1. PublishTrimmed=true ✅ (PRIMARY)
- **Removes unused code** from dependencies
- Can reduce size by **30-50%**
- Enabled in `fermm-agent.csproj`

### 2. PublishReadyToRun=true ✅
- **Pre-compiles** IL to native code ahead-of-time
- Improves startup time (bonus)
- Slight size increase but worth it for performance

### 3. DebugSymbols=false & DebugType=none ✅
- **Removes debug information**
- Usually saves 10-20MB

### 4. TrimMode=link ✅
- **Assembly-level trimming** (most aggressive but safe)

### 5. IlcOptimizeSize=true ✅
- **Optimize for size** over performance
- Good for constrained environments

## Expected Size Reduction

| Setting | Size Impact |
|---------|------------|
| PublishTrimmed | -30% to -50% |
| DebugSymbols off | -10% to -20% |
| ReadyToRun | +5% to +10% |
| **Net Result** | **-30% to -55%** |

**Example:**
- Before: 200 MB
- After: 90-140 MB ✅

## How to Build

### Automatic (via csproj)
The `fermm-agent.csproj` now includes these settings, so:

```bash
dotnet publish -c Release -r win-x64 \
  -p:PublishSingleFile=true \
  -p:SelfContained=true
```

Will use all optimizations automatically.

### Manual with Script
```bash
bash build-optimized.sh
```

This builds Windows, Linux, and macOS simultaneously.

## Further Size Reduction (Advanced)

### Option A: Runtime-Dependent (Not Recommended)
If users have .NET 8 installed, you can reduce to ~10-20MB:

```xml
<SelfContained>false</SelfContained>
```

**Pros:** Very small
**Cons:** Requires .NET 8 installed on user's machine

### Option B: UPX Packing (Use with Caution)
[UPX](https://upx.github.io/) can compress executables:

```bash
upx --best --lzma fermm-agent.exe
```

**Result:** ~50-70% smaller
**Cons:** Can be flagged as malware by antivirus, slower startup

### Option C: Native AOT Compilation (Future)
.NET 8 supports Native AOT (experimental):

```xml
<PublishAot>true</PublishAot>
```

**Result:** 20-30MB single-file exe
**Status:** Still experimental for GUI apps

## Testing

After building, check sizes:
```bash
ls -lh fermm-agent/dist/windows/
ls -lh fermm-agent/dist/linux/
ls -lh fermm-agent/dist/macos/
```

## Trimming Issues?

If trimming breaks the app, you can whitelist assemblies:

```xml
<TrimmerRootAssembly Include="FermmAgent" />
<TrimmerRootAssembly Include="Serilog" />
```

## Current Settings (Updated)

File: `fermm-agent/fermm-agent.csproj`

```xml
<PublishTrimmed>true</PublishTrimmed>
<PublishReadyToRun>true</PublishReadyToRun>
<DebugType>none</DebugType>
<DebugSymbols>false</DebugSymbols>
<TrimMode>link</TrimMode>
<IlcOptimizeSize>true</IlcOptimizeSize>
```

## Build in CI/CD

The GitHub Actions workflow will use these settings automatically when building releases.

To test:
```bash
git tag v1.0.0
git push origin v1.0.0
```

Check the release artifacts size - they should be significantly smaller!
