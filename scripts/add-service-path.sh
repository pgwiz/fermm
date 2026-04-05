#!/bin/bash
set -e

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
RED='\033[0;31m'
NC='\033[0m'

if [[ $EUID -ne 0 ]]; then 
    echo -e "${RED}✗${NC} Must run with sudo"
    exit 1
fi

echo -e "\n${BLUE}╔════════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║${NC}  Add Service Path${NC}"
echo -e "${BLUE}╚════════════════════════════════════════════════════════╝${NC}\n"

CONF_DIR="conf.d"

if [[ ! -d "$CONF_DIR" ]]; then
    echo -e "${RED}✗${NC} conf.d directory not found"
    exit 1
fi

read -p "Enter domain (e.g., rmm.bware.systems): " domain
[ -z "$domain" ] && exit 1

DOMAIN_CONF="$CONF_DIR/${domain}.conf"

if [[ ! -f "$DOMAIN_CONF" ]]; then
    echo -e "${RED}✗${NC} Config for $domain not found at $DOMAIN_CONF"
    echo "   Create the domain first with: sudo bash scripts/quick-domain.sh"
    exit 1
fi

echo "Existing services on $domain:"
grep -E "location /|proxy_pass" "$DOMAIN_CONF" | head -10 || echo "   (base config only)"
echo ""

read -p "Enter path (e.g., /1, /2, /api, /admin): " path
if [[ ! "$path" =~ ^/ ]]; then
    path="/$path"
fi

read -p "Enter backend port: " backend_port
if ! [[ "$backend_port" =~ ^[0-9]+$ ]] || [ "$backend_port" -lt 1 ] || [ "$backend_port" -gt 65535 ]; then
    echo -e "${RED}✗${NC} Invalid port number"
    exit 1
fi

# Backup original config
cp "$DOMAIN_CONF" "$DOMAIN_CONF.backup.$(date +%s)"

# Generate the new location block
LOCATION_BLOCK=$(cat <<LOCEOF
    # Service path: $path
    location $path {
        proxy_pass http://localhost:$backend_port;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_set_header X-Script-Name $path;
    }
LOCEOF
)

# Check if path already exists in config
if grep -q "location $path {" "$DOMAIN_CONF"; then
    echo -e "${YELLOW}⚠${NC} Path $path already exists in config"
    read -p "Overwrite? (y/n): " overwrite
    if [[ "$overwrite" != "y" ]]; then
        echo "Cancelled"
        rm "$DOMAIN_CONF.backup.$(date +%s)"
        exit 0
    fi
    
    # Remove existing location block for this path
    # Find the line with "location $path {" and delete until the matching "}"
    sed -i "/location $path {/,/^    }/d" "$DOMAIN_CONF"
fi

# Insert the new location block before the closing brace of server block
# Find the last "}" in the file and insert before it
sed -i "/^}$/i\\$LOCATION_BLOCK" "$DOMAIN_CONF"

echo -e "${GREEN}✓${NC} Added path: $path"
echo -e "${GREEN}✓${NC} Backend: http://localhost:$backend_port"

# Validate nginx config
echo ""
echo "Testing nginx configuration..."
if docker exec fermm-nginx nginx -t 2>&1 | grep -q "successful"; then
    echo -e "${GREEN}✓${NC} Config is valid"
    
    # Reload nginx
    docker exec fermm-nginx nginx -s reload
    echo -e "${GREEN}✓${NC} Nginx reloaded"
else
    echo -e "${RED}✗${NC} Config validation failed"
    echo "Rolling back..."
    mv "$DOMAIN_CONF.backup.$(date +%s)" "$DOMAIN_CONF"
    exit 1
fi

echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${GREEN}✓${NC} Service path added!"
echo ""
echo "   URL: http://${domain}:8080${path}"
echo "   Backend: localhost:${backend_port}"
echo ""
echo "List all services on this domain:"
echo "   grep 'location' $DOMAIN_CONF"
echo ""
echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}\n"
