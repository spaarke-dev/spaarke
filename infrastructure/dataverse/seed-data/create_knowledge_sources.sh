#!/bin/bash
TOKEN=$(cat /tmp/dv_token.txt)
DEPLOYMENT_ID="f28de21f-71d9-f011-8406-7c1e520aa4df"
BASE_URL="https://spaarkedev1.crm.dynamics.com/api/data/v9.2"

# sprk_type choices: 100000000=Document, 100000001=Rule, 100000002=Template, 100000003=RAG_Index

echo "=== Creating Knowledge Sources ==="

# 1. Standard Contract Templates (Template type)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/sprk_analysisknowledges" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "OData-MaxVersion: 4.0" \
  -H "OData-Version: 4.0" \
  -d "{\"sprk_name\":\"Standard Contract Templates\",\"sprk_description\":\"Library of standard contract templates including NDAs, service agreements, and employment contracts.\",\"sprk_deploymentid@odata.bind\":\"/sprk_knowledgedeployments($DEPLOYMENT_ID)\",\"sprk_type\":100000002}")
if [ "$CODE" = "204" ] || [ "$CODE" = "201" ]; then echo "✓ Standard Contract Templates"; else echo "✗ Standard Contract Templates ($CODE)"; fi

# 2. Company Policies (Rule type)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/sprk_analysisknowledges" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "OData-MaxVersion: 4.0" \
  -H "OData-Version: 4.0" \
  -d "{\"sprk_name\":\"Company Policies\",\"sprk_description\":\"Internal company policies, procedures, and guidelines.\",\"sprk_deploymentid@odata.bind\":\"/sprk_knowledgedeployments($DEPLOYMENT_ID)\",\"sprk_type\":100000001}")
if [ "$CODE" = "204" ] || [ "$CODE" = "201" ]; then echo "✓ Company Policies"; else echo "✗ Company Policies ($CODE)"; fi

# 3. Business Writing Guidelines (Rule type)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/sprk_analysisknowledges" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "OData-MaxVersion: 4.0" \
  -H "OData-Version: 4.0" \
  -d "{\"sprk_name\":\"Business Writing Guidelines\",\"sprk_description\":\"Style guides, formatting standards, and writing best practices for professional documents.\",\"sprk_deploymentid@odata.bind\":\"/sprk_knowledgedeployments($DEPLOYMENT_ID)\",\"sprk_type\":100000001}")
if [ "$CODE" = "204" ] || [ "$CODE" = "201" ]; then echo "✓ Business Writing Guidelines"; else echo "✗ Business Writing Guidelines ($CODE)"; fi

# 4. Legal Reference Materials (Document type)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/sprk_analysisknowledges" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "OData-MaxVersion: 4.0" \
  -H "OData-Version: 4.0" \
  -d "{\"sprk_name\":\"Legal Reference Materials\",\"sprk_description\":\"Legal terminology glossaries, clause explanations, and regulatory reference materials.\",\"sprk_deploymentid@odata.bind\":\"/sprk_knowledgedeployments($DEPLOYMENT_ID)\",\"sprk_type\":100000000}")
if [ "$CODE" = "204" ] || [ "$CODE" = "201" ]; then echo "✓ Legal Reference Materials"; else echo "✗ Legal Reference Materials ($CODE)"; fi

# 5. Example Analyses (RAG_Index type)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/sprk_analysisknowledges" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "OData-MaxVersion: 4.0" \
  -H "OData-Version: 4.0" \
  -d "{\"sprk_name\":\"Example Analyses\",\"sprk_description\":\"Examples of completed document analyses that can be used as reference for style and format.\",\"sprk_deploymentid@odata.bind\":\"/sprk_knowledgedeployments($DEPLOYMENT_ID)\",\"sprk_type\":100000003}")
if [ "$CODE" = "204" ] || [ "$CODE" = "201" ]; then echo "✓ Example Analyses"; else echo "✗ Example Analyses ($CODE)"; fi

echo ""
echo "Knowledge Sources created!"
