# Pre-Dispatch Audit Punch List — /provision-environment Automation

- **Date**: 2026-08-27
- **Origin workflow**: `wf_aef5ac94-9dd` (10-agent audit + adversarial verify)
- **Input journal**: `.../wf_aef5ac94-9dd/journal.jsonl`
- **Directive**: user instruction "we need this process to work, full stop ... every issue is a priority" — ALL findings fix, no triage-for-scope.
- **Owner boundary**: per root `CLAUDE.md` §3 (Sub-Agent Write Boundary) — any fix touching `.claude/**` MUST run on the main session; `src/`, `tests/`, `scripts/`, `infrastructure/` can fan out to subagents.

---

## Executive Summary

- **Total unique findings after dedup**: **127** (94 audit findings + 33 verifier-missed findings; zero were dedup-collapsed to a different ID because each finding cited a distinct file/line/logical defect — cross-cited defects are surfaced in the Duplicates section but retained as separate rows so downstream remediation can cite the specific reviewer's note).
- **Severity distribution**: **critical=38**, high=51, medium=31, low=7.
- **Owner distribution**: **main-session-only=94** (must run inline on this session per Sub-Agent Write Boundary), **subagent-ok=33** (fan-out safe).
- **Aggregate estimated effort**: **~168h known-effort across 85 findings**; 42 findings carry no numeric estimate (mostly DEP/EXEC/COMP verifier items whose sizing is folded into the wave they anchor).
- **Refuted findings (adversarial-verify overturned)**: **0** — all three verifier agents (`dependency-and-parallel-safety`, `EXECUTION_REALISM`, `completeness`) returned `findings_refuted: []`. Every audit finding survives; verifiers only ADDED 33 net-new missed findings.
- **Blocker verdict**: DEP verifier declared the audit `audit_is_complete: true` for dependency safety; EXEC + COMP declared `audit_is_complete: false` and enumerated 8 + 12 execution blockers respectively — those blockers are the source of the WAVE0/WAVE7/WAVE8 items below.

---

## Refuted findings (drop from remediation)

None. All three adversarial-verify agents returned empty `findings_refuted` arrays. Confirmed findings (surfaced by verifiers as extra corroboration):

- **DEP verifier confirmed**: SKILL-01, SKILL-02, SKILL-03, SKILL-04, SKILL-05, SKILL-06, SKILL-07, SKILL-08, HANDLER-01 (9 findings)
- **EXEC verifier confirmed**: HANDLER-01, SKILL-03, SKILL-06, plus one implicit registry finding (4 findings)
- **COMP verifier confirmed**: 0 (COMP scope was completeness, not audit-item verification)

No finding is dropped from remediation.

---

## Remediation Waves (dependency-ordered)

The dependency-and-parallel-safety verifier (DEP) supplies the wave contract. Wave N+1 hard-depends on Wave N landing. Within each wave, sub-lanes are labelled with owner boundary so a `/project-pipeline` dispatcher can fan out safely.

### Wave 0: Human Judgment Gates (Path A/B/C selection per CLAUDE.md §6.5)

**Rationale.** Four DEP-08 items + two additional cross-audit design decisions must be answered by a human operator before any remediation dispatch. Waves 1+ hard-depend on these choices.

**Wave sizing.** 8 findings; ~15.8h known-effort; 4 findings without numeric estimate.

**Main-session-only (8):**

- **`DEP-08` [MEDIUM] [dependency-and-parallel-safety]**: Four findings mix mechanical fix with architecture judgment: (a) SKILL-02 option (a) API addition vs option (b) registry probe — a design choice, not a mechanical fix; (b) DEP-01 tenantId path canonicalization needs an o...
  - **file:where** — SKILL-02 (registry-probe replacement), DEP-01 (tenantId ADR-note), registry CustomerRunGuard MI-FIC seam decision, batchMode failure/gate/quarantine policy defaults
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Before dispatching Wave 1, surface these four as 🔔 Human Input Required with concrete option A/B/C choices and recommendations. Freeze the answers into the DEP-01 ADR-note and DEP-07 wave plan.
  - **consequence_if_unfixed** — Sub-agents make silent architectural decisions that later require costly reversal.
  - **why_audit_missed** — Each audit proposed a fix without labeling the judgment vs mechanical boundary.

- **`DEP-01` [CRITICAL] [dependency-and-parallel-safety]**: Three separate audits (skillDrift SKILL-03, intake_summary, registry §Step-1f) each propose fixes for how tenantId reaches handlers — but each proposes a DIFFERENT locus (skill: put in top-level; intake: put in nonSecret...
  - **file:where** — cross-audit: intake CreateRunRequest shape ↔ SKILL-03 Step 2 body ↔ registry Step 1f placeholder write ↔ every handler's NonSecret["tenantId"] read
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Wave 1 pre-work: publish ONE ADR-note (`.claude/adr/ADR-028-addendum-tenantid-flow.md` or inline in provisioning constraints) declaring the canonical tenantId propagation path: intake→nonSecretParameters→Parameters.NonSecret→handlers, with Step 1f sprk_tenantid as read-projection only. THEN dispatch SKILL-03, intake-shape, and registry fixes as Wave 2 in parallel, each referencing the ADR note.
  - **consequence_if_unfixed** — Parallel fix waves for skill/intake/registry converge on incompatible tenantId plumbing. Best case: burn a re-work cycle. Worst case: two loci both look correct in isolation but the runtime path silently drops tenantId (as it does today) and the tenant-isolation invariant remains broken. Since all three audits list their fix as CRITICAL, they will be dispatched in parallel and collide.
  - **why_audit_missed** — Each audit examined ONE surface (skill, intake, registry) and proposed a locally-correct fix. No audit had cross-surface visibility to notice that all three fixes independently touch the tenantId plumbing at different layers.

- **`SKILL-02` [CRITICAL] [skill-drift-audit]**: Step 1a instructs the operator/skill to `GET /api/runs?customerId={id}` and inspect the returned run history to decide fresh-vs-upgrade
  - **file:where** — .claude/skills/provision-environment/SKILL.md:348 (Step 1a customerId uniqueness probe)
  - **fix estimate** — 15 min for option (b) · **main-session-only?** yes
  - **proposed_fix** — Either (a) add a real GET /api/runs?customerId= list endpoint in L2 (spec change), OR (b) rewrite Step 1a to probe the REGISTRY via Dataverse MCP against sprk_dataverseenvironment.sprk_customerid + sprk_provisionedon (non-null == prior run) — this reuses DataverseRegistryConcurrencyStore's alt-key lookup shape. Option (b) is faster and aligns with the ADR-044 canonical registry ownership.
  - **verification** — Trace Step 1a for an existing `customerId=trial1` — the probe should return the placeholder row + sprk_provisionedon; skill prompts operator to confirm upgrade-mode.
  - **consequence_if_unfixed** — Step 1a call returns a 404 (Missing route) or 405. The skill's fresh-vs-upgrade branch never fires; the operator is never asked to confirm an upgrade. Downstream: the skill silently starts a NEW run on a customerId that may already have provisioned state, corrupting registry (I5 concurrency guard 409s at CreateRun) or bypassing upgrade-mode preflight.

- **`BAT-03` [CRITICAL] [batch-mode]**: Step 3 requires the operator to type the literal phrase 'proceed with provisioning' — 'a bare y or yes is INSUFFICIENT' (line 58 MUST rule + line 705)
  - **file:where** — .claude/skills/provision-environment/SKILL.md:653-708 (Step 3 Confirmation Gate)
  - **fix estimate** — 2h (schema + skill + audit-field wiring) · **main-session-only?** yes
  - **proposed_fix** — Recommend Path A: add `confirmationAcknowledgment` to intake schema as `{type: 'string', const: 'proceed with provisioning'}` with `required` gated on `mode==execute`; Step 3 in batch mode logs the value into the L2 resume-body audit field. Path B is safer but reduces batch utility.
  - **verification** — Author a batch intake with `confirmationAcknowledgment: 'proceed with provisioning'`; dispatch; assert L2 audit record for the run contains that verbatim string tagged with operator UPN + batch-file SHA-256 hash.
  - **consequence_if_unfixed** — Batch dispatch either (a) silently hangs at Step 3 with no operator, or (b) worse, if some future maintainer 'fixes' it by auto-supplying the phrase, NFR-11 audit trail is violated — audit shows 'operator confirmed' when no operator did.

- **`EXEC-02` [CRITICAL] [EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.]**: SKILL-03's fix removes the fictional `mode:preflight` field but does not redesign Step 2/3/4 to fit reality
  - **file:where** — .claude/skills/provision-environment/SKILL.md Steps 2→3→4 (~lines 491-720) vs RunsEndpoints.cs:172 (POST /api/runs unconditionally enqueues H0)
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Rewrite Step 2 as 'client-side dry-run using prereqs.yaml + intake schema validation'. Move Step 3 confirmation phrase gate to fire BEFORE the Step 4 POST /api/runs. Delete the notion of a server-side preflight-only mode from the skill. Alternatively, file a spec change for L2 to add real preflight-only semantics with an explicit `Enqueued-Awaiting-Confirm` Cosmos state.
  - **consequence_if_unfixed** — Operator hits `proceed with provisioning` at Step 3 believing they still have an escape hatch — but H1/H2a have already enqueued and (for Model 1 Prod) possibly already spun infra. Any 'abort' at Step 3 is unclean rollback territory (T-series traps). This ships a false confirmation gate to operators and the very NFR-11 audit trail it's meant to strengthen is meaningless.
  - **why_audit_missed** — SKILL-03 stopped at 'remove fictional field' without redesigning the flow the field implied. Placeholder audit swept literal tokens; skill-drift audit swept endpoint shape. No one audited the operator-experience *sequence*.

- **`EXEC-03` [CRITICAL] [EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.]**: Step 3 mandates literal `proceed with provisioning` phrase; bare 'y' insufficient (§4
  - **file:where** — SKILL.md Step 3 + Step 1.0 batch-mode declaration + root CLAUDE.md §6.5 (unstated but binding)
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Reserve batch mode for `--dry-run` / `--to-step 3` (intake validation + preflight only). Refuse `--batch` when combined with an unattended dispatch beyond Step 3. Alternately, sign the intake JSON with the operator's AAD-issued JWT (embed a fresh access-token hash) and require the L2 API to verify at POST /api/runs.
  - **consequence_if_unfixed** — Batch mode either hangs at Step 3 waiting for stdin (per batch-mode audit) OR treats a JSON field as attestation-equivalent (silently defeats NFR-11 audit trail requirement). Neither is acceptable — the user directive 'this process must work' means Step 4 must actually run, but the compliance model forbids the mechanism by which the batch dispatcher would run it.
  - **why_audit_missed** — Batch-mode audit inventoried MISSING intake fields but did not ask 'can any field ever satisfy this gate'. Skill-drift audit saw the phrase requirement but not the batch tension.

- **`ISH-03` [CRITICAL] [intake-schema-vs-handlers]**: Skill Step 2 sends `mode = "preflight"` as a top-level field of the CreateRun body — no `Mode` property exists on `CreateRunRequest`; grep for `"mode"` in Sprk
  - **file:where** — .claude/skills/provision-environment/SKILL.md:497 (Step 2 `mode = "preflight"`) + SKILL.md:721 (Step 4 `POST /api/runs/{id}/resume` with body `{ mode = "execute" }`) vs src/server/services/Sprk.Provisioning.ControlPlane.Api/Api/RunsEndpoints.cs:574-611 (ResumeRun takes NO body — parameters are `(string id, string? customerId, IProvisioningRunRepository, IHandlerEnqueuer, HttpContext, CancellationToken)`)
  - **fix estimate** — 2h (option a) / 1d (option b) · **main-session-only?** yes
  - **proposed_fix** — Option (a) is smaller: (1) remove `mode` from Step 2 body and Step 4 resume body; (2) rewrite Step 3 gate to hold BEFORE the initial POST /api/runs — the confirmation-gate must precede the CreateRun call, not follow a fake preflight-only call; (3) preflight can be run via a separate `POST /api/runs/{id}/preflight` endpoint which already exists and is truly re-runnable. Option (b) is a spec change (add real preflight-only mode to L2) — larger.
  - **verification** — Seam test: POST /api/runs with body `{...mode:'preflight'}` — assert L2 202s (does not 400 on unknown field), assert the run advances through H0.5+ automatically (i.e. proving mode is ignored). Skill dry-run confirms confirmation-gate now precedes any L2 mutation.
  - **consequence_if_unfixed** — Operator believes preflight is 'H0 only' — but L2 actually enqueues H0 and, on H0 success, immediately advances to H1 via the reconciler. The confirmation-gate is a lie: by the time the operator sees 'proceed with provisioning', L2 may already be executing H1-H2a (30-min irreversible Bicep deploy). NFR-11 auditability compromised — the operator's 'yes' does not gate mutation.

- **`HANDLER-11` [MEDIUM] [l2-handlers]**: RunStatus enum defines exactly: NotStarted, Running, WaitingOnGate, Completed, Failed, Cancelled, Quarantined
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Models/ProvisioningRun.cs:212-239 (RunStatus enum) vs task 186 auditor's expected transitional-state list ({WaitingOnGate, Failed-with-Retryable*, Quarantined, Drifted})
  - **fix estimate** — 30 min for triage; 3h if Drifted must be added to code · **main-session-only?** yes
  - **proposed_fix** — Read design.md §4C to determine whether Drifted was ever specified. If NOT: strike `Drifted` from the SKILL.md state-machine references. If YES: add `Drifted` to RunStatus enum + IsTerminalStatus decision + DagAdvancer termination check + Cosmos serialization test.
  - **verification** — Grep design.md + all POMLs for `Drifted` — find canonical source. Update code OR docs to match.
  - **consequence_if_unfixed** — Operator reading the SKILL / audit prompt expects to see runs land in `Drifted` after an upgrade-mode drift detection; the L2 will instead land them in `Failed` with rejectionCode `upgrade-drift-detected`. Diagnostic mismatch between docs and code — low blast radius but noise in live-ceremony triage.

---

### Wave 1: ADR / design pre-work (writes the constraints Waves 2-6 will reference)

**Rationale.** Publish (a) canonical tenantId propagation ADR-note (DEP-01), (b) new Step 2->3->4 confirmation-gate design that squares with L2 unconditional-enqueue (EXEC-02), (c) batch-mode vs mandatory-attestation resolution (EXEC-03/BAT-03), (d) H4Shared/H4b/H9 DAG edge design (EXEC-01/HANDLER-01), (e) sprk_currentrunid terminal-state release contract (EXEC-07/REG-01). These are ADR-note / design-doc writes only; no code edits. Waves 2-6 cite the notes.

**Wave sizing.** 1 findings; ~0.0h known-effort; 1 findings without numeric estimate.

**Main-session-only (1):**

- **`DEP-07` [HIGH] [dependency-and-parallel-safety]**: No unified wave plan exists across the seven audits — remediation risks either serialized-everything (slow) or parallel-everything-collides (broken intermediate states)
  - **file:where** — wave plan aggregate
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Adopt the wave plan above as the remediation-orchestration doc. Attach to projects/customer-provisioning-orchestration-r1/notes/ as the coordinator input for /project-pipeline.
  - **consequence_if_unfixed** — Without an explicit wave plan, the 94 aggregate findings (14+14+16+12+7+19+12) will dispatch in unpredictable order — collisions, re-work, or partial-fix rot.
  - **why_audit_missed** — No individual audit had scope to synthesize the aggregate wave plan.

---

### Wave 2 (B1): Wave 2 / Lane B1: DagAdvancer + HandlerIds registration (subagent-ok)

**Rationale.** Missing H4Shared / H4b keys in HandlerDependencies + missing H9 edges back to H3/H4b. Pure server-code fix; touches only src/server/services/... + tests/. Wave-1 DEP-01 note declares handler-id constants; this wave applies them.

**Wave sizing.** 4 findings; ~1.8h known-effort; 2 findings without numeric estimate.

**Main-session-only (1):**

- **`EXEC-01` [CRITICAL] [EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.]**: HANDLER-01's proposed fix adds HandlerH4Shared={H2a} and HandlerH4b={H4, H4Shared} to HandlerDependencies but leaves HandlerH9's deps as `{HandlerH3}` and HandlerH8's deps as `{HandlerH3}`
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Reconciler/DagAdvancer.cs:139-140 (H9 + H8 deps)
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — In DagAdvancer.HandlerDependencies: `[HandlerH9] = new[] { HandlerH3, HandlerH4b };` and audit whether H8 needs H4Shared. Add DagAdvancerTests coverage asserting H9 does not appear in ready-set until BOTH H3 AND H4b are in CompletedPhases.
  - **consequence_if_unfixed** — First live Model 1 Prod dispatch: H9 zip-deploys BFF the moment H3 finishes; H4b hasn't run yet; BFF boots against half-populated app-settings and fails at /healthz with the same F20 IOptions chain the r1 project exists to eliminate. The dispatch-halt described in task 186 STILL fires — HANDLER-01's fix as written is necessary but not sufficient.
  - **why_audit_missed** — HANDLER-01 focused on the missing KEYS in the map but treated dependency EDGES from H8/H9 back into H4b/H4Shared as out of scope. Audit author saw one half of the graph edit.

**Sub-agent OK (3):**

- **`HANDLER-01` [CRITICAL] [l2-handlers]**: HandlerIds
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Reconciler/DagAdvancer.cs:126-148 (HandlerDependencies) vs Handlers/HandlerIds.cs:144-147 (Dispatchable)
  - **fix estimate** — 45 min · **main-session-only?** no
  - **proposed_fix** — Edit src/server/services/Sprk.Provisioning.ControlPlane.Core/Reconciler/DagAdvancer.cs: (1) add `public const string HandlerH4Shared = HandlerIds.H4Shared;` and `public const string HandlerH4b = HandlerIds.H4b;` next to the existing HandlerH* consts. (2) Add `[HandlerH4Shared] = new[] { HandlerH2a }` and `[HandlerH4b] = new[] { HandlerH4, HandlerH4Shared }` to HandlerDependencies. (3) Change `[HandlerH9] = new[] { HandlerH3 }` to `[HandlerH9] = new[] { HandlerH3, HandlerH4b }` so H9 gates on the batched app-settings landing. (4) Update the ASCII DAG in the file header comment to match. (5) Add regression tests in Tests/Reconciler/DagAdvancerTests.cs for the new shapes (H2a completed → H4Shared ready; H4+H4Shared completed → H4b ready; H3+H4b completed → H9 ready).
  - **verification** — Add DagAdvancerTests: `ComputeReadyHandlers_WithH2aCompleted_IncludesH4Shared`, `ComputeReadyHandlers_WithH4AndH4SharedCompleted_IncludesH4b`, `ComputeReadyHandlers_WithH3ButWithoutH4b_DoesNotIncludeH9`. Run `dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests` — expect the three new tests to fail before the DAG fix and pass after.
  - **consequence_if_unfixed** — First live Model 1 Prod dispatch runs H0→H1→H2a→{H2b|H4|H5}→H3→{H8|H9}→... and skips H4-shared + H4b entirely. H9 zip-deploys the BFF against an EMPTY shared KV and an incomplete app-settings surface; BFF crashes at boot with exit 134 on the F20 SpeAdminModule chain (verified 2026-08-22 in lessons-learned). The DAG will report H9 Failed(Resumable) with no automated path forward — the entire r1 F19/F20 automation is inert. This is the live-ceremony halt described in task 186.

- **`HANDLER-02` [CRITICAL] [l2-handlers]**: Grep for `EnqueueAsync|IHandlerEnqueuer|_enqueuer` in both H4-shared and H4b returns zero hits
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/H4SharedKvSecretsPopulationHandler.cs (real code, no self-enqueue) + src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/H4bBulkAppSettingsHandler.cs (real code, no self-enqueue)
  - **fix estimate** — 0 min — subsumed by HANDLER-01 · **main-session-only?** no · **depends_on** HANDLER-01
  - **proposed_fix** — No handler-body change. Fix is exclusively HANDLER-01 (DagAdvancer).
  - **verification** — After HANDLER-01 fix: run an in-memory dispatch simulation (or observe Task-181 e2e validation runner) and confirm CompletedPhases records both 'H4-shared' and 'H4b' entries between H4 completion and H9 dispatch.
  - **consequence_if_unfixed** — Confirms HANDLER-01 is a genuine halt (not accidentally papered over by a chaining call somewhere else). Absent the DAG fix, no code path in the repository will ever dispatch H4-shared or H4b.

- **`HANDLER-12` [MEDIUM] [l2-handlers]**: DagAdvancerTests header enumerates 15 shape tests, none of which mention H4-shared or H4b (grep returns zero hits)
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Tests/Reconciler/DagAdvancerTests.cs (15 shape tests) — no test for H4Shared or H4b ready-set shape
  - **fix estimate** — 1h · **main-session-only?** no · **depends_on** HANDLER-01
  - **proposed_fix** — Add a parity test: `HandlerDependencies.Keys.Except(EntryPointHandlers).Should().BeEquivalentTo(Dispatchable.Except(EntryPointHandlers))`. Add shape tests HANDLER-01 lists in its verification section.
  - **verification** — New parity test fails before HANDLER-01 fix, passes after. Any future Dispatchable addition without a DAG entry fails the parity test at build time.
  - **consequence_if_unfixed** — Any future handler added to Dispatchable without a DAG entry silently drops from reconciler dispatch — exactly the failure mode task 186 is trying to prevent.

---

### Wave 2 (B2): Wave 2 / Lane B2: RunsEndpoints CreateRunRequest + resume-body + poll-query contract (subagent-ok)

**Rationale.** Widen CreateRunRequest to accept tenantId + confirmation-phrase + upgrade acknowledgement (or standardize nonSecretParameters route per Wave-1 DEP-01 decision). Pair with resume-body accepting operator UPN. This unblocks SKILL-03/04/05/07 in Wave 4.

**Wave sizing.** 4 findings; ~5.0h known-effort; 0 findings without numeric estimate.

**Main-session-only (3):**

- **`ISH-04` [HIGH] [intake-schema-vs-handlers]**: Intake schema defines ONE `region` field intended as an Azure region (examples westus2/westus3/eastus2)
  - **file:where** — intake.schema.json:50-54 (`region` examples: westus2/westus3/eastus2) vs src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/DataverseEnvCreation/H5DataverseEnvCreationHandler.cs:127 (`region` = 'Dataverse region code (e.g. unitedstates)') vs src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BicepInfraDeploy/H2aBicepInfraDeployHandler.cs:120 (`location` = Azure region)
  - **fix estimate** — 45m · **main-session-only?** yes · **depends_on** ISH-01
  - **proposed_fix** — Amend intake.schema.json to split the fields. Add `dataverseGeo: string` (optional; skill can derive from azureRegion via a lookup table in the skill itself — westus2→unitedstates, westeurope→europe, etc.). Rename H5's key to `dataverseRegion` for clarity. Update SKILL.md Step 1 to explain the two orthogonal region concepts.
  - **verification** — 1) ajv validate: intake with only region (no dataverseGeo) passes; skill test asserts Dataverse geo is derived. 2) H5 unit test with region='westus2' → returns InvalidDataverseRegion rejection; with region='unitedstates' → succeeds.
  - **consequence_if_unfixed** — If skill maps intake.region → NonSecret["region"] literally, H5 receives 'westus2' → PPAC API rejects with 'invalid location' when creating Dataverse env → H5 fails Retryable* → operator's only path is to override manually. If skill maps → NonSecret["location"] only, H2a works but H5 gets nothing and defaults incorrectly (or falls back to a hardcoded default).

- **`ISH-05` [HIGH] [intake-schema-vs-handlers]**: Intake schema defines `tier` as cost-envelope tier (shared-trial/smb/enterprise/dedicated) used by H0 preflight cost-envelope check
  - **file:where** — intake.schema.json:60-64 (`tier` enum = ['shared-trial', 'smb', 'enterprise', 'dedicated']) vs src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/DataverseEnvCreation/H5DataverseEnvCreationHandler.cs:129-130 (`tier` = 'env tier (Sandbox / Production / Trial)')
  - **fix estimate** — 45m · **main-session-only?** yes · **depends_on** ISH-01
  - **proposed_fix** — Amend intake.schema.json: rename `tier` → `costTier` (keep current enum). Add derivation in skill Step 1 (or introduce `dataverseTier` field). Update H5 docstring to reference `dataverseTier` key. Add H5 unit test asserting invalid tier value returns rejection code (not silent default).
  - **verification** — 1) H5 unit test: tier='shared-trial' returns InvalidTier rejection; tier='Sandbox' succeeds. 2) Skill dry-run confirms derived dataverseTier matches expected per tenancyModel×profile matrix.
  - **consequence_if_unfixed** — If skill forwards intake.tier='shared-trial' → NonSecret["tier"], H5's PPAC call includes tier='shared-trial' which is not a valid Dataverse tier — PPAC returns 400 or H5 defaults to Sandbox silently, misprovisioning the customer's actual tier.

