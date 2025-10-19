#!/bin/bash
#
# Deploy Web Resource using solution export/import approach
# This is more reliable than direct Web API calls
#

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"
WEB_RESOURCE_PATH="$REPO_ROOT/src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/WebResources/sprk_subgrid_commands.js"
SOLUTION_NAME="spaarke_document_management"
EXPORT_DIR="$REPO_ROOT/temp_webresource_export"

echo "=============================================="
echo "Web Resource Deployment Script"
echo "=============================================="
echo ""

# Verify Web Resource exists
if [ ! -f "$WEB_RESOURCE_PATH" ]; then
    echo "Error: Web Resource not found at: $WEB_RESOURCE_PATH"
    exit 1
fi

FILE_SIZE=$(wc -c < "$WEB_RESOURCE_PATH")
echo "[1/7] Web Resource file verified"
echo "      File: sprk_subgrid_commands.js"
echo "      Size: $FILE_SIZE bytes"
echo ""

# Check PAC CLI authentication
echo "[2/7] Checking PAC CLI authentication..."
PAC_AUTH_OUTPUT=$(pac auth list)
ORG_URL=$(echo "$PAC_AUTH_OUTPUT" | grep "\*" | awk '{print $NF}' | tr -d '\r')

if [ -z "$ORG_URL" ]; then
    echo "Error: No active PAC CLI authentication found"
    exit 1
fi

echo "      Connected to: $ORG_URL"
echo ""

# Create temp directory
echo "[3/7] Creating temporary export directory..."
mkdir -p "$EXPORT_DIR"
echo ""

# Export solution
echo "[4/7] Exporting solution from Dataverse..."
pac solution export \
    --name "$SOLUTION_NAME" \
    --path "$EXPORT_DIR/${SOLUTION_NAME}.zip" \
    --managed false

echo ""
echo "      ✓ Solution exported"
echo ""

# Unpack solution
echo "[5/7] Unpacking solution..."
pac solution unpack \
    --zipfile "$EXPORT_DIR/${SOLUTION_NAME}.zip" \
    --folder "$EXPORT_DIR/${SOLUTION_NAME}_unpacked" \
    --packagetype Unmanaged

echo ""
echo "      ✓ Solution unpacked"
echo ""

# Create WebResources folder in unpacked solution if it doesn't exist
WEBRES_DIR="$EXPORT_DIR/${SOLUTION_NAME}_unpacked/WebResources"
mkdir -p "$WEBRES_DIR"

# Copy Web Resource
echo "[6/7] Adding Web Resource to solution..."
cp "$WEB_RESOURCE_PATH" "$WEBRES_DIR/sprk_subgrid_commands.js"
echo "      ✓ Web Resource copied"
echo ""

# Repack and import
echo "[7/7] Repacking and importing solution..."
pac solution pack \
    --zipfile "$EXPORT_DIR/${SOLUTION_NAME}_updated.zip" \
    --folder "$EXPORT_DIR/${SOLUTION_NAME}_unpacked" \
    --packagetype Unmanaged

echo ""
echo "      ✓ Solution repacked"
echo ""

echo "      Importing updated solution..."
pac solution import \
    --path "$EXPORT_DIR/${SOLUTION_NAME}_updated.zip" \
    --force-overwrite \
    --publish-changes

echo ""
echo "      ✓ Solution imported and published"
echo ""

# Cleanup
echo "Cleaning up..."
rm -rf "$EXPORT_DIR"
echo ""

echo "=============================================="
echo "✓ Web Resource Deployed"
echo "=============================================="
echo ""
echo "Web Resource Details:"
echo "  Name: sprk_subgrid_commands.js"
echo "  Status: Published"
echo ""
