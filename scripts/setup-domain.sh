#!/bin/bash

###############################################################################
# FERMM Domain Setup Script
# 
# Interactive script to:
# 1. Choose a domain name
# 2. Configure nginx reverse proxy
# 3. Setup SSL with Let's Encrypt (optional)
# 4. Start/restart services
#
# Usage:
#   sudo bash scripts/setup-domain.sh          # Interactive setup
#   sudo bash scripts/setup-domain.sh --test   # Test domain detection
###############################################################################

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Directories
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FERMM_ROOT="$(dirname "$SCRIPT_DIR")"
NGINX_CONF="$FERMM_ROOT/nginx.conf"
DOCKER_COMPOSE_FILE="$FERMM_ROOT/docker-compose.ubuntu.yml"

###############################################################################
# Functions
###############################################################################

print_header() {
    echo -e "\n${BLUE}╔════════════════════════════════════════════════════════╗${NC}"
    echo -e "${BLUE}║${NC}  FERMM Domain Setup${NC}"
    echo -e "${BLUE}╚════════════════════════════════════════════════════════╝${NC}\n"
}

print_section() {
    echo -e "\n${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "${BLUE}→${NC} $1"
    echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}\n"
}

print_success() {
    echo -e "${GREEN}✓${NC} $1"
}

print_error() {
    echo -e "${RED}✗${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠${NC} $1"
}

