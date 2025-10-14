#!/bin/bash
# Test script to verify Phase 4 cache functionality
# Makes multiple requests to the /api/me endpoint to demonstrate cache hits

API_BASE="https://spe-api-dev-67e2xz.azurewebsites.net"

echo "=== Phase 4 Cache Performance Test ==="
echo "Testing OBO token caching on /api/me endpoint"
echo ""

# Get token
echo "Step 1: Getting auth token..."
pac auth token > /tmp/token.txt 2>&1
TOKEN=$(cat /tmp/token.txt)
echo "✓ Token obtained (${#TOKEN} chars)"
echo ""

# Make 5 requests to see cache behavior
echo "Step 2: Making 5 requests to /api/me endpoint"
echo "Expected: First request = cache MISS (OBO exchange ~200ms)"
echo "         Subsequent requests = cache HIT (~5ms)"
echo ""

for i in {1..5}; do
  echo "Request $i..."
  START_TIME=$(date +%s%3N)

  RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
    -H "Authorization: Bearer $TOKEN" \
    "$API_BASE/api/me")

  END_TIME=$(date +%s%3N)
  ELAPSED=$((END_TIME - START_TIME))

  HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
  TIME_TOTAL=$(echo "$RESPONSE" | grep "TIME_TOTAL:" | cut -d':' -f2)
  BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS:" | grep -v "TIME_TOTAL:")

  echo "  HTTP Status: $HTTP_STATUS"
  echo "  Response Time: ${TIME_TOTAL}s (${ELAPSED}ms client-side)"

  if [ $i -eq 1 ]; then
    echo "  Expected: Cache MISS (first request, performing OBO)"
  else
    echo "  Expected: Cache HIT (using cached Graph token)"
  fi

  # Parse response to show user info
  if command -v jq &> /dev/null; then
    USER_NAME=$(echo "$BODY" | jq -r '.displayName // "Unknown"' 2>/dev/null)
    if [ "$USER_NAME" != "Unknown" ] && [ -n "$USER_NAME" ]; then
      echo "  User: $USER_NAME"
    fi
  fi

  echo ""

  # Small delay between requests
  sleep 0.5
done

echo "=== Test Complete ==="
echo ""
echo "To verify cache behavior, check Azure logs:"
echo "  az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2"
echo ""
echo "Look for log messages:"
echo "  - 'Cache MISS for token hash' (first request)"
echo "  - 'Cache HIT for token hash' (subsequent requests)"
echo "  - 'Using cached Graph token (cache hit)' (in GraphClientFactory)"
echo ""
echo "Expected Performance:"
echo "  Request 1: ~200-300ms (OBO exchange + Graph API call)"
echo "  Requests 2-5: ~50-100ms (cached token, only Graph API call)"
echo "  Cache reduces OBO overhead by 97% (~200ms → ~5ms)"
