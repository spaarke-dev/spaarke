#!/usr/bin/env bash
# =============================================================================
# Create-ToolSeedRecords.sh
# AIPL-033: Create 8 system Tool records (TL-001 through TL-008) in Dataverse
#
# Entity:     sprk_analysistool (collection: sprk_analysistools)
# Environment: https://spaarkedev1.crm.dynamics.com
#
# Usage:
#   bash projects/ai-spaarke-platform-enhancements-r1/scripts/Create-ToolSeedRecords.sh
#
# Prerequisites:
#   - Azure CLI authenticated (az login)
#   - Access to spaarkedev1.crm.dynamics.com
# =============================================================================

set -euo pipefail

DATAVERSE_URL="https://spaarkedev1.crm.dynamics.com"
API_BASE="${DATAVERSE_URL}/api/data/v9.2"
COLLECTION="sprk_analysistools"

echo "=== AIPL-033: Create Tool Seed Records ==="
echo "Environment: ${DATAVERSE_URL}"
echo ""

# --- Get bearer token ---
echo "Getting Dataverse access token..."
TOKEN=$(az account get-access-token --resource "${DATAVERSE_URL}" --query accessToken -o tsv)
if [ -z "$TOKEN" ]; then
  echo "ERROR: Failed to get access token. Run 'az login' first."
  exit 1
fi
echo "Token acquired (length: ${#TOKEN})"
echo ""

# --- Helper: POST a tool record ---
create_tool() {
  local toolcode="$1"
  local name="$2"
  local description="$3"
  local handlerclass="$4"
  local configuration="$5"

  echo "Creating ${toolcode}: ${name}..."

  local body
  body=$(printf '{"sprk_name":"%s","sprk_description":"%s","sprk_handlerclass":"%s","sprk_configuration":%s,"sprk_toolcode":"%s"}' \
    "$name" "$description" "$handlerclass" "$configuration" "$toolcode")

  local response
  response=$(curl -s -o /dev/null -w "%{http_code}" \
    -X POST \
    "${API_BASE}/${COLLECTION}" \
    -H "Authorization: Bearer ${TOKEN}" \
    -H "Content-Type: application/json" \
    -H "OData-MaxVersion: 4.0" \
    -H "OData-Version: 4.0" \
    -H "Accept: application/json" \
    -d "$body")

  if [ "$response" = "204" ] || [ "$response" = "201" ]; then
    echo "  -> ${toolcode} created successfully (HTTP ${response})"
  else
    echo "  -> ERROR: HTTP ${response} for ${toolcode}"
    # Show error body
    curl -s \
      -X POST \
      "${API_BASE}/${COLLECTION}" \
      -H "Authorization: Bearer ${TOKEN}" \
      -H "Content-Type: application/json" \
      -H "OData-MaxVersion: 4.0" \
      -H "OData-Version: 4.0" \
      -H "Accept: application/json" \
      -d "$body" | head -c 500
    echo ""
  fi
}

# =============================================================================
# STEP 1: Verify no TL-001–TL-008 records exist
# =============================================================================
echo "--- Step 1: Checking for existing TL-001–TL-008 records ---"
EXISTING=$(curl -s \
  "${API_BASE}/${COLLECTION}?\$select=sprk_name,sprk_toolcode&\$filter=sprk_toolcode ne null" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "OData-MaxVersion: 4.0" \
  -H "OData-Version: 4.0" \
  -H "Accept: application/json")

echo "Existing records with sprk_toolcode set:"
echo "$EXISTING" | python3 -c "
import sys, json
data = json.load(sys.stdin)
items = data.get('value', [])
if not items:
    print('  (none — safe to create)')
else:
    for item in items:
        print(f'  - {item.get(\"sprk_toolcode\", \"?\")} : {item.get(\"sprk_name\", \"?\")}')
" 2>/dev/null || echo "$EXISTING"
echo ""

# =============================================================================
# STEP 3: Create 8 tool records
# =============================================================================
echo "--- Step 3: Creating 8 tool records ---"

# TL-001: DocumentSearch
# HandlerClass → ToolType.Custom (no matching substring in MapHandlerClassToToolType)
create_tool \
  "TL-001" \
  "DocumentSearch" \
  "Search the knowledge base and document index for relevant content matching a query" \
  "DocumentSearchHandler" \
  '{"type":"object","properties":{"query":{"type":"string","description":"Search query text"},"topK":{"type":"integer","description":"Maximum number of results to return","default":5},"indexName":{"type":"string","description":"Optional: target a specific AI Search index"}},"required":["query"]}'

# TL-002: AnalysisRetrieval
# HandlerClass → ToolType.Custom
create_tool \
  "TL-002" \
  "AnalysisRetrieval" \
  "Retrieve previously computed analysis results for a specific document or analysis session" \
  "AnalysisQueryHandler" \
  '{"type":"object","properties":{"documentId":{"type":"string","description":"Document identifier to retrieve analysis for"},"analysisType":{"type":"string","description":"Optional: filter by analysis type (e.g., summary, risk)"}},"required":["documentId"]}'

