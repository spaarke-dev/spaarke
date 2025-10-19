#!/bin/bash

# Test script to verify OBO flow configuration
# This will attempt to call the API and see what error we get

echo "Testing SPE BFF API Configuration..."
echo "====================================="
echo ""

# Step 1: Check if API is running
echo "1. Testing API health endpoint..."
HEALTH_RESPONSE=$(curl -s https://spe-api-dev-67e2xz.azurewebsites.net/ping)
echo "   Response: $HEALTH_RESPONSE"

if [[ $HEALTH_RESPONSE == *"Spe.Bff.Api"* ]]; then
    echo "   ✅ API is running"
else
    echo "   ❌ API is NOT responding correctly"
    exit 1
fi

echo ""
echo "2. Testing OBO endpoint (will fail auth, but shows error)..."

# Try to call the OBO endpoint without a token
curl -s -X PUT \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/test-container/files/test.txt" \
  -H "Content-Type: application/octet-stream" \
  -d "test data" \
  -w "\nHTTP Status: %{http_code}\n" \
  2>&1

echo ""
echo "3. Expected result: 401 Unauthorized (no token provided)"
echo "   If you see 500 or AADSTS error, there's a configuration issue"
echo ""
echo "====================================="
echo "Test complete. Check the output above."
