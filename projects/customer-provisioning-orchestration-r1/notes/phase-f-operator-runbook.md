# Phase F Operator Runbook — Model 2 (Dedicated) E2E Acceptance

> **Project**: customer-provisioning-orchestration-r1 · Task 089 (SPLIT MODE, owner half)
> **Author**: subagent (scaffolding-only dispatch, 2026-08-18)
> **Purpose**: A thin, sequenced wrapper around invoking `/provision-environment` for the Phase F acceptance run. This runbook does NOT duplicate the skill's logic — it tells you which command to run, in what order, what to expect, and where to file the result. For deep detail on any step, follow the pointer to `.claude/skills/provision-environment/SKILL.md`.
> **Primary path** (per the 2026-08-18 Path A exception): `tenancyModel=Model2Dedicated`, `customerId=trial-2026-08-18`, `profile=trial`. Model 1 is now discretionary — see Step 9.
> **Companion documents**: `notes/phase-f-verification-harness.md` (exact verification commands per trap/invariant/naming/cost), `notes/phase-f-report-skeleton.md` (report template to fill in as you go).
> **Scope note**: everything in this runbook is OWNER-EXECUTED (interactive main-session, your own AAD identity). The scaffolding subagent that authored this file did not and could not run any of these commands.

---

## Before you start

