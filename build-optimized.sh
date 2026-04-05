#!/bin/bash
# Build optimized Windows exe with size reduction

set -e

cd "$(dirname "$0")/fermm-agent"

echo "📦 Building optimized Windows executable..."
echo ""
echo "Optimizations enabled:"
echo "  ✓ PublishTrimmed=true (removes unused code)"
echo "  ✓ PublishReadyToRun=true (faster startup, slightly larger)"
echo "  ✓ DebugSymbols=false (removes debug info)"
echo "  ✓ IlcOptimizeSize=true (optimize for size)"
echo ""

# Windows x64 build
dotnet publish -c Release -r win-x64 \
  -p:PublishSingleFile=true \
  -p:SelfContained=true \
  -p:PublishTrimmed=true \
  -p:PublishReadyToRun=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  --output ./dist/windows

# Linux x64 build
dotnet publish -c Release -r linux-x64 \
  -p:PublishSingleFile=true \
  -p:SelfContained=true \
  -p:PublishTrimmed=true \
  -p:PublishReadyToRun=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  --output ./dist/linux

# macOS x64 build
dotnet publish -c Release -r osx-x64 \
  -p:PublishSingleFile=true \
  -p:SelfContained=true \
  -p:PublishTrimmed=true \
  -p:PublishReadyToRun=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  --output ./dist/macos

echo ""
echo "📊 Build complete! Size comparison:"
echo ""
ls -lh dist/windows/fermm-agent || echo "Windows: build output not found"
ls -lh dist/linux/fermm-agent || echo "Linux: build output not found"
ls -lh dist/macos/fermm-agent || echo "macOS: build output not found"
