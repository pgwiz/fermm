#!/bin/bash
set -e
if [[ $EUID -ne 0 ]]; then echo "Must run with sudo"; exit 1; fi
read -p "Enter domain: " domain
[ -z "$domain" ] && exit 1
mkdir -p ../conf.d
cat > "../conf.d/${domain}.conf" <<'CONFEOF'
server {
    listen 8080;
    server_name $domain www.$domain;
    location / {
        proxy_pass http://fermm-server:8000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
    }
}
CONFEOF
cd ..
docker-compose -f docker-compose.ubuntu.yml down 2>/dev/null || true
docker-compose -f docker-compose.ubuntu.yml up -d
echo "Done: http://${domain}:8080"
