# Wave 0 ADR-Note — SESSION 15 pre-dispatch remediation architectural decisions

- **Date**: 2026-08-27
- **Status**: BINDING for Waves 1-8 of the SESSION 15 pre-dispatch remediation
- **Operator authority**: User directive 2026-08-27 "we need this process to work, full stop ... every issue is a priority ... don't wait for me — everything needs to be correct so there is no such thing as 'priority'"
- **Author authority**: Best-judgment decisions applied per CLAUDE.md §6.5 protocol; documented here for post-hoc operator review + auditable revert
- **Origin punchlist**: `notes/pre-dispatch-audit-punchlist-2026-08-27.md`
- **Waves that cite this note**: 2, 3, 4, 5, 6, 7, 8

## Purpose

The comprehensive pre-dispatch audit (workflow `wf_aef5ac94-9dd`, 127 findings) surfaced 10 items requiring architectural decisions before remediation dispatch. Per user directive, decisions are made unilaterally and documented here rather than blocking on operator round-trips. Any decision can be reverted by the operator; targeted re-work will result but the whole pipeline is not held hostage.

---

## Decision 1: DEP-01 — canonical `tenantId` propagation path

**Chosen**: **(A) `nonSecretParameters["tenantId"]` as the ONLY canonical source.**

**Rationale**:
- Smallest surface-change: no widening of `CreateRunRequest`
- Matches existing handler pattern — every handler already reads `run.Parameters.NonSecret[...]` for cross-cutting concerns
- Simpler mental model — no precedence rules ("top-level trumps nonSecret" or reverse) to enforce
- Skill Step 2 already sends tenantId; needs only to move it from top-level to `nonSecretParameters` in the POST body
- Aligns with the ADR-028 spec that `sprk_tenantid` in Dataverse is a projection-of-truth, not the primary source

**Downstream implications**:
- Skill Step 2 rewrite (Wave 4) MUST send tenantId inside `nonSecretParameters`, not top-level
- Every handler reads `run.Parameters.NonSecret["tenantId"]`; must throw if missing (fail-fast per §4D I1)
- Step 1f placeholder-write includes `sprk_tenantid` for registry-side read-projection, but the RUN's tenantId is nonSecretParameters — NEVER re-write sprk_tenantid post-placeholder-create
- ArchTest: every handler that consumes tenantId must fail if `NonSecret["tenantId"]` is absent
- Intake schema (Wave 5): `tenantId` remains a top-level intake field (operator-facing), but the skill MOVES it into `nonSecretParameters` before POST

## Decision 2: SKILL-02 — Step 1a customerId uniqueness probe

**Chosen**: **(B) Dataverse MCP alt-key GET on `sprk_dataverseenvironment` filtered by `sprk_customerid`.**

**Rationale**:
- 15min effort vs (A) spec change to add a new L2 REST endpoint
- Reuses existing `DataverseRegistryConcurrencyStore` alt-key pattern
- Aligns with ADR-044 canonical registry ownership
- Dataverse-MCP-first pattern (with pac data + raw Web API fallback per Fallback Matrix F1)
- Skill Step 0d already validates MCP status; degrades gracefully if disconnected

**Downstream implications**:
- Skill Step 1a rewrite (Wave 4): probe via `mcp__dataverse__read_query` on `sprk_dataverseenvironment` filtered by `sprk_customerid eq '{customerId}'`, project `sprk_provisionedon`
- If row present with `sprk_provisionedon != null` → prompt operator to confirm upgrade-mode
- If row present with `sprk_provisionedon == null` → prior halt/quarantine; prompt operator per Fallback Matrix
- If row absent → fresh customerId (proceed to Step 1f placeholder-create as normal)

## Decision 3: BAT-03 — batch-mode confirmation phrase vs NFR-11 audit trail

**Chosen**: **(A) intake schema adds `confirmationAcknowledgment` field with `const: "proceed with provisioning"`.**