get_configured_domains() {
    local domains=""
    
    # Check Docker nginx.conf (FERMM-managed)
    if [[ -f "$NGINX_CONF" ]]; then
        domains+=$(grep 'server_name' "$NGINX_CONF" 2>/dev/null | \
            grep -v '^#' | \
            sed 's/.*server_name\s*//g' | \
            sed 's/[;#].*//' | \
            tr ' ' '\n' | \
            grep -v '^_$' | \
            grep -v '^www\.' | \
            grep -v '^$' || true)
    fi
    
    # Check system nginx configs (existing domains on server)
    if [[ -d /etc/nginx/sites-enabled ]]; then
        domains+=$'\n'
        # Find all files and grep each one individually (more reliable than grep -r)
        for file in /etc/nginx/sites-enabled/*; do
            if [[ -f "$file" ]] && [[ -r "$file" ]]; then
                grep 'server_name' "$file" 2>/dev/null | \
                    grep -v '^#' | \
                    sed 's/.*server_name\s*//g' | \
                    sed 's/[;#].*//' | \
                    tr ' ' '\n' | \
                    grep -v '^_$' | \
                    grep -v '^www\.' | \
                    grep -v '^$' || true
                domains+=$'\n'
            fi
        done
    fi
    
    # Return sorted unique domains
    echo "$domains" | grep -v '^$' | sort -u
}

check_prerequisites() {
    print_section "Checking Prerequisites"

    local all_good=true

    # Check if running as root or with sudo
    if [[ $EUID -ne 0 ]]; then
        print_error "This script must be run with sudo"
        echo "  Usage: sudo bash scripts/setup-domain.sh"
        all_good=false
    else
        print_success "Running with sudo"
    fi

    # Check if docker-compose is installed
    if ! command -v docker-compose &> /dev/null; then
        print_error "docker-compose is not installed"
        all_good=false
    else
        print_success "docker-compose is installed"
    fi

    # Check if docker is running
    if ! docker ps &> /dev/null; then
        print_warning "Docker daemon is not running"
        print_warning "Starting docker service..."
        sudo systemctl start docker || {
            print_error "Failed to start docker"
            all_good=false
        }
    else
        print_success "Docker daemon is running"
    fi

    # Check if nginx.conf exists
    if [[ ! -f "$NGINX_CONF" ]]; then
        print_error "nginx.conf not found at $NGINX_CONF"
        all_good=false
    else
        print_success "nginx.conf found"
    fi

    # Check if docker-compose.yml exists
    if [[ ! -f "$DOCKER_COMPOSE_FILE" ]]; then
        print_error "docker-compose file not found at $DOCKER_COMPOSE_FILE"
        all_good=false
    else
        print_success "docker-compose file found"
    fi

    if [[ "$all_good" == false ]]; then
        print_error "Prerequisites check failed"
        exit 1
    fi
}

prompt_domain() {
    print_section "Domain Configuration"

    # Get already configured domains and ensure uniqueness
    local domains_raw=$(get_configured_domains)
    local -a existing_domains=($(echo "$domains_raw" | sort -u))
    
    # If domains exist, show them
    if [[ ${#existing_domains[@]} -gt 0 ]]; then
        print_success "Currently configured domains:"
        for i in "${!existing_domains[@]}"; do
            echo "  $((i+1)). ${existing_domains[$i]}"
        done
        echo ""
        
        # Ask if user wants to use existing or add new
        local choice=""
        while [[ -z "$choice" ]]; do
            read -p "Select domain (number) or press Enter to add new domain: " choice
            
            # If empty, add new domain
            if [[ -z "$choice" ]]; then
                choice="new"
                break
            fi
            
            # If number, validate and select
            if [[ "$choice" =~ ^[0-9]+$ ]]; then
                local idx=$((choice - 1))
                if [[ $idx -ge 0 && $idx -lt ${#existing_domains[@]} ]]; then
                    echo "${existing_domains[$idx]}"
                    return
                else
                    print_error "Invalid selection"
                    choice=""
                fi
            else
                print_error "Please enter a number or leave blank for new domain"
                choice=""
            fi
        done
    fi

    # Prompt for new domain
    local domain=""
    while [[ -z "$domain" ]]; do
        read -p "Enter your domain name (e.g., fermm.example.com): " domain
        
        # Basic validation
        if [[ ! "$domain" =~ ^[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$ ]]; then
            print_error "Invalid domain format"
            domain=""
        fi
    done

    echo "$domain"
}

prompt_email() {
    print_section "SSL Configuration"

    local email=""
    while [[ -z "$email" ]]; do
        read -p "Enter email for Let's Encrypt SSL (for renewal notifications): " email
        
        if [[ ! "$email" =~ ^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$ ]]; then
            print_error "Invalid email format"
            email=""
        fi
    done

    echo "$email"
}

prompt_use_ssl() {
    print_section "SSL Setup"

    local use_ssl=""
    while [[ "$use_ssl" != "y" && "$use_ssl" != "n" ]]; do
        read -p "Setup SSL with Let's Encrypt? (y/n): " use_ssl
        use_ssl=$(echo "$use_ssl" | tr '[:upper:]' '[:lower:]')
    done

    [[ "$use_ssl" == "y" ]]
}

prompt_port() {
    print_section "Port Configuration"

    echo "Current port bindings:"
    echo "  80   (HTTP)"
    echo "  443  (HTTPS)"
    echo ""

    # Auto-detect if standard ports are in use
    local std_80_in_use=false
    local std_443_in_use=false
    
    if netstat -tuln 2>/dev/null | grep -q ":80 "; then
        std_80_in_use=true
        print_warning "Port 80 is already in use"
    fi
    
    if netstat -tuln 2>/dev/null | grep -q ":443 "; then
        std_443_in_use=true
        print_warning "Port 443 is already in use"
    fi
    
    # If standard ports are available, use them by default
    if [[ "$std_80_in_use" == "false" ]] && [[ "$std_443_in_use" == "false" ]]; then
        echo "✓ Standard ports are available"
        echo "8000|8443"
        return
    fi
    
    # If ports are in use, auto-use alternate ports
    if [[ "$std_80_in_use" == "true" ]] || [[ "$std_443_in_use" == "true" ]]; then
        print_warning "Using alternate ports 8080/8443 instead"
        echo "8080|8443"
        return
    fi
    
    # Fallback to manual selection
    local use_custom=""
    while [[ "$use_custom" != "y" && "$use_custom" != "n" ]]; do
        read -p "Use custom ports? (y/n): " use_custom
        use_custom=$(echo "$use_custom" | tr '[:upper:]' '[:lower:]')
    done

    local http_port=80
    local https_port=443

    if [[ "$use_custom" == "y" ]]; then
        read -p "HTTP port (default 80): " http_port
        http_port=${http_port:-80}
        read -p "HTTPS port (default 443): " https_port
        https_port=${https_port:-443}
    fi

    echo "$http_port|$https_port"
}

check_ports_available() {
    local http_port=$1
    local https_port=$2

    print_section "Port Status"

    if netstat -tuln 2>/dev/null | grep -q ":$http_port "; then
        print_warning "Port $http_port is in use - showing process:"
        lsof -i :$http_port 2>/dev/null || true
    else
        print_success "Port $http_port is available"
    fi

    if netstat -tuln 2>/dev/null | grep -q ":$https_port "; then
        print_warning "Port $https_port is in use"
    else
        print_success "Port $https_port is available"
    fi

    return 0
}

generate_nginx_config() {
    local domain=$1
    local http_port=$2
    local https_port=$3
    local use_ssl=$4

    cat > "$NGINX_CONF" <<'EOF'
user nginx;
worker_processes auto;
error_log /var/log/nginx/error.log warn;
pid /var/run/nginx.pid;

events {
    worker_connections 1024;
}

http {
    include /etc/nginx/mime.types;
    default_type application/octet-stream;

    log_format main '$remote_addr - $remote_user [$time_local] "$request" '
                    '$status $body_bytes_sent "$http_referer" '
                    '"$http_user_agent" "$http_x_forwarded_for"';

    access_log /var/log/nginx/access.log main;

    sendfile on;
    tcp_nopush on;
    tcp_nodelay on;
    keepalive_timeout 65;
    types_hash_max_size 2048;
    client_max_body_size 20M;

    # Gzip compression
    gzip on;
    gzip_vary on;
    gzip_proxied any;
    gzip_comp_level 6;
    gzip_types text/plain text/css text/xml text/javascript 
               application/json application/javascript application/xml+rss 
               application/rss+xml font/truetype font/opentype 
               application/vnd.ms-fontobject image/svg+xml;

    # Rate limiting
    limit_req_zone $binary_remote_addr zone=general:10m rate=10r/s;
    limit_req_zone $binary_remote_addr zone=api:10m rate=30r/s;

    # Upstream to FERMM backend
    upstream fermm_backend {
        server fermm-server:8000;
    }

    # HTTP server - redirect to HTTPS if SSL enabled
    server {
        listen PORT_HTTP;
        server_name DOMAIN_NAME www.DOMAIN_NAME _;

        # Certbot challenge directory
        location /.well-known/acme-challenge/ {
            root /var/www/certbot;
        }

        # Redirect to HTTPS if configured
        SSL_REDIRECT

        # Root route to FERMM API
        location / {
            limit_req zone=general burst=20 nodelay;
            
            proxy_pass http://fermm_backend;
            proxy_http_version 1.1;
            
            # Headers
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            proxy_set_header X-Forwarded-Host $host;
            proxy_set_header X-Forwarded-Port $server_port;
            
            # WebSocket support
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            
            # Timeouts
            proxy_connect_timeout 60s;
            proxy_send_timeout 60s;
            proxy_read_timeout 60s;
        }

        # API routes with stricter rate limiting
        location /api/ {
            limit_req zone=api burst=50 nodelay;
            
            proxy_pass http://fermm_backend;
            proxy_http_version 1.1;
            
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            
            proxy_connect_timeout 60s;
            proxy_send_timeout 60s;
            proxy_read_timeout 60s;
        }

        # WebSocket endpoint
        location /ws {
            proxy_pass http://fermm_backend;
            proxy_http_version 1.1;
            
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            
            proxy_read_timeout 3600s;
            proxy_send_timeout 3600s;
        }

        # Health check endpoint
        location /health {
            access_log off;
            proxy_pass http://fermm_backend;
            proxy_http_version 1.1;
            proxy_set_header Host $host;
        }

        # Deny access to sensitive files
        location ~ /\. {
            deny all;
            access_log off;
            log_not_found off;
        }
    }

    # HTTPS server (if SSL enabled)
    SSL_SERVER_BLOCK
}
EOF

    # Replace placeholders
    sed -i "s|PORT_HTTP|${http_port}|g" "$NGINX_CONF"
    sed -i "s|DOMAIN_NAME|${domain}|g" "$NGINX_CONF"

    if [[ "$use_ssl" == "true" ]]; then
        # Add HTTPS redirect
        sed -i "s|# Redirect to HTTPS if configured|return 301 https://\$server_name\$request_uri;|g" "$NGINX_CONF"

        # Add HTTPS server block
        local https_block=$(cat <<HTTPS_EOF
    # HTTPS server with SSL
    server {
        listen ${https_port} ssl http2;
        server_name ${domain} www.${domain};

        # SSL certificates
        ssl_certificate /etc/letsencrypt/live/${domain}/fullchain.pem;
        ssl_certificate_key /etc/letsencrypt/live/${domain}/privkey.pem;
        ssl_trusted_certificate /etc/letsencrypt/live/${domain}/chain.pem;

        # SSL configuration
        ssl_protocols TLSv1.2 TLSv1.3;
        ssl_ciphers HIGH:!aNULL:!MD5;
        ssl_prefer_server_ciphers on;
        ssl_session_cache shared:SSL:10m;
        ssl_session_timeout 10m;

        # HSTS
        add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
        add_header X-Frame-Options "SAMEORIGIN" always;
        add_header X-Content-Type-Options "nosniff" always;
        add_header X-XSS-Protection "1; mode=block" always;
        add_header Referrer-Policy "strict-origin-when-cross-origin" always;

        location / {
            limit_req zone=general burst=20 nodelay;
            
            proxy_pass http://fermm_backend;
            proxy_http_version 1.1;
            
            proxy_set_header Host \$host;
            proxy_set_header X-Real-IP \$remote_addr;
            proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto \$scheme;
            proxy_set_header X-Forwarded-Host \$host;
            proxy_set_header X-Forwarded-Port \$server_port;
            
            proxy_set_header Upgrade \$http_upgrade;
            proxy_set_header Connection "upgrade";
            
            proxy_connect_timeout 60s;
            proxy_send_timeout 60s;
            proxy_read_timeout 60s;
        }

        location /api/ {
            limit_req zone=api burst=50 nodelay;
            
            proxy_pass http://fermm_backend;
            proxy_http_version 1.1;
            
            proxy_set_header Host \$host;
            proxy_set_header X-Real-IP \$remote_addr;
            proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto \$scheme;
            
            proxy_connect_timeout 60s;
            proxy_send_timeout 60s;
            proxy_read_timeout 60s;
        }

        location /ws {
            proxy_pass http://fermm_backend;
            proxy_http_version 1.1;
            
            proxy_set_header Host \$host;
            proxy_set_header X-Real-IP \$remote_addr;
            proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto \$scheme;
            
            proxy_set_header Upgrade \$http_upgrade;
            proxy_set_header Connection "upgrade";
            
            proxy_read_timeout 3600s;
            proxy_send_timeout 3600s;
        }

        location /health {
            access_log off;
            proxy_pass http://fermm_backend;
            proxy_http_version 1.1;
            proxy_set_header Host \$host;
        }

        location ~ /\. {
            deny all;
            access_log off;
            log_not_found off;
        }
    }
HTTPS_EOF
)
        sed -i "s|# HTTPS server (if SSL enabled)|$https_block|g" "$NGINX_CONF"
    else
        sed -i '/# HTTPS server (if SSL enabled)/d' "$NGINX_CONF"
    fi
}

update_docker_compose() {
    local http_port=$1
    local https_port=$2

    print_section "Updating Docker Compose Configuration"

    # Update ports in docker-compose file
    # This is a safe replacement that only changes port mappings
    sed -i "s|\"80:80\"|\"${http_port}:80\"|g" "$DOCKER_COMPOSE_FILE"
    sed -i "s|\"443:443\"|\"${https_port}:443\"|g" "$DOCKER_COMPOSE_FILE"

    print_success "Docker Compose configuration updated"
    echo "  HTTP:  $http_port"
    echo "  HTTPS: $https_port"
}

setup_ssl_with_certbot() {
    local domain=$1
    local email=$2

    print_section "Setting Up SSL Certificate"

    print_warning "Before continuing, ensure:"
    print_warning "  1. Your domain is pointing to this server's IP"
    print_warning "  2. Port 80 is accessible from the internet"
    print_warning "  3. You can access http://${domain}/"
    echo ""
    read -p "Press Enter to continue when ready..."

    # Create certbot command
    local certbot_cmd="sudo docker run --rm -it \
        -v certbot_certs:/etc/letsencrypt \
        -v certbot_www:/var/www/certbot \
        -p 80:80 \
        certbot/certbot certonly \
        --webroot \
        -w /var/www/certbot \
        --email $email \
        -d $domain \
        -d www.$domain \
        --agree-tos \
        --no-eff-email"

    print_success "Running certbot to obtain SSL certificate..."
    eval "$certbot_cmd" || {
        print_error "SSL certificate setup failed"
        print_error "You can try again later with: certbot certonly --standalone -d $domain"
        return 1
    }

    print_success "SSL certificate obtained successfully"
}

restart_services() {
    print_section "Restarting Services"

    print_warning "Stopping existing services..."
    cd "$FERMM_ROOT"
    sudo docker-compose -f docker-compose.ubuntu.yml down || true

    print_warning "Starting services with new configuration..."
    sudo docker-compose -f docker-compose.ubuntu.yml up -d

    sleep 5

    # Check if services are running
    if sudo docker-compose -f docker-compose.ubuntu.yml ps | grep -q "Up"; then
        print_success "Services started successfully"
    else
        print_error "Services failed to start"
        return 1
    fi
}

verify_setup() {
    local domain=$1

    print_section "Verifying Setup"

    print_success "Configuration Summary:"
    echo "  Domain: $domain"
    echo "  Nginx Config: $NGINX_CONF"
    echo "  Docker Compose: $DOCKER_COMPOSE_FILE"
    echo ""

    print_success "Access your FERMM server at:"
    echo "  → http://$domain"
    echo ""

    print_success "Dashboard access:"
    echo "  → http://$domain/"
    echo ""

    print_success "API endpoints:"
    echo "  → http://$domain/api/..."
    echo ""

    print_warning "Next steps:"
    echo "  1. Access your domain in a browser to verify it's working"
    echo "  2. Check logs: docker logs fermm-server"
    echo "  3. For SSL setup, run: sudo bash scripts/setup-domain.sh again"
}

###############################################################################
# Main Script
###############################################################################

test_domain_detection() {
    print_header
    print_section "Testing Domain Detection"
    
    local domains_raw=$(get_configured_domains)
    local -a domains=($(echo "$domains_raw" | sort -u))
    
    echo "Checking for configured domains..."
    echo ""
    echo "Docker nginx.conf: $NGINX_CONF"
    echo "System nginx: /etc/nginx/sites-enabled"
    echo ""
    
    if [[ ${#domains[@]} -eq 0 ]]; then
        print_warning "No domains detected"
    else
        print_success "Found ${#domains[@]} domain(s):"
        for i in "${!domains[@]}"; do
            echo "  $((i+1)). ${domains[$i]}"
        done
    fi
    
    echo ""
    print_success "Domain detection test complete"
    exit 0
}

main() {
    # Check for --test flag
    if [[ "$1" == "--test" ]]; then
        test_domain_detection
    fi
    
    print_header

    # Check prerequisites
    check_prerequisites

    # Prompt for configuration
    DOMAIN=$(prompt_domain)
    USE_SSL=$(prompt_use_ssl)
    PORTS=$(prompt_port)
    
    HTTP_PORT=$(echo "$PORTS" | cut -d'|' -f1)
    HTTPS_PORT=$(echo "$PORTS" | cut -d'|' -f2)

    check_ports_available "$HTTP_PORT" "$HTTPS_PORT"

    EMAIL=""
    if [[ "$USE_SSL" == "true" ]]; then
        EMAIL=$(prompt_email)
    fi

    # Generate configuration
    print_section "Generating Configuration"
    generate_nginx_config "$DOMAIN" "$HTTP_PORT" "$HTTPS_PORT" "$USE_SSL"
    print_success "nginx.conf generated"

    # Update docker-compose
    update_docker_compose "$HTTP_PORT" "$HTTPS_PORT"

    # Setup SSL if requested
    if [[ "$USE_SSL" == "true" ]]; then
        setup_ssl_with_certbot "$DOMAIN" "$EMAIL" || {
            print_warning "SSL setup skipped, you can configure it later"
        }
    fi

    # Restart services
    if restart_services; then
        verify_setup "$DOMAIN"
    else
        print_error "Failed to restart services"
        exit 1
    fi

    print_section "Domain Setup Complete!"
    echo -e "${GREEN}✓${NC} Your FERMM server is now accessible at ${BLUE}http://$DOMAIN${NC}"
    echo ""
}

# Run main function
main "$@"