# TL-003: KnowledgeRetrieval
# HandlerClass → ToolType.Custom
create_tool \
  "TL-003" \
  "KnowledgeRetrieval" \
  "Retrieve specific knowledge source content by identifier or type from the knowledge store" \
  "KnowledgeRetrievalHandler" \
  '{"type":"object","properties":{"knowledgeId":{"type":"string","description":"Knowledge source identifier"},"contentType":{"type":"string","description":"Type of knowledge content to retrieve","enum":["inline","rag","document"]}},"required":["knowledgeId"]}'

# TL-004: TextRefinement
# HandlerClass → ToolType.Custom
create_tool \
  "TL-004" \
  "TextRefinement" \
  "Refine, reformat, or restructure a text section using AI-assisted editing" \
  "TextRefinementHandler" \
  '{"type":"object","properties":{"text":{"type":"string","description":"Input text to refine"},"instruction":{"type":"string","description":"Refinement instruction (e.g., summarize, rewrite formally, bullet points)"},"maxLength":{"type":"integer","description":"Maximum output length in characters"}},"required":["text","instruction"]}'

# TL-005: CitationExtractor
# HandlerClass contains "EntityExtractor" → ToolType.EntityExtractor
create_tool \
  "TL-005" \
  "CitationExtractor" \
  "Extract and normalize citation references from analysis results and document text" \
  "CitationExtractorHandler" \
  '{"type":"object","properties":{"text":{"type":"string","description":"Text to extract citations from"},"format":{"type":"string","description":"Citation format standard","enum":["bluebook","apa","mla","auto"],"default":"auto"},"includeContext":{"type":"boolean","description":"Include surrounding context for each citation","default":false}},"required":["text"]}'

# TL-006: SummaryGenerator
# HandlerClass contains "Summary" → ToolType.Summary
create_tool \
  "TL-006" \
  "SummaryGenerator" \
  "Generate structured summaries of document sections or complete documents" \
  "SummaryGeneratorHandler" \
  '{"type":"object","properties":{"text":{"type":"string","description":"Text to summarize"},"summaryType":{"type":"string","description":"Type of summary to generate","enum":["executive","detailed","bullet","one-sentence"],"default":"executive"},"maxWords":{"type":"integer","description":"Maximum word count for the summary","default":200},"focusAreas":{"type":"array","items":{"type":"string"},"description":"Optional: specific topics to emphasize in the summary"}},"required":["text"]}'

# TL-007: RedFlagDetector
# HandlerClass contains "RiskDetector" → ToolType.RiskDetector
create_tool \
  "TL-007" \
  "RedFlagDetector" \
  "Detect risk indicators, problematic clauses, and compliance issues in document sections" \
  "RedFlagDetectorHandler" \
  '{"type":"object","properties":{"text":{"type":"string","description":"Document text to analyze for risk indicators"},"riskCategories":{"type":"array","items":{"type":"string"},"description":"Risk categories to check (e.g., liability, compliance, financial)"},"severity":{"type":"string","description":"Minimum severity threshold","enum":["low","medium","high","critical"],"default":"medium"}},"required":["text"]}'

# TL-008: PartyExtractor
# HandlerClass contains "EntityExtractor" → ToolType.EntityExtractor
create_tool \
  "TL-008" \
  "PartyExtractor" \
  "Extract and normalize party information (people, organizations, roles) from document text" \
  "PartyExtractorHandler" \
  '{"type":"object","properties":{"text":{"type":"string","description":"Document text to extract parties from"},"partyTypes":{"type":"array","items":{"type":"string"},"description":"Party types to extract","enum":["person","organization","role","all"],"default":["all"]},"includeAliases":{"type":"boolean","description":"Include alternate names or aliases","default":true}},"required":["text"]}'

echo ""
echo "--- Step 4: Verifying created records ---"

VERIFY=$(curl -s \
  "${API_BASE}/${COLLECTION}?\$select=sprk_name,sprk_toolcode,sprk_handlerclass&\$filter=sprk_toolcode ne null&\$orderby=sprk_toolcode asc" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "OData-MaxVersion: 4.0" \
  -H "OData-Version: 4.0" \
  -H "Accept: application/json")

echo "All records with sprk_toolcode:"
echo "$VERIFY" | python3 -c "
import sys, json
data = json.load(sys.stdin)
items = data.get('value', [])
print(f'Total: {len(items)} records')
for item in items:
    print(f'  {item.get(\"sprk_toolcode\", \"?\")} | {item.get(\"sprk_name\", \"?\")} | {item.get(\"sprk_handlerclass\", \"?\")}')
" 2>/dev/null || echo "$VERIFY"

echo ""
echo "--- Alternate key lookup test: TL-001 ---"
TL001=$(curl -s \
  "${API_BASE}/${COLLECTION}(sprk_toolcode='TL-001')?\$select=sprk_name,sprk_toolcode,sprk_handlerclass" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "OData-MaxVersion: 4.0" \
  -H "OData-Version: 4.0" \
  -H "Accept: application/json" \
  -w "\nHTTP_STATUS:%{http_code}")
echo "$TL001"

echo ""
echo "=== Done ==="