**Rationale**:
- Preserves batch utility (no HARD STOP on batch runs)
- NFR-11 auditability preserved via two evidence layers: (1) operator TYPED the phrase into the intake file (typing = intent expression); (2) SHA-256 hash of the intake JSON is included in the L2 audit record + Step 7 postmortem
- The audit trail becomes: "operator {upn} attested via intake file {sha256} at {timestamp}"
- Interactive mode still requires stdin-typed phrase per skill Step 3 (unchanged)
- Batch mode requires the field + rejects any other value

**Downstream implications**:
- Intake schema (Wave 5): add `confirmationAcknowledgment` field required when `mode==execute` (or unconditionally, since batch always executes)
- Skill Step 3 batch branch (Wave 4/6): read `confirmationAcknowledgment` from intake; compare to literal; HARD STOP if absent or wrong value
- L2 CreateRun endpoint (Wave 2 B2): accept `confirmationAcknowledgment` + `intakeFileSha256` in `nonSecretParameters`, log to audit record
- Step 7 postmortem (Wave 6): captures the SHA-256 hash + phrase verbatim

## Decision 4: EXEC-02 — Step 2/3/4 confirmation-gate architectural redesign

**Chosen**: **(A) Rewrite Step 2 as client-side dry-run + move confirmation gate BEFORE POST /api/runs.**

**Rationale**:
- No L2 spec change required (Option B would add `Enqueued-Awaiting-Confirm` Cosmos state)
- Matches actual L2 behavior — POST /api/runs unconditionally enqueues H0; there is no server-side preflight-only mode
- Operator's confirmation gate becomes a REAL gate — nothing mutates before "proceed with provisioning" is captured
- New Step 2 becomes client-side validation: (a) intake schema validate, (b) prereqs.yaml Step 0.5 iteration, (c) tenant-isolation invariant pre-checks operator-side, (d) show run plan to operator
- Step 3 confirmation gate fires; operator types phrase (or batch supplies from intake per Decision 3)
- Step 4 POSTs to L2 with `confirmationAcknowledgment` + `intakeFileSha256` in nonSecretParameters; L2 begins execution

**Downstream implications**:
- Skill Steps 2, 3, 4 aggregate rewrite (Wave 4)
- L2 CreateRun endpoint (Wave 2 B2): accepts + logs confirmation fields
- The (mock) `mode: preflight` / `mode: execute` fields are DELETED from skill body examples
- Task 186 dispatch flow changes: no separate `/preflight` intermediate step; Step 2 client-side + Step 3 gate + Step 4 direct-to-execute

## Decision 5: EXEC-03 — combined with BAT-03

Folded into Decision 3. No separate action needed.

## Decision 6: ISH-03 — intake schema field boundary

**Chosen**: **(A) mechanical prune to match current `CreateRunRequest`.**

**Rationale**:
- Consistent with Decision 1 (tenantId flows via `nonSecretParameters`, not top-level)
- Less code churn — no `CreateRunRequest` widening
- Consumer-side change: skill maps intake operator-facing fields → `nonSecretParameters` in the L2 POST body
- All intake fields that aren't `CreateRunRequest`'s exact top-level shape (customerId, environmentId, tenancyModel, profile, nonSecretParameters) go into `nonSecretParameters`

**Downstream implications**:
- Intake schema (Wave 5): keep operator-facing shape (customerId, tenantId, tenancyModel, environment, profile, region, openAiRegion, tier, operatorUpn, confirmationAcknowledgment, notes); document that tenantId + region + openAiRegion + tier + operatorUpn + confirmationAcknowledgment all flow through `nonSecretParameters` in the L2 POST
- Skill Step 2/4 (Wave 4): construct L2 POST body by placing top-level fields in top-level + all others in `nonSecretParameters` map
- CreateRunRequest (Wave 2 B2): validates `nonSecretParameters["tenantId"]` non-empty; `nonSecretParameters["confirmationAcknowledgment"]` == expected literal

## Decision 7: HANDLER-11 — Drifted RunStatus

**Chosen**: **(B) strike `Drifted` from SKILL.md state-machine references.**