- **`ISH-06` [HIGH] [intake-schema-vs-handlers]**: Intake schema explicitly makes `environmentId` optional/nullable and documents 'Step 1f auto-creates the placeholder when omitted'
  - **file:where** — intake.schema.json:45-49 (`environmentId` optional, type ['string','null']) vs src/server/services/Sprk.Provisioning.ControlPlane.Api/Api/RunsEndpoints.cs:321-324 (validates non-empty; returns 400 'environmentId is required')
  - **fix estimate** — 30m (option a) / 2h (option b) · **main-session-only?** yes
  - **proposed_fix** — Option (b) is cleanest: extend CreateRun endpoint to auto-create the sprk_dataverseenvironment placeholder when environmentId is null/empty (using IDataverseRegistryClient injected). Option (a) is simpler but pushes coupling to the caller. Document the choice in schema description.
  - **consequence_if_unfixed** — Coordination-fragile: any refactor that skips Step 1f (e.g., new batch mode that doesn't run through Step 1f) causes L2 400 at CreateRun. Failure is loud (400 with detail), not silent, so lower impact than ISH-01/02, but it forecloses batch-mode use cases (like CI pipelines) that don't want to touch Dataverse to create a placeholder first.

**Sub-agent OK (1):**

- **`ISH-01` [CRITICAL] [intake-schema-vs-handlers]**: Skill Step 2 POSTs body `{ customerId, tenantId, environmentId, tenancyModel, profile, mode }` as TOP-LEVEL fields
  - **file:where** — scripts/provisioning-prereqs/intake.schema.json + .claude/skills/provision-environment/SKILL.md:490-508 + src/server/services/Sprk.Provisioning.ControlPlane.Api/Api/RunsEndpoints.cs:861-880
  - **fix estimate** — 1h · **main-session-only?** no
  - **proposed_fix** — Prefer option (b): add `[JsonPropertyName("tenantId")] public string TenantId { get; init; } = string.Empty;` to `CreateRunRequest`; validate non-empty in `CreateRun`; populate `run.Parameters.NonSecret["tenantId"] = request.TenantId;` alongside the existing NonSecretParameters copy loop. Symmetric change: update skill Step 2 body to send tenantId at top-level (already does) — no skill change needed if L2 fix lands. Test: seam test at tests/integration/seam/L2Provisioning/ that POSTs a full CreateRunRequest and asserts NonSecret[tenantId] round-trips.
  - **verification** — New seam test: POST /api/runs with body containing tenantId → GET /api/runs/{id} → assert response.Parameters.NonSecret contains tenantId with the posted value. Existing handler unit tests remain green.
  - **consequence_if_unfixed** — First real dispatch (Step 2 preflight) POSTs to L2 → L2 returns 202 Accepted → H0 dequeues envelope → reads run.Parameters.NonSecret[tenantId] → not present → returns `HandlerResult.Failure(FailureClass.Resumable, "missing-tenant-id", ...)`. Run status flips to Failed within ~15s. Operator sees a rejection code that says 'H0.5 consent-callback must populate this' but for Model 1 there IS no H0.5 — dead loop.

---

### Wave 2 (B3): Wave 2 / Lane B3: Handler bodies + tenancyModel safety (subagent-ok)

**Rationale.** Handler-body defects (silent no-op branches, missing outcome fields, tenancyModel silent-fallback-to-Model2). Independent per-handler edits, fan-out cleanly. HANDLER-14 is explicitly a deferred sweep candidate.

**Wave sizing.** 11 findings; ~26.0h known-effort; 2 findings without numeric estimate.

**Main-session-only (2):**

- **`HANDLER-14` [LOW] [l2-handlers]**: Grep for `az support|support-plan|Microsoft
  - **file:where** — SKILL F8 auto-file support ticket + F9 Support Plan check — not in preflight
  - **fix estimate** — large — defer · **main-session-only?** yes
  - **proposed_fix** — Deferred to R2 per SKILL 'roadmap' — mark this as low priority + tracked in devops-idea. Do not attempt to land in R1 live-ceremony window.
  - **verification** — Not required for R1 close; document deferral in projects/customer-provisioning-orchestration-r1/notes/deferred.md.
  - **consequence_if_unfixed** — Portal auto-denies fresh-sub quota requests; without automation, operator is manually filing tickets or the run just fails. Session 2 saw this — fully manual today.

- **`EXEC-04` [CRITICAL] [EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.]**: `TenancyModel: string
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BicepInfraDeploy/H2aBicepInfraDeployHandler.cs:290
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Two-part fix: (1) H2aBicepInfraDeployHandler must FAIL FAST when run.TenancyModel is blank (`if (string.IsNullOrWhiteSpace(run.TenancyModel)) return HandlerResult.PermanentFailure('MissingTenancyModel')` — no defaulting); (2) intake.schema.json must place tenancyModel as top-level with a strict enum {Model1Shared, Model2Dedicated} and the skill must map it to the top-level CreateRunRequest field, NEVER to nonSecretParameters.
  - **consequence_if_unfixed** — Model 1 Shared trial1 dispatch deploys an entire per-customer Model 2 stack (~$400/mo baseline per NFR-04) instead of reusing the shared trial fabric (~$430/mo TOTAL for many trial customers). Silent cost blow-up + tenancy-model invariant violation, no error surfaced to operator, only detectable at H13 acceptance (E13CostEnvelopeChecker will FAIL far too late in the run).
  - **why_audit_missed** — Intake audit flagged shape drift but stopped at 'field is dropped'. No auditor traced the defaulting behavior into the H2a code path to see what happens WHEN it's dropped.

**Sub-agent OK (9):**

- **`HANDLER-03` [HIGH] [l2-handlers]**: H0 orchestrates 4 quota/readiness probes
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/Preflight/H0PreflightHandler.cs (four probes wired: ArmCognitiveServicesTpmProbe / BapRestEnvironmentRateProbe / ArmComputeVCpuProbe / KeyVaultCertBootstrapProbe) — F1 pin freshness is not among them
  - **fix estimate** — 2h · **main-session-only?** no
  - **proposed_fix** — Add src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/Preflight/ArmOpenAiPinFreshnessProbe.cs (mirrors ArmCognitiveServicesTpmProbe.cs shape); wire it into H0PreflightHandler's probe list; add rejection code `quota-openai-pin-stale` to BuildRejectionCode; unit-test with a fake Azure.ResourceManager.CognitiveServices client returning a mix of GA + Deprecating models.
  - **verification** — Unit test: given probe returning one Deprecating pin, H0PreflightHandler returns HandlerResult.Failure(Resumable, 'quota-openai-pin-stale', ...). Live: run against a sub with a known-Deprecating pin in bicepparam and confirm H0 fails within 30s (not 20min into H2a).
  - **consequence_if_unfixed** — First provisioning run using a stale pin (openai models deprecate every 4-6 months per user's MEMORY.md) will fail deep inside H2a's Bicep deploy with ServiceModelDeprecated, wasting the 20-30 min H2a window and requiring manual pin bump + full re-run.

- **`HANDLER-04` [HIGH] [l2-handlers]**: H1 ArmSubscriptionReadinessProbe verifies exactly ONE provider registration: Microsoft
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SubscriptionReadiness/ArmSubscriptionReadinessProbe.cs (only checks Microsoft.ManagedServices registration state)
  - **fix estimate** — 3h · **main-session-only?** no
  - **proposed_fix** — Extend ArmSubscriptionReadinessProbe to accept a required-providers list from BicepInfraDeployOptions (canonical list of ~10 RPs derived from the Bicep templates). Add a `RegisterAndPollAsync` helper that POSTs `/providers/{ns}/register?api-version=2022-09-01` and polls `GET /providers/{ns}` until `registrationState == Registered` or 5min timeout. Return `provider-registration-failed` rejection code on timeout.
  - **verification** — Unit test with mock ArmClient returning NotRegistered → probe issues register call → polls → passes. Integration: run against a subscription with Microsoft.Cache un-registered and confirm H1 registers it before H2a starts.
  - **consequence_if_unfixed** — F6 verbatim: `az provider register` reports success but state stays NotRegistered on fresh subs → H2a fails 90-120s into `az deployment sub create` with `MissingSubscriptionRegistration` for a random RP, wasting the deployment attempt.

- **`HANDLER-05` [HIGH] [l2-handlers]**: H2a runs an ARM What-If diff before deploy (drift detection) but does NOT call the check-name-availability endpoints for globally-namespaced resources (Storage account, KeyVault, App Service, Service Bus, Redis, Cosmos, ...
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BicepInfraDeploy/ArmWhatIfDriftDetector.cs + BicepInfraDeployOptions.cs — no `check-name` precondition anywhere in the H2a pipeline
  - **fix estimate** — 3h · **main-session-only?** no
  - **proposed_fix** — Add src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BicepInfraDeploy/IResourceNameAvailabilityProbe.cs + ArmResourceNameAvailabilityProbe.cs. Wire into H2aBicepInfraDeployHandler after inspector but before runner. Precompute the check list from FileBicepTemplateInspector output.
  - **verification** — Unit test with a fake ArmClient returning 'unavailable' for a chosen storage-account name → H2a returns Failure(Resumable, 'resource-name-taken', ...) in <10s. Live: run against a sub where `sprk-{env}-sb` is pre-existing.
  - **consequence_if_unfixed** — F10 verbatim: burned 16m35s on the Session 2 first deploy because a Service Bus `-sb` suffix was already reserved globally. Every fresh-sub first run will lose a full H2a window (~20min) per conflicting resource until the operator manually resolves.

- **`HANDLER-06` [HIGH] [l2-handlers]**: Grep for `RequestConflict|CogSvc|soft-lock|linear backoff` in Handlers/BicepInfraDeploy returns zero hits
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BicepInfraDeploy/ArmDeploymentRunner.cs (F11 CogSvc RequestConflict soft-lock retry-with-backoff not in code)
  - **fix estimate** — 2h · **main-session-only?** no
  - **proposed_fix** — Wrap the CognitiveServices-scoped ARM writes in ArmDeploymentRunner with a retry policy: on ResponseException with StatusCode == 409 && ErrorCode contains 'RequestConflict', await [30,90,180,300] seconds and retry (max 3 attempts). Log each retry with the backoff duration.
  - **verification** — Unit test with a fake pipeline that returns 409 RequestConflict twice then 200 → runner succeeds after ~2min real time; test with 4 successive 409 → runner returns Failure(Resumable, 'cogsvc-soft-lock-persistent', ...).
  - **consequence_if_unfixed** — Session 2 burned 3 failed retries; only an explicit 3-min `sleep 180` broke through. Live-ceremony will hit this on any account that was recently used or partially failed.

- **`HANDLER-07` [HIGH] [l2-handlers]**: Grep in H6 for `Required Applications|PowerBI_Anchor|pac application install` returns zero hits
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SolutionImport/H6SolutionImportHandler.cs — no Required-Applications gate for msft_PowerBI_Anchor
  - **fix estimate** — 4h · **main-session-only?** no
  - **proposed_fix** — New seam IRequiredApplicationsInstaller with PacRequiredApplicationsInstaller impl (shells `pac application install`); manifest file `scripts/canonical-solutions/required-applications.yaml`; H6 invokes it after H5 handoff and before CanonicalSolutionCatalog resolve.
  - **verification** — Integration test with a fresh env that lacks msft_PowerBI_Anchor → H6 installs it (returns Success after ~6min poll) → then proceeds to solution imports without MissingDependency.
  - **consequence_if_unfixed** — F13 verbatim: fresh Production-tier envs do NOT include Power BI Extensions; SpaarkeMaster env-var carries a spurious dep on powerbimashupparameter → import fails 5min in with 1 unresolved MissingDependency. Every fresh Model 1 Prod run will hit this and waste an H6 attempt.

- **`HANDLER-08` [HIGH] [l2-handlers]**: Grep in H6 for `maxuploadfilesize|Org Settings|pac org update-settings|F14` returns zero hits
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SolutionImport/H6SolutionImportHandler.cs — no Org Settings Contract application
  - **fix estimate** — 2h · **main-session-only?** no
  - **proposed_fix** — New seam IOrgSettingsContractApplier with PacOrgSettingsContractApplier impl; config file scripts/canonical-solutions/org-settings-contract.yaml; H6 invokes it in the same pre-import step alongside HANDLER-07.
  - **verification** — Integration test: fresh env at maxuploadfilesize=5242880 → H6 pre-check bumps to 25_600_000 in one call → solution imports succeed.
  - **consequence_if_unfixed** — F14 verbatim: fresh Production-tier envs default maxuploadfilesize=5MB → UniversalDocumentUpload PCF bundle exceeds this → import fails 5min in with 'Webresource content size is too big'. Fresh Model 1 Prod run will hit this and lose an H6 attempt.

- **`HANDLER-09` [HIGH] [l2-handlers]**: Grep for `operator|SignedInUser|Secrets Officer|b86a8fe4-44ce-4948-aee5-eccb2c155cd7|role assignment
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/H4KvSecretsPopulationHandler.cs AND H4SharedKvSecretsPopulationHandler.cs — no operator-KV-RBAC-bootstrap step
  - **fix estimate** — 4h · **main-session-only?** no
  - **proposed_fix** — Add src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/IOperatorKvRbacBootstrapper.cs + ArmOperatorKvRbacBootstrapper.cs (uses raw `az rest --method put` per F15b). Wire into H4KvSecretsPopulationHandler AND H4SharedKvSecretsPopulationHandler.HandleAsync as step (1) before parameter guards.
  - **verification** — Unit test with a fake ArmClient returning 403 initially then 200 after role grant → handler bootstrap-grants + polls → proceeds to secret writes. Live: run against a fresh RBAC-enabled KV where operator is Owner but has no data-plane role.
  - **consequence_if_unfixed** — F15 + F18 verbatim: fresh RBAC-enabled KVs grant NO data-plane access even to subscription Owner. Both H4-per-tenant AND H4-shared will hit 403 on the very first `SecretClient.SetSecretAsync` call and fail Resumable. Session 2 manually granted this for BOTH KVs; automation would eliminate the operator step entirely.

- **`HANDLER-10` [HIGH] [l2-handlers]**: Grep for `SystemAssigned
  - **file:where** — F16 + F16.5 kvRefIdentity — H2a Bicep template inspector doesn't reject SystemAssigned-with-only-UAMI, and H4's kvRefIdentity T1 patcher may still hit the CLI Bad Request bug
  - **fix estimate** — 3h · **main-session-only?** no
  - **proposed_fix** — (a) Extend FileBicepTemplateInspector rule set with `kvRefIdentityInvalidDetector`; add BicepDeployRejectionCodes.KvRefIdentityInvalid. (b) Read ArmAppServiceIdentityPatcher.cs — if it currently shells `az webapp update --set`, replace with `ArmClient` SDK PATCH or raw `az rest --method patch` per F16.5. Add unit tests for both.
  - **verification** — Bicep template with `keyVaultReferenceIdentity: 'SystemAssigned'` + only UAMI attached → H2a inspect fails fast Resumable with clear error. H4 T1 patch on a site where CLI --set returns Bad Request → PATCH fallback succeeds.
  - **consequence_if_unfixed** — F16 verbatim: shared BFF App Service kvRefIdentity may still be literal 'SystemAssigned' from a copy-forward Bicep; ALL @Microsoft.KeyVault(...) refs silently unresolvable at runtime. F16.5 bypass may not be in code, so H4's T1 patch fails with `Bad Request` if the CLI is invoked.

- **`HANDLER-13` [MEDIUM] [l2-handlers]**: Grep in Handlers/Preflight + Handlers/BicepInfraDeploy for `az cognitiveservices usage list|GlobalStandard|auto-granted|deployment-set|recompose|F5` returns zero hits
  - **file:where** — SKILL F5 (auto-quota TPM detection → deployment-set auto-recomposition) — not in H0 or H2a
  - **fix estimate** — 3h · **main-session-only?** no
  - **proposed_fix** — Extend BicepInfraDeployOptions with an `openaiDeploymentSetPolicy: 'strict' | 'auto-recompose'` field. When 'auto-recompose', H2a's bicepparam generator reads auto-granted TPM per model and drops non-granted models from the deployment set (with an operator-visible warning in Cosmos.notes).
  - **verification** — Test with a mock CognitiveServices usage response showing 0 TPM for gpt-5.4 and 500 TPM for gpt-5-mini → H2a's bicepparam contains only gpt-5-mini + logs the downgrade.
  - **consequence_if_unfixed** — F5 verbatim: fresh subs auto-grant mini/embedding TPM generously but frontier tiers (gpt-5.4, gpt-5-pro) = 0. Session 2 manually recomposed the deployment set. Every fresh-sub first run into a region without pre-granted frontier TPM will fail H2a's openai deployment step.

---

### Wave 2 (B4): Wave 2 / Lane B4: sprk_dataverseenvironment write-path expansion (subagent-ok)

**Rationale.** Nine currently-un-written columns + terminal-state currentrunid release + ClearQuarantine cascade. Pure code edits in Sprk.Provisioning.ControlPlane.Core + a new registry-client method. REG-04 is main-only (touches skill), split into Wave 4.

**Wave sizing.** 8 findings; ~13.5h known-effort; 2 findings without numeric estimate.

**Main-session-only (2):**

- **`REG-04` [HIGH] [registry-write-path]**: SKILL
  - **file:where** — .claude/skills/provision-environment/SKILL.md:840-862 (§Step 6a) + DataverseRegistrySetupStatusUpdater.cs:140-149
  - **fix estimate** — 1h · **main-session-only?** yes
  - **proposed_fix** — Rewrite Step 6a to: (1) state honestly 'server writes sprk_setupstatus + sprk_currentrunid; operator MUST write sprk_provisionedon, sprk_bffversion, sprk_solutionversion (H0 upgrade-mode depends on the first)'; (2) show the two-step MCP flow explicitly — first `mcp__dataverse__read_query` with `sprk_dataverseenvironments?$filter=sprk_customerid eq '{cid}'&$select=sprk_dataverseenvironmentid&$top=1` then extract `sprk_dataverseenvironmentid` then call `update_record`; (3) make Step 6a HARD-STOP on any failure (operator retry required, cannot skip).
  - **verification** — Operator does clean provisioning run, checks Dataverse row via `pac data list` afterward, and each of the 3 fields is populated.
  - **consequence_if_unfixed** — Operator reads 'in practice server-side' and skips Step 6a (thinking it's redundant). sprk_provisionedon / sprk_bffversion / sprk_solutionversion stay null. H0 upgrade-mode detection on next run fails (see REG-01 consequence). Additionally, operator who does execute Step 6a hits MCP error 'recordId is not a valid GUID' because the placeholder syntax `{resolved from customerId via sprk_customerid alt-key}` isn't a real API call.

- **`EXEC-07` [HIGH] [EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.]**: REGISTRY audit noted ClearQuarantine doesn't release sprk_currentrunid
  - **file:where** — sprk_dataverseenvironment write path — no code path releases sprk_currentrunid on RunStatus.Failed transition
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Add sprk_currentrunid = null write to the CompletedPhases-terminal-transition path in StateReconcilerService (or wherever RunStatus is written to Cosmos on terminal). Add integration test asserting a second POST /api/runs after a Failed run succeeds with a fresh runId.
  - **consequence_if_unfixed** — A single Failed run permanently poisons the customerId — POST /api/runs returns 409 forever until an operator manually edits sprk_dataverseenvironment via `pac data` or Dataverse portal. This is a real-world halt on Session-13's 'downstream writes will flow cleanly' promise the moment the first Failed run happens (statistically inevitable during F19/F20 hardening).
  - **why_audit_missed** — REGISTRY audit found the ClearQuarantine gap but stopped there; did not trace the invariant to all four terminal states.

**Sub-agent OK (6):**

- **`REG-01` [CRITICAL] [registry-write-path]**: The ONLY server-side write to sprk_dataverseenvironment is DataverseRegistrySetupStatusUpdater
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/DataverseRegistrySetupStatusUpdater.cs:143-149 + IDataverseEnvironmentRegistryClient.cs:100-102
  - **fix estimate** — large — needs decomposition (Path A: 1h skill edit; Path B: 8-12h code + tests + Bicep param + secret-free auth verification) · **main-session-only?** no
  - **proposed_fix** — Path A (thin, matches current architecture): keep server-side to just Setup Status + CurrentRunId; move Session-13 SKILL.md wording from 'populated by later handlers' to 'populated by operator via Step 6a MCP write' + spell out the record lookup (`GET .../sprk_dataverseenvironments?$filter=sprk_customerid eq '{cid}'&$select=sprk_dataverseenvironmentid`) BEFORE the update call; make Step 6a HARD-STOP (not belt-and-suspenders). Path B (server-side): extend `IDataverseEnvironmentRegistryClient` with a generic `UpdateColumnsAsync(envId, IReadOnlyDictionary<string,object?>)` and add a new H13 sub-step that PATCHes {sprk_provisionedon: run.CompletedOn, sprk_bffversion: run.InterStepState.BffVersion, sprk_solutionversion: run.InterStepState.SolutionVersion, sprk_azuresubscriptionid: from run params, sprk_resourcegroupname, sprk_appservicename, sprk_keyvaultname, sprk_containertypeid, sprk_ClientCacheBustToken} in a single request BEFORE the Ready transition (so a failed columns-PATCH keeps status=InProgress rather than silently marking Ready with stale mirror data).
  - **verification** — Live-invocation seam test on canary row: run POST /api/runs → wait for H13 Success → GET the row via Web API and assert all promoted columns are non-null / non-placeholder. Alternatively, extend `DataverseEnvironmentRegistryClientTests` with a shape-assertion on the PATCH body verifying it carries the full column set.
  - **consequence_if_unfixed** — The registry `sprk_dataverseenvironment` row for a successfully-provisioned customer will reflect ONLY sprk_setupstatus=Ready + placeholder values from Step 1f (sprk_name=customerId, sprk_dataverseurl=https://placeholder-{customerId}.crm.dynamics.com, etc.). Downstream consumers reading the registry (H0 upgrade-mode detection via `provisionedOn` parameter mirror; portfolio dashboards; customer-support tools) will see stale/placeholder data. Most severely: H0's upgrade-mode branch relies on `sprk_provisionedon IS NOT NULL` semantics (mirrored into run params), so on the SECOND run for the same customer, upgrade mode NEVER triggers — H0 always runs as fresh-provision, breaking §14A upgrade model.

- **`REG-02` [CRITICAL] [registry-write-path]**: DataverseRegistryConcurrencyStore uses `new ClientSecretCredential(_options
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Concurrency/CustomerRunGuardOptions.cs:60-66 + DataverseRegistryConcurrencyStore.cs:294-302 + infrastructure/bicep/modules/controlplane-worker-app-service.bicep:398-403
  - **fix estimate** — 4-6h (mechanical migration; test coverage exists for the pattern via DataverseEnvironmentRegistryClientTests) · **main-session-only?** no
  - **proposed_fix** — Migrate DataverseRegistryConcurrencyStore to Path X: copy the DataverseEnvironmentRegistryClient.AcquireTokenAsync shape verbatim (DefaultAzureCredential + ManagedIdentityClientId, scope={adminEnvUrl}/.default) — that path is proven live per its own live-seam test. Delete ClientId/ClientSecret/TenantId from CustomerRunGuardOptions (they become dead code in the secret-free future). Prerequisite: L2 UAMI must already be registered as a Dataverse Application User on the admin env — task 111's Grant-ControlPlaneIdentity.ps1 already does this for DataverseEnvironmentRegistryClient, so no new grant needed. Then flip Bicep customerRunGuardEnabled=true unconditionally.
  - **verification** — Boot Worker with requireSecretFreeIdentity=true + CustomerRunGuard__Enabled=true → no ClientSecret configured → app starts (no Validate() throw). Live seam test: POST /api/runs twice concurrently for same customerId → assert second returns 409 with correct WinningRunId.
  - **consequence_if_unfixed** — In production (secret-free), the entire I5 concurrency guard is a no-op. Two simultaneous POST /api/runs for the same customerId both succeed → two runs execute in parallel against the same customer's Azure resources → race conditions in H2a Bicep deploy (both trying to acquire same resource group), H4/H4b KV writes (racing secret population), H5 Dataverse env creation (creates two envs), H10 App User creation (duplicate systemuser rows). This is the CATASTROPHIC failure §4D I5 exists to prevent.

- **`REG-03` [HIGH] [registry-write-path]**: ClearQuarantine calls `clearService
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Api/Api/RunsEndpoints.cs:690-786 (ClearQuarantine handler)
  - **fix estimate** — 30 min · **main-session-only?** no
  - **proposed_fix** — In RunsEndpoints.ClearQuarantine after `case QuarantineClearResult.Success:`, add: `var release = await runGuard.ReleaseAsync(customerId!, id, cancellationToken); if (release is ReleaseResult.TransientFailure txf) { logger.LogWarning(...); }` (verbatim mirror of CancelRun lines 664-675). Note that ReleaseAsync's stale-value guard (only clears when current value matches this runId) means the release is safe against races.
  - **verification** — Integration test: quarantine a run, POST clear-quarantine, then POST a fresh /api/runs for the same customer — assert 202 (not 409).
  - **consequence_if_unfixed** — After operator clears quarantine on customer C, the next POST /api/runs for C reads sprk_currentrunid = {old-quarantined-runId}. CustomerRunGuard finds a different runId → returns Conflict. But the winning run's Cosmos status is now Failed (not Quarantined), so DetermineConflictReasonAsync returns AlreadyInFlight (fallback). The operator's fresh run 409s indefinitely. Recovery requires manual Web API PATCH to clear sprk_currentrunid — but the operator has no tooling for that path documented in the skill.

- **`REG-05` [MEDIUM] [registry-write-path]**: TWO independent configuration sections both point at what MUST be the same admin Dataverse env: CustomerRunGuard:TargetDataverseUrl (writes sprk_currentrunid) and DataverseEnvironmentRegistry:AdminEnvironmentUrl (writes ...
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Concurrency/CustomerRunGuardOptions.cs:46 (TargetDataverseUrl) vs Registry/DataverseEnvironmentRegistryOptions.cs:59 (AdminEnvironmentUrl)
  - **fix estimate** — 1h standalone (or absorbed by REG-02's Path X migration) · **main-session-only?** no
  - **proposed_fix** — In DataverseEnvironmentRegistryModule.PostConfigure OR CustomerRunGuardModule.PostConfigure, add a cross-check: `if (customerRunGuardOptions.Enabled && !string.Equals(new Uri(customerRunGuardOptions.TargetDataverseUrl).Host, new Uri(registryOptions.AdminEnvironmentUrl).Host, StringComparison.OrdinalIgnoreCase)) throw`. Better: consolidate as part of REG-02 fix (Path X migration collapses CustomerRunGuard's DV URL onto DataverseEnvironmentRegistry:AdminEnvironmentUrl).
  - **verification** — Boot-time test setting mismatched URLs → assert InvalidOperationException naming both settings.
  - **consequence_if_unfixed** — If an operator or a future Bicep edit points the two settings at different envs, CustomerRunGuard writes sprk_currentrunid to env A while DataverseRegistrySetupStatusUpdater clears it from env B → the row in env A stays locked forever → next POST /api/runs 409s indefinitely. The failure is silent (no error, both PATCHes return 2xx against their own env).

- **`REG-06` [MEDIUM] [registry-write-path]**: The column name is authored with MIXED CASE `sprk_ClientCacheBustToken` (capital C's) as a string literal in the AllColumns array and in MapFromJson's `TryGetProperty("sprk_ClientCacheBustToken", 
  - **file:where** — src/server/api/Sprk.Bff.Api/Services/Registration/DataverseEnvironmentRecord.cs:149 + :217
  - **fix estimate** — 1h (includes schema query) · **main-session-only?** no
  - **proposed_fix** — Query the actual deployed schema on spaarkedev1 (Dataverse MCP describe or `az rest`), then edit `DataverseEnvironmentRecord.cs:149` and `:217` and `SKILL.md` §Step 1f + §Step 6a to a single canonical casing. Add an ArchTest asserting all AllColumns entries match `[a-z0-9_]+` if the deployed name is lowercase.
  - **verification** — Live read of a populated row via GET .../sprk_dataverseenvironments({id})?$select=sprk_clientcachebusttoken (or Cased variant) returns the expected value.
  - **consequence_if_unfixed** — sprk_ClientCacheBustToken always reads back as null even when populated → clients never invalidate cached bundles on upgrade → stale-cache bugs post-deployment (users see old bundle until 60min localStorage TTL expires). Also risks 400 on the eventual write (REG-01 fix) if the write path uses one casing and the schema was deployed with the other.

- **`REG-07` [MEDIUM] [registry-write-path]**: POST /api/runs accepts `environmentId` as a required string and validates only IsNullOrWhiteSpace
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Api/Api/RunsEndpoints.cs:321-324 (CreateRun) + Api/Models/CreateRunRequest
  - **fix estimate** — 3-4h (new registry client method + endpoint plumbing + tests) · **main-session-only?** no
  - **proposed_fix** — In CreateRun, after the concurrency-guard acquire and before Cosmos write, call `IDataverseEnvironmentRegistryClient.LookupByEnvironmentIdAsync` (new method — mirror of LookupByTenantIdAsync but keyed on sprk_dataverseenvironmentid) and assert: row exists, sprk_customerid matches request.CustomerId, sprk_setupstatus == InProgress. Return 400 with a targeted diagnostic on any mismatch. Bonus: also cross-check sprk_tenantid matches request.TenantId (if we add tenantId to CreateRunRequest — currently absent, per REG-08 followup).
  - **verification** — Endpoint tests covering: (a) unknown environmentId → 400; (b) customerId mismatch → 400; (c) setupstatus=Ready → 400 (row is already finalized); (d) happy path → 202.
  - **consequence_if_unfixed** — Operator supplies wrong environmentId (typo, or points at another customer's row, or Step 1f partially failed and returned a stale GUID). H1-H12 run to completion. H13 PATCHes the wrong row (or 404s). Successful run's registry state lands on some OTHER customer's row (cross-customer bleed — a §4D I1 tenant-isolation invariant violation). If 404, the run marks Resumable (per H13Rejections.RegistryUpdateFailed) but there's no recovery path because the wrong environmentId is baked into Cosmos.

---

### Wave 3: prereqs pipeline hardening (subagent for prereqs.yaml edits + main-session for SKILL-08 resolver extension)

**Rationale.** Recipe-contract + placeholder + bash-var-expansion defects. DEP-03 mandates single sub-plan owning: SKILL-08 resolver extension + all PRQ-xx + all PLX-04..07 prereqs.yaml placeholders. Land before Wave 4 so SKILL.md rewrite can trust the recipe surface.

**Wave sizing.** 18 findings; ~15.6h known-effort; 3 findings without numeric estimate.

**Main-session-only (18):**

- **`SKILL-08` [CRITICAL] [skill-drift-audit]**: Substitution loop covers ONLY `{env}` and `{openAiRegion}`
  - **file:where** — .claude/skills/provision-environment/SKILL.md:206-208 (Step 0.5b substitution block)
  - **fix estimate** — 1h (build the placeholder resolver + wire config source lookup) · **main-session-only?** yes
  - **proposed_fix** — Extend the substitution block with all 17 remaining placeholders, sourced from: (a) Step 0 context (subId from `az account show`; operator identity), (b) config/environments.json per-env stanza (adminDvUrl, sbNamespace, dvUrl, containerTypeId, l2UamiPrincipalId, l2UamiClientId, l2UamiSpId, graphAppId, bffAppId, artifactsStorageId, acrId, bffAppServiceId, kvResourceId, region), (c) intake (customerId — only when scope reaches once_per_customer, not at Step 0.5). Alternatively, restructure prereqs.yaml so recipes take zero placeholders (each carries the substitution list explicitly with lookup rules), and refactor Step 0.5 to enumerate the yaml's substitution manifest.
  - **verification** — Run Step 0.5 with dev env — every once_per_tenant + once_per_subscription + once_per_env prereq recipe executes to completion (exit 0 or explicit exit 1); no `az: unrecognized argument '{...}'` errors.
  - **consequence_if_unfixed** — Recipes for PRQ-S-04 (`{l2UamiPrincipalId} {subId}`), PRQ-S-05, PRQ-E-06 through PRQ-E-13 (subscription/env-scoped) receive literal '{l2UamiPrincipalId}' — az CLI parse fails, recipe exits non-zero, Step 0.5 HARD STOPs the operator with an unactionable error. This aborts EVERY provisioning run at Step 0.5, permanently.

- **`PRQ-01` [CRITICAL] [prereqs-yaml-recipe-contract-plus-placeholder]**: Recipe passes `--headers "Authorization=Bearer $graphAppOnlyToken"` inside `bash -c`
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml PRQ-T-01 line 58 + PRQ-T-02 line 73
  - **fix estimate** — 45 min for both recipes + PRQ-C-05 companion fix · **main-session-only?** yes · **depends_on** PRQ-04
  - **proposed_fix** — Rewrite T-01/T-02 recipes to acquire the token in-line: `token=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv); [ -z "$token" ] && exit 1; result=$(az rest --method get --uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes/{containerTypeId}" --headers "Authorization=Bearer $token" 2>/dev/null); echo "$result" | grep -q "{containerTypeId}" || exit 1`. Also fix PRQ-C-05's `$customerTenantToken` (task 207 also missed this — same class).
  - **verification** — In `bash -c`, run the rewritten recipe with a valid Spaarke tenant login; verify exit 0 when container-type exists, exit 1 when GUID substituted is bogus.
  - **consequence_if_unfixed** — PRQ-T-01 and PRQ-T-02 (both critical SPE container-type prereqs) fail 100% at operator invocation with an opaque 401. Operator interprets as prereq failure, blocks provisioning, has no self-service path. Same failure mode as SESSION 14's PRQ-E-07 `$filter` bug — task 207 caught ONE instance and missed the other two.

- **`PRQ-02` [HIGH] [prereqs-yaml-recipe-contract-plus-placeholder]**: Recipe passes `--headers "Authorization=Bearer $customerTenantToken"` to check whether the customer tenant has admin-consented the BFF multitenant app
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml PRQ-C-05 line 458-463
  - **fix estimate** — 30 min (delete + document deferral in expect field) · **main-session-only?** yes
  - **proposed_fix** — Either mark PRQ-C-05 as SERVER-SIDE-ONLY (H0.5 handler asserts consent via its own multitenant app-only token) and remove the operator-side check_recipe, OR extend Step 0.5b to accept `--customer-tenant-token` intake field for Model 2 batch runs. Recommend option A for MVP — matches skill's existing H0.5 consent-callback flow.
  - **verification** — Run Step 0.5 iteration for a Model 2 customer without operator-side customer tenant credentials; verify PRQ-C-05 is skipped (not silently failing).
  - **consequence_if_unfixed** — PRQ-C-05 is 100% broken for Model 2 (the ONLY case where it applies). Failure is silent — empty header → 401 → `az rest` exits non-zero → recipe fails at operator invocation with no explanation. Operator has no path to satisfy the check.

- **`PRQ-03` [CRITICAL] [prereqs-yaml-recipe-contract-plus-placeholder]**: Recipe loops over 6 services (openai/docintel/search/servicebus/storage/redis) and dumps `roleDefinitionName` for each
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml PRQ-E-06 lines 271-277
  - **fix estimate** — 2h for E-06 rewrite + 30 min each for E-11, C-01, C-04, C-06 = ~4h added to task 206's estimate · **main-session-only?** yes
  - **proposed_fix** — Amend task 206 acceptance criteria to require the assertion RECOMPUTED against `expect:` semantics, not just an exit-1 guard added. Recipe needs the per-service role map inline. Same class applies to PRQ-E-11 (AND condition between Sender+Receiver), PRQ-C-01 (numeric limit>=current+load), PRQ-C-04 (used<limit), PRQ-C-06 (>=25600000), PRQ-C-02 (multi-pin iteration — recipe uses one `{pinnedVer}` placeholder but there are 3 pins).
  - **verification** — Synthesize a role-assignment set with 5 of 6 correct + 1 wrong role on wrong service; verify recipe exits 1 identifying the missing one.
  - **consequence_if_unfixed** — Task 206 lands, adds `[ -n "$result" ] || exit 1`, marks PRQ-E-06 compliant — but F19 (KV extract from source services) still silent-fails because ANY role assignment (even irrelevant ones) satisfies the guard. The exact failure mode the SESSION 14 audit flagged as CRITICAL survives task 206.

- **`PRQ-04` [CRITICAL] [prereqs-yaml-recipe-contract-plus-placeholder]**: Recipe loops over 12 required RP namespaces and echoes `$ns=<state>` for each
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml PRQ-S-03 lines 168-174
  - **fix estimate** — 20 min · **main-session-only?** yes
  - **proposed_fix** — Rewrite loop with `failed=0` flag pattern. Same class applies to PRQ-E-06 loop and PRQ-C-02 (which needs to iterate over 3 pins, not use one `{pinnedVer}` placeholder).
  - **verification** — Set one namespace to unregister state on a canary sub; verify recipe exits 1 identifying that namespace.
  - **consequence_if_unfixed** — Fresh-sub F6 (`az deployment sub create` fails on unregistered provider) still catches operators mid-deploy — the prereq that exists specifically to catch it silent-passes. Task 206 lists this fix but framed as "add exit 1" which understates the required loop restructure.

- **`PRQ-05` [HIGH] [prereqs-yaml-recipe-contract-plus-placeholder]**: Declared `scope: once_per_env` (line 359) but recipe references `{customerId}` (line 365) — a value only known after Step 1 intake
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml PRQ-E-13 lines 358-368
  - **fix estimate** — 15 min · **main-session-only?** yes
  - **proposed_fix** — In prereqs.yaml: delete PRQ-E-13. In SKILL.md Step 1f: add explicit post-create verification of the GUID (already partially present at line 439). Update audit note to reflect removal.
  - **verification** — prereqs.yaml no longer contains PRQ-E-13; Step 1f explicitly verifies GUID after placeholder create; validate.ps1 still PASSES.
  - **consequence_if_unfixed** — Either the prereq perpetually fails at operator invocation (broken from day 1), or task 207 introduces a new post-intake iteration phase in the skill (substantial complexity for one prereq). Deleting the prereq is simpler.

- **`PRQ-06` [HIGH] [prereqs-yaml-recipe-contract-plus-placeholder]**: Defense-in-depth check uses `$expected -match "``([^``]+)``"` — captures ONLY the first backtick-quoted token from expect, then checks output contains it via `-notmatch [regex]::Escape($Matches[1])`
  - **file:where** — .claude/skills/provision-environment/SKILL.md Step 0.5b lines 234-240
  - **fix estimate** — 30 min for classifier iteration fix; 2h for full-deletion + recipe pushback path · **main-session-only?** yes · **depends_on** PRQ-03
  - **proposed_fix** — Amend Step 0.5b to iterate all backtick pairs: `foreach ($m in ([regex]::Matches($expected,'`([^`]+)`'))) { if ($output -notmatch [regex]::Escape($m.Groups[1].Value)) { $passed=$false; ... } }`. But this INCREASES false-fails on prose-literal expects like `>= 25600000`. Better: DELETE defense-in-depth entirely and push assertion semantics into recipes (PRQ-03). Belt-and-braces was well-intentioned but the belt is broken and the braces make it worse.
  - **verification** — Craft a synthetic recipe output that contains "Sender" but not "Receiver"; verify PRQ-E-11 exits with fail state.
  - **consequence_if_unfixed** — Even with task 206's exit-1 guards, several silent-PASS classes persist because defense-in-depth is broken. The classifier gives false confidence — operators trust the PASS signal that is grammatically wrong.

- **`PRQ-07` [MEDIUM] [prereqs-yaml-recipe-contract-plus-placeholder]**: PRQ-E-08 uses `pac org list-users --environment 
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml PRQ-E-08 line 301 + PRQ-C-04 line 448
  - **fix estimate** — 45 min · **main-session-only?** yes
  - **proposed_fix** — Add a task-206 sub-step: for each `pac` invocation in prereqs.yaml, run `pac --help` at the current CLI version and verify subcommand exists. Replace non-existent subcommands with `az rest` calls to Dataverse Web API endpoints (documented in `docs/architecture/` Dataverse patterns).
  - **verification** — Run each pac invocation in-line against a canary env; verify exit 0 + expected shape output.
  - **consequence_if_unfixed** — Recipes error at execution with `pac: 'list-users' is not a command`, producing an exit-1 that operator MIGHT read as prereq-fail rather than tool-drift. Adds noise; erodes trust in Step 0.5 signal.

- **`PRQ-08` [MEDIUM] [prereqs-yaml-recipe-contract-plus-placeholder]**: PRQ-T-01 expect: `HTTP 200 with matching containerTypeId in body`
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml PRQ-T-01 line 59, PRQ-T-02 line 74
  - **fix estimate** — 20 min · **main-session-only?** yes
  - **proposed_fix** — T-01: append `| jq -e '.id == "{containerTypeId}"' >/dev/null || exit 1`. T-02: change query to filter for the specific app-reg + assert non-empty result.
  - **verification** — Manually invoke against a canary tenant with wrong containerType ID; verify exit 1.
  - **consequence_if_unfixed** — T-01/T-02 silent-pass on unrelated tenant state (someone else's container type, wrong app grants). Not the most likely failure mode but the prereq name implies a stronger check than the recipe delivers.

- **`PRQ-09` [MEDIUM] [prereqs-yaml-recipe-contract-plus-placeholder]**: Recipe: `az rest --method get --url "
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml PRQ-S-02 lines 156-158
  - **fix estimate** — 30 min · **main-session-only?** yes
  - **proposed_fix** — Rewrite PRQ-S-02 to distinguish 200 / 403 / other. Add to task 206 scope as separate work item.
  - **verification** — Simulate a 401 (bad token) — verify recipe distinguishes from 403 (no plan).
  - **consequence_if_unfixed** — Network glitches / auth expiry / transient 500s all masquerade as "no support plan" during Step 0.5 iteration. Operator gets false-fail with wrong remediation link.

- **`PRQ-10` [HIGH] [prereqs-yaml-recipe-contract-plus-placeholder]**: Task 206 acceptance criteria (criterion 1) reads: "Every recipe in `check_recipe
  - **file:where** — projects/customer-provisioning-orchestration-r1/tasks/206-prereqs-yaml-recipe-contract-remediation.poml acceptance-criteria block lines 66-71
  - **fix estimate** — 15 min to amend task POML · **main-session-only?** yes
  - **proposed_fix** — Edit task 206 POML: (a) amend criterion 1 wording, (b) add criterion 6 listing the 8 recipes requiring assertion-recompute (E-06, E-11, S-03, C-01, C-02, C-04, C-06, and any others surfaced in re-audit), (c) bump `<estimated-effort>` to 8-12h, (d) add reference to this audit note as background.
  - **verification** — Read amended task 206 POML; criterion 1 wording explicitly requires assertion computation not just exit-1 wrapper.
  - **consequence_if_unfixed** — Task 206 lands, all recipes get exit-1 guards, validate.ps1 passes, Step 0.5 iteration reports all-green — but F19/F20/T108/F14 root causes STILL silent-pass because the assertion semantics were never computed. Task 206 becomes theatre. This exactly reproduces the SESSION 12 mistake (updating contract without updating recipes) at one meta-level up.

- **`PRQ-11` [HIGH] [prereqs-yaml-recipe-contract-plus-placeholder]**: Task 207 enumerates 19 placeholder tokens (`{containerTypeId}`, `{l2UamiPrincipalId}`, etc
  - **file:where** — projects/customer-provisioning-orchestration-r1/tasks/207-prereqs-yaml-placeholder-substitution-remediation.poml prompt lines 21-35
  - **fix estimate** — 15 min to amend task POML · **main-session-only?** yes
  - **proposed_fix** — Edit task 207 POML: (a) add explicit list of 4 `$var` bash-expansion sites with per-site resolution in prompt block, (b) reference PRQ-01 + PRQ-02 above, (c) add acceptance criterion: "No `$var` reference in `check_recipe.cli` that is not defined in-recipe (via `var=$(...)`) OR explicitly documented as intake-supplied."
  - **verification** — Read amended task 207 POML; prompt enumerates all 4 `$var` sites; acceptance criterion 6 exists.
  - **consequence_if_unfixed** — Task 207 lands, `$filter` fixed, `{token}` placeholders substituted — but PRQ-T-01, PRQ-T-02, PRQ-C-05 STILL 100% broken because their `$var` traps unfixed. Three critical prereqs remain non-functional; audit reports "placeholder work complete" while real defect persists.

- **`PRQ-12` [CRITICAL] [prereqs-yaml-recipe-contract-plus-placeholder]**: Reconfirmed SESSION 14 finding: 12 recipes use `az … -o tsv` with `--query "[0]
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml multiple recipes (T-03 through T-07, S-04, E-03, E-04, E-05, E-09, E-10, E-13, C-05)
  - **fix estimate** — Part of task 206 · **main-session-only?** yes · **depends_on** PRQ-10
  - **proposed_fix** — Included in task 206 sweep, but ensure PRQ-10 wording change lands so the sweep is uniform. Add to task 206 acceptance a companion smoke test: pick 3 of these 12, delete the underlying resource in a canary env, verify recipe exits 1 identifying the absence.
  - **verification** — Smoke test: in canary env, delete PRQ-T-03 Outlook add-in app-reg; run recipe; verify exit 1 with actionable message.
  - **consequence_if_unfixed** — 12 critical prereqs (SPE grants, all Office add-in app-regs, Power BI SP, multitenant BFF app, subscription roles, artifact storage RBAC, ACR RBAC, Website Contributor RBAC, KV catalog presence, KV Secrets User, placeholder record, customer admin consent) silent-pass at every operator invocation. Step 0.5 becomes a rubber stamp. SESSION 14 audit correctly counted these — task 206 must actually apply the fix uniformly.

- **`PLX-04` [HIGH] [placeholder-xlayer]**: Recipes pass literal `--scope "{artifactsStorageId}"`, `--scope "{acrId}"`, `--scope "{bffAppServiceId}"`, `--scope "{kvResourceId}"` to `az role assignment list`
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:237, 248, 259, 325 (PRQ-E-03, E-04, E-05, E-10)
  - **fix estimate** — 45 min · **main-session-only?** yes
  - **proposed_fix** — In SKILL.md Step 0, add a resource-ID-resolution block: `$artifactsStorageId = az storage account show -g rg-spaarke-platform-$env -n "sprk${env}artifacts" --query id -o tsv` (etc. for acr, bffAppService, kv). Extend the substitution chain in Step 0.5b with corresponding `-replace` lines. Guard each with `if ($? -and $artifactsStorageId) {...}` so a missing resource emits a targeted error (`PRQ-E-01 must pass first`) rather than a cryptic `{artifactsStorageId}` az CLI parse error.
  - **verification** — Manual dry-run: each PRQ-E-0x recipe emits an az call against a real resource ID; when the underlying resource doesn't exist, the substitution-time guard emits the correct dependency-order error message.
  - **consequence_if_unfixed** — PRQ-E-03/E-04/E-05/E-10 fail at Step 0.5b with az CLI complaining `The provided scope '{artifactsStorageId}' does not match expected format`. HARD STOP blocks Model 1 shared-service RBAC verification — silently regresses to needing operator to hand-run each check.

- **`PLX-05` [HIGH] [placeholder-xlayer]**: PRQ-E-11 cli: `az servicebus namespace show -g rg-spaarke-platform-{env} -n {sbNamespace}`
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:339, 352 (PRQ-E-11, E-12) — `{sbNamespace}` referenced twice
  - **fix estimate** — 15 min · **main-session-only?** yes
  - **proposed_fix** — In SKILL.md Step 0, add `$sbNamespace = "spaarke-servicebus-$env"` (matching Deploy-ControlPlane.ps1:185 default). Extend substitution chain with `-replace '\{sbNamespace\}', $sbNamespace`.
  - **verification** — PRQ-E-11 + E-12 issue real az servicebus commands against the resolved namespace name.
  - **consequence_if_unfixed** — PRQ-E-11 (SB Data Sender/Receiver RBAC) and PRQ-E-12 (queue session+dedup config) both fail at Step 0.5b with `az servicebus` reporting `The Resource 'Microsoft.ServiceBus/namespaces/{sbNamespace}' under resource group ...` not found. HARD STOP blocks provisioning; regresses to c5.1 dispatcher-DOA class.

- **`PLX-06` [HIGH] [placeholder-xlayer]**: PRQ-E-07 cli: `az rest --method get --url "https://graph
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:288 (PRQ-E-07)
  - **fix estimate** — 10 min (after PLX-02) · **main-session-only?** yes · **depends_on** PLX-02
  - **proposed_fix** — Add hardcoded Microsoft Graph appId to skill constants: `$graphAppId = '00000003-0000-0000-c000-000000000000'`. Extend substitution chain with both `-replace '\{graphAppId\}', $graphAppId -replace '\{l2UamiSpId\}', $l2UamiSpId` (latter already resolved per PLX-02).
  - **verification** — PRQ-E-07 issues real Graph call and returns 14 role IDs when parity is intact.
  - **consequence_if_unfixed** — PRQ-E-07 (L2 UAMI Graph app-role parity — 14 of 14 grants per GraphAppRoles.cs) fails at Step 0.5b with a malformed Graph URL. HARD STOP; regresses to c5.8 silent-403-on-every-graph-call class — one of the highest-impact silent-fail traps in the T-catalog.

- **`PLX-07` [HIGH] [placeholder-xlayer]**: Four recipes reference Dataverse URLs / customerId that have no substituter: PRQ-E-08 `pac org list-users --environment "{adminDvUrl}" --query "[?applicationId=='{l2UamiClientId}']
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:301, 365, 473, 487 (PRQ-E-08, E-13, C-06, C-07) — mix of `{adminDvUrl}`, `{dvUrl}`, `{l2UamiClientId}`, `{customerId}`
  - **fix estimate** — 1h — includes small design adjustment for `{customerId}`-scoped iteration ordering · **main-session-only?** yes
  - **proposed_fix** — (1) Add `{adminDvUrl}` + `{dvUrl}` resolution at Step 0 from a Spaarke constants file (or per-env intake, hardcoded map). (2) Move `{customerId}`-dependent recipes from `once_per_env` scope to `once_per_customer` (PRQ-E-13's scope-comment at line 359 acknowledges this: `# NOTE: per customer being provisioned; created BEFORE POST /api/runs`) — then substitute `{customerId}` AFTER Step 1 intake, running that subset at Step 1.5 or immediately before Step 2 preflight. (3) Extend the substitution chain.
  - **verification** — Manual dry-run confirms PRQ-E-08/C-06/C-07 (env-scoped) succeed at Step 0.5b, PRQ-E-13 succeeds at post-intake iteration.
  - **consequence_if_unfixed** — PRQ-E-08 (Path X Dataverse app user), PRQ-E-13 (`sprk_dataverseenvironment` placeholder record), PRQ-C-06 (org max upload), PRQ-C-07 (Required Applications) all fail at Step 0.5b with `pac` complaining about literal `{customerId}` / `{dvUrl}` args. HARD STOP. Additionally, the ordering issue for `{customerId}` reveals a scoping-mismatch design gap.

- **`EXEC-10` [MEDIUM] [EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.]**: SKILL-08 correctly identifies that Step 0
  - **file:where** — SKILL.md Step 0.5b substitution + prereqs.yaml placeholders like {customerId}, {runId}, {sbNamespace}
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Extend prereqs.yaml schema with a `scope: [pre-intake|post-intake|post-preflight]` field per recipe. Split Step 0.5 into 0.5a (pre-intake) and 1.5 (post-intake). SKILL Step 0.5b substitution keys per scope.
  - **consequence_if_unfixed** — Even with SKILL-08 fix applied, any recipe referencing {customerId} or {runId} still receives literal `{customerId}` at Step 0.5. Recipes are either skipped silently (worst case) or executed with literal-in-CLI (az error).
  - **why_audit_missed** — Placeholder audit enumerated the 19 tokens but did not consider that some tokens are scope-bound in the operator workflow.

---

### Wave 4: SKILL.md aggregate rewrite (main-session-only, serialized SINGLE PASS per DEP-02)

**Rationale.** Same-file write contention. DEP-02 mandates ONE main-session task rewriting SKILL.md end-to-end applying SKILL-01..14 + all PLX-01..03, PLX-08..17 + Wave-1 architectural decisions from EXEC-02/03. Do NOT fan out.

**Wave sizing.** 34 findings; ~72.4h known-effort; 10 findings without numeric estimate.

**Main-session-only (34):**

- **`SKILL-01` [CRITICAL] [skill-drift-audit]**: Skill asserts Spaarke tenant ID `a221a95e-6fa6-4f6b-9a3c-19a1c1a56d7e` and HARD STOPs on mismatch
  - **file:where** — .claude/skills/provision-environment/SKILL.md:102
  - **fix estimate** — 2 min · **main-session-only?** yes
  - **proposed_fix** — Replace the literal `a221a95e-6fa6-4f6b-9a3c-19a1c1a56d7e` with `a221a95e-6abc-4434-aecc-e48338a1b2f2` on line 102 of SKILL.md.
  - **verification** — Run `/provision-environment` interactively; Step 0b PASSes for an operator signed in as `ralph.schroeder@spaarke.com`.
  - **consequence_if_unfixed** — EVERY operator invocation of the skill HARD STOPs at Step 0b — the assertion `tenantId MUST equal a221a95e-6fa6-4f6b-9a3c-19a1c1a56d7e` will never match reality, so the skill refuses to enter Step 1. Complete blocker.

- **`SKILL-03` [CRITICAL] [skill-drift-audit]**: Step 2 POSTs `{ customerId, tenantId, environmentId, tenancyModel, profile, mode: "preflight" }` to `POST /api/runs`
  - **file:where** — .claude/skills/provision-environment/SKILL.md:491-498 (Step 2 preflight POST body)
  - **fix estimate** — 30 min (requires rewriting Step 2 + Step 3 flow narrative around actual endpoint shape) · **main-session-only?** yes
  - **proposed_fix** — Step 2 body MUST be `{ customerId, environmentId, tenancyModel, profile, nonSecretParameters: { tenantId: <tenantId> } }` (or pass tenantId via the placeholder row on Step 1f, which already sets sprk_tenantid). Delete the fictional `mode: "preflight"` line entirely and rewrite Step 2 to reflect that POST /api/runs is a fire-and-run intake — actual preflight-only re-run uses POST /api/runs/{id}/preflight per RunsEndpoints.cs:188.
  - **verification** — Trace a Step 2 call via `curl -X POST /api/runs -d '{...}'`; response is 202 with `status:"NotStarted"`; H0 lands on the Service Bus queue.
  - **consequence_if_unfixed** — `tenantId` is silently dropped — this DIRECTLY VIOLATES §4D I1 tenant-isolation invariant because the intake tenantId never reaches L2 or the registry write, propagating a wrong-tenant assumption through the entire run. `mode: "preflight"` is dropped — the skill believes it is doing a scoped preflight but has actually enqueued the full H0 → reconciler-driven cascade. Step 3 confirmation gate is then meaningless because H0 already ran.

- **`SKILL-04` [CRITICAL] [skill-drift-audit]**: Step 4 POSTs body `{ mode: "execute" }` to `POST /api/runs/{runId}/resume` to transition the run from `Preflight-Only` to `Executing`
  - **file:where** — .claude/skills/provision-environment/SKILL.md:717-722 (Step 4 execute-transition POST)
  - **fix estimate** — 20 min · **main-session-only?** yes
  - **proposed_fix** — Rewrite Step 4 to: (a) call `POST /api/runs/{runId}/resume?customerId={customerId}` with NO body (or omit resume entirely if the reconciler already auto-advances after H0 completes — verify against StateReconcilerService), (b) remove Preflight-Only/Executing state names, (c) reflect that H0's Succeeded → H1 dispatch is reconciler-driven, not operator-initiated.
  - **verification** — Trace Step 4 call in test env; H1 handler enqueues after operator's confirm.
  - **consequence_if_unfixed** — The POST body is silently ignored, but more critically: `customerId` MUST be passed as a QUERY parameter (line 582: `TryValidateRouteAndPartition` returns 400 if missing). Skill Step 4 passes no customerId → 400 Bad Request. Even if that were fixed, the resume envelope carries `CurrentPhase` from wherever the reconciler left it — the skill's mental model of `preflight-only → execute` doesn't map to the actual state machine.

- **`SKILL-05` [CRITICAL] [skill-drift-audit]**: Step 4a: `Poll GET /api/runs/{runId} at 10s intervals`
  - **file:where** — .claude/skills/provision-environment/SKILL.md:728 (Step 4a poll)
  - **fix estimate** — 15 min (find + replace every URL construction site + update poll snippet) · **main-session-only?** yes
  - **proposed_fix** — Every poll and every non-POST /api/runs call in Step 4, Step 5, and Step 6 MUST include `?customerId=$(Uri.EscapeDataString($customerId))`. Update Step 4a, 4b transition table + associated poll snippets, and cross-references in F1/F2/F3 (Fallback Matrix) that show URL construction.
  - **verification** — Poll `GET /api/runs/{runId}?customerId=trial1` returns 200 with the ProvisioningRun JSON. Poll without query param returns 400.
  - **consequence_if_unfixed** — Poll returns 400 on every tick forever. The skill's execute loop never sees a state transition and hangs at 'poll' until session timeout. The Fallback Matrix F3 assumes 5xx/timeout — 400 doesn't trigger it, so this is silent hang.

- **`SKILL-06` [CRITICAL] [skill-drift-audit]**: Skill lists these run states: `Accepted`, `Executing`, `WaitingOnGate`, `Succeeded`, `Failed`, `Quarantined`, `Drifted`
  - **file:where** — .claude/skills/provision-environment/SKILL.md:740-750 (Step 4b state-transition table)
  - **fix estimate** — 20 min · **main-session-only?** yes
  - **proposed_fix** — Rewrite the Step 4b table with the actual enum values: NotStarted, Running, WaitingOnGate, Completed, Failed, Cancelled, Quarantined. Drop the fictional Accepted/Executing/Succeeded/Drifted rows. Update Step 6 (`When the run reaches Succeeded`) to `When the run reaches Completed`. Update handoff-report template (lines 878) similarly. Add a Cancelled branch to Step 4b.
  - **verification** — Grep SKILL.md for `Succeeded|Executing|Drifted|Accepted` — every hit must either be removed or replaced with a real enum value.
  - **consequence_if_unfixed** — The skill's state-detection logic never matches. Poll returns run.status='NotStarted' but skill is looking for 'Accepted' → skill mishandles the initial state. Poll returns 'Running' but skill expects 'Executing' → skill mishandles active state. Poll returns 'Completed' but skill Step 6 waits for 'Succeeded' → completion handoff NEVER fires; operator sees infinite poll. 'Drifted' handling code is dead. Cancelled runs (operator abort) fall through to no branch.

- **`SKILL-07` [CRITICAL] [skill-drift-audit]**: Step 5 (all gate flavors) says: 'Type resume when the action is complete' + 'always call POST /api/runs/{id}/resume and let L2 re-verify'
  - **file:where** — .claude/skills/provision-environment/SKILL.md:820-832 (Step 5d generic gate handling) + Step 5a-c
  - **fix estimate** — 20 min · **main-session-only?** yes
  - **proposed_fix** — Rewrite Step 5b, 5c, and 5d to call `POST /api/runs/{id}/gates/{gateId}/advance?customerId={cid}` for operator-cleared gates. Keep 5a (H0.5) as auto-detect via HMAC callback (already correct — no explicit call). Add a concrete note that /resume is only for Failed-state retry per RunsEndpoints.cs:232-244 WithDescription text.
  - **verification** — Trace a WaitingOnGate H1 case; operator calls /gates/{gateId}/advance → reconciler transitions gateState to Verified → phase advances.
  - **consequence_if_unfixed** — Operator hits /resume for an H1-quota-bump gate; L2's reconciler doesn't advance the gate because gate state wasn't touched; the run stays at WaitingOnGate forever. Or worse: /resume triggers a re-dispatch of CurrentPhase which may re-fail the same way if the gate condition wasn't actually cleared, burning a retry budget entry.

- **`SKILL-09` [HIGH] [skill-drift-audit]**: `az ad app show --id api://spaarke-provisioning-controlplane-dev --query "id"` — uses app-reg identifier URI `api://spaarke-provisioning-controlplane-dev`
  - **file:where** — .claude/skills/provision-environment/SKILL.md:1242 (Auth Flow section)
  - **fix estimate** — 2 min · **main-session-only?** yes
  - **proposed_fix** — Change line 1242 to `az ad app show --id api://spaarke.com/provisioning-controlplane-dev --query "id"`. Grep the file for `api://spaarke-provisioning-controlplane` — that string must not appear (App Service names use that pattern; identifierUris/audience uses the spaarke.com verifier-domain form).
  - **verification** — `az ad app show --id api://spaarke.com/provisioning-controlplane-dev` returns the SP objectId.
  - **consequence_if_unfixed** — An operator following the Auth Flow's grant-instruction snippet copies the wrong URI, gets `AADSTS500011: resource not found`, and cannot self-service their Operator app-role. Even worse, the wrong URI has 'spaarke' as a bare tenant sub-domain rather than the verifier-domain form (`spaarke.com/...`) that DS-5 C5.2 forced.

- **`SKILL-10` [HIGH] [skill-drift-audit]**: Comment claims `response: { runId: "
  - **file:where** — .claude/skills/provision-environment/SKILL.md:506-508 (Step 2 preflight response comment)
  - **fix estimate** — 3 min · **main-session-only?** yes
  - **proposed_fix** — Change the response-shape comment to `response: { runId: "...", customerId: "...", status: "NotStarted", location: "/api/runs/{runId}?customerId=..." }`.
  - **verification** — Grep SKILL.md for `status.*Accepted` — no matches referring to L2 response.
  - **consequence_if_unfixed** — If a follow-on step ever gates on `response.status == "Accepted"`, it dead-ends. This is documentation drift today; the runId extraction still works. But this misleads any future maintainer or agent editing the flow to add a status check.

- **`SKILL-11` [CRITICAL] [skill-drift-audit]**: Step 6a: `When the run reaches Succeeded (H13 acceptance passed):` … `Status: Succeeded`
  - **file:where** — .claude/skills/provision-environment/SKILL.md:836-858 (Step 6a completion handoff) + line 748 (Step 4b Succeeded row)
  - **fix estimate** — 10 min (grep + confirm no false positives) · **main-session-only?** yes
  - **proposed_fix** — Global replace `Succeeded` → `Completed` in Step 4b and Step 6 (headings, body, handoff report template). Keep 'Ready' where it refers to sprk_setupstatus display name — that is a different enum (EnvironmentSetupStatus.Ready = 2) and IS correct.
  - **verification** — Trace a full run through H13 completion; skill enters Step 6a on RunStatus=Completed and writes the handoff report.
  - **consequence_if_unfixed** — The skill's condition to enter Step 6 never fires. Handoff report is never written. Registry belt-and-suspenders re-write never happens. The operator sees infinite poll on a completed run.

- **`SKILL-12` [HIGH] [skill-drift-audit]**: Skill's H8 gate text: `Container-type created successfully but Microsoft-side replication takes ~24h before H8
  - **file:where** — .claude/skills/provision-environment/SKILL.md:803-818 (Step 5c H8 SPE 24h replication wait)
  - **fix estimate** — 15 min · **main-session-only?** yes
  - **proposed_fix** — Rewrite Step 5c: (a) begin polling H8.a immediately at 30-60s intervals for 15 min; (b) if still failing after 15 min, back off to 5-min intervals for the next 45 min; (c) only surface the 25h fallback ceiling after 1h of failed polling. Cite the operator-memory finding and the 2026-08-22 stand-up evidence.
  - **verification** — Trace an H8 completion; H8.a re-verify succeeds within minutes; skill does NOT exit the run.
  - **consequence_if_unfixed** — Operator abandons the run for 24h based on the message, when in fact H8.a would have verified within a minute or two of container-type create. This adds one full day of unnecessary latency to every fresh Model 2 provisioning run and demoralizes operators (the whole spec is <1h wall-clock per NFR-03).

- **`SKILL-13` [MEDIUM] [skill-drift-audit]**: Step 3: `Handlers to execute (13 for Model1Shared / 17 for Model2Dedicated)`
  - **file:where** — .claude/skills/provision-environment/SKILL.md:667 (Step 3 RUN PLAN handler count) + line 42 (Quick Reference)
  - **fix estimate** — 10 min · **main-session-only?** yes
  - **proposed_fix** — Recount both places against design.md §H0-H14 authoritative table; use the true totals (probably 21 for Model2Dedicated / 18 for Model1Shared after skipping H0.5 + H11); include a footnote listing which handlers are model-1-only / model-2-only.
  - **verification** — Bullet count == parenthetical count in both Step 3 and Quick Reference.
  - **consequence_if_unfixed** — Operator loses trust in the plan because the count doesn't match the bullet list. In future audits, a maintainer may accidentally delete a 'duplicate' bullet to align the count, silently dropping a real handler. Minor but a common trust-erosion pattern.

- **`SKILL-14` [MEDIUM] [skill-drift-audit]**: MCP update template says `recordId: {resolved from customerId via sprk_customerid alt-key}` with no explicit code to perform the lookup
  - **file:where** — .claude/skills/provision-environment/SKILL.md:846-857 (Step 6a MCP update payload)
  - **fix estimate** — 10 min · **main-session-only?** yes
  - **proposed_fix** — Change Step 6a to `recordId: $environmentId` (variable already in scope from Step 1f). Add a comment: 'reuses the placeholder GUID captured at Step 1f'. If session was re-invoked (skill detects existing in-progress run), add a lookup fallback: `pac data list --entity sprk_dataverseenvironment --filter "sprk_customerid eq '$customerId'" --query "[0].sprk_dataverseenvironmentid"`.
  - **verification** — End-to-end run reaches Step 6a; MCP update lands on correct row; sprk_setupstatus flips to 2 (Ready).
  - **consequence_if_unfixed** — An operator following the skill literally has no code to resolve recordId at Step 6a — they will either skip the update (registry stale if server-side updater failed) or invent the wrong call (mcp__dataverse__update_record with a placeholder string as recordId → 400/404).

- **`PLX-01` [CRITICAL] [placeholder-xlayer]**: Recipes contain literal `{containerTypeId}` in Graph URL: `https://graph
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:57, 72 (PRQ-T-01, PRQ-T-02 `check_recipe.cli`); resolver at .claude/skills/provision-environment/SKILL.md:206-208
  - **fix estimate** — 30 min · **main-session-only?** yes · **depends_on** PLX-13
  - **proposed_fix** — Two-part: (1) In SKILL.md:206-208, extend the `-replace` chain to `-replace '\{containerTypeId\}', $containerTypeId` where `$containerTypeId` is loaded from a Spaarke constants source at Step 0 alongside `$env`. (2) Add `spaarkeContainerTypeId` (or similar) to `scripts/canonical-secret-catalog/manifest.yaml` as a non-secret constant, OR introduce `scripts/provisioning-prereqs/spaarke-constants.yaml` with `{ containerTypeId: '<the-guid>', graphAppId: '00000003-0000-0000-c000-000000000000' }` and load it at Step 0.
  - **verification** — Grep confirms `{containerTypeId}` is expanded before `bash -c` invocation; unit-test Step 0.5b iterator with mocked recipe returns exit 0 on happy-path Graph 200.
  - **consequence_if_unfixed** — At Step 0.5b iteration, `az rest --uri https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes/{containerTypeId}` is executed literally. Graph returns 400/404 or az CLI errors on the malformed GUID → recipe exits non-zero → PRQ-T-01 marked FAIL → Step 0.5c HARD STOP → operator can NEVER proceed with `/provision-environment` without invoking `-SkipStep0_5` (which itself is a documented risk per PRQ-T-01 `never_delete: true`).

- **`PLX-02` [CRITICAL] [placeholder-xlayer]**: Seven distinct `check_recipe
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:185, 197, 237, 248, 259, 273, 288, 325, 338 (7 recipes: PRQ-S-04, S-05, E-03..E-08, E-10, E-11); resolver at SKILL.md:206-208
  - **fix estimate** — 45 min · **main-session-only?** yes
  - **proposed_fix** — In SKILL.md Step 0 (before 0.5b), add: `$uami = az identity show -g "rg-spaarke-platform-$env" -n "sprk-controlplane-$env-uami" -o json | ConvertFrom-Json; $l2UamiPrincipalId = $uami.principalId; $l2UamiClientId = $uami.clientId; $l2UamiSpId = (az ad sp show --id $l2UamiClientId --query id -o tsv)`. Extend the `-replace` chain at 206-208 with three additional `-replace '\{l2UamiPrincipalId\}', $l2UamiPrincipalId` etc.
  - **verification** — Dry-run against dev sub: each of the 7 recipes produces a real `az` call with a real GUID and returns exit 0 when the role is present, exit 1 (with meaningful `no matching role assignment`) when absent.
  - **consequence_if_unfixed** — Every RBAC preflight recipe (PRQ-S-04 sub Contributor, PRQ-E-03 blob reader, PRQ-E-04 AcrPull, PRQ-E-05 Website Contributor, PRQ-E-06 shared-service roles, PRQ-E-07 Graph app-roles, PRQ-E-08 Path X Dataverse app user, PRQ-E-10 KV Secrets User, PRQ-E-11 Service Bus roles) fails at Step 0.5b with `az` rejecting `{l2UamiPrincipalId}` as an invalid assignee GUID. HARD STOP blocks provisioning for the seven MOST critical RBAC prerequisites — the same ones whose absence causes silent-fail traps T1-T3.

- **`PLX-03` [CRITICAL] [placeholder-xlayer]**: Recipes contain literal `--scope /subscriptions/{subId}` (PRQ-S-04 cli+remediation, PRQ-S-05 cli) and `https://management
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:185, 187, 197, 435 (PRQ-S-04, S-05, C-03); resolver at SKILL.md:206-208
  - **fix estimate** — 15 min · **main-session-only?** yes
  - **proposed_fix** — In SKILL.md Step 0, add: `$subId = az account show --query id -o tsv`. Extend the substitution chain with `-replace '\{subId\}', $subId -replace '\{sub\}', $subId`. Note the two-name inconsistency (`subId` vs `sub`) is itself a smell — recommend normalizing prereqs.yaml to use `{subId}` throughout, then only one substitution rule needed.
  - **verification** — PRQ-S-04/S-05/C-03 issue real az calls against a real sub GUID.
  - **consequence_if_unfixed** — PRQ-S-04 (sub Contributor), PRQ-S-05 (operator sub roles), PRQ-C-03 (SB namespace availability) all fail at Step 0.5b with az CLI parsing errors on the literal `{subId}` / `{sub}` — same HARD STOP class as PLX-02.

- **`PLX-08` [HIGH] [placeholder-xlayer]**: PRQ-C-01 cli: `az cognitiveservices usage list --location {region} 
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:406, 420, 421 (PRQ-C-01, C-02) — `{region}`, `{pinnedVer}`
  - **fix estimate** — 30 min after design decision · **main-session-only?** yes
  - **proposed_fix** — Two-path decision: (A) if `{region}` / `{pinnedVer}` are client-side per SKILL.md Step 0.5, extend the substitution chain: `-replace '\{region\}', $openAiRegion` (region==openAiRegion for these two recipes) and refactor PRQ-C-02 to be a per-pin loop like PRQ-E-14 does at line 382. (B) if `once_per_customer` recipes are server-side (L2 H0), document that clearly + skip them at Step 0.5b (already partially handled by scope filter at SKILL.md:198 which only accepts `once_per_tenant` / `once_per_subscription` / `once_per_env`).
  - **verification** — Confirm scope-filter at SKILL.md:198 correctly SKIPS `once_per_customer` (yes — line 198-199 filters to those three scopes) — so these two are DE FACTO skipped by the client-side iterator. But this means the yaml carries placeholders that no consumer ever substitutes — should either (a) be moved to server-side manifest or (b) documented as 'H0-consumed'.
  - **consequence_if_unfixed** — PRQ-C-01 (OpenAI TPM headroom) and PRQ-C-02 (per-region GA per pin) fail with cryptic az CLI errors. Since these are `once_per_customer` scope + must run at Step 2 preflight time, the design intent is likely for L2 H0 handler to run them — in which case the placeholders SHOULD live in the yaml as `{region}` and be substituted by H0, not by the skill. Confirm the design intent: is C-* class 'client-side at Step 0.5' or 'server-side at H0'?

- **`PLX-09` [MEDIUM] [placeholder-xlayer]**: PRQ-C-03 cli: `az rest --url "https://management
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:435-436 (PRQ-C-03) — `{sub}`, `{sbName}`
  - **fix estimate** — 20 min after PLX-08 decision · **main-session-only?** yes · **depends_on** PLX-03,PLX-08
  - **proposed_fix** — Rename `{sub}` → `{subId}` throughout yaml for consistency (single find/replace). For `{sbName}`, decide server-side vs client-side per PLX-08 decision. If client-side, add runtime derivation `$sbName = "spaarke-$customerId-$env-sbus"` and extend the substitution chain — but only AFTER Step 1 intake resolves `$customerId`.
  - **verification** — Grep confirms zero `{sub}` remain (only `{subId}`); PRQ-C-03 runs with concrete values when its consumer (server-side H0 or client-side post-intake) invokes it.
  - **consequence_if_unfixed** — PRQ-C-03 (global SB namespace availability, `once_per_customer` scope so currently skipped by the Step 0.5 scope filter) is unused server-side too. Same 'yaml carries orphaned placeholders' pattern as PLX-08.

- **`PLX-10` [MEDIUM] [placeholder-xlayer]**: PRQ-C-05 cli: `az rest --url "https://graph
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:461 (PRQ-C-05) — `{bffAppId}`
  - **fix estimate** — 30 min · **main-session-only?** yes · **depends_on** PLX-13
  - **proposed_fix** — Add `$bffAppId` to Spaarke constants file (per-env; see PLX-13). Rework the recipe to also emit the `$customerTenantToken` acquisition step (or document that H0 injects it into the bash env). This recipe is currently orphaned pending scope-scheduling design.
  - **verification** — Recipe issues a real Graph call and returns non-empty consent grants when consent has been given.
  - **consequence_if_unfixed** — PRQ-C-05 (Model 2 admin consent verification, `once_per_customer` scope so currently skipped by Step 0.5 iterator) has two orthogonal defects. When it is eventually invoked server-side or post-intake, both need resolving.

- **`PLX-11` [HIGH] [placeholder-xlayer]**: Step 5a shows the customer admin the URL: `https://login
  - **file:where** — .claude/skills/provision-environment/SKILL.md:770-773 (Step 5a Model 2 admin-consent URL)
  - **fix estimate** — 15 min · **main-session-only?** yes · **depends_on** PLX-13
  - **proposed_fix** — Add explicit PowerShell substitution in Step 5a: `$consentUrl = "https://login.microsoftonline.com/$tenantId/adminconsent?client_id=$bffAppId&redirect_uri=...&state=$runId"; Write-Host $consentUrl`. Also delete the literal-in-prose URL template OR annotate it `(shape only — substituted at runtime)` to match the pattern used elsewhere.
  - **verification** — Read Step 5a code (once implemented) confirms substitution before Write-Host; end-to-end dry-run generates a real clickable URL.
  - **consequence_if_unfixed** — If Step 5a is implemented literally per the SKILL.md prose (no substitution shown), the customer admin receives a broken URL with literal `{customerTenantId}` etc. → cannot consent → H0.5 gate never clears → run stalls indefinitely at `WaitingOnGate`. Even the fallback (operator hand-crafts URL) is error-prone since the skill's template pretends to be complete.

- **`PLX-12` [MEDIUM] [placeholder-xlayer]**: Step 6b displays a triple-backtick markdown block as the handoff-report template
  - **file:where** — .claude/skills/provision-environment/SKILL.md:866-930 (Step 6b handoff-report markdown template) — 15+ tokens: `{runId}`, `{customerId}`, `{tenantId}`, `{tenancyModel}`, `{profile}`, `{startedAt}`, `{completedAt}`, `{duration}`, `{l2Base}`, `{amount}`, `{escalation notes if any}`, `{timestamp}`, `{version}`, `{URL}`, and Step 6c summary block `{customerId}`, `{runId}`, `{duration}`, ...
  - **fix estimate** — 30 min · **main-session-only?** yes
  - **proposed_fix** — Add a PowerShell substitution block immediately after the markdown fence in Step 6b (mirror the shape of Step 7a lines 973-981): `$report = Get-Content <template-path> -Raw; $report = $report -replace '\{runId\}', $runId -replace '\{customerId\}', $customerId ...; Set-Content -Path "runs/$runId.md" -Value $report`. Or, better, factor the template out to `.claude/skills/provision-environment/refs/handoff-report.md` and reuse the Step 7a lessons-learned pattern.
  - **verification** — Live run produces `runs/<real-runId>.md` with all placeholders resolved to real values.
  - **consequence_if_unfixed** — Handoff report — the mandatory audit-trail artifact per SKILL.md 'MUST produce a handoff report at runs/{runId}.md' (line 60) and 'the report is the audit trail + the resumption baseline' (line 1333) — is corrupted with literal placeholder text if the skill follows the template verbatim. Operator-facing report has no real customer/run identity.

- **`PLX-13` [HIGH] [placeholder-xlayer]**: The skill has no consolidated source for Spaarke platform constants (Graph appId, per-env BFF multitenant app-id, SPE container-type GUID, canonical resource-name templates)
  - **file:where** — .claude/skills/provision-environment/SKILL.md Step 0 (skill preamble) — no constants file exists
  - **fix estimate** — 1h — plus dependency-review of what other skills / handlers would benefit · **main-session-only?** yes
  - **proposed_fix** — (1) Create `scripts/provisioning-prereqs/spaarke-constants.yaml` with two sections: `microsoft_constants:` (Graph appId, ARM audience, etc.) and `spaarke_constants: { dev: {...}, prod: {...} }` (per-env). (2) SKILL.md Step 0 loads it via `$constants = Get-Content ... | ConvertFrom-Yaml; $graphAppId = $constants.microsoft_constants.graphAppId; ...`. (3) All PLX-01/06/10/11 fixes reference `$constants.*` rather than hardcoding values inline.
  - **verification** — Grep confirms zero hardcoded GUIDs in SKILL.md; unit-test constants loader; integration test PLX-01/06/10/11 pass with real values.
  - **consequence_if_unfixed** — Blocks proper resolution of PLX-01 (containerTypeId), PLX-06 (graphAppId), PLX-10 (bffAppId), PLX-11 (bffAppId in Step 5a). Each is otherwise fixable in isolation but with hardcoded values scattered across the skill — a maintenance liability that repeats the source-of-truth-drift class PLX-03 exemplifies (`{sub}` vs `{subId}` inconsistency).

- **`PLX-14` [MEDIUM] [placeholder-xlayer]**: The defense-in-depth classifier at lines 234-240 only fires when `expect` contains a backticked pattern: `if ($passed -and $expected -match "``([^``]+)``")`
  - **file:where** — .claude/skills/provision-environment/SKILL.md:236-240 (Step 0.5b classifier defense-in-depth)
  - **fix estimate** — 10 min · **main-session-only?** yes
  - **proposed_fix** — In Step 0.5b, immediately after the `-replace` chain (line 208), add a regex sanity check: `if ($recipe -match '\{[a-zA-Z_][a-zA-Z_0-9]*\}') { Write-Error "[skill-config] Recipe for $($prereq.id) references unresolved placeholder $($Matches[0]). Extend the substitution block at SKILL.md:207-208."; continue }`. This turns silent literal-in-text into a loud config-error the maintainer sees.
  - **verification** — Introduce a fake `{fake_token}` in a test recipe → iterator emits the targeted error before running bash.
  - **consequence_if_unfixed** — New prereqs added to `prereqs.yaml` that reference NEW placeholders silently regress to the PLX-01..PLX-10 class — the failure mode is 'operator hits HARD STOP on the new prereq without a clear diagnostic pointing at the placeholder-substitution gap'. Slows every future prereq-catalog extension.

- **`PLX-15` [LOW] [placeholder-xlayer]**: Placeholder appears in `remediation:` prose field, not in `check_recipe
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml:147 (PRQ-S-01 remediation) — `provisioning-runs/{customerId}-{runId}/intake.md`
  - **fix estimate** — 0 · **main-session-only?** yes
  - **proposed_fix** — None. Included for completeness so the auditor's tally distinguishes 'literal-in-cli bug class (PLX-01..PLX-10)' from 'literal-in-prose documentation (PLX-15 class)'. Prereqs.yaml `consequence_of_absence` / `remediation` fields with `{token}` are all in this class and non-actionable.
  - **verification** — N/A.
  - **consequence_if_unfixed** — None — this is by-design documentation.

- **`PLX-16` [LOW] [placeholder-xlayer]**: Every `{
  - **file:where** — infrastructure/bicep/**/*.bicep — all `{...}` occurrences (spot-checked: customer.bicep:134,143,152,155; alerts.bicep:26,29,32; modules/acs-communication.bicep:25; platform-controlplane.bicep:10,13)
  - **fix estimate** — 0 · **main-session-only?** yes
  - **proposed_fix** — None. Included so the auditor's tally documents that Bicep is clean of the placeholder bug class.
  - **verification** — N/A — Bicep compile+deploy already validates real substitution paths.
  - **consequence_if_unfixed** — None — by-design documentation.

- **`PLX-17` [LOW] [placeholder-xlayer]**: Every `{
  - **file:where** — scripts/**/*.ps1 — all `{...}` occurrences (spot-checked: Verify-Sidecar-Live.ps1:55-77, Seed-PlatformKeyVault.ps1:3-131, Deploy-ControlPlane.ps1:130-310, Grant-ControlPlaneIdentity.ps1:73, Provision-Customer.ps1:9-32)
  - **fix estimate** — 0 · **main-session-only?** yes
  - **proposed_fix** — None. Included so the auditor's tally documents that PowerShell scripts are clean of the placeholder bug class.
  - **verification** — N/A.
  - **consequence_if_unfixed** — None — by-design documentation.

- **`PLX-18` [LOW] [placeholder-xlayer]**: Configs like `src/server/api/Sprk
  - **file:where** — src/**/appsettings*.json — uses `#{TOKEN}#` scheme, NOT `{token}`
  - **fix estimate** — N/A (separate follow-up audit) · **main-session-only?** yes
  - **proposed_fix** — Not this audit's scope, but log a follow-up: 'Verify Sprk.Bff.Api appsettings token-replacement transformer is wired into deploy-bff-api.yml and produces literal-free appsettings.json in the App Service publish artifact.'
  - **verification** — N/A for this dimension.
  - **consequence_if_unfixed** — None for THIS audit dimension. Separate follow-up: verify the deploy pipeline runs the token-replace transformer before deploying appsettings.template.json to the App Service — if the transformer is missing/skipped, BFF boots with literal `#{TENANT_ID}#` values → fail-fast at `.ValidateOnStart()` (F20 progressive-discovery class).

- **`PLX-19` [LOW] [placeholder-xlayer]**: Each is inside a YAML comment or a workflow step's `name:` (display-only field)
  - **file:where** — .github/workflows/deploy-bff-api.yml:19,188 (`bff-api-{buildId}.zip`); .github/workflows/deploy-infrastructure.yml:353 (`rg-spaarke-{customerId}-{environment}`); .github/workflows/ci-router.yml:74 (`ci-router-{pr_number}`)
  - **fix estimate** — 5 min (optional) · **main-session-only?** yes
  - **proposed_fix** — OPTIONAL: In deploy-bff-api.yml:188, change `name: Create artifact zip (bff-api-{buildId}.zip)` to `name: Create artifact zip (bff-api-<buildId>.zip)` to signal 'shape, not substitution' consistently with the surrounding CI convention. Skip if the ambiguity is deemed acceptable.
  - **verification** — N/A.
  - **consequence_if_unfixed** — None runtime. Minor readability improvement possible.

- **`REG-04` [HIGH] [registry-write-path]**: SKILL
  - **file:where** — .claude/skills/provision-environment/SKILL.md:840-862 (§Step 6a) + DataverseRegistrySetupStatusUpdater.cs:140-149
  - **fix estimate** — 1h · **main-session-only?** yes
  - **proposed_fix** — Rewrite Step 6a to: (1) state honestly 'server writes sprk_setupstatus + sprk_currentrunid; operator MUST write sprk_provisionedon, sprk_bffversion, sprk_solutionversion (H0 upgrade-mode depends on the first)'; (2) show the two-step MCP flow explicitly — first `mcp__dataverse__read_query` with `sprk_dataverseenvironments?$filter=sprk_customerid eq '{cid}'&$select=sprk_dataverseenvironmentid&$top=1` then extract `sprk_dataverseenvironmentid` then call `update_record`; (3) make Step 6a HARD-STOP on any failure (operator retry required, cannot skip).
  - **verification** — Operator does clean provisioning run, checks Dataverse row via `pac data list` afterward, and each of the 3 fields is populated.
  - **consequence_if_unfixed** — Operator reads 'in practice server-side' and skips Step 6a (thinking it's redundant). sprk_provisionedon / sprk_bffversion / sprk_solutionversion stay null. H0 upgrade-mode detection on next run fails (see REG-01 consequence). Additionally, operator who does execute Step 6a hits MCP error 'recordId is not a valid GUID' because the placeholder syntax `{resolved from customerId via sprk_customerid alt-key}` isn't a real API call.

- **`EXEC-05` [HIGH] [EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.]**: The DAG advances only when StateReconcilerService
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Reconciler/StateReconcilerService.cs (BackgroundService) + App Service idle-scale behavior
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Add a Step 4a health probe: if 3 consecutive polls return identical `updatedAt` on the run doc, fetch `/healthz` on L2 AND emit a `POST /api/runs/{id}/resume?customerId=` nudge (which per RunsEndpoints.cs re-enqueues the CurrentPhase envelope). Document App Service AlwaysOn as a Step 0 prereq check.
  - **consequence_if_unfixed** — Skill Step 4a poll returns `status:Running` with no CompletedPhases advancement for many minutes. The skill's timeout is not documented — Fallback F3 assumes 5xx/timeout but a stuck-Running response is 200 OK. Skill has no way to distinguish 'still working' from 'reconciler asleep'. Operator abandons ceremony; skill has no diagnostic path.
  - **why_audit_missed** — All 7 audits examined static code and skill text; none stepped into the runtime concurrency model where the reconciler's liveness is a hidden dependency.

- **`EXEC-06` [HIGH] [EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.]**: Operator acquires bearer token at Step 4 start
  - **file:where** — SKILL.md Step 4 poll loop + Fallback Matrix F2 + `az account get-access-token` semantics
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Instrument Step 4a: on every 401, silently re-acquire token via `az account get-access-token` and retry once. Document token-lifetime as a first-class concern with a concrete refresh recipe in the skill body.
  - **consequence_if_unfixed** — Any run hitting a real-world manual gate 401s the poll after ~1hr. Operator sees the poll die; skill treats this as F3 (L2 unreachable) which prescribes 'resume from Cosmos state' — but that path is not stress-tested for stale-token scenario. Real world result: operator must restart Step 4 from scratch on partial state.
  - **why_audit_missed** — Skill-drift audit inspected endpoint shape (URL + query params) but not the auth-header lifetime story.

- **`EXEC-08` [HIGH] [EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.]**: Step 6a calls `mcp__dataverse__update_record`
  - **file:where** — SKILL.md Step 6a (`mcp__dataverse__update_record` for registry update) + Fallback F1 (undocumented `pac data` recipe)
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Write out the F1 fallback recipe verbatim in Step 6a body: `az rest --method patch --uri {dvUrl}/api/data/v9.2/sprk_dataverseenvironments(sprk_customerid='{customerId}') --headers 'If-Match=*' 'Prefer=return=representation' --body '{...}'`. Prefer this path OVER MCP for non-interactive/batch invocations; MCP only for interactive fallback.
  - **consequence_if_unfixed** — Skill completes H13 successfully but Step 6a registry write fails silently or hangs — the whole point of the r1 project (registry now consistently reflects reality) is defeated at the final step. Subsequent operator tooling reads stale registry state.
  - **why_audit_missed** — All audits assumed MCP works in an interactive session. None modeled the non-interactive batch dispatch case where the very same skill is being invoked by another agent.

- **`COMP-04` [CRITICAL] [completeness]**: Every audit assumes the L2 API is deployed, reachable, and running the current image containing H4Shared + H4b handler registrations
  - **file:where** — The Azure App Service hosting Sprk.Provisioning.ControlPlane.Api + the Worker container (or App Service Plan)
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Add SKILL.md Step 0f: `L2 deployment probe` — az webapp show + /healthz with build-tag assertion. HARD STOP if not current.
  - **consequence_if_unfixed** — Task 186 fires, Step 2 POST to `/api/runs` returns 404 (app not deployed) or 502 (crashed) or 200 with an OLD image that lacks H4Shared/H4b. In the latter case the halt is disguised — operator sees a successful POST but reconciler silently skips new handlers.
  - **why_audit_missed** — The audits verified code + config on disk; none verified deployed state in Azure. Deployment-state is the runtime companion to skill-drift and neither the skill-drift audit nor the handler audit owned it.

- **`COMP-08` [CRITICAL] [completeness]**: SKILL
  - **file:where** — .claude/skills/provision-environment/SKILL.md Step 1a (customerId probe fix per SKILL-02) + Step 6a (registry update)
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Add explicit non-MCP fallback path to SKILL.md Step 6a: raw Web API PATCH against sprk_dataverseenvironment(sprk_customerid='...') with az CLI Dataverse token. Test both paths in a batch dispatch dry-run.
  - **consequence_if_unfixed** — Batch dispatch of task 186 completes handlers → reaches Step 6a → subagent has no MCP → `mcp__dataverse__update_record` fails with 'MCP not authenticated' → run is Complete in Cosmos but registry never marked → next operator sees stale state → duplicate runs.
  - **why_audit_missed** — Skill drift audit read the skill's happy path; batch mode audit inventoried the skip-gate gaps but did not sim-run against MCP-tool availability.

- **`COMP-15` [HIGH] [completeness]**: SKILL
  - **file:where** — .claude/skills/provision-environment/SKILL.md Step 0 prereqs + batch dispatch subagent environment
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Add batch-mode contract test to Step 0a: probe for pwsh version, az/pac/gh binaries, az account show freshness, MCP status. Fail fast with actionable error.
  - **consequence_if_unfixed** — task 186 batch dispatch subagent lacks az CLI (or has expired token) → Step 0.5 recipes fail on `az account show` → HARD STOP. Currently manifest as an unhelpful error string.
  - **why_audit_missed** — Batch mode audit focused on skill-declared skip-gates; runtime-toolchain is invisible to a static skill-text audit.

---

### Wave 5: intake.schema.json update + intake JSON contract sweep (main-session)

**Rationale.** DEP-04 groups intake shape drift into one contract-alignment sweep. Drop vestigial fields, add required fields, wire batch-mode policy fields. Serialize after Wave 2/B2 lands endpoint changes.

**Wave sizing.** 8 findings; ~6.2h known-effort; 1 findings without numeric estimate.

**Main-session-only (6):**

- **`ISH-02` [CRITICAL] [intake-schema-vs-handlers]**: 10 handlers require `run
  - **file:where** — scripts/provisioning-prereqs/intake.schema.json (no subscriptionId property) + src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SubscriptionReadiness/H1SubscriptionReadinessHandler.cs:210 (+ H2a:239, H4:*, H4b:*, H4Shared:*, H8:150, H9:169, H13:130, H14:101 all read `NonSecret["subscriptionId"]`)
  - **fix estimate** — 1h · **main-session-only?** yes
  - **proposed_fix** — 1) Add to intake.schema.json: `"subscriptionId": { "type": "string", "format": "uuid", "description": "Target Azure subscription id — required for Model 2 (customer's sub); Model 1 auto-defaults to shared Spaarke sub." }` (validated by allOf.if.then: required when tenancyModel=Model2Dedicated). 2) Update SKILL.md Step 1 to prompt. 3) Route into CreateRunRequest.NonSecretParameters['subscriptionId']. 4) Add seam test asserting a Model 2 CreateRunRequest missing subscriptionId returns 400.
  - **verification** — 1) ajv-cli validate a Model2 intake missing subscriptionId → error. 2) Seam test POST /api/runs with subscriptionId → run.Parameters.NonSecret['subscriptionId'] round-trips. 3) H1 unit test with subscriptionId populated → succeeds; without → returns MissingSubscriptionId.
  - **consequence_if_unfixed** — First Model 2 dispatch: H0 preflight fails with `MissingSubscriptionId`; if H0 passes because it treats subscriptionId as optional-for-preflight, H1 fails hard-stop within ~20s. Operator's only recourse is manual Cosmos edit — no L2 endpoint to add nonSecret params post-CreateRun. Model 1 might work if the skill auto-injects the Spaarke shared sub-id at CreateRun time, but that is not documented anywhere in SKILL.md.

- **`ISH-07` [MEDIUM] [intake-schema-vs-handlers]**: Intake schema defines `operatorUpn` as optional email 'for audit trail'
  - **file:where** — intake.schema.json:65-68 (`operatorUpn`) — grep confirms zero handlers read NonSecret['operatorUpn']
  - **fix estimate** — 15m · **main-session-only?** yes
  - **proposed_fix** — Delete `operatorUpn` from intake.schema.json. Update SKILL.md Step 1 to remove the operatorUpn prompt. Add a comment referencing the JWT source of authoritative identity.
  - **consequence_if_unfixed** — Operator supplies a value that goes nowhere. If it drifts from the actual authenticated identity (e.g., operator pastes wrong email), audit trail has misleading metadata that looks authoritative but isn't. Low-severity misleadingness rather than a hard failure.

- **`ISH-09` [HIGH] [intake-schema-vs-handlers]**: prereqs
  - **file:where** — scripts/provisioning-prereqs/prereqs.yaml (grep output: {subId}, {l2UamiPrincipalId}, {l2UamiSpId}, {l2UamiClientId}, {graphAppId}, {artifactsStorageId}, {acrId}, {bffAppServiceId}, {kvResourceId}, {sbNamespace}, {sbName}, {bffAppId}, {dvUrl}, {adminDvUrl}, {pinnedVer}, {containerTypeId}, {env}) vs intake.schema.json (defines only customerId, tenantId, region, openAiRegion, environment)
  - **fix estimate** — 2h · **main-session-only?** yes
  - **proposed_fix** — Add a companion `scripts/provisioning-prereqs/context-defaults.dev.json` (and .prod.json) files providing all non-intake tokens as environment-scoped constants. Extend intake.schema.json OR add a companion `provisioning-context.schema.json` documenting the shape. Update validate.ps1 to merge intake + context-defaults into one substitution map before token expansion. Cross-reference from schema description.
  - **verification** — Dry-run validate.ps1 with a Model 1 intake — assert every prereqs.yaml token is substituted (grep the rendered CLI commands for any remaining `{...}` and fail if found).
  - **consequence_if_unfixed** — Any prereqs.yaml check whose substitution token is unresolved either (i) executes with a literal `{subId}` string which the az CLI treats as a resource name (returns weird 404s masking the real prereq failure), (ii) fails ps1 substitution with a parse error, or (iii) silently uses `$null` and returns 'not present' — all three produce misleading preflight results and could cause the skill to declare 'prereqs OK' when a critical piece is missing.

- **`ISH-10` [HIGH] [intake-schema-vs-handlers]**: The Step 0 Operator-role probe sends `profile: 'dev'` — not a valid enum value per schema
  - **file:where** — .claude/skills/provision-environment/SKILL.md:124 (Step 0 role-probe body: `{"customerId":"__role-probe__","tenancyModel":"Model1Shared","profile":"dev","tenantId":"__probe__"}`) vs intake.schema.json:37-44 (profile enum = spaarke-hosted-model1-trial | spaarke-hosted-model2 | customer-owned-model2)
  - **fix estimate** — 45m · **main-session-only?** yes
  - **proposed_fix** — Replace Step 0 probe with `GET /api/runs?customerId=__probe__` (Reader-scoped): 200 = has at least Reader role; 403 = missing role; 401 = token invalid. Doesn't touch validation surface. Alternative: dedicated `GET /api/whoami` endpoint that returns the caller's role claims — clearest intent.
  - **consequence_if_unfixed** — Step 0 role probe returns 400 (bad body) — skill interprets as 'unauthorized' → aborts session even when the operator has correct role. False-negative auth check.

- **`ISH-12` [MEDIUM] [intake-schema-vs-handlers]**: Schema requires `environment` as top-level
  - **file:where** — intake.schema.json:31-35 (`environment` required enum dev/demo/prod) — grep confirms no handler reads NonSecret['environment']; skill uses it to pick L2 base URL + Bicep param file
  - **fix estimate** — 30m · **main-session-only?** yes
  - **proposed_fix** — Rename in intake.schema.json + SKILL.md. Update prereqs.yaml token from {env} to {controlPlaneEnv}. Add note that H2a.environmentName is separately derived.
  - **consequence_if_unfixed** — Operator confusion when Model 2 stamps are all deployed via `environment=prod` control-plane but the customer stamp itself is a Sandbox/Trial. The single-word field name invites misinterpretation.

- **`COMP-14` [HIGH] [completeness]**: Intake audit flagged tenantId/mode/region/tier/operatorUpn/openAiRegion as vestigial or wrongly-shaped in CreateRunRequest
  - **file:where** — runs/trial1-intake.json + scripts/provisioning-prereqs/intake.schema.json
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Make `environment` REQUIRED in intake.schema.json; add a Step 0.5a validator that fails fast if `environment` is empty. Remove or repurpose the vestigial fields (region, tier, operatorUpn, openAiRegion) with explicit consumer documentation.
  - **consequence_if_unfixed** — trial1-intake.json omits environment (typo, missing field) → Step 0.5b substitutes empty `{env}` → prereqs recipes hit `spaarke-prov--kv` (missing env token) → all Step 0.5 recipes 404 → HARD STOP with cryptic error.
  - **why_audit_missed** — Intake audit compared schema vs L2 CreateRunRequest but did not compare schema vs SKILL-step consumers (Step 0.5b substitutes environment; nothing enforces its presence).

**Sub-agent OK (2):**

- **`ISH-08` [MEDIUM] [intake-schema-vs-handlers]**: Schema declares openAiRegion as optional with a long description referencing customer
  - **file:where** — intake.schema.json:55-58 (`openAiRegion`) — no handler reads NonSecret['openAiRegion']; H2a reads only 'location'
  - **fix estimate** — 1h · **main-session-only?** no
  - **proposed_fix** — Option (a) preferred: add H2a.OpenAiLocationParameterKey; wire into Bicep deploy runner as `openAiLocation` parameter override. Update H2a unit test to verify override precedence. Docstring on schema field should say 'When omitted, Bicep parameter file default (westus3) wins'.
  - **verification** — H2a seam test: run with NonSecret['openAiRegion']='eastus2' — captured Bicep params include openAiLocation='eastus2'.
  - **consequence_if_unfixed** — Operator's `openAiRegion` selection has no effect at runtime; Bicep uses default westus3. If operator selects eastus2 to co-locate with a customer-region OpenAI capacity constraint, they get silent-default-westus3 and the model deploy fails or costs more.

- **`ISH-11` [MEDIUM] [intake-schema-vs-handlers]**: Schema enforces via allOf that Model1Shared pairs ONLY with spaarke-hosted-model1-trial, and Model2Dedicated pairs ONLY with spaarke-hosted-model2 | customer-owned-model2
  - **file:where** — intake.schema.json:80-104 (allOf.if.then invariants: tenancyModel×profile) vs src/server/services/Sprk.Provisioning.ControlPlane.Api/Api/RunsEndpoints.cs:325-332 (CreateRun validates non-empty only, no enum + no cross-field invariant)
  - **fix estimate** — 45m · **main-session-only?** no
  - **proposed_fix** — Add enum validation to CreateRunRequest (either via [AllowedValues] attribute, FluentValidation, or explicit switch in CreateRun). Add cross-field invariant check. Mirror the schema's allOf logic.
  - **consequence_if_unfixed** — A direct-API caller (test harness, retry script) supplying an invalid pair gets a run created + handlers that read tenancyModel misbehave (H5 tier derivation, H11 user provisioning gate). Downstream failures are cryptic (not 'invalid tenancy/profile pair').

---

### Wave 6: Batch-mode remediation (main-session; per DEP-06 gate to post-Waves-1-5)

**Rationale.** BAT-01..16 depend on Wave 4 SKILL.md gates being ready to consume batch-source variables, on Wave 5 intake.schema.json having the batch-policy fields, on Wave 2/B2 endpoints accepting the batch fields, and on Wave 1 ADR having chosen batch-vs-attestation resolution.

**Wave sizing.** 15 findings; ~12.8h known-effort; 0 findings without numeric estimate.

**Main-session-only (13):**

- **`BAT-01` [CRITICAL] [batch-mode]**: Step 1
  - **file:where** — .claude/skills/provision-environment/SKILL.md:320-321
  - **fix estimate** — 1h · **main-session-only?** yes
  - **proposed_fix** — 1) Wrap every prompt-line in Steps 0d, 0.5 (if any interactive), 1a, 1g, 2 gate-question, 4 Failed prompt, 5a-5d, 7b prompts with `if ($script:SkipInteractiveIntake) { <use batch source or fail-fast> } else { <existing prompt> }`. 2) Add explicit consumption of $script:SkipStep0_5 at Step 0.5b entry — early-return the iteration.
  - **verification** — Run trial1-intake.json in --batch mode with mocked L2; assert the skill does not read from stdin at any point (redirect stdin to /dev/null; skill should still complete or exit non-zero with a documented reason).
  - **consequence_if_unfixed** — Dispatch of trial1-intake.json in --batch mode will proceed past Step 1.0 with pre-filled values, then BLOCK on stdin at the first interactive prompt (Step 0d MCP if disconnected, else Step 1g 'Proceed to preflight?'). The batch invocation appears to work but hangs indefinitely with no operator to answer.

- **`BAT-02` [CRITICAL] [batch-mode]**: Line 208 substitutes `-replace '\{openAiRegion\}', $openAiRegion` in prereq recipes, comment says 'populated from batch intake or default westus3'
  - **file:where** — .claude/skills/provision-environment/SKILL.md:208 (Step 0.5b recipe substitution) and lines 313-321 (batch pre-fill block)
  - **fix estimate** — 10 min · **main-session-only?** yes
  - **proposed_fix** — Add `$openAiRegion = if ($intake.openAiRegion) { $intake.openAiRegion } else { 'westus3' }` inside the batch pre-fill block between lines 319 and 320.
  - **verification** — Run --batch with trial1-intake.json, capture the bash -c invocation for PRQ-E-14 (or equivalent), assert the substituted recipe contains `--location westus3` not `--location ''`.
  - **consequence_if_unfixed** — Step 0.5 PRQ-E-14 (OpenAI model catalog check per DS-5 c6-1) runs with `--location ''` — either fails with az CLI error or silently returns wrong region, defeating F1/F4 fresh-sub gotcha prevention.

- **`BAT-04` [HIGH] [batch-mode]**: Step 0d asks operator 'Continue anyway? (yes/no)' when Dataverse MCP is disconnected
  - **file:where** — .claude/skills/provision-environment/SKILL.md:129-140 (Step 0d MCP prompt)
  - **fix estimate** — 20 min · **main-session-only?** yes · **depends_on** BAT-01
  - **proposed_fix** — Add `mcpDisconnectPolicy` enum field to intake.schema.json; add `if ($script:SkipInteractiveIntake) { <read policy> } else { <existing prompt> }` around Step 0d prompt.
  - **verification** — Simulate MCP disconnect; run --batch with `mcpDisconnectPolicy: proceedWithFallback`; assert no stdin read + fallback path used.
  - **consequence_if_unfixed** — Any batch invocation on a machine with disconnected Dataverse MCP hangs at Step 0d. This is common per skill Line 1334 ('MCP disconnect is common').

- **`BAT-05` [HIGH] [batch-mode]**: Step 1a: 'if any run exists, present the operator with the existing run history and confirm: customerId {id} has {N} prior runs
  - **file:where** — .claude/skills/provision-environment/SKILL.md:347-351 (Step 1a upgrade probe)
  - **fix estimate** — 30 min · **main-session-only?** yes · **depends_on** BAT-01
  - **proposed_fix** — Add `acknowledgeUpgradeMode` field; gate the prompt on $script:SkipInteractiveIntake; on batch + prior-runs-detected + acknowledgeUpgradeMode=false → HARD STOP.
  - **verification** — Run --batch with customerId that has prior runs + acknowledgeUpgradeMode=false; assert non-zero exit and clear diagnostic.
  - **consequence_if_unfixed** — trial1 has already been used in prior sessions — this batch dispatch WILL hit this prompt and hang on stdin (or silently proceed as upgrade without operator awareness, silently changing invariant semantics per §14A upgrade model).

- **`BAT-06` [HIGH] [batch-mode]**: Step 1g asks 'Proceed to preflight (H0)? (yes/no)' and Step 2 asks 'Preflight passed
  - **file:where** — .claude/skills/provision-environment/SKILL.md:471-474 (Step 1g preflight gate) and lines 525 (Step 2 proceed-to-Step 3 gate)
  - **fix estimate** — 15 min · **main-session-only?** yes · **depends_on** BAT-01
  - **proposed_fix** — Wrap both gate-questions with `if ($script:SkipInteractiveIntake) { Write-Host '(batch: auto-advance)' } else { <existing prompt> }` and cross-reference the Step 3 rule so operators don't think all gates are auto-advanced.
  - **verification** — Batch dispatch to end of Step 2 — asserts no stdin read + progress logs contain 'batch: auto-advance' at 1g and 2.
  - **consequence_if_unfixed** — Batch invocation blocks at 1g even before reaching the Step 3 phrase gate.

- **`BAT-07` [HIGH] [batch-mode]**: Table row: 'Failed → Present failure + POST /api/runs/{id}/resume option to operator'
  - **file:where** — .claude/skills/provision-environment/SKILL.md:748 (Step 4b Failed row) and line 749 (Quarantined row)
  - **fix estimate** — 1h · **main-session-only?** yes · **depends_on** BAT-01
  - **proposed_fix** — Add both policy fields; add `if ($script:SkipInteractiveIntake) { <apply policy> } else { <ask> }` around the Failed prompt; for Quarantined + batch, ALWAYS write structured JSON diagnostic to runs/{runId}-quarantine.json + exit 3 (distinct non-zero).
  - **verification** — Simulate Failed run in batch with each policy value; assert correct L2 API call (resume once vs no call) + correct exit code.
  - **consequence_if_unfixed** — Batch dispatch that hits a Failed handler blocks on stdin waiting for 'resume/abandon' answer. Quarantine has no exit path.

- **`BAT-08` [CRITICAL] [batch-mode]**: All four manual-gate patterns rely on an interactive operator ('type resume', 'type abandon', 'type status')
  - **file:where** — .claude/skills/provision-environment/SKILL.md:756-832 (Step 5 Manual Gate Handling — 5a admin consent, 5b quota bump, 5c SPE 24h, 5d generic)
  - **fix estimate** — 3h · **main-session-only?** yes · **depends_on** BAT-01
  - **proposed_fix** — 1) Add Step 5.0 'Batch-mode manual gate handling' section documenting the three artifacts + exit code. 2) Add onManualGatePolicy to intake schema. 3) Wrap each 5a-5d prompt with $script:SkipInteractiveIntake gate → invoke batch protocol. 4) Add --resume flag to skill to re-invoke after gate cleared (reads runs/{runId}-gate.json).
  - **verification** — Simulate H1 quota-bump gate in batch; assert (a) exit code 4, (b) runs/{runId}-WAITING.md written with actionable content, (c) runs/{runId}-gate.json parseable + contains handler=H1.
  - **consequence_if_unfixed** — Batch dispatch that hits H0.5 admin consent, H1 quota bump, H8 SPE 24h, or any other WaitingOnGate hangs indefinitely OR (worse) silently exits with no artifacts — operator cannot resume because they don't know a gate was hit. This IS realistic — task 186 dispatch may well hit H8 (SPE 24h) or another gate.

- **`BAT-09` [HIGH] [batch-mode]**: Line 984 header mentions '(or batch mode: --postmortem-file <path
  - **file:where** — .claude/skills/provision-environment/SKILL.md:984-997 (Step 7b) — --postmortem-file
  - **fix estimate** — 2h · **main-session-only?** yes
  - **proposed_fix** — 1) Add Step 7 batch-mode subsection documenting the three cases (file provided + valid; file provided + invalid; file omitted → auto-generated minimum). 2) Add validator + writer code sketches. 3) Cross-reference --postmortem-file in Step 1.0 top-of-section and in Trigger Phrases list.
  - **verification** — Batch dispatch to Succeeded; assert lessons-learned.md written + INDEX.md row appended; repeat with --postmortem-file pointing to a stub file with missing sections; assert clear error.
  - **consequence_if_unfixed** — Step 7 in batch either hangs on interactive prompts (line 986 'Present the operator with each template section') or is silently skipped, regressing the 'two-level lessons process' rule in line 1021. INDEX.md row is not written; cross-run audit corpus loses this run.

- **`BAT-11` [HIGH] [batch-mode]**: No section documents what batch invocations output on mid-way failure
  - **file:where** — .claude/skills/provision-environment/SKILL.md — batch failure UX (no section exists)
  - **fix estimate** — 2h · **main-session-only?** yes
  - **proposed_fix** — Author the section; ensure Steps 4, 5, 6, 7 error paths all write to runs/{runId}-error.json + set the documented exit code.
  - **verification** — Fault-inject each failure mode; assert artifact + exit code match table.
  - **consequence_if_unfixed** — task 186 batch dispatch — if it hits any failure — leaves no machine-readable artifact for the dispatching orchestrator to reason about. Cannot resume, cannot triage.

- **`BAT-12` [MEDIUM] [batch-mode]**: Schema declares `notes` field with description 'archived to provisioning-runs/{customerId}-{runId}/intake
  - **file:where** — scripts/provisioning-prereqs/intake.schema.json:75-77 — `notes` field
  - **fix estimate** — 20 min · **main-session-only?** yes
  - **proposed_fix** — Add a code block at end of Step 1.0 batch pre-fill: `New-Item -ItemType Directory -Force $runDir; Set-Content -Path (Join-Path $runDir 'intake.md') -Value <template with notes + sha256 + upn + ts>`.
  - **verification** — Dispatch trial1-intake.json --batch; assert provisioning-runs/trial1-{runId}/intake.md exists with the notes content and computed SHA-256.
  - **consequence_if_unfixed** — Schema promises an audit artifact that never materializes. Every batch dispatch violates its own schema documentation.

- **`BAT-13` [MEDIUM] [batch-mode]**: Trigger phrases list only mentions `/provision-environment {customerId}` and `/provision-environment` (interactive)
  - **file:where** — .claude/skills/provision-environment/SKILL.md:22-29 (Trigger phrases block) and line 1264 (Dry-Run Mode)
  - **fix estimate** — 15 min · **main-session-only?** yes
  - **proposed_fix** — Extend Trigger phrases bullet list; add 'Invocation matrix' subsection at top summarizing interactive vs batch vs dry-run vs resume + which CLI flags each accepts.
  - **verification** — Grep for '--batch' in top 100 lines of SKILL.md — must appear alongside slash-command triggers.
  - **consequence_if_unfixed** — Batch mode is under-discoverable; future operators + subagents miss it and re-invent.

- **`BAT-14` [MEDIUM] [batch-mode]**: Step 0c hard-codes `$env = "dev"  # or prod (from intake or arg)` and uses `$env` throughout for L2 base + token resource
  - **file:where** — .claude/skills/provision-environment/SKILL.md:110 and line 315 — variable `$env` vs `$environment`
  - **fix estimate** — 30 min · **main-session-only?** yes
  - **proposed_fix** — Rename `$env` to `$environment` throughout Steps 0c, 0.5b, 4a token-refresh; delete the hard-coded assignment on line 110; add `$environment = if ($env) { $env } else { 'dev' }` fallback for interactive mode where operator hasn't chosen yet.
  - **verification** — Batch dispatch with `environment: prod`; grep skill runtime logs for L2 base URL — must be `spaarke-provisioning-controlplane-prod`, not `-dev`.
  - **consequence_if_unfixed** — In batch mode, Step 0c's `$env = "dev"` line runs unchanged — every batch dispatch is FORCED TO DEV regardless of `intake.environment`. A batch dispatch aimed at prod would silently hit dev L2. Data-plane divergence.

- **`BAT-15` [MEDIUM] [batch-mode]**: The skill code sketch assumes `$BatchIntakeFile` is populated somehow, but the mechanism is never documented
  - **file:where** — .claude/skills/provision-environment/SKILL.md:293 — comment '# Skill invoked with --batch flag: $BatchIntakeFile is the JSON path'
  - **fix estimate** — 30 min · **main-session-only?** yes
  - **proposed_fix** — Add `param([string]$BatchIntakeFile, [string]$PostmortemFile, [switch]$Resume, [switch]$DryRun)` block explicitly to the Step 1.0 code sample; document the slash-command → param translation in a new subsection.
  - **verification** — Independent reader can invoke skill with a JSON path and no other setup — no reliance on ambient variable.
  - **consequence_if_unfixed** — task 186 dispatch mechanism is undocumented — the batch-mode invocation contract lives only in comments. Cannot be re-implemented safely.

**Sub-agent OK (2):**

- **`BAT-10` [MEDIUM] [batch-mode]**: Intake schema has 5 required + 6 optional fields
  - **file:where** — scripts/provisioning-prereqs/intake.schema.json — schema-wide
  - **fix estimate** — 45 min · **main-session-only?** no
  - **proposed_fix** — Extend intake.schema.json with the 8 fields + update examples[] to show a batch-safe combination. Bump $id or version note if schema-versioning is used.
  - **verification** — ajv validate the extended schema against current trial1-intake.json (must still pass — new fields all optional) and against a new full-batch example.
  - **consequence_if_unfixed** — Even after BAT-01..09 fixes, operator can't parameterize batch behavior without adding fields ad-hoc. Schema becomes drift source.

- **`BAT-16` [LOW] [batch-mode]**: Dry-Run Mode section documents `--dry-run` slash-command flag but never addresses interaction with `--batch`
  - **file:where** — .claude/skills/provision-environment/SKILL.md:1262-1279 (Dry-Run Mode)
  - **fix estimate** — 10 min · **main-session-only?** no
  - **proposed_fix** — Add a one-paragraph 'Combining with --batch' subsection at end of Dry-Run Mode.
  - **verification** — Read section end-to-end; unambiguous combination semantics.
  - **consequence_if_unfixed** — Ambiguous behavior when a smoke-test wants to combine both.

---

### Wave 7: Missing-dimension sweeps (auth-flow, network egress, RBAC, cost, Service Bus, load, structured logs)

**Rationale.** COMP verifier surfaced 12 dimensions no single audit owned. Each is a self-contained sweep-then-fix; subagents can own the subagent-ok items in parallel.

**Wave sizing.** 11 findings; ~0.0h known-effort; 11 findings without numeric estimate.

**Main-session-only (1):**

- **`COMP-12` [HIGH] [completeness]**: CLAUDE
  - **file:where** — Cross-worktree coordination: auth-v4-coord-response subagent is active in this session + KV secret lifecycle governance in root CLAUDE.md §17
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Coordinate task 186 window with auth-v4 subagent (SendMessage to auth-v4-coord-response) BEFORE dispatch. Longer term: handler-side 401 retry with token-refresh.
  - **consequence_if_unfixed** — Live-fire task 186 races auth-v4 rotation → mid-H7 handler 401 → FailureClassifier likely marks Failed (auth 401 not commonly classified as Retryable) → Quarantined → operator abandons a good run.
  - **why_audit_missed** — Cross-worktree parallel work is a session-orchestration concern; static audits did not consider concurrent parallel branches.

**Sub-agent OK (10):**

- **`COMP-01` [CRITICAL] [completeness]**: Zero of the 7 audits touched auth-flow: OBO between operator and L2, MI FIC from L2/Worker UAMI to per-customer app-reg, token-cache TTL vs long-running handler duration (H8 SPE container-type provisioning historically >...
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/**/Handlers/** (every handler that mints a customer-tenant token) + Concurrency/CustomerRunGuard.cs
  - **fix estimate** — ? · **main-session-only?** no
  - **proposed_fix** — Add an AUTH-FLOW sweep as an 8th audit lens: grep every GetTokenAsync + FederatedTokenCredential + ClientSecretCredential + ManagedIdentityCredential; for each hit, document: (identity used, target audience, expected RBAC grant, expiry-refresh strategy, failure-classification path). Explicit deliverable: an auth-flow matrix table before task 186 dispatch.
  - **consequence_if_unfixed** — H1/H3/H5/H6/H7/H9/H10/H11/H13 can mid-run 401 on customer tenant with no retry-classification (FailureClassifier not proven to map AAD 401 to Retryable). Worse: enabling CustomerRunGuard=true on any real prod stack crashes the API at boot on missing KV secret. Silent halt.
  - **why_audit_missed** — The 7 audits were structured around static artifacts (skill text, handler bodies, schema, registry table, placeholders, prereqs). Auth is a *dynamic runtime concern* that crosses all of them — no single audit owned it, so it fell through.

- **`COMP-02` [CRITICAL] [completeness]**: No audit verified that the L2 App Service and Worker have unblocked egress to every downstream endpoint that H0
  - **file:where** — L2 App Service (Sprk.Provisioning.ControlPlane.Api) + Worker outbound to: Dataverse (variable per-customer *.crm.dynamics.com), Graph (graph.microsoft.com), ARM (management.azure.com), Cosmos, Service Bus, Platform KV, per-customer KV, ACR (for sidecar image pulls)
  - **fix estimate** — ? · **main-session-only?** no
  - **proposed_fix** — Before task 186 dispatch, run an egress probe from the deployed L2 App Service: `Invoke-WebRequest` against management.azure.com, graph.microsoft.com, the trial1 Dataverse URL, the shared KV, the customer KV, Cosmos, Service Bus, ACR. Fail-fast if any is unreachable. Add to prereqs.yaml as PRQ-S-15.
  - **consequence_if_unfixed** — First live run against a customer with SafeList / Private Endpoint / VNet-restricted platform stack silently hangs on outbound TCP for 60+ seconds before returning generic HttpRequestException. FailureClassifier likely maps this to unclassified failure → Quarantined without diagnostic → operator abandons run.
  - **why_audit_missed** — The audits looked at *what code calls* and *what config declares*, not at *whether the network path exists at runtime*. Networking is an operational-topology concern the static-artifact audits couldn't see.

- **`COMP-03` [CRITICAL] [completeness]**: trial1-intake
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/**/ProfileRegistry.cs (or equivalent) + trial1-intake.json line 6 (profile=spaarke-hosted-model1-trial)
  - **fix estimate** — ? · **main-session-only?** no
  - **proposed_fix** — Confirm ProfileRegistry.Resolve('spaarke-hosted-model1-trial') returns a non-empty phase-list that includes H4Shared and H4b (which handler audit already flagged as missing from DAG). Add a fixture test that fails if the string is not registered.
  - **consequence_if_unfixed** — If profile lookup silently fails-open, CreateRun accepts the intake and dispatches with an empty/default phase-list; run enters NotStarted → sits idle in Cosmos → operator sees no progress → assumes L2 is broken. If the lookup throws on unknown, run enters Failed(unclassified) → Quarantined with a stack-trace-only diagnostic.
  - **why_audit_missed** — Intake audit tracked field shape at the API-contract layer (CreateRunRequest); handler audit tracked implementation. Profile-string → phase-list *mapping* is the seam between the two, and neither audit owned it.

- **`COMP-05` [CRITICAL] [completeness]**: Handler audit confirmed handler classes exist but did not test: (a) worker crashes AFTER an external side-effect commits (e
  - **file:where** — Sprk.Provisioning.ControlPlane.Worker/Program.cs + Core/Reconciler/StateReconcilerService.cs + every handler with external side effects (H5 DV env create, H1 CogSvc create, H2a Bicep deploy, H9 zip deploy)
  - **fix estimate** — ? · **main-session-only?** no
  - **proposed_fix** — Add integration test in tests/integration/seam/ that kills the Worker mid-H5 and asserts run resumes to a consistent state. Document per-handler idempotency in handler XML docs.
  - **consequence_if_unfixed** — Real Azure crash (App Service restart, Worker OOM, deploy-slot swap) mid-H5 leaves a Dataverse env orphaned and a Cosmos run stuck in Running. Second dispatch on same customerId either 409s (blocked by sprk_currentrunid) OR duplicates the env (breaking §I5) OR silently succeeds on read but next handler fails on mismatched state.
  - **why_audit_missed** — Handler audit read handler code but did not construct kill-scenarios. Registry audit knew about sprk_currentrunid locking but did not simulate a crash to see if the lock is released or leaves an orphan run.

- **`COMP-06` [HIGH] [completeness]**: Rollback code exists (FailureClassifier, RollbackTransitions, QuarantineClearService) but no audit verified: (a) every handler failure path lands in a defined class, (b) Retryable* actually retries with backoff, (c) clea...
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Rollback/FailureClassifier.cs + RollbackTransitions.cs + QuarantineClearService.cs
  - **fix estimate** — ? · **main-session-only?** no
  - **proposed_fix** — 8th audit lens: rollback-taxonomy sweep. Deliverable: per-handler failure-class table + a test that proves clear-quarantine releases the lock.
  - **consequence_if_unfixed** — Registry audit already found clear-quarantine leaves sprk_currentrunid populated → customer is permanently blocked from re-provisioning. Combined with unclassified failures becoming Quarantined by default (per FailureClassifier fallback), any classification miss → permanent block on that customerId.
  - **why_audit_missed** — Handler audit stopped at 'handler body is real code'; registry audit noticed the lock-leak in passing but did not treat rollback as a first-class dimension.

- **`COMP-07` [HIGH] [completeness]**: Skill-drift audit flagged unabsorbed F15/F16/F16
  - **file:where** — infrastructure/bicep/** RBAC assignments + per-customer FIC SP creation in H3 + operator UAMI RBAC in provisioning-prereqs
  - **fix estimate** — ? · **main-session-only?** no
  - **proposed_fix** — RBAC matrix as a build artifact + a 'wait for propagation' helper in handlers that require freshly-granted RBAC (H3→H5, H10→H11).
  - **consequence_if_unfixed** — First live run: H3 creates the per-customer app-reg → grants it Contributor on the customer subscription → H5 immediately tries to use it → 403 because RBAC not propagated → FailureClassifier marks Failed (probably unclassified because 403 could be many things) → Quarantined → operator abandons.
  - **why_audit_missed** — Skill drift audit flagged the isolated symptoms (F15/F16/F16.5/F18) but there is no dimension that owns the *composite* RBAC picture.

- **`COMP-09` [HIGH] [completeness]**: No audit verified: (a) HandlerEnvelope size stays under 256KB (Service Bus Standard tier limit) — envelopes with runtime refs bundle (H4b bulk app-settings) could balloon, (b) DLQ has an operator-visible drain path, (c) ...
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Enqueue/HandlerEnvelope.cs + Worker/Dispatch
  - **fix estimate** — ? · **main-session-only?** no
  - **proposed_fix** — Add a size-guard in HandlerEnqueuer + a DLQ probe recipe in prereqs.yaml (PRQ-S-16). Document Service Bus tier requirement (Standard 256KB vs Premium 1MB).
  - **consequence_if_unfixed** — H4b envelope exceeds 256KB → Service Bus rejects with 413 at enqueue time → handler never dispatched → run silently stalls at WaitingOnGate-equivalent. OR double-retry burns down the customer's quota on side-effecting handlers (H1 CogSvc soft-lock, H5 DV env create).
  - **why_audit_missed** — Enqueue path is neither skill nor handler nor registry — it is transport, which no audit lens covered.

- **`COMP-10` [HIGH] [completeness]**: Cost envelope code exists (task 183) but no audit verified it is actually BLOCKING at H0
  - **file:where** — src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/ArmCostEnvelopeChecker.cs + H0
  - **fix estimate** — ? · **main-session-only?** no
  - **proposed_fix** — Assert H0 fail-fast semantics with a red-path test; add batch-mode default `costEnvelopePolicy: abort-on-overrun`.
  - **consequence_if_unfixed** — Silent overspend on first Model 1 Prod run. Or worse: preflight passes with warnings that the batch-mode caller ignores (per batch-mode audit's finding that gates without documented defaults halt) → run proceeds and provisions expensive resources.
  - **why_audit_missed** — Cost is a spec-NFR concern not owned by any of the 7 audit lenses.

- **`COMP-11` [MEDIUM] [completeness]**: Registry audit covered same-customer concurrency (sprk_currentrunid lock)
  - **file:where** — Worker parallelism + Cosmos partition contention + shared platform KV writes across parallel handlers
  - **fix estimate** — ? · **main-session-only?** no
  - **proposed_fix** — Load-test scenario in tests/integration/Sprk.Provisioning.ControlPlane.LoadTests + rate-limit guard in ServiceBusHandlerEnqueuer.
  - **consequence_if_unfixed** — Multi-tenant onboarding batch (probable Q4 use case) throttles at platform KV or Cosmos, some runs succeed and some Quarantine on 429. Operator cannot distinguish real failure from throttling.
  - **why_audit_missed** — Registry audit scoped to one-customer semantics; no audit owned multi-customer horizontal load.

- **`COMP-13` [MEDIUM] [completeness]**: NFR-11 requires every operator action be auditable (operator's OWN AAD identity, per §17 skill entry)
  - **file:where** — Every SKILL.md step + L2 API + Worker logging
  - **fix estimate** — ? · **main-session-only?** no
  - **proposed_fix** — Add structured-log schema; verify with a test that emits a fake run and asserts required fields present + secrets absent.
  - **consequence_if_unfixed** — Post-run forensic (which handler halted?) fails because logs are unstructured or secrets leaked. Compliance failure on multi-tenant Model 1 Prod.
  - **why_audit_missed** — Observability was not in any of the 7 lenses.

---

### Wave 8: Runtime resilience follow-ons (EXEC gaps not consumed elsewhere)

**Rationale.** Any EXEC-xx findings not folded into earlier waves; small integration tests + Model1Shared branching audit.

**Wave sizing.** 1 findings; ~0.0h known-effort; 1 findings without numeric estimate.

**Main-session-only (1):**

- **`EXEC-09` [MEDIUM] [EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.]**: The DAG has no branch for Model 1 Shared vs Model 2 Dedicated
  - **file:where** — DagAdvancer.HandlerDependencies + HandlerDispatchRegistrationModule (no TenancyModel-conditional branching)
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Add a Model1Shared end-to-end integration test that runs a full DAG with a shared-fabric fixture and asserts each handler's Succeeded outcome carries the 'shared-reused' or 'noop' evidence in outcome payload. Handler-by-handler audit for Model1Shared conditional logic — dispatch failures under Model1Shared are silent today.
  - **consequence_if_unfixed** — First Model 1 Shared trial1 dispatch may either (a) duplicate shared infra per-customer (EXEC-04 already flagged the H2a fallback) OR (b) collide with existing shared resources and 409/400 at Bicep/ARM. No integration test asserts Model 1 Shared full-dispatch cleanliness.
  - **why_audit_missed** — Handler audit inspected each handler body for correctness but did not systematically cross-check Model1Shared branching.

---

### Unassigned findings (BUG in wave plan — every finding must land in a wave)

- **`DEP-02` [HIGH] [dependency-and-parallel-safety]**: 12+ findings all edit 
  - **file:where** — .claude/skills/provision-environment/SKILL.md — SKILL-01, SKILL-02, SKILL-03, SKILL-04, SKILL-05, SKILL-06, SKILL-07, SKILL-08, plus Step 5a and Step 6b placeholder templates from placeholders audit, plus batchMode intake-schema references
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Sequence: build a single SKILL.md remediation task that applies ALL of SKILL-01..08 + Step 5a/6b placeholder substitutions + batchMode intake-schema references in ONE pass, ideally as a Write (full rewrite) rather than incremental Edits. Execute as one main-session task; do NOT fan out to sub-agents.
  - **consequence_if_unfixed** — Attempting parallel dispatch produces (a) permission denials on sub-agents, (b) Edit-conflicts when two agents chase overlapping unique-string requirements, (c) partial edits leaving SKILL.md in intermediate broken state during the window between edits.
  - **why_audit_missed** — Individual audits scoped to their own concern; none examined the aggregate write-locus contention across audits.

- **`DEP-03` [HIGH] [dependency-and-parallel-safety]**: Three audits each propose fixes that touch prereqs
  - **file:where** — SKILL-08 (Step 0.5b substitution extension) vs prereqsDebt recipe rewrites (semantic-assertion, bash-var expansion) vs placeholders findings on prereqs.yaml
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Create a single Wave 3 sub-plan titled 'prereqs pipeline hardening' that owns SKILL-08 + all prereqsDebt findings + placeholders prereqs.yaml findings. Sub-agent can edit prereqs.yaml (not under .claude/); main-session must edit SKILL.md; coordinate via one plan doc.
  - **consequence_if_unfixed** — Partial wave completion leaves Step 0.5 either silently broken (recipes exit 0 on unresolved literal via missing exit-1 guard) or loudly broken (az unrecognized-argument on unresolved placeholders). Both aborted-provisioning modes for every subsequent operator invocation.
  - **why_audit_missed** — Placeholder + recipe-contract + resolver-extension were audited by three different lenses; none saw the wave-composition need.

- **`DEP-04` [MEDIUM] [dependency-and-parallel-safety]**: The same underlying defect — skill posts a shape L2 doesn't accept — is reported four times: SKILL-03 (Step 2 body has fictional mode + tenantId), SKILL-04 (Step 4 resume body), SKILL-05 (missing customerId query param),...
  - **file:where** — SKILL-03/04/05 (skillDrift) ↔ intake_summary fictional-mode + resume-body-drop + missing-customerId-query ↔ CreateRunRequest shape drift
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Group under one wave task 'L2 endpoint contract alignment sweep' covering both SKILL.md edits and intake.schema.json field pruning (drop mode, drop top-level tenantId in favor of nonSecretParameters, drop openAiRegion + operatorUpn as vestigial). Single verification pass: curl -X POST /api/runs with the corrected body against dev L2, then curl every poll/gate/resume endpoint with corrected URL shape.
  - **consequence_if_unfixed** — Four separate PRs or four separate task IDs inflate accounting overhead and risk one sub-fix landing without the others, leaving an inconsistent intermediate state (e.g. Step 2 fixed but Step 4 resume still posts fictional body).
  - **why_audit_missed** — Four audits, each catching one instance of the same shape-drift defect.

- **`DEP-05` [MEDIUM] [dependency-and-parallel-safety]**: Aggregated remediation set spans BOTH boundary classes: 
  - **file:where** — Wave planning across all seven audits — mix of .claude/** paths (main-only) and src/**, scripts/**, infrastructure/** paths (subagent-safe)
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Publish a 2-lane wave plan explicitly: Lane A (main-only, serialized): SKILL.md aggregate rewrite + intake.schema.json update + constraints/provisioning.md update. Lane B (parallel-subagents): (b1) DagAdvancer + HandlerIds consts + registration, (b2) RunsEndpoints CreateRunRequest + Cosmos state machine, (b3) DataverseEnvironmentRecord + handler NonSecret writes (H2a/H5/H6/H7/H9/H13), (b4) prereqs.yaml recipe rewrites (semantic assertions, bash-var escaping, expect-block classifier). Kick off Lane B first (long-running), then complete Lane A in the main session.
  - **consequence_if_unfixed** — Dispatching without boundary-awareness produces sub-agent permission denials on SKILL.md and wastes clock. Or, worse, dispatcher assumes all findings are subagent-safe and never coordinates the main-session Lane A pass — Lane B lands but Lane A rots.
  - **why_audit_missed** — Each audit surfaced findings without boundary annotation; only cross-cutting synthesis reveals the two-lane structure.

- **`DEP-06` [HIGH] [dependency-and-parallel-safety]**: Batch mode findings (16) enumerate downstream operator-prompt sites that lack intake-schema fields
  - **file:where** — cross-wave: batchMode findings (16 items) depend on Waves 1-3 landing first
  - **fix estimate** — ? · **main-session-only?** yes
  - **proposed_fix** — Explicitly gate batchMode audit remediation as Wave 4 post-Waves-1-3. Do NOT dispatch batchMode fixes in parallel with SKILL-06/07/08.
  - **consequence_if_unfixed** — Batch-mode schema built against broken flow — when subsequent SKILL fixes rename states or restructure gates, schema fields become vestigial or misnamed, requiring re-work.
  - **why_audit_missed** — BatchMode audit examined its own surface without considering that the surface's stability depends on other audits settling first.

---

## Duplicate / cross-cited findings

These defects appeared under multiple audit lenses. Each cluster designates ONE canonical ID; the cross-refs are retained in the wave plan so remediation cites all auditors, but the FIX effort is estimated only once (against the canonical).

- **tenantId + intake shape drift (top-level vs nonSecretParameters)**
  - **Canonical**: `ISH-01`
  - **Cross-refs**: `SKILL-03`, `DEP-01`, `DEP-04`
- **/resume body drops operator UPN + phrase**
  - **Canonical**: `ISH-07`
  - **Cross-refs**: `SKILL-04`, `DEP-04`
- **Missing ?customerId= query on GET/POST endpoints**
  - **Canonical**: `SKILL-05`
  - **Cross-refs**: `ISH-08`, `DEP-04`
- **Poll enum drift (Succeeded/Drifted vs Completed/Failed/Cancelled/Quarantined)**
  - **Canonical**: `SKILL-06`
  - **Cross-refs**: `EXEC-01 (adjacent)`, `DEP-04`
- **DagAdvancer missing H4Shared/H4b keys**
  - **Canonical**: `HANDLER-01`
  - **Cross-refs**: `HANDLER-02 (subsumed)`, `EXEC-01 (extends: H9 edges)`
- **sprk_currentrunid never released on Failed terminal state**
  - **Canonical**: `REG-01`
  - **Cross-refs**: `EXEC-07`, `COMP-06 (rollback taxonomy sweeps this)`
- **Dataverse MCP unavailable in batch/subagent context - need az rest fallback**
  - **Canonical**: `COMP-08`
  - **Cross-refs**: `EXEC-08`
- **prereqs.yaml Step 0.5b placeholder substitution only handles {env}/{openAiRegion}**
  - **Canonical**: `SKILL-08`
  - **Cross-refs**: `PLX-04`, `PLX-05`, `PLX-06`, `PLX-07`, `PRQ-04`, `EXEC-10 (extends: scope-bound tokens)`
- **Step 2->3->4 confirmation gate fires AFTER POST /api/runs enqueues H0**
  - **Canonical**: `EXEC-02`
  - **Cross-refs**: `SKILL-03 (removes fictional mode= but leaves gap)`, `BAT-03 (batch-vs-attestation)`
- **tenancyModel silent fallback to Model2Dedicated in H2a**
  - **Canonical**: `EXEC-04`
  - **Cross-refs**: `ISH-01 (root cause: nonSecret vs top-level placement)`
- **Batch-mode skip-gate flags assigned but never read**
  - **Canonical**: `BAT-01`
  - **Cross-refs**: `BAT-04`, `BAT-05`, `BAT-06`, `BAT-07`, `BAT-08 (all depend_on BAT-01)`

---

## Findings requiring human judgment (escalate BEFORE Wave 1)

These are not mechanically fixable — each needs an operator decision (per root `CLAUDE.md` §6.5 ADR-conflict resolution). All flow through Wave 0 and their answers feed the Wave 1 ADR-note.

- **`DEP-01` [CRITICAL]**: Canonical tenantId propagation path: (A) nonSecretParameters["tenantId"] as the ONLY source; (B) top-level CreateRunRequest.tenantId field; (C) both, with precedence.
- **`SKILL-02` [CRITICAL]**: Registry probe replacement for fictional GET /api/runs?customerId=: (A) Dataverse MCP alt-key GET on sprk_dataverseenvironment; (B) call CustomerRunGuard endpoint; (C) skip pre-run history and rely on 409 conflict semantics.
- **`BAT-03` [CRITICAL]**: Batch-mode vs mandatory attestation collision: (A) intake schema adds confirmationAcknowledgment const-string; (B) batch mode HARD STOP after Step 2 in execute mode.
- **`EXEC-02` [CRITICAL]**: Step 2->3->4 architectural redesign: (A) turn Step 2 into client-side dry-run + move confirmation phrase to fire BEFORE POST /api/runs; (B) file L2 spec change adding Enqueued-Awaiting-Confirm Cosmos state.
- **`EXEC-03` [CRITICAL]**: Combined with BAT-03. Reserve batch for --dry-run OR sign intake with operator JWT + L2 verifies.
- **`ISH-03` [CRITICAL]**: Intake schema field boundary: (A) mechanical prune to match current CreateRunRequest; (B) widen CreateRunRequest to accept intake fields (1d effort).
- **`HANDLER-11` [MEDIUM]**: Add Drifted as a first-class RunStatus? (medium effort if yes; triage-only if no.)
- **`DEP-08` [MEDIUM]**: Umbrella item that enumerates the above four decisions.
- **`REG-04` [HIGH]**: CustomerRunGuard MI-FIC seam decision (mentioned in DEP-08).
- **`COMP-12` [HIGH]**: Coordinate task 186 window with parallel auth-v4 subagent (SendMessage timing).

---

## Not-scoped-here (out-of-band tracking)

Findings that require infrastructure or coordination outside this project's remediation window. Each is retained in-wave (nothing dropped per user directive) but with an escape-valve for topology/coordination work.

- **`COMP-02` [CRITICAL]**: L2 outbound network egress verification requires Azure networking probe from deployed App Service. If VNet/Private Endpoint work is out-of-project, track as Azure networking follow-up ticket.
- **`COMP-07` [HIGH]**: RBAC propagation matrix build artifact. If per-customer FIC RBAC lifecycle is owned by auth-v4 worktree, coordinate handoff there.
- **`COMP-11` [MEDIUM]**: Concurrent multi-customer load test needs a load-test project (tests/integration/Sprk.Provisioning.ControlPlane.LoadTests). If load-testing infra does not exist, spike a new load-test project.
- **`COMP-12` [HIGH]**: Auth-v4 rotation race is inherently cross-worktree coordination; treat as SendMessage protocol between task-186 dispatch and auth-v4 subagent.
- **`COMP-05` [CRITICAL]**: Idempotency/crash-resume integration tests need tests/integration/seam/ infra + a way to kill the Worker mid-handler. If seam test-harness gaps exist, spike them as prerequisite.
- **`COMP-13` [MEDIUM]**: Structured-log schema enforcement typically requires a log-shape ArchTest + observability review; may extend beyond this project window.

---

## Appendix A: Finding index by source layer

- **EXECUTION_REALISM — mental dispatch run of `/provision-environment trial1 --batch runs/trial1-intake.json` assuming all 7 audits' 94 findings are cleanly applied.** (10): EXEC-01, EXEC-02, EXEC-03, EXEC-04, EXEC-05, EXEC-06, EXEC-07, EXEC-08, EXEC-09, EXEC-10
- **batch-mode** (16): BAT-01, BAT-02, BAT-03, BAT-04, BAT-05, BAT-06, BAT-07, BAT-08, BAT-09, BAT-10, BAT-11, BAT-12, BAT-13, BAT-14, BAT-15, BAT-16
- **completeness** (15): COMP-01, COMP-02, COMP-03, COMP-04, COMP-05, COMP-06, COMP-07, COMP-08, COMP-09, COMP-10, COMP-11, COMP-12, COMP-13, COMP-14, COMP-15
- **dependency-and-parallel-safety** (8): DEP-01, DEP-02, DEP-03, DEP-04, DEP-05, DEP-06, DEP-07, DEP-08
- **intake-schema-vs-handlers** (12): ISH-01, ISH-02, ISH-03, ISH-04, ISH-05, ISH-06, ISH-07, ISH-08, ISH-09, ISH-10, ISH-11, ISH-12
- **l2-handlers** (14): HANDLER-01, HANDLER-02, HANDLER-03, HANDLER-04, HANDLER-05, HANDLER-06, HANDLER-07, HANDLER-08, HANDLER-09, HANDLER-10, HANDLER-11, HANDLER-12, HANDLER-13, HANDLER-14
- **placeholder-xlayer** (19): PLX-01, PLX-02, PLX-03, PLX-04, PLX-05, PLX-06, PLX-07, PLX-08, PLX-09, PLX-10, PLX-11, PLX-12, PLX-13, PLX-14, PLX-15, PLX-16, PLX-17, PLX-18, PLX-19
- **prereqs-yaml-recipe-contract-plus-placeholder** (12): PRQ-01, PRQ-02, PRQ-03, PRQ-04, PRQ-05, PRQ-06, PRQ-07, PRQ-08, PRQ-09, PRQ-10, PRQ-11, PRQ-12
- **registry-write-path** (7): REG-01, REG-02, REG-03, REG-04, REG-05, REG-06, REG-07
- **skill-drift-audit** (14): SKILL-01, SKILL-02, SKILL-03, SKILL-04, SKILL-05, SKILL-06, SKILL-07, SKILL-08, SKILL-09, SKILL-10, SKILL-11, SKILL-12, SKILL-13, SKILL-14

## Appendix B: Wave assignment cross-reference

| Finding | Wave | Owner |
| --- | --- | --- |
| `BAT-01` | Wave 6 | main-session-only |
| `BAT-02` | Wave 6 | main-session-only |
| `BAT-03` | Wave 0 | main-session-only |
| `BAT-04` | Wave 6 | main-session-only |
| `BAT-05` | Wave 6 | main-session-only |
| `BAT-06` | Wave 6 | main-session-only |
| `BAT-07` | Wave 6 | main-session-only |
| `BAT-08` | Wave 6 | main-session-only |
| `BAT-09` | Wave 6 | main-session-only |
| `BAT-10` | Wave 6 | subagent-ok |
| `BAT-11` | Wave 6 | main-session-only |
| `BAT-12` | Wave 6 | main-session-only |
| `BAT-13` | Wave 6 | main-session-only |
| `BAT-14` | Wave 6 | main-session-only |
| `BAT-15` | Wave 6 | main-session-only |
| `BAT-16` | Wave 6 | subagent-ok |
| `COMP-01` | Wave 7 | subagent-ok |
| `COMP-02` | Wave 7 | subagent-ok |
| `COMP-03` | Wave 7 | subagent-ok |
| `COMP-04` | Wave 4 | main-session-only |
| `COMP-05` | Wave 7 | subagent-ok |
| `COMP-06` | Wave 7 | subagent-ok |
| `COMP-07` | Wave 7 | subagent-ok |
| `COMP-08` | Wave 4 | main-session-only |
| `COMP-09` | Wave 7 | subagent-ok |
| `COMP-10` | Wave 7 | subagent-ok |
| `COMP-11` | Wave 7 | subagent-ok |
| `COMP-12` | Wave 7 | main-session-only |
| `COMP-13` | Wave 7 | subagent-ok |
| `COMP-14` | Wave 5 | main-session-only |
| `COMP-15` | Wave 4 | main-session-only |
| `DEP-01` | Wave 0 | main-session-only |
| `DEP-02` | _UNASSIGNED_ | main-session-only |
| `DEP-03` | _UNASSIGNED_ | main-session-only |
| `DEP-04` | _UNASSIGNED_ | main-session-only |
| `DEP-05` | _UNASSIGNED_ | main-session-only |
| `DEP-06` | _UNASSIGNED_ | main-session-only |
| `DEP-07` | Wave 1 | main-session-only |
| `DEP-08` | Wave 0 | main-session-only |
| `EXEC-01` | Wave 2 (B1) | main-session-only |
| `EXEC-02` | Wave 0 | main-session-only |
| `EXEC-03` | Wave 0 | main-session-only |
| `EXEC-04` | Wave 2 (B3) | main-session-only |
| `EXEC-05` | Wave 4 | main-session-only |
| `EXEC-06` | Wave 4 | main-session-only |
| `EXEC-07` | Wave 2 (B4) | main-session-only |
| `EXEC-08` | Wave 4 | main-session-only |
| `EXEC-09` | Wave 8 | main-session-only |
| `EXEC-10` | Wave 3 | main-session-only |
| `HANDLER-01` | Wave 2 (B1) | subagent-ok |
| `HANDLER-02` | Wave 2 (B1) | subagent-ok |
| `HANDLER-03` | Wave 2 (B3) | subagent-ok |
| `HANDLER-04` | Wave 2 (B3) | subagent-ok |
| `HANDLER-05` | Wave 2 (B3) | subagent-ok |
| `HANDLER-06` | Wave 2 (B3) | subagent-ok |
| `HANDLER-07` | Wave 2 (B3) | subagent-ok |
| `HANDLER-08` | Wave 2 (B3) | subagent-ok |
| `HANDLER-09` | Wave 2 (B3) | subagent-ok |
| `HANDLER-10` | Wave 2 (B3) | subagent-ok |
| `HANDLER-11` | Wave 0 | main-session-only |
| `HANDLER-12` | Wave 2 (B1) | subagent-ok |
| `HANDLER-13` | Wave 2 (B3) | subagent-ok |
| `HANDLER-14` | Wave 2 (B3) | main-session-only |
| `ISH-01` | Wave 2 (B2) | subagent-ok |
| `ISH-02` | Wave 5 | main-session-only |
| `ISH-03` | Wave 0 | main-session-only |
| `ISH-04` | Wave 2 (B2) | main-session-only |
| `ISH-05` | Wave 2 (B2) | main-session-only |
| `ISH-06` | Wave 2 (B2) | main-session-only |
| `ISH-07` | Wave 5 | main-session-only |
| `ISH-08` | Wave 5 | subagent-ok |
| `ISH-09` | Wave 5 | main-session-only |
| `ISH-10` | Wave 5 | main-session-only |
| `ISH-11` | Wave 5 | subagent-ok |
| `ISH-12` | Wave 5 | main-session-only |
| `PLX-01` | Wave 4 | main-session-only |
| `PLX-02` | Wave 4 | main-session-only |
| `PLX-03` | Wave 4 | main-session-only |
| `PLX-04` | Wave 3 | main-session-only |
| `PLX-05` | Wave 3 | main-session-only |
| `PLX-06` | Wave 3 | main-session-only |
| `PLX-07` | Wave 3 | main-session-only |
| `PLX-08` | Wave 4 | main-session-only |
| `PLX-09` | Wave 4 | main-session-only |
| `PLX-10` | Wave 4 | main-session-only |
| `PLX-11` | Wave 4 | main-session-only |
| `PLX-12` | Wave 4 | main-session-only |
| `PLX-13` | Wave 4 | main-session-only |
| `PLX-14` | Wave 4 | main-session-only |
| `PLX-15` | Wave 4 | main-session-only |
| `PLX-16` | Wave 4 | main-session-only |
| `PLX-17` | Wave 4 | main-session-only |
| `PLX-18` | Wave 4 | main-session-only |
| `PLX-19` | Wave 4 | main-session-only |
| `PRQ-01` | Wave 3 | main-session-only |
| `PRQ-02` | Wave 3 | main-session-only |
| `PRQ-03` | Wave 3 | main-session-only |
| `PRQ-04` | Wave 3 | main-session-only |
| `PRQ-05` | Wave 3 | main-session-only |
| `PRQ-06` | Wave 3 | main-session-only |
| `PRQ-07` | Wave 3 | main-session-only |
| `PRQ-08` | Wave 3 | main-session-only |
| `PRQ-09` | Wave 3 | main-session-only |
| `PRQ-10` | Wave 3 | main-session-only |
| `PRQ-11` | Wave 3 | main-session-only |
| `PRQ-12` | Wave 3 | main-session-only |
| `REG-01` | Wave 2 (B4) | subagent-ok |
| `REG-02` | Wave 2 (B4) | subagent-ok |
| `REG-03` | Wave 2 (B4) | subagent-ok |
| `REG-04` | Wave 4 | main-session-only |
| `REG-05` | Wave 2 (B4) | subagent-ok |
| `REG-06` | Wave 2 (B4) | subagent-ok |
| `REG-07` | Wave 2 (B4) | subagent-ok |
| `SKILL-01` | Wave 4 | main-session-only |
| `SKILL-02` | Wave 0 | main-session-only |
| `SKILL-03` | Wave 4 | main-session-only |
| `SKILL-04` | Wave 4 | main-session-only |
| `SKILL-05` | Wave 4 | main-session-only |
| `SKILL-06` | Wave 4 | main-session-only |
| `SKILL-07` | Wave 4 | main-session-only |
| `SKILL-08` | Wave 3 | main-session-only |
| `SKILL-09` | Wave 4 | main-session-only |
| `SKILL-10` | Wave 4 | main-session-only |
| `SKILL-11` | Wave 4 | main-session-only |
| `SKILL-12` | Wave 4 | main-session-only |
| `SKILL-13` | Wave 4 | main-session-only |
| `SKILL-14` | Wave 4 | main-session-only |
