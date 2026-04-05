#!/bin/bash

# FERMM Docker Build and Publish Script
# Builds and publishes the FERMM server Docker image to Docker Hub
# Usage: ./scripts/build-and-publish-docker.sh [version] [push-to-docker-hub]
# Example: ./scripts/build-and-publish-docker.sh 1.0.0 true

set -e

# Configuration
DOCKER_USERNAME="${DOCKER_USERNAME:-popox15}"
IMAGE_NAME="fermm-server"
REGISTRY="${REGISTRY:-docker.io}"
FULL_IMAGE="${REGISTRY}/${DOCKER_USERNAME}/${IMAGE_NAME}"

# Get version from argument or git tag
VERSION="${1:-latest}"
if [ "$VERSION" = "latest" ]; then
  VERSION=$(git describe --tags --always 2>/dev/null || echo "latest")
fi

# Parse push flag
PUSH_TO_REGISTRY="${2:-false}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}════════════════════════════════════════${NC}"
echo -e "${GREEN}FERMM Docker Build & Publish Script${NC}"
echo -e "${GREEN}════════════════════════════════════════${NC}"
echo ""
echo -e "Repository: ${YELLOW}${FULL_IMAGE}${NC}"
echo -e "Version: ${YELLOW}${VERSION}${NC}"
echo -e "Push to registry: ${YELLOW}${PUSH_TO_REGISTRY}${NC}"
echo ""

# Check if Docker is installed and running
if ! command -v docker &> /dev/null; then
  echo -e "${RED}Error: Docker is not installed${NC}"
  exit 1
fi

if ! docker info &> /dev/null; then
  echo -e "${RED}Error: Docker daemon is not running${NC}"
  exit 1
fi

# Check if we're in the right directory
if [ ! -f "docker-compose.yml" ] || [ ! -f "fermm-server/Dockerfile" ]; then
  echo -e "${RED}Error: Must be run from project root directory${NC}"
  exit 1
fi

echo -e "${YELLOW}Step 1: Building Docker image...${NC}"
docker build \
  -f fermm-server/Dockerfile \
  -t "${FULL_IMAGE}:${VERSION}" \
  -t "${FULL_IMAGE}:latest" \
  --build-arg BUILD_DATE="$(date -u +'%Y-%m-%dT%H:%M:%SZ')" \
  --build-arg VCS_REF="$(git rev-parse --short HEAD 2>/dev/null || echo 'unknown')" \
  .

if [ $? -ne 0 ]; then
  echo -e "${RED}Build failed${NC}"
  exit 1
fi

echo -e "${GREEN}✓ Image built successfully${NC}"
echo ""

# Show image info
echo -e "${YELLOW}Step 2: Image information:${NC}"
docker inspect "${FULL_IMAGE}:${VERSION}" | grep -E '(Id|Size|Created)' | head -5
echo ""

# Test image locally
echo -e "${YELLOW}Step 3: Testing image...${NC}"
if docker run --rm "${FULL_IMAGE}:${VERSION}" python -c "import sys; print(f'Python {sys.version}')" &> /dev/null; then
  echo -e "${GREEN}✓ Image test passed${NC}"
else
  echo -e "${YELLOW}Warning: Could not verify image${NC}"
fi
echo ""

# Push to registry if requested
if [ "${PUSH_TO_REGISTRY}" = "true" ] || [ "${PUSH_TO_REGISTRY}" = "1" ]; then
  echo -e "${YELLOW}Step 4: Logging into Docker Hub...${NC}"
  
  # Check if docker credentials are available
  if [ -z "$DOCKER_USERNAME" ] || [ -z "$DOCKER_PASSWORD" ]; then
    echo -e "${YELLOW}Note: Using existing Docker credentials (run 'docker login' first)${NC}"
  else
    echo "${DOCKER_PASSWORD}" | docker login -u "${DOCKER_USERNAME}" --password-stdin
  fi
  
  echo -e "${YELLOW}Step 5: Pushing image to registry...${NC}"
  docker push "${FULL_IMAGE}:${VERSION}"
  docker push "${FULL_IMAGE}:latest"
  
  echo -e "${GREEN}✓ Image pushed successfully${NC}"
  echo ""
  echo -e "${GREEN}Available commands:${NC}"
  echo -e "  ${YELLOW}docker pull ${FULL_IMAGE}:${VERSION}${NC}"
  echo -e "  ${YELLOW}docker pull ${FULL_IMAGE}:latest${NC}"
  echo ""
  
  # Docker compose pull example
  echo -e "${GREEN}To use in docker-compose.yml:${NC}"
  echo -e "  ${YELLOW}image: ${FULL_IMAGE}:${VERSION}${NC}"
  echo -e "  ${YELLOW}or${NC}"
  echo -e "  ${YELLOW}image: ${FULL_IMAGE}:latest${NC}"
else
  echo -e "${YELLOW}Step 4: Skipping registry push${NC}"
  echo ""
  echo -e "${GREEN}To push later, run:${NC}"
  echo -e "  ${YELLOW}docker push ${FULL_IMAGE}:${VERSION}${NC}"
  echo -e "  ${YELLOW}docker push ${FULL_IMAGE}:latest${NC}"
fi

echo ""
echo -e "${GREEN}════════════════════════════════════════${NC}"
echo -e "${GREEN}Build complete!${NC}"
echo -e "${GREEN}════════════════════════════════════════${NC}"
