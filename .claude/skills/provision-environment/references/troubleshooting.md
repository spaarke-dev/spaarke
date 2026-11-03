# Troubleshooting, tool matrix, auth flow and failure modes

Moved from the SKILL.md sections "Tool Matrix", "Auth Flow", "Troubleshooting", "Related Skills", "Related
Documentation", "Tips for AI" and "Failure Modes & Recovery" (2026-11-03). The rules among them that apply on every
run (prereqs non-negotiable, never default a tenantId, the literal confirmation phrase, poll don't reach, no auto-retry,
quarantine is a hard stop, the handoff report on every outcome) stay in SKILL.md Critical Rules.

Corrections annotated in place (original text kept):

- **MOVE-MAP D2** — `pac data` has only `export` / `import` (pac 1.46); every "`pac data` fallback" below is the Web
  API (`Invoke-RegistryWebApi`, `Sync-RegistryOnCompletion.ps1 -Action ApplyWebApi`).
- **MOVE-MAP D11** — two lines below say "call `POST /api/runs/{id}/resume`" to pass a manual gate. That contradicts
  Step 5 and `RunsEndpoints.cs`: a gate is passed with `POST /api/runs/{id}/gates/{gateId}/advance`; `/resume` is only
  for a `Failed` run. `GET /api/runs?customerId=` (list) does not exist: an in-progress run is found from the registry
  row (Step 1a probe, `REGISTRY=in-progress`) and read with `GET /api/runs/{runId}?customerId=`.

## Contents

- Tool Matrix
- Auth Flow
- Troubleshooting
- Step 2 — sample preflight display (illustrative)
- Related Skills · Related Documentation
- Tips for AI
- Failure Modes & Recovery

## Tool Matrix (per spec §4.3a.1 — 15 tools)

