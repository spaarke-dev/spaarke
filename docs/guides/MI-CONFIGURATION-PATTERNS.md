# BFF MI Configuration Patterns (Per-Env Promotion Runbook)

> **Purpose**: Single-page operator runbook for the 6 critical patterns discovered during Phase 5 demo cutover (sdap-bff-api-remediation-fix project, 2026-05-25). Use as the canonical checklist when promoting BFF to a new environment.
>
> **Why this exists**: These 6 patterns are scattered across 4+ docs (`auth-deployment-setup.md` §3.5, `INFRASTRUCTURE-PACKAGING-STRATEGY.md`, `azure-deployment.md`, `FAILURE-MODES.md`). Without consolidation, a new operator can miss one and produce a stuck deployment. This runbook is the one-stop checklist; each item links back to the authoritative source for full context.

## When to use this runbook

Before deploying BFF to any new environment (test, demo, prod, partner, customer). Reference during incident response when startup fails with `OptionsValidationException` or downstream features (email send, document upload, etc.) hit 403/404/500.

## The 6 patterns

### Pattern 1 — Five managed-identity ClientId keys, not one

When `Graph__ManagedIdentity__Enabled=true`, the BFF binds the UAMI clientId through 5 separate keys. **Each is read by a different code path; missing any one produces `OptionsValidationException` at startup.**

| Key | Used by | Failure mode if missing |
|---|---|---|
| `Graph__ManagedIdentity__Enabled=true` | Switch into MI mode | Stays in ClientSecret mode |
| `Graph__ManagedIdentity__ClientId={uami-client-id}` | `GraphOptions` validator | `OptionsValidationException: Graph:ManagedIdentity:ClientId is required when ManagedIdentity is enabled` |
| `ManagedIdentity__ClientId={uami-client-id}` | Separate `ManagedIdentityOptions` class | Different OptionsValidationException for that class |
| `AZURE_CLIENT_ID={uami-client-id}` | `DefaultAzureCredential` env-var convention (Azure SDK standard) | `DefaultAzureCredential` resolves to wrong identity / fails |
| `UAMI_CLIENT_ID={uami-client-id}` | Custom convention used by scripts + diagnostics | Diagnostic output incorrect |

**All 5 must be set to the SAME UAMI clientId.** Authoritative: `auth-deployment-setup.md` §3.5 MI identity disambiguation.

### Pattern 2 — Cosmos DB is mandatory infrastructure

`AiPersistenceModule.cs:56` throws `InvalidOperationException` if `CosmosPersistence__Endpoint` is null. **The BFF will not start without Cosmos.**

Required per env:
1. Cosmos account (Serverless SKU recommended for non-prod)
2. Database `spaarke-ai`
3. 5 containers (`sessions`, `prompts`, `audit`, `memory`, `feedback`) all `/tenantId` partition key
4. **First time per subscription**: `az provider register --namespace Microsoft.DocumentDB --wait`
5. Grant BFF UAMI Cosmos DB Built-in Data Contributor RBAC (role id `00000000-0000-0000-0000-000000000002`)
6. App Settings: `CosmosPersistence__Endpoint=https://{cosmos-account}.documents.azure.com:443/` + `CosmosPersistence__DatabaseName=spaarke-ai`

Authoritative: `auth-deployment-setup.md` §3.5 + `docs/guides/AI-DEPLOYMENT-GUIDE.md` Cosmos section.

### Pattern 3 — AgentService placeholders for envs not using Agent Framework

The BFF validates `AgentServiceOptions` at startup even when Agent Framework isn't actively used. Set placeholder values to pass validation:

```
AgentService__Endpoint=https://placeholder.services.ai.azure.com
AgentService__AgentId=placeholder-agent-id
AgentService__ThreadCacheExpiryMinutes=60
AgentService__MaxConcurrency=2
AgentServiceOptions__Enabled=true
AgentServiceOptions__Endpoint=https://placeholder.services.ai.azure.com
AgentServiceOptions__AgentId=placeholder-agent-id
Analysis__AgentService__Enabled=false
Analysis__AgentService__Endpoint=https://placeholder.services.ai.azure.com
Analysis__AgentService__ThreadCacheExpiryMinutes=60
Analysis__AgentService__AgentId=placeholder-agent-id
Analysis__AgentService__MaxConcurrency=2
```

