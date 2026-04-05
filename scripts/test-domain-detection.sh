#!/bin/bash

# Standalone test to debug domain detection
# Usage: sudo bash scripts/test-domain-detection.sh

echo "=== Testing Domain Detection ==="
echo ""

NGINX_CONF="./nginx.conf"

echo "Checking Docker nginx.conf..."
echo "File: $NGINX_CONF"
if [[ -f "$NGINX_CONF" ]]; then
    echo "✓ File exists"
    echo "Contents of server_name declarations:"
    grep 'server_name' "$NGINX_CONF" | grep -v '^#' || echo "  (none found)"
else
    echo "✗ File not found"
fi

echo ""
echo "Checking system nginx configs..."
echo "Directory: /etc/nginx/sites-enabled"
if [[ -d /etc/nginx/sites-enabled ]]; then
    echo "✓ Directory exists"
    echo "Contents of server_name declarations:"
    grep -r 'server_name' /etc/nginx/sites-enabled 2>/dev/null | grep -v '^#' || echo "  (none found)"
else
    echo "✗ Directory not found"
fi

echo ""
echo "=== Testing sed parsing ==="
echo ""

# Test on a sample line
SAMPLE="    server_name rmm.bware.systems; # managed by Certbot"
echo "Sample line: $SAMPLE"
echo ""

# Step 1: Extract domain
echo "Step 1 - Extract after 'server_name':"
echo "$SAMPLE" | sed 's/.*server_name\s*//g'

echo ""
echo "Step 2 - Remove semicolon and comments:"
echo "$SAMPLE" | sed 's/.*server_name\s*//g' | sed 's/;.*//'

echo ""
echo "=== Testing Full Function ==="
echo ""

get_configured_domains() {
    local domains=""
    
    # Check Docker nginx.conf (FERMM-managed)
    if [[ -f "$NGINX_CONF" ]]; then
        echo "[DEBUG] Checking $NGINX_CONF" >&2
        local docker_domains=$(grep 'server_name' "$NGINX_CONF" 2>/dev/null | \
            grep -v '^#' | \
            sed 's/.*server_name\s*//g' | \
            sed 's/;.*//' | \
            tr ' ' '\n' | \
            grep -v '^_$' | \
            grep -v '^www\.' | \
            grep -v '^$' || true)
        echo "[DEBUG] Docker domains found: $docker_domains" >&2
        domains+="$docker_domains"
    fi
    
    # Check system nginx configs (existing domains on server)
    if [[ -d /etc/nginx/sites-enabled ]]; then
        echo "[DEBUG] Checking /etc/nginx/sites-enabled" >&2
        domains+=$'\n'
        for file in /etc/nginx/sites-enabled/*; do
            if [[ -f "$file" ]] && [[ -r "$file" ]]; then
                echo "[DEBUG]   File: $file" >&2
                local system_domains=$(grep 'server_name' "$file" 2>/dev/null | \
                    grep -v '^#' | \
                    sed 's/.*server_name\s*//g' | \
                    sed 's/[;#].*//' | \
                    tr ' ' '\n' | \
                    grep -v '^_$' | \
                    grep -v '^www\.' | \
                    grep -v '^$' || true)
                if [[ ! -z "$system_domains" ]]; then
                    echo "[DEBUG]   Domains: $system_domains" >&2
                fi
                domains+="$system_domains"
                domains+=$'\n'
            fi
        done
    fi
    
    # Return sorted unique domains
    echo "$domains" | grep -v '^$' | sort -u
}

echo "Calling get_configured_domains():"
DOMAINS=$(get_configured_domains)
echo "Result:"
echo "$DOMAINS"

if [[ -z "$DOMAINS" ]]; then
    echo "✗ No domains found"
else
    echo "✓ Domains found:"
    echo "$DOMAINS" | nl
fi
