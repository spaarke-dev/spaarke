# TASK-INDEX — field-mapping-server-write-path-r1

> **Generated**: 2026-10-19 by `/project-pipeline` · 43 tasks · Source: [spec.md](../spec.md) · [plan.md](../plan.md)
> Execute every task via `task-execute` (root CLAUDE.md §4). Status: 🔲 not started · 🔄 in progress · ✅ done · ⛔ blocked

## Phase 1 — W1 Canonical engine and configuration

| ID | Task | FRs | Tier / effort | Rigor | Group | Deps | Status |
|---|---|---|---|---|---|---|---|
| 001 | [Promote canonical mapping engine](001-promote-canonical-mapping-engine.poml) | FR-01 | sonnet / high | FULL | W1-A | — | ✅ |
| 002 | [Fill-only applier + pair/protected target guard](002-fill-only-applier-and-target-guard.poml) | FR-02, FR-05e | sonnet / high | FULL | W1-B | 001 | ✅ |
| 003 | [Mapping-source resolver + Read-as-caller probe](003-mapping-source-resolver-and-read-probe.poml) | FR-03, FR-04 | opus / high | FULL | W1-B | 001 | ✅ |
| 004 | [Config cleanup: mode column, dormant columns, seeder #1318](004-config-cleanup-mode-column-dormant-seeder.poml) | FR-05a-c | sonnet / high | FULL | W1-B | 001 | ✅ |
| 005 | [`sprk_syncmode` read + migration script](005-syncmode-read-and-migration-script.poml) | FR-05d | sonnet / high | FULL | W1-C | 001 | ✅ |
| 006 | [Remove OOB businessunit→sprk_matter map](006-remove-oob-businessunit-matter-entitymap.poml) | FR-05f | sonnet / medium | STANDARD | W1-C | — | ✅ |

## Phase 2 — W2 Unified `RecordCreationService`

| ID | Task | FRs | Tier / effort | Rigor | Group | Deps | Status |
|---|---|---|---|---|---|---|---|
| 010 | [W2 coordination gate (UAC-r2, ontology-r1)](010-w2-coordination-gate.poml) | NFR-08 | sonnet / medium | MINIMAL | W2-gate | — | ✅ |
| 011 | [Extract `CallerWriteAuthorization` (pure move)](011-extract-caller-write-authorization.poml) | FR-08 | opus / xhigh | FULL | none | 010 | ✅ |
| 012 | [Per-table rules registry](012-per-table-rules-registry.poml) | FR-11 | opus / high | FULL | W2-A | 010 | ✅ |
| 013 | [Service Create mode: principals, pipeline, refusals](013-record-creation-service-create-pipeline.poml) | FR-06, FR-07, FR-12 | opus / xhigh | FULL | none | 011, 012 | ✅ |
| 014 | [Mapping step + merged-payload authorization](014-mapping-step-and-merged-payload-authz.poml) | FR-06, FR-08 | opus / xhigh | FULL | none | 002, 003, 013 | ✅ |
| 015 | [Record-first owner + search-index default](015-record-first-owner-and-search-index.poml) | FR-09, FR-10 | opus / high | FULL | none | 014 | ✅ |
| 016 | [Repair mode](016-repair-mode.poml) | FR-14 | opus / high | FULL | none | 015 | ✅ |
| 017 | [Re-point route, chat and decision callers](017-repoint-route-chat-decision-callers.poml) | FR-13 | opus / high | FULL | none | 016 | ✅ |
| 018 | [Office quick-create adapter; delete Office service](018-office-quick-create-adapter.poml) | FR-13, FR-12 | opus / high | FULL | none | 016 | ✅ |
| 039 | [Server-owned search index (#2399)](039-server-owned-search-index.poml) | FR-38 | sonnet / high | FULL | none | 017 | ✅ |
| 019 | [W2 deploy, live checks, adversarial review](019-w2-deploy-and-adversarial-review.poml) | NFR-01, NFR-05 | sonnet / high | FULL | none | 017, 018, 039 | ✅ |
| 041 | [Index routing follows the content type (#2502)](041-content-type-index-routing.poml) | FR-40 (FR-10, FR-38) | opus / high | FULL | none | 019, 039 | ✅ (#2541, 249809ce0b) |
| 042a | [#2483 window characterization (tests only; lands alone)](042a-stamp-before-owner-characterization.poml) | FR-41 | sonnet / high | STANDARD | none | none | ✅ (#2521) |
| 042b | [Derive the stamp before the owner (#2483, DI-009)](042b-stamp-before-owner-fix.poml) | FR-41 | opus / high | FULL | none | 042a, 019 | ✅ (#2562, 0d4aa87ee0) |
| 042c | [Signal / DR core record: caller AppendTo + core first in the owner question (#2561, DI-019)](042c-core-record-owner-and-appendto.poml) | FR-41 | opus / high | FULL | none | 042b | ✅ [done] PR #2569 merged 2026-11-05 (D-204 refusal) |
| 043 | [A Document is a field-mapping source (owner 2026-11-04)](043-document-as-mapping-source.poml) | FR-03, FR-04, FR-19 | opus / high | FULL | none | 031 | ✅ [done] merged into the work branch 2026-11-05 (18361d7dcc) |
| 044 | [Core pair written whole, only by the server and the ledger writers: field security (owner 2026-11-04)](044-core-pair-server-only-field-security.poml) | FR-41 | opus / high | FULL | none | 042c; dev import gated on the owner's go in UAC-r2's session | ✅ [done] master via #2617 (cc11294300, 2026-11-07); dev deploy in UAC-r2's batch  |
| 045 | [Regarding pair always complete and propagated: inventory + design first (owner 2026-11-04)](045-regarding-pair-complete-and-propagated.poml) | FR-23, FR-41 | opus / high | FULL | none | 042c, 044; part B after owner approval | 🔲 [open] |
| 046 | [Feature permission matrix, proposed role changes (UAC-r2), role guard (owner 2026-11-04)](046-feature-permission-matrix-and-role-guard.poml) | FR-08, FR-26 | opus / high | FULL | none | 031, 042c | ✅ [done] merged into the work branch 2026-11-05 (0b0edfbe35); role changes in 046b |
| 046b | [Owner's role rulings on 046's gaps: role files + apply script; writer-only rule at role level; standalone add-ons checked alone (owner 2026-11-05)](046b-role-changes-apply-script-and-standalone-add-ons.poml) | FR-08, FR-26 | opus / high | FULL | none | 046; live apply by UAC-r2 under the owner's go | ✅ [done] master via #2592 (c5aa7ab6cd, 2026-11-06); additions and R1 applied on dev |
| 046c | [Basic User is read-only on Spaarke tables: role file, -Remove, guard (owner 2026-11-06)](046c-basic-user-read-only-on-spaarke-tables.poml) | FR-08, FR-26 | opus / high | FULL | none | 046b; live removal by UAC-r2 under the owner's go | ✅ repo side on master via #2610 (fb5738b60d); applied on dev 2026-11-07 (owner go in fm session; G17 03:33:12Z, R3 80 removals 03:36:46Z, both -Verify PASS; UAC-r2 2 h watch to 05:40Z); was: dev apply (G17, then -Remove -Gap R3) waits for the owner's go |
| 048 | [Signal, Decision Record and Budget Revision changed only by their writers: generic update tools refuse them (owner 2026-11-04, D-204 extended)](048-writer-only-tables-refused-by-generic-updates.poml) | FR-41 | opus / high | FULL | none | 042c | ✅ [done] PR #2570 merged 2026-11-05 (0f53e053bf) |
| 049 | [Create path within NFR-02: concurrent caller checks, assigned-access grant after the response (owner 2026-11-05)](049-create-path-latency-nfr02.poml) | NFR-02 | opus / high | FULL | none | 021, 042c | ✅ [done] master via #2599 (20669df6a9, 2026-11-06); dev deploy via UAC-r2 |
| 049b | Create path: when the maker is the root's Assigned To, grant only the maker inline and queue the other subjects (UAC-r2 ruling 2026-11-06) | NFR-02 | opus / high | FULL | none | 049 | 🔄 merged into the work branch 2026-11-07 (049b + fix1 + fix2, UAC-r2 approved, ruling b); master PR next |
| 049c | [Create path within NFR-02, round 2: cut the pre-create steps (owner 2026-11-07)](049c-create-path-speed-up.poml) | NFR-02 | opus / high | FULL | none | 049, 049b | 🔲 after 049b merges |

## Phase 3 — W3 Product creators onto the service

| ID | Task | FRs | Tier / effort | Rigor | Group | Deps | Status |
|---|---|---|---|---|---|---|---|
| 020 | [Child-records route: Matter, Project, `mappingSource`](020-child-records-route-matter-project.poml) | FR-15 | opus / high | FULL | none | 019 | ✅ (#2551, aef86b89f7) |
| 021 | [Matter wizard (reference) + latency before/after](021-matter-wizard-migration.poml) | FR-16, NFR-02 | sonnet / high | FULL | W3-wizards | 020, 041 | 🔄 code + review fix round 1 on task/fm-021 (d6dc73e9f6). Re-measure 2026-11-06 after 049, with the Project→Matter profile: still over NFR-02 (maker is Assigned To: p95 +10.4 s, inline grant of every subject → 049b; queued: about +2 s). Owner decision pending (task-021-live.md); wizard deploy waits |
| 022 | [Project wizard (incl. secure Project)](022-project-wizard-migration.poml) | FR-16, FR-15 | opus / high | FULL | W3-wizards | 021, 041 | 🔲 |
| 023 | [Event wizard](023-event-wizard-migration.poml) | FR-16 | sonnet / high | FULL | W3-wizards | 022, 041 | 🔲 |
| 024 | [To Do wizard](024-todo-wizard-migration.poml) | FR-16 | sonnet / high | FULL | W3-wizards | 023, 041 | 🔲 |
| 025 | [Invoice wizard](025-invoice-wizard-migration.poml) | FR-16 | sonnet / high | FULL | W3-wizards | 024, 041 | 🔲 |
| 026 | [Work Assignment wizard](026-work-assignment-wizard-migration.poml) | FR-16 | sonnet / high | FULL | W3-wizards | 025, 041 | 🔲 |
| 027 | [Report Card wizard (fill-only regression)](027-report-card-wizard-migration.poml) | FR-16, FR-02 | sonnet / high | FULL | W3-wizards | 026, 041 | 🔲 |
| 028 | [Analysis wizard + email mapping at send](028-analysis-wizard-and-email-send-mapping.poml) | FR-16, FR-17 | sonnet / xhigh | FULL | W3-wizards | 027, 041 | 🔲 |
| 029 | [Document Upload + Summarize Files wizards](029-document-upload-and-summarize-wizards.poml) | FR-16 | sonnet / high | FULL | W3-wizards | 028, 041 | 🔲 |
| 030 | [Office To Do + document save](030-office-todo-and-document-save.poml) | FR-18 | opus / high | FULL | W3-server | 019 | 🔄 030a on master via #2610 (fb5738b60d, 2026-11-07; fix rounds 1-2 re-checked clean); 030b: association in the existing create + guards (W3 direction, 2026-11-06) |
| 031 | [Typed routes: events, analysis create](031-typed-routes-events-analysis.poml) | FR-19 | sonnet / high | FULL | W3-server | 019 | ✅ [done] PR #2564 merged 2026-11-04; live on dev 909754869f |
| 032 | [Invoice extraction](032-invoice-extraction.poml) | FR-20 | sonnet / high | FULL | W3-server | 019 | 🔲 re-scope: fill-only mapping step inside the persister (owner 2026-11-06) |
| 033 | [System creators: generation, playbook, Signal, Decision](033-system-creators-generation-playbook-signal-decision.poml) | FR-21 | opus / high | FULL | W3-server | 019 | 🔲 re-scope under the W3 direction (2026-11-06) before it runs; likely dropped |
| 034 | [System creators: inbound ingestion](034-system-creators-inbound-ingestion.poml) | FR-21 | opus / high | FULL | W3-server | 019 | 🔲 re-scope: named exceptions + guards (W3 direction, 2026-11-06) |
| 035 | [Delete client mapping engine + guard](035-delete-client-mapping-engine.poml) | FR-22 | sonnet / high | FULL | none | 029 | 🔲 |
| 036 | [W3 server paths deploy + live check](036-w3-server-paths-deploy-and-live-check.poml) | FR-18–FR-21 | sonnet / high | FULL | none | 030–034 | 🔲 |
| 037 | [Work Assignment Assigned To default (#2354)](037-work-assignment-assignedto-default.poml) | FR-36 | opus / high | FULL | W7-A | none (UAC-r2 diff review) | ✅ [done] #2370 |
| 038 | [WA wizard: optional Assigned To picker](038-wa-wizard-assignedto-field.poml) | FR-37 | sonnet / high | FULL | none | 037 | ✅ [done] #2376 |

## Phase 4 — W4 `/push`

| ID | Task | FRs | Tier / effort | Rigor | Group | Deps | Status |
|---|---|---|---|---|---|---|---|
| 040 | [Push on the canonical engine + preview / Assigned To / pair edges](040-push-on-canonical-engine.poml) | FR-23, FR-34, FR-35 (actions) | sonnet / xhigh | FULL | W4 | 001, 005, 019, 070, 071; UAC 195 | ✅ [done] master via #2617 (cc11294300, 2026-11-07); dev deploy in UAC-r2's batch (+ live UI checks a-d after the client pieces) |

## Phase 5 — W5 Records created outside the product

| ID | Task | FRs | Tier / effort | Rigor | Group | Deps | Status |
|---|---|---|---|---|---|---|---|
| 050 | [Resolve W5 open questions (marker, quick-create flags)](050-resolve-w5-open-questions.poml) | spec UQ 1–2 | sonnet / high | STANDARD | W5-A | 019 | ✅ |
| 051 | [Native UI: ribbon New, quick create off, Assign hidden](051-native-ui-redirect-quickcreate-assign.poml) | FR-24 | sonnet / high | FULL | W5-B | 050, 029 | 🔲 |
| 052 | [Repair pass `IScheduledJob` + on-demand trigger](052-repair-pass-scheduled-job.poml) | FR-25 | opus / xhigh | FULL | W5-B | 016, 033, 050 | 🔲 |
| 053 | [W5 deploy + live 5-row import check](053-w5-deploy-and-live-import-check.poml) | FR-24, FR-25 | sonnet / high | FULL | none | 051, 052 | 🔲 |

## Phase 6 — W6 Standard, guards, docs, retirement

| ID | Task | FRs | Tier / effort | Rigor | Group | Deps | Status |
|---|---|---|---|---|---|---|---|
| 060 | [Ownership matrix + exception assessment](060-ownership-matrix-and-exception-assessment.poml) | FR-26 | opus / high | STANDARD | W6 | 033, 034 | 🔲 |
| 061 | [Census and client guards](061-census-and-client-guards.poml) | FR-27 | sonnet / high | FULL | W6 | 035, 036 | 🔲 |
| 062 | [Docs rewrite + root CLAUDE.md + CHANGELOG (main session)](062-docs-rewrite-and-root-claude-md.poml) | FR-28 | sonnet / high | MINIMAL | none | 040, 053, 060 | 🔲 |
| 063 | [Dead code + issue hygiene](063-dead-code-and-issue-hygiene.poml) | FR-28 | sonnet / medium | STANDARD | W6 | 035 | 🔲 |

## Phase 7 — W7 Record family service and Family tool (spec amendment 2026-10-27; design.md §12)

| ID | Task | FRs | Tier / effort | Rigor | Group | Deps | Status |
|---|---|---|---|---|---|---|---|
| 070 | [RecordFamilyEdges registry + guards](070-record-family-edge-registry.poml) | FR-29 | opus / high | FULL | W7-A | none (UAC 194 merged) | ✅ [done] #2367 |
| 071 | [RecordFamilyReader (OBO walk)](071-record-family-reader.poml) | FR-30 | opus / xhigh | FULL | W7-B | 070; UAC 194b | ✅ [done] #2442 |
| 072 | [GET /records/{table}/{id}/family](072-record-family-endpoint.poml) | FR-31 | sonnet / high | FULL | W7-B | 071 | ✅ [done] #2459 |
| 073 | [RecordFamilyModal (shared UI)](073-record-family-modal.poml) | FR-32 | sonnet / high | FULL | W7-A | none | ✅ [done] #2366 |
| 074 | [TrackingFieldTrio Family tool](074-trackingfieldtrio-family-tool.poml) | FR-33 | sonnet / high | FULL | W7-C | 073 | ✅ [done] #2383 |
| 075 | [Push Updates ribbon opens the Family view](075-push-ribbon-opens-family-view.poml) | FR-35 | sonnet / high | FULL | W7-C | 073 (build); merges with 040 | ✅ [done] master via #2617 (cc11294300, 2026-11-07); dev deploy in UAC-r2's batch (+ live UI checks a-d after the client pieces) |
| 076 | [W7 deploy + live Family check](076-w7-deploy-and-live-check.poml) | FR-29–FR-33, FR-35 | sonnet / high | FULL | none | 072, 074, 038 | 🔲 [open] |

## Wrap-up

| ID | Task | FRs | Tier / effort | Rigor | Group | Deps | Status |
|---|---|---|---|---|---|---|---|
| 090 | [Project wrap-up (test diet, lessons, sync)](090-project-wrap-up.poml) | — | sonnet / medium | MINIMAL | none | 061, 062, 063, 076, 037, 038 | 🔲 |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| W1-A ∥ W1-C ∥ W2-gate | 001, 006, 010 | none | Start here. 006 and 010 have no code dependency on 001 |
| W1-B | 002, 003, 004 | 001 | Separate files under `Services/Dataverse/FieldMapping/` + seeder |
| W1-C | 005 | 001 | `Spaarke.Dataverse` read path + script; live run needs owner OK |
| W2-A | 012 (∥ 011 only if 011 does not touch the same file) | 010 | 011 is `parallel-safe=false` (hot `OwnedChildWrite.cs`) |
| W2 core (sequential) | 013 → 014 → 015 → 016 → {017, 018} → 019 | 011, 012 (+002, 003 for 014) | One core file; no parallelism |
| W3-server | 030, 031, 032, 033, 034 | 019 | Separate callers; run `/conflict-check` for ontology/UAC files |
| W2 follow-ups | 041; 042a → 042b → 042c | 019 (+039 for 041) | 041 and 042b both edit `RecordCreationService*.cs`: not parallel; whichever lands second rebases and reconciles design §15.5. 042a (tests only) may run beside 041 and lands alone |
| W2 follow-ups (2026-11-04) | 043; 042c → 044 → 045; 046 | 031 (043, 046), 042c (044, 046) | 044 and 045 both edit `CallerWriteAuthorization.cs`: sequential. 046 writes no `src/` file and may run beside any of them. 045 stops after its inventory for the owner |
| W3-wizards (sequential) | 020 → 021 → … → 029 | 019, 041 | FR-16: one wizard at a time, live-verified. 041 first: W3 cannot verify "the wizard sends no index" against a server that still copies the BU value |
| W4 | 040 | 001, 005, 019 | Parallel with Phase 3 |
| W5-A | 050 | 019 | Read-only investigation |
| W5-B | 051, 052 | 050 (+029 for 051, 016 for 052) | Ribbon vs BFF job: disjoint files |
| W6 | 060, 061, 063 | see deps | 062 (main session; `.claude/` writes) after 040/053/060 |
| W7-A | 037, 070, 073 | none | Disjoint files: create defaults (BFF) / family registry (BFF) / shared modal (client). Startable now |
| W7-B (sequential) | 071 → 072 | 070 + UAC 194b | Reader then endpoint |
| W7-C | 074, 075 | 073 | PCF vs ribbon + code page: disjoint |
| W7 deploy | 076 | 072, 074, 075 | BFF via R3 batch with the owner's go |

## Critical Path

010 → 011 → 013 → 014 → 015 → 016 → 017 → 019 → {020, 041} → 021 → … → 029 → 035 → 061 → 090 (≈ 23 tasks deep).

## High-Risk Items

- **011, 013, 014, 015** — security boundary (caller authorization, owner). FULL rigor, Opus, adversarial review at 019.
- **017** — hot files shared with UAC-r2 (PR #2110) and ontology-r1 cores.
- **052** — writes to existing rows; forward-only and idempotency are hard requirements; adversarial review.
- **021** — reference wizard migration + NFR-02 latency gate (+500 ms p95 escalation).

## Goal-Eligibility (task-create Step 3.85)

No wave is goal-eligible:
- **W1-B** includes 003, a security probe.
- **W2** is security-critical and sequential.
- **W3-wizards** deploy and are live-verified one at a time.
- **W3-server** changes ownership on live create paths.
- **W5/W6** involve deploys, live writes or `.claude/` edits.

Run every task with a normal per-task "continue".
