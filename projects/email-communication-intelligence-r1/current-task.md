# Current Task State — email-communication-intelligence-r1

> **Last Updated**: 2026-07-29 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | **Execution — Phase 4 (Job C + intent)** |
| **Task** | **✅ 042 DONE (2026-07-30) — regarding-vs-related intent (FR-12).** ✅ 041 (`742d05e08`); ✅ 031 (`4aca6d65a`). Worktree synced to master (`bc5bd5b54`). **NEXT: 060 (deploy BFF+Dataverse) → 061 (UAT) → 090 (wrap).** |
| **Status** | **042 COMPLETE.** Owner dissolved the ADR-024 tension in-session (`notes/042-regarding-vs-related-owner-decision.md`): ONE relationship = `regarding`, NO related field/second mechanism; cross-refs noted in `sprk_triagesummary`. As-built: deterministic `NewRecordIntentDetector` (no LLM Action) → in-rung capture-time confidence cap (0.90→0.65, demote-only, safe-by-construction) + best-effort `email-regarding-intent` enrichment step (summary note + gated create-new Proposed row, human-confirmed). ADR-024 AMENDED + spec §6.5/FR-12 re-cast. 21 tests pass (15 detector + 2 rung + 4 seam); 5 pre-existing sender-identity baseline failures unchanged. Build clean; publish ~47 MB compressed (Δ≈0, +0 pkgs); no new CVE. code-review SHIP / adr-check CLEAN. |
| **Next Action** | **All r1 feature tasks (010–051) complete.** Proceed to **060 — deploy BFF + Dataverse to `spaarkedev1`** (`work on task 060`). 060 prereqs to remember: (a) BFF app user needs `prvActOnBehalfOfAnotherUser` for the 031 Job B apply impersonation (go-live gate); (b) the r1 deltas since last deploy = 7-entity identifier rung, C-1 auto-file narrowing, triage Action+persist, RI-confidence, Job B propose/apply/queue-feed, Job C create-task + attachment-action, FR-12 intent. Then 061 (operator browser UAT, success criteria 1–9) → 090 (wrap + `/test-diet`). |
| **021 note** | Caught + fixed a write-set regression (decoupled write-eligibility from auto-file-eligibility) so rung 2/3 fallback associations still written on auto-file. ADR-045 path-A, kill-switch `AutoFileOptions.Rung2And3AutoFileEnabled`. |
| **Progress** | ✅ 020 021 022 023 024 025 030 031 032 040 051a. **031 DONE** (Option 2 impersonation; shared write seam extended additively; new `CommunicationProposalApplyService` + apply endpoint; 6 seam tests green; code-review SHIP-WITH-FIXES → must-fixes applied; publish-size Δ≈0; no new CVE). Remaining: 041, 042, then 060→061→090. **Next: commit 031, then 041 (attachment-grounded extraction, opus).** |
| **031 PLAN (Option 2 impersonation — grounded in STEP 0)** | Additive optional `Guid? impersonateSystemUserId=null` threaded through the shared write core (byte-unchanged for existing callers): `IActionSeam.UpdateRecordRequest`+`ActionSeam` → `UpdateRecordActionCore`(Input+UpdateAsync) → `IFieldMappingDataverseService.UpdateRecordFieldsAsync` → BOTH impls (`DataverseWebApiService` MSCRMCallerID via `CreateAuthenticatedRequestAsync`; `DataverseServiceClientImpl` via cloned `ServiceClient.CallerId` per `UserPrivilegeChecker`). Apply endpoint on existing `/api/communications` group: resolve caller via `ICallerSystemUserResolver` (403 fail-closed) → load open `Proposed` row (030) → re-validate `sprk_emailupdatefield` allow-list AT APPLY → re-verify citation (NFR-06) → `IActionSeam.UpdateRecordAsync` under impersonation → write `sprk_emailreviewlog` `Approved`+`Applied` (actortype=Human). 5 neg/pos tests. **Go-live prereq: BFF app user needs `prvActOnBehalfOfAnotherUser` (→ task 060).** |
| **🔑 030 DESIGN DECISIONS (orchestrator-made, autonomous)** | (1) **Proposal store = `sprk_emailreviewlog` `Proposed` rows** (no new table; matches the schema's designed Classified→Proposed→Approved→Applied lifecycle; open = Proposed w/o terminal row). Supersedes POML §11 "separate store" framing w/ documented rationale. (2) **Propose Action = Job B's own targeted field-extraction** (single Action call, NOT a redundant classification pass — escalation trigger does NOT fire). Both in `notes/030-job-b-propose.md`. |
| **Baseline test note** | 5 pre-existing Communication failures (sender-identity/DTO) = branch debt, documented in `notes/wave2-review-findings.md`. NOT r1's; don't fix, don't regress. Suite: 705 pass / 5 pre-existing fail. |
| **✅ OWNER DECISIONS (2026-07-29)** | (1) **031 write-identity = Option 2 impersonation** (`MSCRMCallerID` = confirming user; native `modifiedby` = human; no OBO exchange). (2) **051b group mailbox = DESCOPED** (central/shared covered by 051a). Both recorded in `notes/031-write-identity-decision.md`; D-07 amended. **031 now UNBLOCKED — next to execute.** |
| **001 findings** | `notes/001-operator-schema-verification.md` — ALL 4 inputs PRESENT. Deltas: `sprk_targetfieldlogicalname` (Job B), `sprk_triageobligation` singular (011/025), contact-row anomaly (filter null number-field), typos already clean. |
| **010 finding (LOAD-BEARING for 020/021)** | **TWO regarding maps exist**: `Engine/RegardingFieldMap.cs` (used by association rungs — 020/021 path) AND `CommunicationService.RegardingLookupMap` (send-time caller-supplied path, carries `EntitySetName`). ADR-024 "one mechanism" tension is PRE-EXISTING. 010 added report-card to BOTH. 020 reads the Engine map + `sprk_recordtype_ref` roster. |
| **Wave-1 build status** | BFF build clean (0 err); AssociationMappingTests 35/35 pass (incl. 2 new report-card). Publish-size: 010 adds 0 packages → delta ≈0 (authoritative measure deferred to task 060). |
| **Execution mode** | **AUTONOMOUS + PARALLEL where possible** (operator's explicit instruction). Wave-by-wave via TASK-INDEX parallel groups; `dotnet build` between waves; `/conflict-check` before each BFF PR. |
| **Branch** | `work/email-communication-intelligence-r1` (all setup committed + pushed, latest `5acd5c00c`) |

### Schema readiness — operator COMPLETED all objects in `spaarkedev1` (2026-07-29)
Task 001 **verifies** these (does NOT create); schema tasks **011/012/013 are now verify-only** (no create/collision):
- `sprk_emailupdatefield` (Job B allow-list) ✅
- `sprk_communication` triage fields: `sprk_triagecategory` (lookup), `sprk_triagepriority`, `sprk_triagesummary`, `sprk_triageobligations` (JSON), `sprk_riconfidence`, `sprk_reviewoutcome` ✅
- `sprk_emailreviewlog` (audit) ✅
- `sprk_triagecategory` (taxonomy config) ✅
- **AS-BUILT option-set values → `notes/schema-to-create.md` § "AS-BUILT option-set values" — implementation MUST use those integer values.** Deltas: `sprk_fieldtype` = Text/Lookup/OptionSet/Number/DateTime/Boolean/Memo/Currency (single **Number** → resolve whole-vs-decimal from field metadata); `sprk_triagepriority` = Urgent/High/Medium/Low; `sprk_action` label "Overriden" (one `d` — code keys on int).

### Critical Context
22 task POMLs validated (PASS). Design §0 authoritative. **Binding locks** (full list in CLAUDE.md): code-directed Action+Binding only (node engine FROZEN); auto-file **C-1 = rung 0 + rung 1 only** (2/3 → `Suggested`); **Job B FULL** (propose→confirm→apply via `IActionSeam.UpdateRecordAsync` under OBO → `sprk_emailreviewlog` audit; allow-listed fields only); IP docketing OUT; **surfaces owned by completed r5** (r1 builds NO UI — feed + apply endpoints only, C-3; contract in `notes/email-intelligence-r1-coordination.md`).

---

## Full State (Detailed)

### Done
- design.md rev-3 (§0.11 closes all §11 decisions; C-1 locked; Job B FULL; IP docketing removed) · spec.md (17 FRs/8 NFRs; C-3/C-4/FR-15 resolved) · plan.md · CLAUDE.md · 22 POMLs + TASK-INDEX (validator PASS) · notes (schema-to-create, coordination, ux-research). All committed + pushed.

### Execution plan (from TASK-INDEX)
- **Critical path**: `001 → 020 → 030 → 031 → 060 → 061 → 090` (Job B deepest track).
- **Parallel groups**: P0(001) → P1(010,011,012,013) → {P2-assoc: 020→021 ‖ P2-triage: 022→023→{024,025}} → P3(030→{031,032}) → P4(040→041; 042); **P5(050→051) fully parallel to P1–P4**; P6(060→061→090).
- **Model tiers**: default sonnet/high; **opus** on 020,030,031,041,042,050; **xhigh** on 020,042; 001/061/090 low.
- **Max concurrency 6/wave**. BFF writers to shared `Services/Communication/` are `parallel-safe: false` (never concurrent). `.claude/` tasks main-session-only.
- **Between waves**: `dotnet build src/server/api/Sprk.Bff.Api/` if any `.cs` changed → STOP on failure.

### Watch items
- 020 highest blast-radius (association correctness); bare-numeric never auto-files alone; multi-entity → `Ambiguous`; read `sprk_recordtype_ref` defensively (typos).
- 030/031 record-mutating — human-confirm + cite + audit + allow-list + OBO; verify cited text exists.
- 050 spike has escalation trigger; 051 gated on its finding.
- Every BFF task: publish-size (≤60 MB; baseline ~49.63 MB) + CVE + `/conflict-check` + tests + golden-utterance eval (new Actions/Bindings).

### To resume (post-compact)
Say "continue" or "work on task 001" → invoke `task-execute` on task 001, then proceed autonomously through the waves.

## Decisions Made
*(appended by task-execute during execution)*

## Implementation Notes
*(none yet — execution not started)*
