#!/usr/bin/env bash
# Batch A — READ-ONLY live tests from live-test-plan.md that need no guest account and no live change.
# Covers the az/gh parts of: T-P2-09, T-P2-10 (Spaarke tenant + prod SWA), T-P3-01, T-P3-18,
# T-P3-07 (steps 1, 3, 5, 6), T-P3-09, T-P3-12, T-P3-16, and the App Insights count in T-P1-12 step 4.
# Nothing is created, changed or deleted. Secret VALUES are never printed:
#   - app settings: value shown only for booleans and for *ClientId/*TenantId/*AppId GUIDs; otherwise a shape
#   - credentials: display name, key id and expiry only (no hint)
# Output: ../working/x06-live-batch-a.md
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
OUT="$HERE/../working/x06-live-batch-a.md"
TMP="$(mktemp -d)"
py() { PYTHONIOENCODING=utf-8 python "$@"; }
SHARED_SUB="cd95fcec-6b89-49ea-8339-c2b579b12587"

# settings_shape <rg> <app> <slot|-> <label> <name-regex>
settings_shape() {
  local rg=$1 app=$2 slot=$3 label=$4 rx=$5
  if [ "$slot" = "-" ]; then az webapp config appsettings list -g "$rg" -n "$app" -o json > "$TMP/s.json" 2>"$TMP/s.err"
  else az webapp config appsettings list -g "$rg" -n "$app" --slot "$slot" -o json > "$TMP/s.json" 2>"$TMP/s.err"; fi
  py - "$label" "$TMP/s.json" "$TMP/s.err" "$rx" <<'EOF'
import json,sys,re
label,path,err,rx=sys.argv[1:5]
print(f"\n#### {label}\n")
try: rows=json.load(open(path,encoding='utf-8'))
except Exception: print("- could not read: "+open(err,encoding='utf-8').read().strip()[:300]); sys.exit()
guid=re.compile(r'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$',re.I)
idname=re.compile(r'(ClientId|TenantId|AppId|TenantIds__\d+)$',re.I)
sel=[r for r in rows if re.search(rx,r['name'],re.I)]
print(f"- {len(rows)} settings in slot; {len(sel)} match the filter\n")
print("| setting | shape / value |\n|---|---|")
for r in sorted(sel,key=lambda x:x['name']):
    n,v=r['name'],str(r.get('value') or '')
    if v.startswith('@Microsoft.KeyVault'):
        m=re.search(r'SecretName=([^;)]+)|secrets/([^/;)]+)',v); s=(m.group(1) or m.group(2)) if m else '?'
        shape=f"Key Vault reference (secret name `{s}`)"
    elif v.lower() in ('true','false'): shape=f"bool `{v.lower()}`"
    elif guid.match(v): shape=f"GUID `{v}`" if idname.search(n) else "GUID (value not shown)"
    elif v.startswith('https://') or v.startswith('http://'): shape="URL (value not shown)" if re.search(r'(key|secret|sas|sig)',v,re.I) else f"URL `{v.split('?')[0]}`"
    elif v=='' : shape="empty"
    else: shape=f"other, length {len(v)} (value not shown)"
    print(f"| `{n}` | {shape} |")
EOF
}

{
echo "# Live batch A — read-only tests (az / gh)"
echo
echo "> Captured $(date -u +%Y-%m-%dT%H:%MZ) by batch-a-readonly-tests.sh. Read-only; no secret values printed."
echo
echo "## Context"
az account show --query "{user:user.name,tenant:tenantId,subscription:name}" -o json
} > "$OUT" 2>&1

# ---------------- T-P2-09: L2 control plane ----------------
{
echo -e "\n## T-P2-09 — L2 control plane\n"
echo "### 1. App exposing api://spaarke.com/provisioning-controlplane-dev"
az ad app list --filter "identifierUris/any(u:u eq 'api://spaarke.com/provisioning-controlplane-dev')" --query "[].{appId:appId,name:displayName,roles:appRoles[].value}" -o json
} >> "$OUT" 2>&1
L2APP=$(az ad app list --filter "identifierUris/any(u:u eq 'api://spaarke.com/provisioning-controlplane-dev')" --query "[0].appId" -o tsv 2>/dev/null)
if [ -n "$L2APP" ]; then
  L2SP=$(az ad sp show --id "$L2APP" --query id -o tsv 2>/dev/null)
  { echo "### 2. Who holds its app roles (appRoleAssignedTo)"
    az rest --method GET --url "https://graph.microsoft.com/v1.0/servicePrincipals/$L2SP/appRoleAssignedTo" --query "value[].{principal:principalDisplayName,type:principalType,appRoleId:appRoleId}" -o table
    az ad sp show --id "$L2APP" --query "{appRoleAssignmentRequired:appRoleAssignmentRequired}" -o json; } >> "$OUT" 2>&1
