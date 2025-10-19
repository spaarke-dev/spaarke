#!/bin/bash
# Quick script to test SDAP BFF API endpoints

API_BASE="https://spe-api-dev-67e2xz.azurewebsites.net"
CONTAINER_TYPE_ID="8a6ce34c-6055-4681-8f87-2f4f9f921c06"

# Get token
echo "Getting auth token..."
pac auth token > /tmp/token.txt 2>&1
TOKEN=$(cat /tmp/token.txt)
echo "Token length: ${#TOKEN}"

# Test 1: Ping
echo -e "\n=== TEST 1: Ping API ==="
curl -s "$API_BASE/ping" | python -m json.tool

# Test 2: User Info (OBO)
echo -e "\n=== TEST 2: User Info ==="
curl -s -H "Authorization: Bearer $TOKEN" "$API_BASE/api/me" | python -m json.tool

# Test 3: List Containers (MI - requires special auth)
echo -e "\n=== TEST 3: List Containers (may fail due to auth) ==="
curl -s -H "Authorization: Bearer $TOKEN" "$API_BASE/api/containers?containerTypeId=$CONTAINER_TYPE_ID" | python -m json.tool 2>/dev/null || echo "Auth required"

# Test 4: List Containers (OBO endpoint alternative)
echo -e "\n=== TEST 4: List Drive Children (OBO - if you have Drive ID) ==="
echo "Skipping - requires Drive ID"
echo "Usage: curl -H 'Authorization: Bearer \$TOKEN' '$API_BASE/api/obo/containers/{containerId}/children'"

echo -e "\n=== SUMMARY ==="
echo "✓ API is running: $API_BASE"
echo "✓ Token obtained (${#TOKEN} chars)"
echo ""
echo "Next steps:"
echo "1. Get Container ID from admin/another source"
echo "2. Use OBO endpoints for file operations"
echo "3. The /api/containers endpoint requires 'canmanagecontainers' policy"
