#!/usr/bin/env bash
# READ-ONLY live checks for the auth system of record (spaarke-auth-system-of-record-r1).
# Every command below only READS Entra / Azure state. Nothing is created, changed or deleted.
# Secret VALUES are never printed: app-setting values whose names look secret are masked, Key Vault
# commands list names only.
# Output: projects/spaarke-auth-system-of-record-r1/working/x04-live-entra-readout.md
set -u
OUT="$(dirname "$0")/../working/x04-live-entra-readout.md"
TMP="$(mktemp -d)"
py() { PYTHONIOENCODING=utf-8 python "$@"; }

{
echo "# Live Entra / Azure read-out (read-only)"
echo
echo "> Captured $(date -u +%Y-%m-%dT%H:%MZ) by entra-readonly-checks.sh. Read-only; secret values masked."
echo
echo "## 0. Signed-in context"
az account show --query "{user:user.name,tenant:tenantId,subscription:name}" -o json
} > "$OUT" 2>&1

# ---- 1. App registrations in Spaarke's workforce tenant -------------------------------------------
APPS=(
 "A1 1e40baad-e065-4aea-a8d4-4b7ab273458c SDAP-BFF-SPE-API (dev BFF; also Teams/Copilot client)"
 "A2 170c98e1-d486-4355-bcbe-170454e0207c SDAP-PCF-CLIENT (dev code pages/PCF; SPE owning app)"
 "A3 5175798e-f23e-41c3-b09b-7a90b9218189 former msal_client (said retired)"
 "A4 b36e9b91-ee7d-46e6-9f6a-376871cc9d54 SPE File Viewer PCF (legacy, still in ribbons)"
 "A5 fd1325aa-a709-4f15-b1f5-600f30d28875 Spaarke DMS-SPE Dev 1 (said deprecated)"
 "A6 c1258e2d-1688-49d2-ac99-a7485ebd9995 Office add-in (dev)"
 "A7 1958aec2-0218-495e-8e3c-37133e9b8357 Office add-in (production)"
 "A9 f257a0a9-1061-4f9b-8918-3ad056fe90db Copilot agent / bot"
 "A14 da03fe1a-4b1d-4297-a4ce-4b83cae498a9 demo BFF app"
 "A15 965a4a01-01e1-442b-97a6-6a98308018b3 L2 control plane AzureAd ClientId (dev)"
 "A16 46670ee2-ac0c-44b0-9ac2-d40ae4dcbdd7 Spaarke Exchange Admin"
 "A17 bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e Spaarke SPE Model 1 Owner"
 "A20 8c85a481-f3a0-46de-b84e-3ede8a4d60c3 github-actions-spe-infrastructure (OIDC)"
 "A21 ed44fb14-7d2a-476c-8e1b-7e43e3cfecb0 unknown (orphan sprk_DocumentDelete.js client)"
 "A22 8c60b44e-5fc7-471c-940e-0f77f395c18d unknown (orphan sprk_DocumentDelete.js resource)"
)
echo -e "\n## 1. App registrations (Spaarke workforce tenant)\n" >> "$OUT"
for row in "${APPS[@]}"; do
  key=${row%% *}; rest=${row#* }; id=${rest%% *}; label=${rest#* }
  az ad app show --id "$id" -o json > "$TMP/app.json" 2>"$TMP/app.err"
  az ad sp show --id "$id" --query "{spObjectId:id,appRoleAssignmentRequired:appRoleAssignmentRequired,type:servicePrincipalType,ownerTenant:appOwnerOrganizationId}" -o json > "$TMP/sp.json" 2>/dev/null || echo '{"servicePrincipal":"none in this tenant"}' > "$TMP/sp.json"
  az ad app federated-credential list --id "$id" --query "[].{name:name,issuer:issuer,subject:subject}" -o json > "$TMP/fic.json" 2>/dev/null || echo '[]' > "$TMP/fic.json"
  py - "$key" "$id" "$label" "$TMP" >> "$OUT" <<'EOF'
import json,sys,os
key,id_,label,tmp=sys.argv[1:5]
print(f"### {key} `{id_}` — {label}\n")
try:
    a=json.load(open(os.path.join(tmp,'app.json'),encoding='utf-8'))
except Exception:
    print("- **App object: NOT FOUND in this tenant** (" + open(os.path.join(tmp,'app.err'),encoding='utf-8').read().strip()[:160] + ")\n"); a=None
if a:
    api=a.get('api') or {}
    print(f"- displayName: {a.get('displayName')}")
    print(f"- signInAudience: **{a.get('signInAudience')}**")
    print(f"- identifierUris: {a.get('identifierUris')}")
    print(f"- spa redirects: {(a.get('spa') or {}).get('redirectUris')}")
    print(f"- web redirects: {(a.get('web') or {}).get('redirectUris')}")
    print(f"- publicClient redirects: {(a.get('publicClient') or {}).get('redirectUris')}")
    print(f"- exposed scopes: {[ (s.get('value'), s.get('isEnabled')) for s in api.get('oauth2PermissionScopes') or []]}")
    print(f"- preAuthorizedApplications: {[ p.get('appId') for p in api.get('preAuthorizedApplications') or []]}")
    print(f"- knownClientApplications: {api.get('knownClientApplications')}")
    print(f"- appRoles: {[ r.get('value') for r in a.get('appRoles') or []]}")
    oc=(a.get('optionalClaims') or {})
    print(f"- optionalClaims.accessToken: {[c.get('name') for c in oc.get('accessToken') or []]}; idToken: {[c.get('name') for c in oc.get('idToken') or []]}")
    print(f"- groupMembershipClaims: {a.get('groupMembershipClaims')}")
    print(f"- requiredResourceAccess (resource -> count): {[ (r.get('resourceAppId'), len(r.get('resourceAccess') or [])) for r in a.get('requiredResourceAccess') or []]}")
    print(f"- passwordCredentials: {len(a.get('passwordCredentials') or [])} secret(s) (names/values not shown); keyCredentials: {len(a.get('keyCredentials') or [])} cert(s)")
print(f"- service principal: {json.load(open(os.path.join(tmp,'sp.json'),encoding='utf-8'))}")
print(f"- federated credentials: {json.load(open(os.path.join(tmp,'fic.json'),encoding='utf-8'))}\n")
EOF
done

echo -e "\n### Per-customer BFF apps and prod BFF app (by display name)\n" >> "$OUT"
az ad app list --filter "startswith(displayName,'spaarke-bff-api')" --query "[].{name:displayName,appId:appId,signInAudience:signInAudience}" -o table >> "$OUT" 2>&1

# ---- 2. BFF App Service settings (dev + staging slot), secrets masked ------------------------------
echo -e "\n## 2. Dev BFF App Service auth settings (secret-looking values masked)\n" >> "$OUT"
RG=$(az webapp list --query "[?name=='spaarke-bff-dev'].resourceGroup | [0]" -o tsv)
echo "- resource group: $RG" >> "$OUT"
for slot in "" "staging"; do
  if [ -z "$slot" ]; then az webapp config appsettings list -g "$RG" -n spaarke-bff-dev -o json > "$TMP/s.json" 2>&1; label=production;
  else az webapp config appsettings list -g "$RG" -n spaarke-bff-dev --slot "$slot" -o json > "$TMP/s.json" 2>&1; label=$slot; fi
  py - "$label" "$TMP/s.json" >> "$OUT" <<'EOF'
import json,sys,re
label,path=sys.argv[1:3]
print(f"\n### slot: {label}\n")
try: rows=json.load(open(path,encoding='utf-8'))
except Exception: print("- could not read settings: "+open(path,encoding='utf-8').read()[:200]); sys.exit()
keep=re.compile(r'^(AzureAd__|Ciam__|Cors__|WorkforceIdentity__|Graph__|AgentToken__|PublicConfig__|TenantRouting__|SpeAdmin__|ManagedIdentity__|AZURE_CLIENT_ID|AZURE_TENANT_ID|Customer__|Dataverse__|Communication__Webhook|Compose__Webhook|EmailProcessing__)',re.I)
# Default-deny: print a value ONLY when the setting name ends in a known identifier-type suffix.
# Everything else is masked. (The first version used a deny-list and leaked Compose__Webhook__ClientState.)
show=re.compile(r'(ClientId|TenantId|Audience|Audiences__\d+|Instance|Domain|AppId|Url|Uri|BffUrl|EnvironmentUrl|ServiceUrl|AllowedOrigins__\d+|CustomerTenantIds__\d+|Order__\d+|Enabled|Id|Scopes__\d+|CertificateName)$',re.I)
print("| setting | value |\n|---|---|")
for r in sorted(rows,key=lambda x:x['name']):
    n,v=r['name'],str(r.get('value') or '')
    if not keep.search(n): continue
    if v.startswith('@Microsoft.KeyVault'): v='(Key Vault reference)'
    elif not show.search(n) or re.search(r'(secret|password|key|token|sas|signing|state)',n,re.I) and not re.search(r'(ClientId|TenantId|AppId)$',n,re.I): v='(masked)'
    print(f"| `{n}` | `{v}` |")
EOF
done

# ---- 3. Managed identities and their Graph app-role grants ----------------------------------------
echo -e "\n## 3. Managed identities and Graph app-role grants\n" >> "$OUT"
az identity list --query "[?contains(name,'bff') || contains(name,'controlplane') || contains(name,'insights') || starts_with(name,'mi-') || contains(name,'uami')].{name:name,rg:resourceGroup,clientId:clientId,principalId:principalId}" -o table >> "$OUT" 2>&1
for sp in 9fd47efb-7962-492b-ac44-e5ccd0268ebb 38f7693f-e6e2-4a3e-9acf-7f9e29dd4044; do
  echo -e "\n### app-role assignments held by service principal $sp\n" >> "$OUT"
  az rest --method GET --url "https://graph.microsoft.com/v1.0/servicePrincipals/$sp/appRoleAssignments" --query "value[].{resource:resourceDisplayName,appRoleId:appRoleId}" -o table >> "$OUT" 2>&1
done

# ---- 4. Key Vault (names only) --------------------------------------------------------------------
echo -e "\n## 4. Key Vault spaarke-spekvcert (names only)\n" >> "$OUT"
echo "### secrets" >> "$OUT";          az keyvault secret list --vault-name spaarke-spekvcert --query "[].name" -o tsv >> "$OUT" 2>&1
echo "### deleted secrets" >> "$OUT";  az keyvault secret list-deleted --vault-name spaarke-spekvcert --query "[].name" -o tsv >> "$OUT" 2>&1
echo "### certificates" >> "$OUT";     az keyvault certificate list --vault-name spaarke-spekvcert --query "[].name" -o tsv >> "$OUT" 2>&1

# ---- 5. Unknown tenant referenced by an orphan web resource (public metadata, no sign-in) ----------
echo -e "\n## 5. Tenant e2e89f3a-98bf-4db2-b149-5b3fe72e8fe7 (public OpenID metadata)\n" >> "$OUT"
curl -s "https://login.microsoftonline.com/e2e89f3a-98bf-4db2-b149-5b3fe72e8fe7/v2.0/.well-known/openid-configuration" | py -c "import json,sys; d=json.load(sys.stdin); print({k:d.get(k) for k in ('issuer','tenant_region_scope','cloud_instance_name','error','error_description')})" >> "$OUT" 2>&1

rm -rf "$TMP"
echo "done -> $OUT"