- Estimated wall-clock: **≤ 1 hour** of active pipeline runtime (NFR-03), **excluding** lead-time gates. The one lead-time gate you're likely to hit is H8's SPE container-type 24h replication wait — this is expected and non-blocking (skill auto-resumes).
- Estimated Azure cost while the stamp exists: **~$400/mo** (Model 2 empty-environment floor per §15 #14). Teardown is reversible (soft-delete on KV secrets; resource group deletion for the rest).
- You need: `az login` (your own AAD, not a service principal), `pac` CLI authenticated, Dataverse MCP connected (optional but recommended — has a fallback), and to be running from the repo root (`c:\code_files\spaarke-wt-customer-provisioning-orchestration-r1`) so the handoff report lands somewhere sensible.
- Have `notes/phase-f-report-skeleton.md` open in another window — you'll be copying values into it as each step completes.

---

## Step 1: Prerequisite checks

**Command(s)**:

```powershell
az login
az account set --subscription <dev-sub-id>
az account show --query "{name:name, tenantId:tenantId, user:user.name}" -o json
az ad signed-in-user show --query "{oid:id, upn:userPrincipalName}" -o json

# Verify Operator role + L2 reachability
$token = az account get-access-token --resource api://spaarke-provisioning-controlplane-dev --query accessToken -o tsv
curl https://spaarke-provisioning-dev.azurewebsites.net/healthz

# Role probe (expect 400, NOT 403 — 400 proves Operator role is granted)
curl -sS -o /dev/null -w "%{http_code}" `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{"customerId":"__role-probe__","tenancyModel":"Model1Shared","profile":"dev","tenantId":"__probe__"}' `
  https://spaarke-provisioning-dev.azurewebsites.net/api/runs

pwsh --version   # >= 7.4
az --version     # >= 2.60
pac --version    # >= 1.35
git --version    # >= 2.40
```

**Expected outcome**: `az account show` returns the Spaarke tenant (`a221a95e-6fa6-4f6b-9a3c-19a1c1a56d7e`) and your real UPN (not an ObjectId). `/healthz` returns 200. The role-probe returns `400` (validation error — proves Operator role). All tool versions meet the minimums.

**If it fails**:
- 403 on the role probe → you don't have the `Operator` app-role on `api://spaarke-provisioning-controlplane-dev`. Ask a control-plane admin to grant it (see SKILL.md "Auth Flow" section for the exact `az rest` command).
- `/healthz` non-200 or unreachable → L2 App Service may be down; check `az webapp show --resource-group rg-spaarke-platform-dev --name spaarke-provisioning-dev --query state`.
- Tool version too old → upgrade before proceeding; do NOT skip this gate (per SKILL.md "MUST run all Step 0 prerequisite checks BEFORE any intake or preflight").
- Full detail: `.claude/skills/provision-environment/SKILL.md` Step 0.

**Fill in report at**: metadata table (Run start timestamp — record when Step 1 passes cleanly).

---

## Step 2: Intake decision

**Command(s)**:

Invoke the skill with the customerId pre-filled:

```
/provision-environment trial-2026-08-18
```

The skill will interactively prompt for the remaining 3 inputs. Answer as follows for the Phase F primary path:

| Prompt | Answer |
|---|---|
| `tenantId` | `{your dev-subscription customer tenant GUID for this dedicated stamp — do NOT default to the Spaarke tenant; if this is a synthetic acceptance customer without a real external tenant, use the dedicated test tenant GUID your team has set aside for Model 2 E2E — confirm this exists before proceeding}` |
| `tenancyModel` | `Model2Dedicated` |
| `profile` | `trial` |

The skill will show an INTAKE SUMMARY and ask "Proceed to preflight (H0)? (yes/no)".

**Expected outcome**: skill echoes back `customerId: trial-2026-08-18`, `tenancyModel: Model2Dedicated`, `profile: trial`, and the dev L2 API base URL. If `trial-2026-08-18` has zero prior runs, this is treated as fresh provisioning (not upgrade mode).

**If it fails**: if the skill reports prior runs exist for this `customerId`, decide whether this is intentional (retry after a prior failed/quarantined run) or a naming collision (pick a different `customerId`, e.g. `trial-2026-08-18b`). Full detail: SKILL.md Step 1.

**Fill in the report at**: Metadata table (`customerId`, `tenantId`, `tenancyModel`, `profile`).

---

## Step 3: Preflight (H0)

**Command(s)**: none — this is driven automatically by the skill once you say "yes" to the intake summary. The skill invokes `POST /api/runs` with `mode: "preflight"` and polls until H0 completes (capped at 60s).

**Expected outcome**: skill prints a PREFLIGHT (H0) RESULT block showing PASS on: Azure OpenAI TPM headroom (Model 2 dedicated-deployment check — different from Model 1's shared-capacity check), App Service plan tier availability, SPE container-type headroom, DNS pre-check, customer tenant reachability, estimated cost (should show ≤$400/mo), estimated duration.

**If it fails**: any FAIL here is a hard stop per the skill's design — do NOT proceed to Step 4. Common failure: Azure OpenAI regional TPM headroom insufficient for a NEW dedicated deployment (Model 2 needs a full deployment's worth of TPM, unlike Model 1 which just needs "+1 tenant" headroom on the shared deployment). If this happens, either pick a different Azure region for this stamp or file a quota-increase request and retry later. Full detail + escalation taxonomy: SKILL.md Step 2 + design.md §4C.

**Decision point — is this preflight acceptable?** Confirm before proceeding:
- [ ] Cost estimate is within the $400/mo Model 2 envelope (or you've explicitly accepted an over-envelope run with rationale documented)
- [ ] No hard quota blocks reported
- [ ] Estimated duration is reasonable (no unexpected lead-time gates already flagged)

**Fill in the report at**: Per-Handler Outcomes table, row "H0 preflight".

---

## Step 4: Confirmation gate

**Command(s)**: none — the skill presents the full RUN PLAN (handler list, estimated cost, estimated duration, manual gates you may encounter) and waits for you to type the exact phrase:

```
proceed with provisioning
```

**IMPORTANT**: a bare "y" or "yes" is explicitly rejected by the skill (per NFR-11 auditability). You must type the literal phrase above.

**Expected outcome**: skill transitions the run from `Preflight-Only` to `Executing` and begins the execute loop (Step 5 below).

**If it fails**: if you're not ready to proceed (e.g., you want to double-check the cost estimate against Cost Management first), just don't type the phrase — the skill will keep waiting. Nothing mutates until you type it.

**Fill in the report at**: nothing yet — this is a gate, not evidence. (Optionally note the exact timestamp you typed the phrase, for audit-trail completeness in Deviations.)

---

## Step 5: Execute loop

**Command(s)**: none — the skill polls `GET /api/runs/{runId}` every 10s and reports handler transitions. You watch and respond to any manual gates (Step 6) as they surface.

**Expected outcome**: handlers advance in sequence: H0.5 (consent-callback, Model 2 REQUIRED) → H1 → H2a → H2b → H3 → H4 → H5 → H6 → H7 → H8 → H9 → H10 → H11 (likely N/A for Model 2 — trial users are a Model 1 concept, confirm in the skill's output) → H12a/b/c → H13 → H14. Skill updates TodoWrite entries as each handler enters/exits `Running`.

**If it fails**: on `Failed` status, the skill presents the failure + asks if you want to `POST /api/runs/{id}/resume`. Do NOT blindly resume — read the failure diagnostic first (check App Insights if the failure reason isn't self-explanatory). On `Quarantined`, this is a hard stop — do not attempt to resume; you must call `POST /api/runs/{id}/clear-quarantine` with a documented reason only after root-causing. Full detail + 4-class failure taxonomy: SKILL.md Step 4 + design.md §4C.

**Fill in the report at**: Per-Handler Outcomes table (fill in as each handler completes, or in bulk at the end from the skill's own `runs/{runId}.md`).

---

## Step 6: Manual gate handling

**H0.5 Model 2 admin consent (you WILL hit this — it's required for Model 2)**:

The skill will surface a 🔔 MANUAL GATE block with an admin-consent URL of the shape:

```
https://login.microsoftonline.com/{customerTenantId}/adminconsent
  ?client_id={bff-multi-tenant-app-id}
  &redirect_uri=https://spaarke-bff-prod.azurewebsites.net/api/onboarding/consent-callback
  &state={runId}
```

**Command(s)**: for a real external customer, send this URL to their Global Admin. **For this Phase F acceptance run against a synthetic/internal test tenant, YOU (or a team member with Global Admin on the designated test tenant) open the URL and consent.**

**Expected outcome**: within a few seconds to a couple minutes, the skill auto-detects the HMAC-verified callback and advances the run past H0.5. It polls every 30s for up to 2 hours before pausing to ask what to do.

**If it fails**: if 2 hours pass with no callback, the skill pauses and asks. Check that the URL wasn't malformed (verify `client_id` and `redirect_uri` match the actual multi-tenant BFF app-reg config) — see SKILL.md Troubleshooting table "Step 5 gate URL 404 on customer admin's browser".

**Other gates you might (rarely) hit**: Azure quota bump (H1) — open a support ticket, wait for approval, type `resume`. SPE 24h replication (H8) — no action needed, the skill exits and auto-resumes H8.a ~25h later, or you can leave the skill running and it polls hourly.

**Fill in the report at**: Manual Gates Encountered table.

---

## Step 7: Completion + report fill-in

**Command(s)**: none — on `Succeeded`, the skill writes its own handoff report to `runs/{runId}.md` and updates the `sprk_dataverseenvironment` registry.

Now run the full verification pass from `notes/phase-f-verification-harness.md` — every T1-T6 trap check, every I1-I5 invariant check, the naming-conformance script, and the cost-envelope query. This is NOT redundant with H13's automated acceptance gate — H13's checks run as PLACEHOLDERS in the current handler implementation (per task 055's Wave-C4 placeholder pattern; live-probes were explicitly deferred to this task). **The verification harness commands ARE the live probes.**

```powershell
pwsh -File scripts/naming-conformance-check.ps1 -Scope r1-owned
```

Run each trap/invariant/cost command from the harness, one at a time, recording results.

**Expected outcome**: 6/6 traps PASS, 5/5 invariants PASS, naming-conformance exits 0, cost within envelope.

**If it fails**: if ANY trap, invariant, or the cost check fails, STOP — do not proceed to mark task 089 complete or hand off to task 090. Escalate per CLAUDE.md §6.5 (this is explicitly called out in the task 089 POML `<escalation>` block — a fail here blocks task 090 wrap-up and requires owner input on remediation path: fix + re-run, documented exception, or ADR amendment).

**Fill in the report at**: copy `notes/phase-f-report-skeleton.md` to `notes/phase-f-e2e-acceptance-2026-08-18.md` and fill EVERY section: Metadata, Setup Status Verdict, Per-Handler Outcomes, Per-Trap Verified, Per-Invariant Verified, Naming-Conformance Verdict, Cost Snapshot, Manual Gates Encountered, Registry State, Deviations/Lessons Learned.

---

## Step 8: Update TASK-INDEX + POML on success

**Command(s)**: manual edit (or ask Claude Code to do it) — once the report is complete and all checks PASS:
- Update `projects/customer-provisioning-orchestration-r1/tasks/089-phase-f-e2e-acceptance.poml` `<metadata><status>` to `completed`.
- Update `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md` row 089 from 🟡 to ✅.
- Update `current-task.md` to point at task 090 next.

**Expected outcome**: task 089 shows ✅ in TASK-INDEX.md; task 090 (wrap-up + `/test-diet`) is unblocked.

**If it fails**: if any acceptance criterion didn't pass, do NOT mark 089 complete — leave it 🟡 or set to 🔄 (needs-retry) and document the gap.

**Fill in the report at**: n/a (this step updates project tracking files, not the acceptance report itself).

---

## Step 9: Model 1 discretionary run (optional)

Per the 2026-08-18 Path A exception, Model 1 is now discretionary (it was the mandatory primary path before this amendment). If you want to also exercise the Model 1 shared-tier path for completeness:

**Command(s)**:

```
/provision-environment trial-2026-08-18-m1
```

Answer `tenancyModel: Model1Shared` at intake. Expect a SHORTER handler list (no H0.5 consent-callback — Model 1 skips it; shares the platform App Service Plan/AI Search/OpenAI rather than provisioning dedicated ones).

**Expected outcome**: reaches `Ready`; §4.1a differences hold (verify shared AI Search index reuse rather than re-creation, per-tenant token-metering headers present, shared platform floor cost ≤$400/mo + marginal ≤$430/mo).

**If it fails / if you skip this step**: document either the result or the explicit skip rationale in the report's "Model 1 Discretionary Run" section. A skip is fully acceptable per the amended constraint — do not treat it as a gap.

**Fill in the report at**: "Model 1 Discretionary Run" section of the report.

---

## Step 10: Teardown checklist (after acceptance is verified)

Only after the report is complete and committed. See the report skeleton's own "Teardown Checklist" section for the full list. Summary:

1. Confirm the report is committed (evidence must survive teardown).
2. Decide: leave the stamp standing for reference, or tear down via `scripts/Decommission-Customer.ps1` (decommission itself is out of scope for r1 per D17 — this is a manual/future action).
3. Verify `sprk_currentrunid` is cleared before any teardown action.
4. Note the retention/teardown decision in the report.

---

## Quick reference — full command sequence (for a clean run)

```powershell
# Step 1
az login
az account set --subscription <dev-sub-id>
az account get-access-token --resource api://spaarke-provisioning-controlplane-dev --query accessToken -o tsv
curl https://spaarke-provisioning-dev.azurewebsites.net/healthz

# Step 2-6 (interactive via Claude Code)
# /provision-environment trial-2026-08-18
#   -> tenantId, tenancyModel=Model2Dedicated, profile=trial
#   -> "proceed with provisioning" at confirmation gate
#   -> handle H0.5 admin-consent gate when it surfaces

# Step 7 (after Succeeded)
pwsh -File scripts/naming-conformance-check.ps1 -Scope r1-owned
# ... run every command from notes/phase-f-verification-harness.md ...

# Step 8
# fill notes/phase-f-e2e-acceptance-2026-08-18.md from the skeleton
# update TASK-INDEX.md row 089 -> ✅

# Step 9 (optional)
# /provision-environment trial-2026-08-18-m1 -> Model1Shared
```
