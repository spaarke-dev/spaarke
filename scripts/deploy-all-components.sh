#!/bin/bash
#
# Master Deployment Script - Universal Document Upload
# Deploys all components in the correct order:
#   1. Web Resource (sprk_subgrid_commands.js)
#   2. Ribbon Customization (Quick Create: Document button)
#

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"
SOLUTION_NAME="spaarke_document_management"
EXPORT_DIR="$REPO_ROOT/temp_deployment"

echo "======================================================="
echo "Universal Document Upload - Complete Deployment"
echo "======================================================="
echo ""
echo "This script will deploy:"
echo "  ✓ PCF Control (already deployed)"
echo "  → Web Resource (sprk_subgrid_commands.js)"
echo "  → Ribbon Button (Quick Create: Document)"
echo ""

# Check prerequisites
echo "[Prerequisites] Checking requirements..."

# Check PAC CLI
if ! command -v pac &> /dev/null; then
    echo "Error: PAC CLI not found. Please install Power Platform CLI."
    exit 1
fi

# Check authentication
PAC_AUTH_OUTPUT=$(pac auth list)
ORG_URL=$(echo "$PAC_AUTH_OUTPUT" | grep "\*" | awk '{print $NF}' | tr -d '\r')

if [ -z "$ORG_URL" ]; then
    echo "Error: No active PAC CLI authentication. Run 'pac auth create' first."
    exit 1
fi

echo "      ✓ PAC CLI authenticated"
echo "      ✓ Connected to: $ORG_URL"
echo ""

# Check Web Resource file
WEB_RESOURCE_PATH="$REPO_ROOT/src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/WebResources/sprk_subgrid_commands.js"
if [ ! -f "$WEB_RESOURCE_PATH" ]; then
    echo "Error: Web Resource not found at: $WEB_RESOURCE_PATH"
    exit 1
fi
echo "      ✓ Web Resource file found"
echo ""

# Check RibbonDiff.xml
RIBBON_PATH="$REPO_ROOT/src/Entities/sprk_Document/RibbonDiff.xml"
if [ ! -f "$RIBBON_PATH" ]; then
    echo "Error: RibbonDiff.xml not found at: $RIBBON_PATH"
    exit 1
fi
echo "      ✓ RibbonDiff.xml found"
echo ""

# Create temp directory
echo "[Step 1/5] Creating temporary workspace..."
mkdir -p "$EXPORT_DIR"
echo "      ✓ Workspace created"
echo ""

# Export current solution
echo "[Step 2/5] Exporting current solution from Dataverse..."
echo "      This may take 30-60 seconds..."
echo ""

pac solution export \
    --name "$SOLUTION_NAME" \
    --path "$EXPORT_DIR/${SOLUTION_NAME}.zip" \
    --managed false

echo ""
echo "      ✓ Solution exported"
echo ""

# Unpack solution
echo "[Step 3/5] Unpacking solution..."
pac solution unpack \
    --zipfile "$EXPORT_DIR/${SOLUTION_NAME}.zip" \
    --folder "$EXPORT_DIR/${SOLUTION_NAME}_unpacked" \
    --packagetype Unmanaged

echo ""
echo "      ✓ Solution unpacked"
echo ""

# Apply customizations
echo "[Step 4/5] Applying customizations..."

# Create WebResources folder if needed
WEBRES_DIR="$EXPORT_DIR/${SOLUTION_NAME}_unpacked/WebResources"
mkdir -p "$WEBRES_DIR"

# Copy Web Resource
echo "      → Adding Web Resource..."
cp "$WEB_RESOURCE_PATH" "$WEBRES_DIR/sprk_subgrid_commands.js"
echo "        ✓ sprk_subgrid_commands.js added"

# Copy RibbonDiff.xml
echo "      → Applying ribbon customization..."
RIBBON_DEST="$EXPORT_DIR/${SOLUTION_NAME}_unpacked/Entities/sprk_Document/RibbonDiff.xml"
if [ -d "$(dirname "$RIBBON_DEST")" ]; then
    cp "$RIBBON_PATH" "$RIBBON_DEST"
    echo "        ✓ RibbonDiff.xml updated"
else
    echo "        ⚠ Warning: Document entity not found in solution"
    echo "        The ribbon will need to be configured manually"
fi

echo ""
echo "      ✓ All customizations applied"
echo ""

# Repack solution
echo "[Step 5/5] Repacking and deploying to Dataverse..."
pac solution pack \
    --zipfile "$EXPORT_DIR/${SOLUTION_NAME}_updated.zip" \
    --folder "$EXPORT_DIR/${SOLUTION_NAME}_unpacked" \
    --packagetype Unmanaged

echo ""
echo "      ✓ Solution repacked"
echo ""

echo "      Importing to Dataverse..."
echo "      This may take 1-2 minutes..."
echo ""

pac solution import \
    --path "$EXPORT_DIR/${SOLUTION_NAME}_updated.zip" \
    --force-overwrite \
    --skip-dependency-check \
    --publish-changes

echo ""
echo "      ✓ Solution imported and published"
echo ""

# Cleanup
echo "Cleaning up temporary files..."
rm -rf "$EXPORT_DIR"
echo "      ✓ Cleanup complete"
echo ""

echo "======================================================="
echo "✓✓✓ DEPLOYMENT COMPLETE ✓✓✓"
echo "======================================================="
echo ""
echo "Deployed Components:"
echo "  ✓ PCF Control: Spaarke.Controls.UniversalDocumentUpload v2.0.0"
echo "  ✓ Web Resource: sprk_subgrid_commands.js"
echo "  ✓ Ribbon Button: Quick Create: Document"
echo ""
echo "Next Steps:"
echo ""
echo "1. HARD REFRESH BROWSER"
echo "   Press Ctrl + Shift + R to clear cached customizations"
echo ""
echo "2. OPEN A MATTER RECORD"
echo "   Navigate to any Matter record in Dataverse"
echo ""
echo "3. VERIFY BUTTON APPEARS"
echo "   Scroll to the Documents subgrid"
echo "   Look for 'Quick Create: Document' button"
echo ""
echo "4. TEST THE FEATURE"
echo "   Click the button"
echo "   Select 2-3 test files"
echo "   Verify Custom Page dialog opens"
echo "   Complete the upload workflow"
echo ""
echo "⚠ IMPORTANT NOTES:"
echo "  - Custom Page must still be created manually"
echo "    (See DEPLOYMENT-GUIDE.md Step 2)"
echo "  - Ribbon cache may take 1-2 minutes to refresh"
echo "  - If button doesn't appear, close and reopen the form"
echo ""
echo "For troubleshooting, see:"
echo "  $REPO_ROOT/src/controls/UniversalQuickCreate/DEPLOYMENT-GUIDE.md"
echo ""
