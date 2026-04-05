#!/bin/bash
# FERMM Ubuntu Quick Start - Copy this to your Ubuntu server and run

set -e

echo "========================================"
echo "FERMM Ubuntu Deployment Script"
echo "========================================"

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}Step 1: Creating project directory${NC}"
mkdir -p ~/fermm
cd ~/fermm

echo -e "${BLUE}Step 2: Creating .env file${NC}"
cat > .env << 'EOF'
POSTGRES_PASSWORD=fermm_secure_password_change_me
JWT_SECRET=your_jwt_secret_key_here_min_32_chars_long
ADMIN_USERNAME=admin
ADMIN_PASSWORD=admin_secure_password_change_me
EOF

chmod 600 .env
echo -e "${GREEN}✓ Created .env (CHANGE PASSWORDS IN PRODUCTION!)${NC}"

echo -e "${BLUE}Step 3: Creating docker-compose.yml${NC}"
cat > docker-compose.yml << 'EOF'
version: '3.8'

services:
  fermm-server:
    image: popox15/fermm-server:latest
    container_name: fermm-server
    restart: unless-stopped
    ports:
      - "8000:8000"
    environment:
      - FERMM_DATABASE_URL=postgresql+asyncpg://fermm:${POSTGRES_PASSWORD:-fermm}@fermm-postgres:5432/fermm
      - FERMM_JWT_SECRET=${JWT_SECRET:-change-me}
      - FERMM_ADMIN_USERNAME=${ADMIN_USERNAME:-admin}
      - FERMM_ADMIN_PASSWORD=${ADMIN_PASSWORD:-admin}
    depends_on:
      fermm-postgres:
        condition: service_healthy
    networks:
      - fermm-network

  fermm-postgres:
    image: postgres:16-alpine
    container_name: fermm-postgres
    restart: unless-stopped
    environment:
      - POSTGRES_USER=fermm
      - POSTGRES_PASSWORD=${POSTGRES_PASSWORD:-fermm}
      - POSTGRES_DB=fermm
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U fermm"]
      interval: 5s
      timeout: 5s
      retries: 5
    networks:
      - fermm-network

networks:
  fermm-network:
    driver: bridge

volumes:
  postgres_data:
EOF

echo -e "${GREEN}✓ Created docker-compose.yml${NC}"

echo -e "${BLUE}Step 4: Pulling Docker images${NC}"
sudo docker-compose pull

echo -e "${BLUE}Step 5: Starting services${NC}"
sudo docker-compose up -d

echo -e "${BLUE}Step 6: Waiting for services to be healthy${NC}"
sleep 10

echo -e "${BLUE}Service Status:${NC}"
sudo docker-compose ps

echo ""
echo -e "${GREEN}========================================"
echo "✓ Deployment Complete!"
echo "========================================${NC}"
echo ""
echo "API Server: http://localhost:8000"
echo "API Health: curl http://localhost:8000"
echo ""
echo "View logs:"
echo "  sudo docker-compose logs -f fermm-server"
echo ""
echo "Stop services:"
echo "  sudo docker-compose stop"
echo ""
echo "Restart services:"
echo "  sudo docker-compose restart"
echo ""
