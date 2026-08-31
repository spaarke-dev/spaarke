# Phase F E2E Acceptance Report — SKELETON (owner fills in)

> **Instructions for the owner**: Copy this file to `notes/phase-f-e2e-acceptance-2026-08-18.md` (or the actual date of the run) and fill in every `{...}` placeholder from the live `/provision-environment` invocation. Do not delete this skeleton file — it's the reusable template for future Phase F-style acceptance runs. Cross-reference `notes/phase-f-verification-harness.md` for the exact command to run for each trap/invariant/naming/cost row.
>
> This report is the binding SC #5 / SC #6 evidence trail for task 090 wrap-up (per the task 089 POML `<constraints>` Path A exception — Model 2 dedicated is now the primary acceptance path).

---

## Metadata

| Field | Value |
|---|---|
| `customerId` | `{trial-2026-08-18}` <!-- kebab-case per intake format --> |
| `tenantId` | `{customer Entra tenant GUID supplied at intake}` |
| `tenancyModel` | `{Model2Dedicated}` <!-- primary path per Path A exception; note if Model1Shared discretionary run also performed --> |
| `profile` | `{trial}` |
| Run start (UTC) | `{ISO 8601 timestamp}` |
| Run end (UTC) | `{ISO 8601 timestamp}` |
| Wall-clock duration | `{Nh Nm}` <!-- compare against NFR-03 ≤1h target, excluding lead-time gates --> |
| L2 run URL | `{https://spaarke-provisioning-dev.azurewebsites.net/api/runs/{runId}}` |
| Handoff report (skill-native) | `{path to runs/{runId}.md written by the skill itself}` |

---

## Setup Status Verdict

<!-- Query sprk_dataverseenvironment for this customer; paste the raw field value -->

| Field | Value |
|---|---|
| `sprk_provisioning_setupstatus` | `{Ready / Failed / Quarantined / other}` |
| Verdict | `{PASS — reached Ready / FAIL — did not reach Ready, see Deviations}` |

---

## Per-Handler Outcomes (H0–H14, Model 2 = 15+ handlers incl. H0.5)

<!-- Fill from the skill's own handoff report (runs/{runId}.md) — this table should mirror it closely,
     with the addition of a Notes column calling out anything Phase-F-acceptance-specific. -->

| # | Handler | Status | Duration | Notes |
|---|---|---|---|---|
| 1 | H0 preflight | `{Succeeded/Failed}` | `{duration}` | `{quota/DNS/reachability notes}` |
| 2 | H0.5 consent-callback | `{Succeeded/Failed/N-A}` | `{duration}` | `{Model 2 REQUIRED — admin consent URL clicked at {timestamp}}` |
| 3 | H1 resource-group provisioning | `{}` | `{}` | `{}` |
| 4 | H2a Bicep infra apply | `{}` | `{}` | `{dedicated stamp: new UAMI/KV/Cosmos/Storage/App Service Plan/AI Search/OpenAI}` |
| 5 | H2b AI Search index deploy | `{}` | `{}` | `{7 canonical indexes on DEDICATED AI Search service}` |
| 6 | H3 Entra grants | `{}` | `{}` | `{14 grants}` |
| 7 | H4 KV secret bootstrap | `{}` | `{}` | `{T1 + T5 owner}` |
| 8 | H5 Dataverse environment creation | `{}` | `{}` | `{}` |
| 9 | H6 Dataverse solutions import | `{}` | `{}` | `{8 solutions, dependency-ordered}` |
| 10 | H7 env-var writes | `{}` | `{}` | `{points at DEDICATED OpenAI/AI Search/App Insights}` |
| 11 | H8 SPE container-type creation | `{}` | `{}` | `{24h replication gate — may show WaitingOnGate}` |
| 12 | H9 BFF deploy | `{}` | `{}` | `{blue-green slot swap}` |
| 13 | H10 Dataverse App User + Graph parity | `{}` | `{}` | `{T2 + T3 owner}` |
| 14 | H11 demo user provisioning | `{}` | `{}` | `{Model 1 only — likely N/A for Model 2; confirm}` |
| 15 | H12a AI seed chain | `{}` | `{}` | `{playbooks + embeddings}` |
| 16 | H12b playbook consumers seed | `{}` | `{}` | `{}` |
| 17 | H12c agents/runtime refs seed | `{}` | `{}` | `{sprk_aimodeldeployment → DEDICATED OpenAI deployment}` |
| 18 | H13 acceptance gate | `{}` | `{}` | `{6/6 traps clear, 5/5 invariants pass — summarized below}` |
| 19 | H14 Exchange ApplicationAccessPolicy | `{}` | `{}` | `{T4 owner}` |

---

## Per-Trap Verified (T1–T6)

<!-- One row per trap. Evidence link = path to a saved command-output log, or inline paste of the key output line. -->

| Trap | Description | Verdict | Evidence |
|---|---|---|---|
| T1 | `keyVaultReferenceIdentity` == UAMI (both slots or UAMI-spans-both) | `{PASS/FAIL}` | `{az webapp show output / log path}` |
| T2 | Dataverse App User exists for MI (systemusers count = 1) | `{PASS/FAIL}` | `{pac data query output / log path}` |
| T3 | UAMI Graph app-role parity (14/14 `GraphAppRoles.cs` roles present) | `{PASS/FAIL}` | `{az rest appRoleAssignments output / log path}` |
| T4 | Exchange ApplicationAccessPolicy — 2 entries (BFF app-reg + UAMI) | `{PASS/FAIL}` | `{Get-ApplicationAccessPolicy output / log path}` |
| T5 | Slot-parity KV RBAC (or structurally-impossible via UAMI) | `{PASS/FAIL/N-A-structural}` | `{az role assignment list output / log path}` |
| T6 | SPE container creation via confidential-client (no delegated 403) | `{PASS/FAIL}` | `{az rest containerType GET output / log path}` |

