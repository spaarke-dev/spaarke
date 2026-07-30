# Current Task State — email-communication-intelligence-r1

> **Last Updated**: 2026-07-29 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | **Execution — Phase 4 (Job C + intent)** |
| **Task** | **ACTIVE: 042 — regarding-vs-related intent (FR-12, opus/xhigh, FULL). NOT STARTED — begin in a FRESH context window** (042 is r1's only xhigh + most misfile-safety-critical task; needs clean budget). ✅ 041 committed+pushed (`742d05e08`); ✅ 031 (`4aca6d65a`). |
| **Status** | 042 not-started — Step 0.5 rigor declared (FULL/opus/xhigh/directional); conflict-check silent pass (no PR overlap on shared `Services/Communication/`). Halted at Step 3 context-budget (this window held the full 041 build + 2 task-execute loads + gate skills). 041 DONE (Path B machine-verified locator; 12 tests; code-review SHIP-WITH-FIXES; adr-check CLEAN; W1 dup-extraction §6.5 Path A follow-up). |
| **Next Action** | New session: `work on task 042`. **Load order** (POML §knowledge): (1) ADR-024 concise (`.claude/adr/ADR-024-polymorphic-resolver-pattern.md`) — the ONE regarding mechanism (resolver-only write) + the **path-A exception** for representing "related-to" distinctly; (2) `Services/Communication/Engine/AutoFileGate.cs` + `AssociationStatusMapper.cs` — where auto-file happens (intent must be able to SUPPRESS it, never widen — C-1/021) + how status maps; (3) `Engine/RegardingFieldMap.cs` — the one regarding map ("related-to" must NOT become a second map); (4) `Engine/Rungs/AiClassificationRung.cs` — the classification signal (022) reused for intent (no 2nd full LLM pass); (5) 020/021 decisions (`notes/` + committed impl) — the identifier association + C-1 narrowing this intent gates; (6) 040's gated-create precedent (`ICommunicationCreateTaskAi` + `IActionSeam.CreateTaskAsync` + the enrichment step pattern from 041/040) for the `new-record-related-to` create. **Then**: author intent Action+Binding (catalog) classifying `file-to-existing | update-existing | new-record-related-to` reusing the 022 signal → demotion wiring: "new filing based on X" demotes X regarding→related + SUPPRESSES auto-file (never silently files onto X; offers create-new/file-onto/link-related) → document the "related-to" relationship model distinctly (ADR-024 path-A, in PR/design note) → propose gated `dataverse.create_record` linking X as related (reuse 040 create leg, proposed+confirmed, nothing auto-finalizes NFR-06/ADR-015) → best-effort enrichment (NFR-04) → golden-utterance eval incl. the "new filing based on X" demotion + 5 seam tests → build+publish+CVE → 9.5 gates → conflict-check → ✅. **ESCALATION TRIGGER**: if "related-to" can't be represented without (a) a second regarding mechanism (ADR-024 violation) or (b) an ambiguous "new filing based on X" vs "file onto X" boundary that risks silent misfile → STOP + escalate per §6/§6.5 (do NOT add a 2nd regarding mechanism; do NOT ship a classifier that silently misfiles). After 042: 060 (deploy)→061 (UAT)→090 (wrap). |
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
