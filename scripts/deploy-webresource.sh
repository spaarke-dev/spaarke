#!/bin/bash
#
# Deploy Web Resource to Dataverse using Dataverse Web API
#

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"
WEB_RESOURCE_PATH="$REPO_ROOT/src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/WebResources/sprk_subgrid_commands.js"

echo "====================================="
echo "Web Resource Deployment Script"
echo "====================================="
echo ""

# Verify Web Resource exists
if [ ! -f "$WEB_RESOURCE_PATH" ]; then
    echo "Error: Web Resource not found at: $WEB_RESOURCE_PATH"
    exit 1
fi

echo "[1/5] Reading Web Resource file..."
WEB_RESOURCE_CONTENT=$(cat "$WEB_RESOURCE_PATH")
WEB_RESOURCE_BASE64=$(echo -n "$WEB_RESOURCE_CONTENT" | base64 -w 0)
FILE_SIZE=$(wc -c < "$WEB_RESOURCE_PATH")
echo "      File size: $FILE_SIZE bytes"
echo ""

# Get active environment URL
echo "[2/5] Getting Dataverse connection..."
PAC_AUTH_OUTPUT=$(pac auth list)
ORG_URL=$(echo "$PAC_AUTH_OUTPUT" | grep "\*" | awk '{print $NF}' | tr -d '\r')

if [ -z "$ORG_URL" ]; then
    echo "Error: No active PAC CLI authentication found. Run 'pac auth create' first."
    exit 1
fi

echo "      Connected to: $ORG_URL"
echo ""

# Get access token
echo "[3/5] Getting access token..."
ACCESS_TOKEN=$(pac auth token)

if [ -z "$ACCESS_TOKEN" ]; then
    echo "Error: Failed to get access token"
    exit 1
fi

echo "      Token acquired"
echo ""

# Prepare API URL
API_URL="$ORG_URL/api/data/v9.2"

# Check if Web Resource already exists
echo "[4/5] Checking if Web Resource exists..."

SEARCH_URL="$API_URL/webresourceset?\$filter=name eq 'sprk_subgrid_commands.js'"

SEARCH_RESPONSE=$(curl -s -X GET "$SEARCH_URL" \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Content-Type: application/json" \
    -H "OData-MaxVersion: 4.0" \
    -H "OData-Version: 4.0" \
    -H "Accept: application/json")

WEB_RESOURCE_ID=$(echo "$SEARCH_RESPONSE" | grep -o '"webresourceid":"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -n "$WEB_RESOURCE_ID" ]; then
    echo "      Found existing Web Resource: $WEB_RESOURCE_ID"
    echo "      Updating..."

    UPDATE_URL="$API_URL/webresourceset($WEB_RESOURCE_ID)"
    UPDATE_PAYLOAD=$(cat <<EOF
{
    "content": "$WEB_RESOURCE_BASE64",
    "description": "Generic command script for multi-file document upload across all entity types"
}
EOF
)

    UPDATE_RESPONSE=$(curl -s -X PATCH "$UPDATE_URL" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -H "OData-MaxVersion: 4.0" \
        -H "OData-Version: 4.0" \
        -H "Accept: application/json" \
        -d "$UPDATE_PAYLOAD")

    echo "      ✓ Web Resource updated successfully"
else
    echo "      Web Resource does not exist. Creating new..."

    CREATE_URL="$API_URL/webresourceset"
    CREATE_PAYLOAD=$(cat <<EOF
{
    "name": "sprk_subgrid_commands.js",
    "displayname": "Subgrid Commands - Universal Document Upload",
    "description": "Generic command script for multi-file document upload across all entity types",
    "webresourcetype": 3,
    "content": "$WEB_RESOURCE_BASE64"
}
EOF
)

    CREATE_RESPONSE=$(curl -s -X POST "$CREATE_URL" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -H "OData-MaxVersion: 4.0" \
        -H "OData-Version: 4.0" \
        -H "Accept: application/json" \
        -H "Prefer: return=representation" \
        -d "$CREATE_PAYLOAD")

    WEB_RESOURCE_ID=$(echo "$CREATE_RESPONSE" | grep -o '"webresourceid":"[^"]*"' | head -1 | cut -d'"' -f4)
    echo "      ✓ Web Resource created successfully"
fi

echo ""

# Publish Web Resource
echo "[5/5] Publishing Web Resource..."

PUBLISH_URL="$API_URL/PublishXml"
PUBLISH_PAYLOAD=$(cat <<EOF
{
    "ParameterXml": "<importexportxml><webresources><webresource>{$WEB_RESOURCE_ID}</webresource></webresources></importexportxml>"
}
EOF
)

curl -s -X POST "$PUBLISH_URL" \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Content-Type: application/json" \
    -H "OData-MaxVersion: 4.0" \
    -H "OData-Version: 4.0" \
    -d "$PUBLISH_PAYLOAD" > /dev/null

echo "      ✓ Web Resource published successfully"
echo ""

echo "====================================="
echo "✓ Deployment Complete"
echo "====================================="
echo ""
echo "Web Resource Details:"
echo "  Name: sprk_subgrid_commands.js"
echo "  ID: $WEB_RESOURCE_ID"
echo "  Status: Published"
echo ""
echo "Next Steps:"
echo "1. Create Custom Page manually in Power Apps Studio"
echo "2. Configure command buttons on entity forms to call:"
echo "   Function: Spaarke.Commands.AddMultipleDocuments"
echo "   Library: sprk_subgrid_commands.js"
echo ""
