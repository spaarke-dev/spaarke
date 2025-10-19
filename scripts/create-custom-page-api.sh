#!/bin/bash
#
# Create Custom Page directly in Dataverse using Web API
# This bypasses the visual designer
#

set -e

echo "=============================================="
echo "Custom Page Creation via Dataverse API"
echo "=============================================="
echo ""

# Get PAC auth
echo "[1/3] Getting Dataverse connection..."
PAC_AUTH_OUTPUT=$(pac auth list)
ORG_URL=$(echo "$PAC_AUTH_OUTPUT" | grep "\*" | awk '{print $NF}' | tr -d '\r')

if [ -z "$ORG_URL" ]; then
    echo "Error: No active PAC CLI authentication"
    exit 1
fi

echo "      Connected to: $ORG_URL"
echo ""

# Get access token
echo "[2/3] Getting access token..."
ACCESS_TOKEN=$(pac auth token)

if [ -z "$ACCESS_TOKEN" ]; then
    echo "Error: Failed to get access token"
    exit 1
fi

echo "      Token acquired"
echo ""

# Create Custom Page
echo "[3/3] Creating Custom Page..."
echo ""

API_URL="$ORG_URL/api/data/v9.2"

# The Custom Page definition (simplified YAML-like format for canvas apps)
# We'll create it as a Canvas App with customPageType
CANVAS_APP_PAYLOAD=$(cat <<'EOF'
{
  "name": "Universal Document Upload",
  "displayname": "Universal Document Upload",
  "description": "Upload multiple documents to SharePoint Embedded",
  "canvasapptype": 3
}
EOF
)

echo "      Note: Custom Pages cannot be fully created via Web API"
echo "      The API only supports basic canvas app creation"
echo ""
echo "=============================================="
echo "RECOMMENDED SOLUTION"
echo "=============================================="
echo ""
echo "Export an existing Custom Page as a template:"
echo ""
echo "1. Create a SIMPLE custom page manually (just blank page with the PCF control)"
echo "2. Export it as a solution"
echo "3. We can then modify the exported files and reimport"
echo ""
echo "OR use this workaround:"
echo ""
echo "Close the current custom page designer and try this simplified approach:"
echo ""
echo "1. In make.powerapps.com, go to Apps"
echo "2. Click '+ New app' -> 'Start with a page design'"
echo "3. Choose 'Blank'"
echo "4. Name it: sprk_universaldocumentupload_page"
echo "5. On the canvas, insert -> Get more components"
echo "6. Import 'UniversalDocumentUpload'"
echo "7. Add it to canvas and resize"
echo "8. For parameters, you may need to use the formula bar at top"
echo "9. In formula bar, you can reference: Param(\"parentEntityName\")"
echo ""
echo "Would you like me to create a complete step-by-step guide with"
echo "screenshots locations instead?"
echo ""
