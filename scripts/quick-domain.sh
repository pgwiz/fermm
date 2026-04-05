#!/bin/bash
set -e

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

if [[ $EUID -ne 0 ]]; then 
    echo -e "${YELLOW}⚠${NC} Must run with sudo"
    exit 1
fi

echo -e "\n${BLUE}╔════════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║${NC}  Quick Domain Setup${NC}"
echo -e "${BLUE}╚════════════════════════════════════════════════════════╝${NC}\n"

read -p "Enter domain: " domain
[ -z "$domain" ] && exit 1

# Check if domain already has config
if [ -f "conf.d/${domain}.conf" ]; then
    read -p "Config for $domain already exists. Overwrite? (y/n): " overwrite
    if [[ "$overwrite" != "y" ]]; then
        echo "Cancelled"
        exit 0
    fi
fi

# Check for existing certificate
if [ -f "/etc/letsencrypt/live/${domain}/fullchain.pem" ]; then
    expiry=$(openssl x509 -in "/etc/letsencrypt/live/${domain}/fullchain.pem" -noout -enddate | cut -d= -f2)
    echo -e "${GREEN}✓${NC} Found SSL certificate, expires: $expiry"
    use_ssl="yes"
else
    use_ssl="no"
fi

mkdir -p conf.d

# Generate appropriate config based on SSL status
if [ "$use_ssl" = "yes" ]; then
    cat > "conf.d/${domain}.conf" <<'CONFEOF'
server {
    listen 8080;
    listen 8443 ssl http2;
    server_name $domain www.$domain;
    
    ssl_certificate /etc/letsencrypt/live/$domain/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/$domain/privkey.pem;
    
    location / {
        proxy_pass http://fermm-server:8000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
CONFEOF
else
    cat > "conf.d/${domain}.conf" <<'CONFEOF'
server {
    listen 8080;
    server_name $domain www.$domain;
    
    location / {
        proxy_pass http://fermm-server:8000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
CONFEOF
fi

echo -e "${GREEN}✓${NC} Created conf.d/${domain}.conf"

# Restart services
echo "Restarting services..."
docker-compose -f docker-compose.ubuntu.yml down 2>/dev/null || true
docker-compose -f docker-compose.ubuntu.yml up -d > /dev/null 2>&1

echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${GREEN}✓${NC} Setup complete!"
echo ""
if [ "$use_ssl" = "yes" ]; then
    echo "   HTTPS: https://${domain}:8443"
fi
echo "   HTTP:  http://${domain}:8080"
echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}\n"