**Traps summary**: `{N}/6 cleared`. <!-- Should be 6/6 for a clean acceptance -->

---

## Per-Invariant Verified (I1–I5)

| Invariant | Description | Verdict | Evidence |
|---|---|---|---|
| I1 | No hardcoded default tenant in provisioning scripts | `{PASS/FAIL}` | `{grep output + ArchTest result}` |
| I2 | AI Search queries include unconditional `tenantId` filter | `{PASS/FAIL}` | `{App Insights trace + ArchTest result}` |
| I3 | Cosmos reads/writes include partition-key predicate | `{PASS/FAIL}` | `{az cosmosdb sql query RU-charge + ArchTest result}` |
| I4 | SPE container ID always tenant-scoped-derived | `{PASS/FAIL}` | `{KV secret + Dataverse env-var match + ArchTest result}` |
| I5 | Graph token acquisition per-tenant scoped | `{PASS/FAIL}` | `{ArchTest result + token tid decode}` |

**Invariants summary**: `{N}/5 sample-verified`. <!-- Should be 5/5 -->

---

## Naming-Conformance Verdict

```
{paste the full pwsh -File scripts/naming-conformance-check.ps1 -Scope r1-owned output here}
```

| Field | Value |
|---|---|
| Exit code | `{0 / non-zero}` |
| Verdict | `{PASS/FAIL}` |
| Non-conforming items (if any) | `{list, or "none"}` |

---

## Cost Snapshot

| Field | Value |
|---|---|
| H0 preflight estimated cost | `${amount}/mo` |
| Actual cost (24-48h extrapolated via Cost Management) | `${amount}/mo` <!-- fill in 24-48h after run; may require a follow-up edit to this report --> |
| Target (Model 2 primary path) | `≤$400/mo` |
| Deviation | `{N% over/under target}` |
| Verdict | `{PASS / DRIFT-FLAGGED (>20% over) / FAIL}` |
| Cost breakdown by SKU (if drift flagged) | `{table or list of top-cost resources}` |

---

## Manual Gates Encountered

<!-- List every WaitingOnGate the run hit, how it was resolved, and how long the wait was. -->

| Gate | Handler | Wait duration | Resolution |
|---|---|---|---|
| `{e.g. Model 2 admin consent}` | `{H0.5}` | `{duration}` | `{customer admin clicked URL at {timestamp}; HMAC callback auto-detected}` |
| `{e.g. Azure quota bump}` | `{H1 or other}` | `{duration}` | `{if encountered — else omit row}` |
| `{e.g. SPE 24h replication}` | `{H8}` | `{duration}` | `{if encountered — note whether acceptance was completed before or after the 24h wait, or whether H8.a auto-resumed}` |

---

## Registry State (`sprk_dataverseenvironment` post-provision)

<!-- Query the record directly and paste the relevant fields -->

| Field | Value |
|---|---|
| `sprk_dataverseenvironmentid` | `{GUID}` |
| `sprk_provisionedon` | `{timestamp}` |
| `sprk_currentrunid` | `{should be null/cleared post-completion}` |
| `sprk_bffversion` | `{version}` |
| `sprk_solutionversion` | `{version}` |
| `sprk_tenantid` | `{GUID — must equal the customer tenantId supplied at intake, per I1}` |
| `sprk_setupstatus` | `{200000004 / Ready}` |

---

## Deviations / Lessons Learned

<!-- Any manual gate that took longer than expected, any handler that needed a resume, any drift
     discovered mid-run, any decision that deviated from the runbook, and why. Use the CLAUDE.md §6.5
     format if an ADR conflict surfaced. -->

`{free-text — list each deviation with a short rationale}`

---

## Model 1 Discretionary Run (if performed)

<!-- Per the Path A exception, Model 1 is now discretionary. If performed, summarize briefly here
     (full detail can point to a second report file if a full Model 1 run was also done). If not
     performed, state the skip rationale explicitly. -->

`{Either: "Not performed. Skip rationale: {reason}." OR a summary of the Model 1 dry-run/full-run
result + confirmation that §4.1a differences (H0/H2a/H2b/H4/H7/H10/H12c/H13 behavior deltas) held.}`

---

## Teardown Checklist (for after acceptance is verified)

<!-- Per plan.md Phase F Deliverables, teardown is discretionary — the trial stamp may be left for
     reference. If the owner chooses to tear down, use this checklist. -->

- [ ] Confirm this report is complete and committed before tearing down (evidence trail must survive teardown)
- [ ] Run `scripts/Decommission-Customer.ps1` (or the current decommission entry point) against `{customerId}` — NOTE: decommission is out of scope for r1 (D17); this is a manual/future-project action
- [ ] Verify `sprk_currentrunid` is cleared before decommission (avoid orphaned concurrency lock)
- [ ] Confirm KV secrets for `{customerId}` are soft-deleted (recoverable) not hard-deleted, per standard KV retention
- [ ] Update `sprk_dataverseenvironment.sprk_setupstatus` to reflect decommissioned state if the schema supports it
- [ ] Note final actual cost incurred for this acceptance run (for portfolio cost tracking)
- [ ] If left standing for reference (not torn down): note the retention decision + expected teardown date here: `{date or "indefinite — reference stamp"}`
