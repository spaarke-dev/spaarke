#!/bin/bash
#
# Deploy Ribbon Button Customization for Document Entity
# This script exports the spaarke_document_management solution with ribbon changes and reimports it
#

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"
SOLUTION_NAME="spaarke_document_management"
EXPORT_DIR="$REPO_ROOT/temp_export"

echo "=============================================="
echo "Ribbon Button Deployment Script"
echo "=============================================="
echo ""

# Check PAC CLI authentication
echo "[1/6] Checking PAC CLI authentication..."
PAC_AUTH_OUTPUT=$(pac auth list)
ORG_URL=$(echo "$PAC_AUTH_OUTPUT" | grep "\*" | awk '{print $NF}' | tr -d '\r')

if [ -z "$ORG_URL" ]; then
    echo "Error: No active PAC CLI authentication found. Run 'pac auth create' first."
    exit 1
fi

echo "      Connected to: $ORG_URL"
echo ""

# Create temp directory
echo "[2/6] Creating temporary export directory..."
mkdir -p "$EXPORT_DIR"
echo "      Export directory: $EXPORT_DIR"
echo ""

# Export the solution (to get current state from Dataverse)
echo "[3/6] Exporting solution from Dataverse..."
echo "      This will export the current state before our ribbon changes"
echo ""

pac solution export \
    --name "$SOLUTION_NAME" \
    --path "$EXPORT_DIR/${SOLUTION_NAME}.zip" \
    --managed false

echo ""
echo "      ✓ Solution exported"
echo ""

# Unpack the solution
echo "[4/6] Unpacking solution..."
echo ""

pac solution unpack \
    --zipfile "$EXPORT_DIR/${SOLUTION_NAME}.zip" \
    --folder "$EXPORT_DIR/${SOLUTION_NAME}_unpacked" \
    --packagetype Unmanaged

echo ""
echo "      ✓ Solution unpacked"
echo ""

# Copy our updated RibbonDiff.xml
echo "[5/6] Applying ribbon customizations..."
RIBBON_SOURCE="$REPO_ROOT/src/Entities/sprk_Document/RibbonDiff.xml"
RIBBON_DEST="$EXPORT_DIR/${SOLUTION_NAME}_unpacked/Entities/sprk_Document/RibbonDiff.xml"

if [ -f "$RIBBON_SOURCE" ]; then
    cp "$RIBBON_SOURCE" "$RIBBON_DEST"
    echo "      ✓ RibbonDiff.xml copied to unpacked solution"
else
    echo "      Error: Source RibbonDiff.xml not found at $RIBBON_SOURCE"
    exit 1
fi
echo ""

# Repack the solution
echo "[6/6] Repacking and importing solution..."
echo ""

pac solution pack \
    --zipfile "$EXPORT_DIR/${SOLUTION_NAME}_updated.zip" \
    --folder "$EXPORT_DIR/${SOLUTION_NAME}_unpacked" \
    --packagetype Unmanaged

echo ""
echo "      ✓ Solution repacked"
echo ""

# Import the updated solution
echo "      Importing updated solution to Dataverse..."
echo ""

pac solution import \
    --path "$EXPORT_DIR/${SOLUTION_NAME}_updated.zip" \
    --force-overwrite \
    --skip-dependency-check \
    --publish-changes

echo ""
echo "      ✓ Solution imported"
echo ""

# Cleanup
echo "Cleaning up temporary files..."
rm -rf "$EXPORT_DIR"
echo "      ✓ Cleanup complete"
echo ""

echo "=============================================="
echo "✓ Deployment Complete"
echo "=============================================="
echo ""
echo "Ribbon Button Details:"
echo "  Button Label: Quick Create: Document"
echo "  Location: Documents subgrid on parent entity forms"
echo "  Function: Spaarke_AddMultipleDocuments"
echo "  Library: sprk_subgrid_commands.js"
echo ""
echo "Next Steps:"
echo "1. Hard refresh browser (Ctrl + Shift + R)"
echo "2. Open a Matter record"
echo "3. Scroll to Documents subgrid"
echo "4. Verify 'Quick Create: Document' button appears"
echo "5. Click button to test Custom Page dialog"
echo ""
echo "NOTE: You may need to wait 1-2 minutes for ribbon cache to clear."
echo "If button doesn't appear immediately, close and reopen the form."
echo ""
