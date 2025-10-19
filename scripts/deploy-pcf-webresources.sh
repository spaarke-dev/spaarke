#!/bin/bash
#
# Deploy PCF Web Resources to Dataverse
#

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"

echo "====================================="
echo "PCF Web Resources Deployment"
echo "====================================="
echo ""

# Get active environment
echo "[1/5] Getting Dataverse connection..."
PAC_AUTH_OUTPUT=$(pac auth list)
ORG_URL=$(echo "$PAC_AUTH_OUTPUT" | grep "\*" | awk '{print $NF}' | tr -d '\r')

if [ -z "$ORG_URL" ]; then
    echo "Error: No active PAC CLI authentication"
    exit 1
fi

echo "      Connected to: $ORG_URL"
echo ""

# Get access token
echo "[2/5] Getting access token..."
ACCESS_TOKEN=$(pac auth token)
echo "      Token acquired"
echo ""

API_URL="$ORG_URL/api/data/v9.2"

# Deploy bundle.js
echo "[3/5] Deploying bundle.js..."
BUNDLE_PATH="$REPO_ROOT/src/controls/UniversalQuickCreate/out/controls/UniversalQuickCreate/bundle.js"
BUNDLE_CONTENT=$(cat "$BUNDLE_PATH" | base64 -w 0)
BUNDLE_NAME="sprk_Spaarke.Controls.UniversalDocumentUpload/bundle.js"

# Check if exists
SEARCH_URL="$API_URL/webresourceset?\$filter=name eq '$BUNDLE_NAME'"
SEARCH_RESPONSE=$(curl -s -X GET "$SEARCH_URL" \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Accept: application/json")

WEB_RESOURCE_ID=$(echo "$SEARCH_RESPONSE" | grep -o '"webresourceid":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -n "$WEB_RESOURCE_ID" ]; then
    echo "      Updating existing bundle.js..."
    UPDATE_URL="$API_URL/webresourceset($WEB_RESOURCE_ID)"
    curl -s -X PATCH "$UPDATE_URL" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d "{\"content\": \"$BUNDLE_CONTENT\"}" > /dev/null
    echo "      ✓ bundle.js updated"
else
    echo "      Creating new bundle.js..."
    CREATE_URL="$API_URL/webresourceset"
    CREATE_RESPONSE=$(curl -s -X POST "$CREATE_URL" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -H "Prefer: return=representation" \
        -d "{
            \"name\": \"$BUNDLE_NAME\",
            \"displayname\": \"Universal Document Upload - Bundle\",
            \"webresourcetype\": 3,
            \"content\": \"$BUNDLE_CONTENT\"
        }")
    WEB_RESOURCE_ID=$(echo "$CREATE_RESPONSE" | grep -o '"webresourceid":"[^"]*"' | head -1 | cut -d'"' -f4)
    echo "      ✓ bundle.js created"
fi

BUNDLE_ID=$WEB_RESOURCE_ID
echo ""

# Deploy CSS
echo "[4/5] Deploying CSS..."
CSS_PATH="$REPO_ROOT/src/controls/UniversalQuickCreate/out/controls/UniversalQuickCreate/css/UniversalQuickCreate.css"
CSS_CONTENT=$(cat "$CSS_PATH" | base64 -w 0)
CSS_NAME="sprk_Spaarke.Controls.UniversalDocumentUpload/css/UniversalQuickCreate.css"

SEARCH_URL="$API_URL/webresourceset?\$filter=name eq '$CSS_NAME'"
SEARCH_RESPONSE=$(curl -s -X GET "$SEARCH_URL" \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Accept: application/json")

WEB_RESOURCE_ID=$(echo "$SEARCH_RESPONSE" | grep -o '"webresourceid":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -n "$WEB_RESOURCE_ID" ]; then
    echo "      Updating existing CSS..."
    UPDATE_URL="$API_URL/webresourceset($WEB_RESOURCE_ID)"
    curl -s -X PATCH "$UPDATE_URL" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d "{\"content\": \"$CSS_CONTENT\"}" > /dev/null
    echo "      ✓ CSS updated"
else
    echo "      Creating new CSS..."
    CREATE_URL="$API_URL/webresourceset"
    CREATE_RESPONSE=$(curl -s -X POST "$CREATE_URL" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -H "Prefer: return=representation" \
        -d "{
            \"name\": \"$CSS_NAME\",
            \"displayname\": \"Universal Document Upload - CSS\",
            \"webresourcetype\": 2,
            \"content\": \"$CSS_CONTENT\"
        }")
    WEB_RESOURCE_ID=$(echo "$CREATE_RESPONSE" | grep -o '"webresourceid":"[^"]*"' | head -1 | cut -d'"' -f4)
    echo "      ✓ CSS created"
fi

CSS_ID=$WEB_RESOURCE_ID
echo ""

# Publish
echo "[5/5] Publishing web resources..."
PUBLISH_URL="$API_URL/PublishXml"
PUBLISH_PAYLOAD="{\"ParameterXml\": \"<importexportxml><webresources><webresource>{$BUNDLE_ID}</webresource><webresource>{$CSS_ID}</webresource></webresources></importexportxml>\"}"

curl -s -X POST "$PUBLISH_URL" \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Content-Type: application/json" \
    -d "$PUBLISH_PAYLOAD" > /dev/null

echo "      ✓ Published"
echo ""

echo "====================================="
echo "✓ Deployment Complete"
echo "====================================="
echo ""
echo "Web Resources Deployed:"
echo "  - sprk_Spaarke.Controls.UniversalDocumentUpload/bundle.js"
echo "  - sprk_Spaarke.Controls.UniversalDocumentUpload/css/UniversalQuickCreate.css"
echo ""