**Rationale**:
- `Drifted` is NOT in the RunStatus enum (verified via audit — enum has NotStarted, Running, WaitingOnGate, Completed, Failed, Cancelled, Quarantined)
- Docs-fix (~2 min) vs code-fix (~3h + tests)
- If a future spec truly requires `Drifted`, it's a design.md amendment + separate task (r2)
- Current spec.md does not appear to explicitly require Drifted (per audit's read of design §4C)

**Downstream implications**:
- Skill Step 4b state-transition table (Wave 4): remove Drifted row
- Fallback Matrix / any docs that mention Drifted: strike references
- If operator reviewing this ADR-note wants Drifted added: reverts to Option A + Wave 2 B1 gains 3h of RunStatus enum + serialization + test work

## Decision 8: DEP-08 — umbrella item

Resolved by Decisions 1-4 + 6-7 above. No separate action.

## Decision 9: REG-04 — `CustomerRunGuard` MI-FIC credential seam

**Chosen**: **DEFER — read finding in-context during Wave 2 B4 and decide in the fix itself.**

**Rationale**:
- Insufficient standalone info in Wave 0 summary to make a durable decision here
- Wave 2 B4 (sprk_dataverseenvironment write-path expansion) is the natural home for this decision — subagent will surface it in the write-path context
- Blocking Wave 0 on this item would delay Wave 2 by hours for something that resolves faster with code in hand

**Downstream implications**:
- Wave 2 B4 subagent prompt (below) MUST inspect REG-04 details in the punchlist + decide the credential-seam pattern; document decision in commit message + append to this ADR-note post-facto

## Decision 10: COMP-12 — auth-v4 rotation window coordination

**Chosen**: **Cross-worktree coordination via a separate ops-note; not a Wave dependency.**

**Rationale**:
- COMP-12 is inherently cross-project (auth-v4 team owns rotation; provisioning owns dispatch window)
- Not a code fix within this project's boundaries
- Blocking Wave 1 on this would gate on external coordination

**Downstream implications**:
- File a `notes/auth-v4-rotation-window-coord-2026-08-27.md` post-Wave-8 documenting the coordination expectation (dispatch task 186 during auth-v4 quiescent windows; SendMessage to auth-v4 branch before dispatch)
- Track as an ops-runbook entry, not code

---

## Reversibility

If any decision above is later revised by the operator, targeted re-work follows:

| Decision revised | Re-work required |
|---|---|
| 1 (tenantId path) | Wave 2 B2 (CreateRunRequest field add) + Wave 4 (skill Step 2 rewrite) + Wave 5 (intake schema) — ~4h |
| 2 (registry probe) | Wave 4 (skill Step 1a re-rewrite) + potentially Wave 2 B2 (add real endpoint) — ~1-8h |
| 3 (confirmation phrase) | Wave 5 (intake schema) + Wave 4 (skill Step 3) + Wave 2 B2 (audit record) — ~3h |
| 4 (Step 2/3/4 redesign) | Wave 4 (skill Steps 2-4 re-rewrite) + Wave 2 B2 (Cosmos state addition if Option B) — ~6-16h |
| 6 (intake field boundary) | Wave 5 (intake schema re-map) + Wave 4 (skill body-construction logic) — ~3h |
| 7 (Drifted state) | Wave 2 B1 (RunStatus enum + DagAdvancer + tests) — ~3h |

---

## Post-hoc operator review checklist

The operator can validate each decision by checking:

- [ ] Decision 1: `run.Parameters.NonSecret["tenantId"]` is the ONLY code path where handlers read tenantId
- [ ] Decision 2: Skill Step 1a probe uses Dataverse MCP (or fallback), not a non-existent REST endpoint
- [ ] Decision 3: Intake schema has `confirmationAcknowledgment` const-string; L2 audit record captures it + intake SHA-256
- [ ] Decision 4: Skill's Step 2 is client-side; confirmation phrase fires BEFORE any POST /api/runs; H0 cannot enqueue before the phrase is captured
- [ ] Decision 6: `CreateRunRequest` top-level shape unchanged; skill body-construction maps intake to `nonSecretParameters`
- [ ] Decision 7: SKILL.md no longer references `Drifted` state
- [ ] Decisions 9 + 10: Wave 2 B4 commit message documents REG-04 decision; ops-note authored for auth-v4 coord

---

*This ADR-note is the constraint doc Waves 2-6 cite. It ships with the SESSION 15 pre-dispatch remediation commit series.*