| # | Capability | Primary | Fallback |
|---|---|---|---|
| 1 | Read design + spec + POML | Read / Glob / Grep | — |
| 2 | Invoke PowerShell scripts | `PowerShell` tool | `Bash` + `pwsh -File` |
| 3 | Invoke bash tooling | `Bash` tool | — |
| 4 | Call L2 REST API | `WebFetch` (bearer from #5) OR `Bash` + `curl` + `az account get-access-token` | `PowerShell` + `Invoke-RestMethod` |
| 5 | AAD bearer for L2 | `az account get-access-token --resource api://spaarke.com/provisioning-controlplane-{env}` | Interactive `az login` first |
| 6 | Read Dataverse | `mcp__dataverse__read_query`, `mcp__dataverse__search` | raw Web API (`pac data` has no query — D2) |
| 7 | Write Dataverse | `mcp__dataverse__update_record` | raw Web API PATCH (`pac data` has no update — D2) |
| 8 | Read Azure resource state | `Bash` + `az resource show / az keyvault / az webapp` | Azure MCP if configured |
| 9 | Read Graph | `Bash` + `az ad / az rest` against Graph endpoints | Graph SDK script |
| 10 | Read Cosmos | `Bash` + `az cosmosdb sql query` | `Invoke-RestMethod` against Cosmos SQL API |
| 11 | File upload to SPE (H13 sample) | `Bash` + `curl` with Graph app-only token | `pnp` CLI OR Graph SDK script |
| 12 | Real-time run status polling | `WebFetch` / `curl` loop against `GET /api/runs/{id}` | — |
| 13 | Structured handoff report | `Write` (markdown to `runs/{runId}.md`) | — |
| 14 | Task tracking | `TodoWrite` tool | — |
| 15 | Multi-agent orchestration (rare) | `Agent` tool with `researcher` / `general-purpose` subagent | — |

---

## Auth Flow

**Identity**: operator's own AAD (`az login`) per NFR-11. NEVER a service principal.

**Role**: `Operator` app-role on the control-plane app-reg `api://spaarke.com/provisioning-controlplane-{env}`. Assigned via:

```
az ad app show --id api://spaarke.com/provisioning-controlplane-dev --query "id"
# → objectId of the app-reg's SP
az rest --method POST --uri "https://graph.microsoft.com/v1.0/servicePrincipals/{spObjId}/appRoleAssignments" `
  --body '{ "principalId":"{operatorObjId}", "resourceId":"{spObjId}", "appRoleId":"{operatorRoleGuid}" }'
```

**Token**: acquired once per run, refreshed on 401:

```powershell
$token = az account get-access-token `
  --resource "api://spaarke.com/provisioning-controlplane-{env}" `
  --query accessToken -o tsv
```

Lifetime ~1 hour. Fallback matrix handles mid-run expiry.

**Model 2 exception**: H0.5 consent-callback endpoint (`POST /api/onboarding/consent-callback`) is anonymous + HMAC-verified — the customer admin authenticates to THEIR tenant via Microsoft, not to Spaarke's control plane. The skill does NOT participate in this exchange directly; L2 auto-detects the HMAC-signed callback.

---
## Troubleshooting

| Issue | Cause | Resolution |
|---|---|---|
| H14 fails `h14m-mailbox-ensure-failed` naming PRQ-E-16 and a write, or showing "A server side error has occurred … try again after some time" | Exchange refused `New-Mailbox` or `Add-DistributionGroupMember` for `Spaarke Exchange Admin` (403 `CmdletAccessDeniedException`; the EXO module hides the 403 behind that message). Usual cause: a role entry narrowed with `Set-ManagementRoleEntry -Parameters` without the PowerShell common parameters (T186 L62) | Do NOT wait it out or open a support case. Compare the roles with guide §4.2.1 step 5 (the `New-Mailbox` entry must list `ErrorAction … WhatIf`); test as the app with check M8 in `projects/customer-provisioning-orchestration-r1/notes/t263-customer-shared-mailbox.md` §5 — never with `-WhatIf` (`.claude/FAILURE-MODES.md` G-23). After a role change allow 30 min – 2 h, then `POST /resume`. Nothing was created |
| Step 0c returns 403 on role probe | Operator not granted `Operator` app-role on `api://spaarke.com/provisioning-controlplane-{env}` | Ask a control-plane admin to run the `az rest` app-role assignment (see Auth Flow) |
| Step 0c token acquisition fails: `AADSTS500011` | Resource URI wrong OR app-reg not exposed in operator's tenant | Verify `az ad app show --id api://spaarke.com/provisioning-controlplane-{env}` returns a value; if not, wrong env |
| Step 1 customerId probe returns 500 | L2 read-side broken; database issue | Check L2 App Insights + escalate to on-call — do NOT bypass |
| Step 2 H0 times out (>60s) | Azure quota query hung OR L2 stuck | Check L2 logs; may need to abort + retry; occasional Azure quota API slowness |
| Step 4 poll returns 401 mid-run | Token expired | Auto-refresh via `az account get-access-token` (see Fallback Matrix); retry the poll |
| Step 4 handler fails with `QuarantineRequired` | Handler cleanup impossible; state indeterminate | STOP — file `POST /api/runs/{runId}/clear-quarantine` with reason after root-cause analysis. NEVER retry without clear-quarantine |
| Step 5 gate URL 404 on customer admin's browser | Model 2 admin consent URL malformed | Check H0.5 output; may need to reconstruct manually with correct redirect_uri |
| Step 6 MCP update fails | MCP disconnected between preflight + completion | Fallback matrix → Web API (`Sync-RegistryOnCompletion.ps1 -Action ApplyWebApi`); the original said `pac data update`, which does not exist (D2) |
| Skill exits before Step 6 (session timeout, network blip) | L2 continues asynchronously; run state persists in Cosmos | Re-invoke `/provision-environment {customerId}` — skill detects the in-progress run via `GET /api/runs?customerId={id}` [no such route — D11: Step 1a probe finds the run id, then `Watch-ProvisioningRun.ps1`] and resumes at Step 4 poll |

---

## Step 2 — sample preflight display (illustrative)

Moved from SKILL.md Step 2. The client cannot produce these server-side checks — H0 runs them as the first handler
of the Step 4 POST (MOVE-MAP D15). Use the shape for the Step 3 plan; report only the checks the skill actually ran.

```
PREFLIGHT (H0) RESULT
  Duration: 8.2s
  [PASS] Azure OpenAI TPM headroom OK (projected 187/2000 sum-across-models)
  [PASS] App Service plan tier available in westus2
  [PASS] SPE container-type headroom OK (7,442 of 10,000 remaining)
  [PASS] DNS pre-check: acme.spaarke.com not reserved
  [PASS] Spaarke tenant reachable (Model 1)
  [PASS] Estimated cost: $450/mo (within tier 'smb' ceiling $700/mo)
  [PASS] Estimated duration: 42 min (H1-H14, no lead-time gates)

Preflight passed. Proceed to Step 3 (confirmation gate)? (yes/no)
```

**Note**: server-side preflight (H0 handler) will run automatically when Step 4 POSTs `/api/runs`; H0 is the FIRST handler in the L2 DAG per `DagAdvancer.cs`. There is no separate "preflight-only" run mode — that concept was a skill fiction. If the operator wants H0-only re-verification WITHOUT triggering H1+, the actual mechanism is `POST /api/runs/{runId}/preflight?customerId={cid}` per `RunsEndpoints.cs:188` on an EXISTING run (upgrade-mode use case).

---

## Related Skills

| Skill | When to use instead |
|---|---|
| `/deploy-new-release` | Deploying a new release to existing environments (different lifecycle stage — provisioning is customer onboarding; deploy-new-release is application updates) |
| `/dataverse-mcp-usage` | Reference for MCP tool call patterns used in Step 6 registry update |
| `/adr-check` | Auto-invoked by task-execute when this skill is authored/modified |
| `/code-review` | Auto-invoked by task-execute when this skill is authored/modified |
| `/conflict-check` | Optional pre-flight before any BFF-touching provisioning follow-up |

---

## Related Documentation

| Document | Purpose |
|---|---|
| [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../../../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) | Full operator runbook including §12 interim manual sequence this skill supersedes |
| [`projects/customer-provisioning-orchestration-r1/spec.md`](../../../../projects/customer-provisioning-orchestration-r1/spec.md) | Authoritative spec — FR-25, §4.3a, NFR-11 |
| [`projects/customer-provisioning-orchestration-r1/design.md`](../../../../projects/customer-provisioning-orchestration-r1/design.md) | Design v3.3 — §4.3a operator toolchain, §4B silent-fail traps, §4C rollback taxonomy, §4D tenant-isolation invariants, §14A upgrade model |
| [`docs/deployment/version-compatibility-matrix.md`](../../../../docs/deployment/version-compatibility-matrix.md) | H0 upgrade-mode preflight consults this |
| [`docs/deployment/customer-comms/`](../../../../docs/deployment/customer-comms/) | 6 U-CB customer communication templates (welcome, gate URLs, escalation) |
| [`.claude/skills/deploy-new-release/SKILL.md`](../../deploy-new-release/SKILL.md) | Reference model this skill's UX pattern derives from |

---

## Tips for AI

- **Prereqs are non-negotiable.** Do NOT proceed past Step 0 if any check fails — silent tool-version mismatches are the #1 cause of silent-fail traps.
- **NEVER default a `tenantId`.** The `Register-EntraAppRegistrations.ps1:63` fix (`1834b77bc`) established that hardcoded default tenant is an I1 invariant violation. Ask; don't assume.
- **The confirmation gate phrase is load-bearing.** "y" and "yes" are insufficient — the operator must type `proceed with provisioning` literally. This is captured in the audit trail per NFR-11.
- **Poll, don't reach.** Never invoke handlers directly or query BFF Service Bus. L2 owns orchestration; the skill is a thin driver.
- **Manual gates require re-verification, not trust.** Always call `POST /api/runs/{id}/resume` [read: `POST /api/runs/{id}/gates/{gateId}/advance` — D11] and let L2 re-verify (Dataverse, Graph, Azure state). The operator's assertion "I did it" is NOT sufficient.
- **Auto-retry hides diagnostics.** On `Failed`, present the failure + operator options; do NOT auto-retry. Operators need to see the failure to root-cause.
- **`QuarantineRequired` is a hard stop.** Never invoke `clear-quarantine` on behalf of the operator; the reason field is auditable + requires human accountability.
- **Handoff report is durable.** Write `runs/{runId}.md` on every completion — success OR failure OR quarantine. The report is the audit trail + the resumption baseline.
- **MCP disconnect is common** (we experienced this 2026-08-14, 2026-08-15). The fallback matrix handles it. Do not treat MCP disconnect as an error — it's expected.
- **The skill is idempotent at the intake level.** If the operator re-invokes with the same `customerId`, the skill detects the existing run + resumes rather than starting fresh. The state lives in Cosmos, not the skill session.
- **BINDING pre-check protocol** (FR-35): before removing any KV alias / fallback spelling, pre-check the LIVE App Service + KV + Dataverse-persisted config. Root CLAUDE.md §10 canonical secret-catalog manifest is the source of truth.
- **KV credential-lifecycle rule** — *a dated summary; the rule is time-boxed, so apply the current text of `.claude/constraints/provisioning.md` §KV credential lifecycle and ADR-028, not this line (root CLAUDE.md §9).*
  As summarised on 2026-08-27: **`BFF-API-ClientSecret` is GONE** — both KV copies + all 4 App Service settings deleted; credential order pinned to `[ManagedIdentityFederated]` with `RequireSecretFreeIdentity=true`. Do NOT re-introduce this secret under any name — `CredentialGuardTests` fails the build on any new `.WithClientSecret(...)` site. H4 **omits** `BFF-API-ClientSecret` unconditionally. Separately, `Dataverse-ClientSecret` never-delete rule STILL in force until 2026-11-23 (auth-v4 owns its retirement). Full rule: `.claude/constraints/provisioning.md` §KV credential lifecycle.

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Step 0 skipped ("just start the run, we know the machine is fine") | Operator confidence + skill impatience | HARD STOP; prereqs are unconditional. Silent tool-version mismatches are the #1 cause of silent-fail traps T1-T7. |
| Confirmation gate bypassed with "y" | Skill accepted a partial phrase | Enforced literal string `proceed with provisioning`. Any other input re-asks. |
| Same-customer concurrent run attempted | Operator forgot the first run is still active | L2 returns 409 via optimistic concurrency on `sprk_currentrunid`. Skill presents the existing run's status + offers to resume/poll rather than starting a second. |
| Handler retried past its retry budget | Auto-retry logic in the skill | REMOVED — the skill never auto-retries. Operator sees failures + decides. |
| Manual gate auto-advanced by trusting operator assertion | Skill said "y" advances the run without L2 re-verifying | ALWAYS call `POST /api/runs/{id}/resume` [read: `.../gates/{gateId}/advance` — D11]; L2 re-verifies the underlying condition (Dataverse / Graph / Azure state). If verification fails, the run stays at `WaitingOnGate` regardless of operator input. |
| `Quarantined` run silently ignored by operator (walked away) | Skill session ended before quarantine surfaced | Quarantine is written to Cosmos + surfaces on next `/provision-environment {customerId}` invocation. Skill presents it as the first order of business + refuses to start new runs until cleared. |
| Handoff report not written on failure | Skill treated failure as "no report needed" | Report is written on ALL terminal RunStatus values (Completed, Failed, Cancelled, Quarantined — no Drifted; drift surfaces as Failed + rejection code). Failure reports capture the failure mode + diagnostic + resumption instructions. |
| Registry update via MCP fails silently — run marked complete but registry stale | MCP disconnect between preflight + completion; skill didn't check | Fallback matrix triggers immediately on MCP failure; registry MUST be updated before completion is reported. If BOTH MCP + fallback fail, run is marked `CompleteButRegistryStale` and operator must manually update via the Web API or the Portal (`runs/{runId}-registry-stale.md`); the original said `pac data update`, which does not exist (D2). |
| Token expires mid-run; skill fails hard | No auto-refresh | Fallback matrix documents `az account get-access-token` auto-refresh on 401 (see Fallback Matrix section, task 076 owns). |
| L2 unreachable mid-run — skill panics | No graceful degradation | Fallback matrix documents escalation + resume-from-Cosmos-state pattern; L2's crash-recovery (I6) re-runs orphaned runs on restart. |