Failure mode if missing: `OptionsValidationException: DataAnnotation validation failed for 'AgentServiceOptions' members: 'Endpoint' with the error: 'The Endpoint field is required.'`

Authoritative: `auth-deployment-setup.md` §3.5 AgentService configuration.

### Pattern 4 — Optional features need explicit `=false`

Several optional modules validate their options at startup. Set `=false` to skip:

```
BingGrounding__Enabled=false
Analysis__BingGrounding__Enabled=false
CodeInterpreter__Enabled=false
Analysis__CodeInterpreter__Enabled=false
RecordSync__Enabled=false
EmailProcessing__EnablePolling=false   # if email not yet wired
EmailProcessing__EnableWebhook=false   # if email not yet wired
```

### Pattern 5 — Dataverse Application User registration is MANDATORY (silent 403)

After the UAMI is created + Graph + KV + Cosmos RBAC granted, **register the UAMI as a Dataverse Application User on the target Dataverse env**. Without this step, BFF Dataverse calls return 403 (which BFF wraps as 500 to clients — symptom is non-obvious "Failed to resolve playbook" or similar 500s in client workflows).

Full Web API walkthrough: `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md` Application User section. Failure mode catalog: `.claude/FAILURE-MODES.md` G-5.

### Pattern 6 — Exchange ApplicationAccessPolicy required for email (×2 policies)

For ANY env that exercises Mail.Send / Mail.Read app-only Graph (Communication or EmailProcessing modules):

1. Create 2 EXO ApplicationAccessPolicy policies — ONE for the BFF app reg, ONE for the BFF MI. Each scoped to a mail-enabled security group containing the in-scope mailboxes.
2. Use `Connect-ExchangeOnline -ShowProgress $true` (NO `-UserPrincipalName` — see `.claude/FAILURE-MODES.md` G-6 for why).
3. Verify with `Test-ApplicationAccessPolicy -Identity {mailbox} -AppId {appid}` — must return `Granted` for each (mailbox, principal) pair.
4. Microsoft documents ~15-30 min propagation before live Graph calls respect the policy.

Authoritative: `auth-deployment-setup.md` §7.

## Quick reference: complete per-env deployment checklist

When provisioning BFF for a new env, work this list top-to-bottom:

- [ ] **Infrastructure**: App Service (Linux .NET 8) + Key Vault + Cosmos DB (Serverless, with `spaarke-ai` DB + 5 containers) + Storage + Service Bus + Redis + AI Search
- [ ] **Identity**: UAMI (`mi-bff-api-{env}`) created, attached to App Service
- [ ] **Identity RBAC**: UAMI granted Key Vault Secrets User + Cosmos Data Contributor + 6 Graph app roles (Mail.Send, Mail.Read, FileStorageContainer.Selected, FileStorageContainerTypeReg.Selected, User.ReadWrite.All, Group.ReadWrite.All — adjust per env's feature scope)
- [ ] **Dataverse Application User**: UAMI registered as systemuser on target Dataverse org with appropriate security role (Pattern 5 above)
- [ ] **App Settings**: Apply all 5 patterns above (~30+ keys). Reference `auth-deployment-setup.md` §3.1 + §3.5.
- [ ] **Email setup** (if applicable): 2 HMAC keys in KV + 17 email App Settings (see `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md`)
- [ ] **Exchange policies** (if email exercised): 2 EXO ApplicationAccessPolicy entries (Pattern 6 above)
- [ ] **Dataverse env var**: `sprk_BffApiBaseUrl` set with format matching dev (currently `/api`-suffixed)
- [ ] **Deploy + verify**: `Deploy-BffApi.ps1`; `/healthz` returns 200; smoke test 323 routes via `scripts/Capture-BffBaseline.ps1`
- [ ] **Operator runbook**: working through Phase 5 took ~4 hours wall-clock for demo; budget 3-5 hours for a similar fresh env

## Cross-references

- **Phase 5 evidence**: `projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md` Phase 5 section (authoritative)
- **Setting-level detail**: `docs/guides/auth-deployment-setup.md` §3.5 (canonical App Settings reference)
- **Cosmos provisioning**: `docs/guides/AI-DEPLOYMENT-GUIDE.md` Cosmos section
- **Email setup**: `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md` (full 17-setting inventory)
- **Dataverse Application User**: `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md`
- **Failure modes**: `.claude/FAILURE-MODES.md` (G-5, G-6, G-7 + AP-4)
- **Architecture rationale**: `docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md` §5