else echo "- no app found for that identifier URI" >> "$OUT"; fi
echo "### 3. L2 hosts — settings (names + shape) and /healthz" >> "$OUT"
az webapp list --query "[?starts_with(name,'sprk-controlplane-dev')].{n:name,rg:resourceGroup}" -o tsv > "$TMP/l2hosts.tsv" 2>/dev/null
while IFS=$'\t' read -r n rg; do
  [ -z "$n" ] && continue
  for slot in - staging; do settings_shape "$rg" "$n" "$slot" "$n slot ${slot/-/production}" '^(ReservedTenants__|AzureAd__|ManagedIdentity__|AZURE_CLIENT_ID|AZURE_TENANT_ID)' >> "$OUT"; done
  echo "- /healthz $n: $(curl -s -o /dev/null -w '%{http_code}' --max-time 20 https://$n.azurewebsites.net/healthz)" >> "$OUT"
done < "$TMP/l2hosts.tsv"
{ echo "### 4. Per-customer stamp apps and App Services"
  az ad app list --filter "startswith(displayName,'spaarke-bff-api-')" --query "[].{appId:appId,name:displayName,acct:optionalClaims.accessToken[?name=='acct'] | length(@)}" -o table
  echo; az webapp list --query "[?starts_with(name,'sprk-') && contains(name,'-api')].{n:name,rg:resourceGroup}" -o table; } >> "$OUT" 2>&1

# ---------------- T-P2-10: consent grants, prod SWA ----------------
echo -e "\n## T-P2-10 — delegated consent grants and production SPA site (Spaarke tenant)\n" >> "$OUT"
for pair in "c1258e2d-1688-49d2-ac99-a7485ebd9995:dev add-in" "170c98e1-d486-4355-bcbe-170454e0207c:PCF client" "f306885a-8251-492c-8d3e-34d7b476ffd0:spaarke-external-access-SPA" "1958aec2-0218-495e-8e3c-37133e9b8357:prod add-in"; do
  id=${pair%%:*}; lbl=${pair#*:}
  sp=$(az ad sp show --id "$id" --query id -o tsv 2>/dev/null)
  echo "### grants by $lbl ($id)" >> "$OUT"
  if [ -n "$sp" ]; then az rest --method GET --url "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?\$filter=clientId eq '$sp'" -o json > "$TMP/g.json" 2>&1
    py - "$TMP/g.json" >> "$OUT" <<'EOF'
import json,sys,subprocess
try: g=json.load(open(sys.argv[1],encoding='utf-8')).get('value',[])
except Exception: print("- could not read grants"); sys.exit()
if not g: print("- no delegated grants")
for x in g:
    rid=x.get('resourceId'); name=subprocess.run(['az','ad','sp','show','--id',rid,'--query','displayName','-o','tsv'],capture_output=True,text=True,shell=True).stdout.strip()
    print(f"- resource {name or rid}: scope `{x.get('scope','').strip()}`, consentType {x.get('consentType')}")
EOF
  else echo "- no service principal in tenant" >> "$OUT"; fi
done
{ echo "### dev add-in pre-authorization on 1e40baad: delegated permission ids, and the scope id map"
  az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "api.preAuthorizedApplications[?appId=='c1258e2d-1688-49d2-ac99-a7485ebd9995'].delegatedPermissionIds" -o json
  az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "api.oauth2PermissionScopes[].{id:id,v:value}" -o table
  echo "### production SPA Static Web App"
  az staticwebapp list --subscription "$SHARED_SUB" --query "[?contains(name,'external-spa')].{name:name,rg:resourceGroup,defaultHostname:defaultHostname,customDomains:customDomains}" -o json; } >> "$OUT" 2>&1

# ---------------- T-P3-01 / T-P3-18: remaining dev BFF settings, both slots ----------------
echo -e "\n## T-P3-01 / T-P3-18 — dev BFF settings (names + shape), production and staging\n" >> "$OUT"
RX='^(Reporting__|PowerBi__|Rag__|ServiceBus__|Redis__|AiSearch__|Notifications__|Communication__|Onboarding__|AZURE_CLIENT_ID|AZURE_TENANT_ID|IdentityLink__|ExternalAccess__|TenantRouting__|Graph__|AgentToken__|AzureAd__|Ciam__|WorkforceIdentity__|Cors__|PublicConfig__|ManagedIdentity__|Customer__|Dataverse__)'
az webapp deployment slot list -g rg-spaarke-dev -n spaarke-bff-dev --query "[].name" -o tsv > "$TMP/slots.txt" 2>&1
echo "- deployment slots on spaarke-bff-dev: $(tr '\n' ' ' < "$TMP/slots.txt")" >> "$OUT"
settings_shape rg-spaarke-dev spaarke-bff-dev - "spaarke-bff-dev production" "$RX" >> "$OUT"
grep -qx staging "$TMP/slots.txt" && settings_shape rg-spaarke-dev spaarke-bff-dev staging "spaarke-bff-dev staging" "$RX" >> "$OUT"
DEMO_RG=$(az webapp list --query "[?name=='spaarke-bff-demo'].resourceGroup | [0]" -o tsv 2>/dev/null)
[ -n "$DEMO_RG" ] && settings_shape "$DEMO_RG" spaarke-bff-demo - "spaarke-bff-demo production" "$RX" >> "$OUT"
az identity show -g rg-spaarke-demo -n mi-bff-api-demo --query "{clientId:clientId,principalId:principalId}" -o json >> "$OUT" 2>&1

# ---------------- T-P3-07: grants, RBAC, FIC audiences, role-use diff ----------------
echo -e "\n## T-P3-07 — permissions, RBAC, federated-credential audiences\n" >> "$OUT"
az ad sp show --id 00000003-0000-0000-c000-000000000000 --query "{scopes:oauth2PermissionScopes[].{id:id,v:value},roles:appRoles[].{id:id,v:value}}" -o json > "$TMP/graph.json" 2>/dev/null
for app in 1e40baad-e065-4aea-a8d4-4b7ab273458c 170c98e1-d486-4355-bcbe-170454e0207c; do
  az ad app show --id "$app" --query requiredResourceAccess -o json > "$TMP/rra.json" 2>/dev/null
  py - "$app" "$TMP/rra.json" "$TMP/graph.json" >> "$OUT" <<'EOF'
import json,sys
app,rra,gp=sys.argv[1:4]
g=json.load(open(gp,encoding='utf-8')); names={**{s['id']:('delegated',s['v']) for s in g['scopes']},**{r['id']:('application',r['v']) for r in g['roles']}}
print(f"\n### requiredResourceAccess resolved — {app}\n")
for res in json.load(open(rra,encoding='utf-8')):
    rid=res['resourceAppId']
    if rid=='00000003-0000-0000-c000-000000000000':
        items=[f"{names.get(a['id'],('?',a['id']))[1]} ({'app' if a['type']=='Role' else 'delegated'})" for a in res['resourceAccess']]
        print(f"- Microsoft Graph: {', '.join(sorted(items))}")
    else: print(f"- {rid}: {len(res['resourceAccess'])} permission(s) (ids: {[a['id'] for a in res['resourceAccess']]})")
EOF
done
{ echo -e "\n### Azure RBAC held by mi-bff-api-dev (principal 9fd47efb…)"
  az role assignment list --assignee 9fd47efb-7962-492b-ac44-e5ccd0268ebb --all --query "[].{role:roleDefinitionName,scope:scope}" -o table
  echo -e "\n### Federated credential audiences"
  for a in 1e40baad-e065-4aea-a8d4-4b7ab273458c 46670ee2-ac0c-44b0-9ac2-d40ae4dcbdd7 bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e 8c85a481-f3a0-46de-b84e-3ede8a4d60c3; do
    echo "- $a:"; az ad app federated-credential list --id "$a" --query "[].{name:name,audiences:audiences}" -o json; done; } >> "$OUT" 2>&1
echo -e "\n### Graph application roles held vs referenced in code (grep of GraphAppRoles.cs)" >> "$OUT"
( cd "$REPO" && git grep -ohE '"[A-Za-z]+\.[A-Za-z.]+All"|"[A-Za-z]+\.(Selected|Send|Read|ReadWrite)"' -- src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs | sort -u | tr '\n' ' ' ) >> "$OUT" 2>&1
echo >> "$OUT"

# ---------------- T-P3-09: credential inventory (metadata only) ----------------
echo -e "\n## T-P3-09 — credential metadata (no values, no hints)\n" >> "$OUT"
for a in "1e40baad-e065-4aea-a8d4-4b7ab273458c:dev BFF" "170c98e1-d486-4355-bcbe-170454e0207c:PCF client / SPE owning app" "8c85a481-f3a0-46de-b84e-3ede8a4d60c3:GitHub OIDC" "da03fe1a-4b1d-4297-a4ce-4b83cae498a9:demo BFF"; do
  id=${a%%:*}; lbl=${a#*:}
  { echo "### $lbl ($id)"; echo "- secrets:"; az ad app credential list --id "$id" --query "[].{name:displayName,keyId:keyId,start:startDateTime,end:endDateTime}" -o table
    echo "- certificates:"; az ad app credential list --id "$id" --cert --query "[].{name:displayName,keyId:keyId,end:endDateTime}" -o table; } >> "$OUT" 2>&1
done
{ echo "### ciam-graph-provisioner-cert exportable?"; az keyvault certificate show --vault-name spaarke-spekvcert -n ciam-graph-provisioner-cert --query "{exportable:policy.keyProperties.exportable,expires:attributes.expires}" -o json; } >> "$OUT" 2>&1

# ---------------- T-P3-12: Insights Function shell ----------------
{ echo -e "\n## T-P3-12 — Insights Function shell\n"
  az functionapp show -g spe-infrastructure-westus2 -n insights-spaarkedev-func --query "{state:state,identity:identity.type,uamis:identity.userAssignedIdentities}" -o json
  az storage account show -g spe-infrastructure-westus2 -n insightsspaarkedevstg --query "{allowSharedKeyAccess:allowSharedKeyAccess,publicNetworkAccess:publicNetworkAccess}" -o json
  echo "### RBAC held by insights-spaarkedev-uami (principal 9e4261c7…)"
  az role assignment list --assignee 9e4261c7-9d3e-4c30-908f-031c5f9ff136 --all --query "[].{role:roleDefinitionName,scope:scope}" -o table
  echo "### RBAC held by insights-search-deploy-uami (principal 42ffe13e…)"
  az role assignment list --assignee 42ffe13e-609c-4539-ab56-a238373b1874 --all --query "[].{role:roleDefinitionName,scope:scope}" -o table; } >> "$OUT" 2>&1
settings_shape spe-infrastructure-westus2 insights-spaarkedev-func - "insights-spaarkedev-func settings" '.' >> "$OUT" 2>/dev/null || true

# ---------------- T-P3-16: GitHub secret/variable names ----------------
{ echo -e "\n## T-P3-16 — GitHub Actions secret and variable NAMES (values are write-only)\n"
  ( cd "$REPO" && gh secret list 2>&1; echo; gh variable list 2>&1; echo; gh api repos/spaarke-dev/spaarke/environments --jq '.environments[].name' 2>&1 ); } >> "$OUT" 2>&1

# ---------------- T-P1-12 step 4: App Insights counts (7 days) ----------------
echo -e "\n## T-P1-12 step 4 — App Insights counts, last 7 days\n" >> "$OUT"
AI=$(az monitor app-insights component show --only-show-errors --query "[?contains(name,'bff') || contains(name,'spaarke-dev') || contains(name,'spaarkedev')].{n:name,rg:resourceGroup,appId:appId}" -o json 2>&1)
echo "candidates: $AI" >> "$OUT"
APPID=$(echo "$AI" | py -c "import json,sys; d=json.load(sys.stdin); print(d[0]['appId'] if d else '')" 2>/dev/null)
if [ -n "$APPID" ]; then
  for pat in "RPA-FALLBACK" "[WF-STANDING]" "[EFFECTIVE-ACCESS]" "Deny veto"; do
    c=$(az monitor app-insights query --only-show-errors --app "$APPID" --analytics-query "traces | where timestamp > ago(7d) | where message has '$pat' | count" --query "tables[0].rows[0][0]" -o tsv 2>&1)
    echo "- \`$pat\`: $c" >> "$OUT"
  done
else echo "- no App Insights component identified (counts not taken)" >> "$OUT"; fi

rm -rf "$TMP"
echo "done -> $OUT"
