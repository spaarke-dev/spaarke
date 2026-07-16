# TASK-INDEX — email-communication-solution-r4

> **Generated**: 2026-07-14 via `/project-pipeline`
> **Total tasks**: 45 (44 work + 1 wrap-up)
> **Legend**: 🔲 not-started · 🔄 in-progress/needs-retry · ✅ complete · ⛔ blocked/gated

---

## Task Registry

| # | Title | Wave | Tags | FR | Deps | Blocks | Parallel-safe | Rigor | Status |
|---|---|---|---|---|---|---|---|---|---|
| 001 | `sprk_communication` schema pass (reply-thread + association columns) | W0 | dataverse, schema | FR-01 | — | 006,011,015,042 | true | STANDARD | ✅ |
| 002 | Add `Suggested`/`Ambiguous` option-set values (verify integers via MCP) | W0 | dataverse, schema | FR-02 | 001 | 015,042 | true | STANDARD | ✅ |
| 003 | Author `sprk_servicerequest` schema doc + wire association target | W0 | dataverse, schema, docs | FR-03 | 001 | 012,013 | true | STANDARD | ✅ |
| 004 | Add `sprk_event` to catalog/priority; correct org → `sprk_organization` (+ `account` to own lookup) | W0 | dataverse, bff-api | FR-04 | 001 | 012,013 | true | FULL | ✅ |
| 005 | Author **ADR-045** Communication ADR (concise + full) | W0 | adr, docs | FR-05 | — | 006,010,016 | **false** (`.claude/`) | STANDARD | ✅ |
| 006 | BFF send-path: attachment doc-fix (NO rename) + `Internet-Message-Id`/`In-Reply-To` capture | W0 | bff-api, communication | FR-06 | 001,005 | 012,022,060 | true | FULL | ✅ (capture needs dev smoke-test) |
| 007 | **Retire OOB-`email` subsystem** + publish-size delta | W0 | bff-api, deletion | FR-07 | 005 | — | true | FULL | ✅ (partial — 3 shared-infra files retained; see current-task) |
| 010 | `ICommunicationEnrichmentService` (both directions; outbound RAG) | W1 | bff-api, communication | FR-08 | 005,006 | 011,052 | true | FULL | ✅ (6 escalations — E2/E3→011, E1/E5 owner, E4/E6 resolved) |
| 011 | Refactor `IncomingAssociationResolver` → Engine over normalized envelope | W1 | bff-api, refactoring | FR-09 | 010 | 012,013,014 | false (serial in W1) | FULL | ✅ (R-7 preserved; envelope engine + rung abstraction + normalizer; gate clean) |
| 012 | Rungs 0–1 (explicit-ref + thread continuity) across 8 targets | W1 | bff-api, communication | FR-10 | 011 | 015,030 | true | FULL | ✅ (rung 0/1 + RegardingFieldMap; gate clean; W1 owner heads-up = subject-ref precedes thread) |
| 013 | Rung 2 (participant correlation; org-by-domain) | W1 | bff-api, communication | FR-10 | 011 | 015,030 | true | FULL | ✅ (from/to/cc→contact+junction memberships+org/account; 0.60–0.85; gate clean, FR-04 org-target correct) |
| 014 | Rung 3 structural detectors (`Detectors/`) | W1 | bff-api, communication | FR-10 | 011 | 015 | true | FULL | ✅ (4 detectors + IStructuralDetector + rung 3; metadata-only; gate PASS; W2→015 always-run-pass carry-forward) |
| 015 | Confidence→status + **auto-file ≥0.85** (ADR-018 kill-switch) | W1 | bff-api, communication | FR-11 | 002,012,013,014 | 030,051 | true | FULL | ✅ (mapper ladder + noisy-OR reinforcement + AI-never-autofile + conflict→Ambiguous + AutoFileGate/IOptionsMonitor kill-switch + provenance JSON; engine now aggregates all det rungs; gates 0 Critical; 310 Comm tests; publish 45.28MB +0.01; Path A cited) |
| 016 | Channel seams (`ICommunicationChannelSender`/`ICommunicationArchiver`) | W1 | bff-api, architecture | NFR-04 | 005 | — | true | FULL | ✅ (agent `6440217b3`; Channels/ seams + email impls + CommunicationType dispatcher; CommunicationService Graph-free [ctor 12→11, ADR-007 improvement]; NO Engine/Enrichment touched; 325 Comm tests green post-merge; publish 45.28MB ~0; §11 NFR-04 justified; 3-way merged CommunicationModule) |
| 017 | Central auth + direction-symmetry + per-rung tests | W1 | bff-api, testing | NFR-03,06,08 | 012,013,014,015 | — | true | FULL | ✅ (central-auth grep-clean NFR-03; direction-symmetry suite [4] + spine seam tests [3, tests/integration/seam/Communication/]; per-rung confidence/provenance by reference to 012–014 [no clone per ADR-038]; 317 Comm unit green; test-only, publish/CVE unchanged) |
| 018 | Engine cache thread-safety (`_recordTypeRefCache` → `ConcurrentDictionary`) — owner-directed, in-project | W1 | bff-api, communication | NFR-06 | 015 | — | false | STANDARD | ✅ (singleton-resolver data race fixed; ConcurrentDictionary; 310 Comm tests green) |
| 020 | `<EmailComposer />` engine + sub-components | W2 | frontend, fluent-ui | FR-12 | 001 | 021,041 | true | FULL | ✅ (18 smoke tests green in agent worktree; not re-run in main — node_modules) |
| 021 | `SendEmailStep`/`SendEmailDialog`/`SendEmailPage` wrappers | W2 | frontend, fluent-ui | FR-12 | 020 | 060,061 | true | FULL | ✅ (agent; new SendEmailDialog wins main barrel — old-API FilePreviewDialog migrates in 060) |
| 022 | `sendCommunication()` refinements (`SendCommunicationError`) | W2 | frontend, communication | FR-13 | 006 | 060 | true | FULL | ✅ (agent; fromResponse + barrel export; no attachment rename) |
| 023 | Composer + wrapper unit tests | W2 | frontend, testing | NFR-08 | 020,021,022 | — | true | TEST-MODIFYING | ✅ (agent; 8 suites/100 tests green; EmailComposer/ 80.39% cov; gates clean; 2 POML expectations adapted per W0 attachment-rename retraction — documented) |
| 030 | Rung 4 — `RecordSearchService` semantic match | W3 | bff-api, ai | FR-14 | 012,013,015 | 032 | true | FULL | 🔲 |
| 031 | Rung 5 — new JPS extract+classify Action → `AppOnlyAnalysisService` | W3 | bff-api, ai, jps | FR-15 | 015 | 032,053 | true | FULL | 🔲 |
| 032 | Per-rung telemetry + rung 4/5 tests | W3 | bff-api, testing | NFR-05,08 | 030,031 | — | true | FULL | 🔲 |
| 040 | Channel-aware Communication Code Page (shell) | W4 | code-page, frontend | FR-16 | 001,020 | 041,042 | true | FULL | ✅ (agent `102351c4b`) · **⚠ SUPERSEDED as form host by W4 pivot (OOB form + PCFs, 2026-07-15).** Shell/prototype-host retained; not the record form. See notes/W4-architecture-pivot-oob-form-pcf.md |
| 041 | Mount `<EmailComposer />`; ~~Form Component Control swap~~ | W4 | code-page, dataverse | FR-16 | 020,040 | 044 | true | FULL | ✅ (agent `30dc9ebf4`) · **⚠ FCC form-swap DROPPED (W4 pivot).** Composer-mount reused as PCF/dialog host (→044); build:prod clean; gates 0 Critical |
| 042 | **Connections PCF** — multi-association review (typed slots + per-slot confidence/confirm + create-from-email) in OOB accessories column | W4 | pcf, frontend, dataverse | FR-17 | 001,002,015 | 043 | true | FULL | ✅ (NEW `src/client/pcf/CommunicationConnections/` — virtual PCF, ports prototype ConnectionsEditor+provenance; build:prod clean [bundle 1.99MB], 14 jest green; gates 0 ADR violations. **C1 fixed**: multi-association = ADDITIVE typed-lookup writes mirroring the task-015 engine [`RegardingFieldMap`], NOT `applyResolverFields` clear-and-set [POML deviation — see note ↓]. View authored [`views/`]; deploy=043) |
| 043 | **PCF deploy + OOB form config** (Connections + Actions PCFs, attachment "Add Existing", subgrid) + UI tests | W4 | pcf, deploy, dataverse, e2e-test | NFR-07 | 042,044 | 060 | true | FULL | 🔄 PACKAGING DONE (POML rewritten to pivot scope; both solution ZIPs built + validated via `/pcf-deploy` — `CommunicationConnectionsSolution_v1.0.0.zip` + `CommunicationActionsSolution_v1.0.0.zip`). **REMAINDER = owner: `pac solution import` to spaarkedev1 + OOB form config (place both PCFs + auth env-vars) + pack the "Awaiting Association" view + remove deployed Send web resource/button + UI tests.** |
| 044 | **Communication Actions PCF** — Reply/Forward/Send/Save/Save-to-SharePoint over existing endpoints + `POST /{id}/archive`; retires ribbon `sprk_communication_send.js` | W4 | pcf, frontend, communication | FR-12,13 | 020,021,022 | 043,062 | true | FULL | ✅ (044a `POST /{id}/archive` [ArchiveExistingAsync, idempotent, .eml+per-attachment Docs] + 2 tests, BFF build clean; 044b CommunicationActions PCF [hosts SendEmailPage; Send/Reply/Forward/Draft/Save-to-SharePoint via @spaarke/auth] build:prod clean + 6 tests; 044c send.js×2 + send ribbon button RETIRED [createtodo KEPT — live feature], equivalence verified. Gates: 0 ADR violations, 0 Critical; code-review W1-W4 all fixed. §10 in `notes/044-communication-actions-completion.md`. **Deployed-button removal = 043.**) |
| 050 | ⛔ **[GATE]** Coordinate W5 with r2-core (Services/Ai ownership) | W5 | coordination, bff-api | — | 015 | 051,052,053,054 | **false** | STANDARD | ⛔ |
| 051 | Complete OutputRouter `record` + `notification` dispositions | W5 | bff-api, ai | FR-18 | 050 | 052 | true | FULL | 🔲 |
| 052 | Wire enrichment → `EventRulesService` → CreateEvent/Task/Notification | W5 | bff-api, ai | FR-19 | 050,051,010 | 054 | true | FULL | 🔲 |
| 053 | "Communication Triage" JPS Action → `DeliverComposite` | W5 | bff-api, ai, jps | FR-20 | 050,031 | 054 | true | FULL | 🔲 |
| 054 | Rule config (Binding + `sprk_matchconditions`); privilege-flag; tests | W5 | bff-api, ai, testing | FR-19 | 052,053 | — | true | FULL | 🔲 |
| 060 | Migrate SummarizeFilesDialog, FilePreviewDialog, DocumentEmailWizard | W6 | frontend, refactoring | FR-21 | 021,022,043 | 062 | true | FULL | 🔲 |
| 061 | Migrate 5 create-record wizards + CreateMatter fork; fix cross-import | W6 | frontend, refactoring | FR-21 | 021 | 062 | true | FULL | 🔲 |
| 062 | Retire `sprk_communication_send.js` after ribbon audit | W6 | dataverse, ribbon, deletion | FR-22 | 060,061 | — | true | FULL | ✅ CLOSED in **044c** — send.js×2 + send ribbon button retired (source); equivalence verified; createtodo button KEPT (live). Deployed removal at 043. |
| 070 | Graph compliance audit (`Mail-Advanced.*` 12-31; EWS 10-01) | W7 | bff-api, compliance | FR-23,NFR-01 | — | — | true | FULL | ✅ (agent; NO exposure — isRead exempt, zero EWS; findings note, no code change) |
| 071 | Subscription lifecycle-notification + `delta` reconciliation backstop | W7 | bff-api, graph | FR-24 | — | — | true | FULL | ✅ (agent; lifecycle dispatch + MailboxDeltaReconciliationService; 10 tests; +0 ArchTest debt via ADR-010 Path C) |
| 072 | Outlook add-in NAA/`@spaarke/auth` migration + unified manifest + org-URL | W7 | office-addins, auth | FR-25 | — | 074 | true | FULL | ✅ (build verified; live NAA smoke-test pending real Outlook+Azure+BFF) |
| 073 | Apply stubbed BFF Office auth filters (`OfficeEndpoints`) | W7 | bff-api, auth | FR-25 | — | — | true | FULL | ✅ (all mapped Office endpoints wired; 401/403 tests; gate clean) |
| 074 | Add-in save pane consumes Association Engine suggestions | W7 | office-addins | FR-25 | 072,012 | — | true | FULL | 🔲 |
| 075 | Index-config tokenization + read/write consolidation | W7 | bff-api, config | FR-26 | — | — | true | FULL | 🔲 |
| 076 | Refresh `knowledge/work-iq` snapshot | W7 | docs, knowledge | DEC-7 | — | — | true | MINIMAL | ✅ |
| 080 | Author `communication-intelligence-architecture.md` | W8 | docs | FR-27 | (W1–W7) | — | true | STANDARD | 🔲 |
| 081 | Update data-model + processing/service arch docs; mark OOB RETIRED | W8 | docs | FR-27 | (W1–W7) | — | true | STANDARD | 🔲 |
| 082 | Update `EMAIL-TRIAGE-MODULE-DESIGN.md` per DEC-10 | W8 | docs | FR-27 | (W1–W7) | — | true | STANDARD | 🔲 |
| 090 | Project wrap-up (README Complete, lessons-learned, `/test-diet`, archive) | Wrap | wrapup | — | (all) | — | **false** | STANDARD | 🔲 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **W0-A** | 001, 005 | — | Schema + ADR (005 main-session-only) |
| **W0-B** | 002, 003, 004 | 001 | Schema extensions (004 = FULL, BFF) |
| **W0-C** | 006, 007 | 005 (006 also 001) | BFF send-path + OOB retire |
| **W1** (serial spine) | 010 → 011 → {012, 013, 014} → 015 → 017 | W0 | 011 serializes engine refactor; 012–014 parallel; 016 anytime after 005 |
| **W2** | 020 → 021, 022 → 023 | W0 (020 needs 001; 022 needs 006) | *Runs concurrent with W1* (C# vs TS disjoint) |
| **W3** | 030, 031 → 032 | W1 (015; 030 also 012/013) | Semantic + AI rungs |
| **W4** | 040 → 041, 042 → 043 | W2 (020) + W1 (015 suggestions) | Code Page |
| **W5** | **050 (GATE)** → 051 → 052, 053 → 054 | W3 + **r2-core coordination** | ⛔ 051–054 blocked until 050 clears |
| **W6** | 060, 061 → 062 | W2 (021/022) + W4 (043) | Caller migration |
| **W7** | 070, 071, 072, 073, 075, 076 (‖); 074 after 072 | W0 | *Parallel track, deadline-driven; runs alongside W1–W6* |
| **W8** | 080, 081, 082 | W1–W7 substantially complete | Per-file doc targets parallel |

**Max concurrency**: 6 agents/wave. `.claude/`-touching tasks (005) + gate (050) + wrap-up (090) run main-session, sequential.

**Model tiers (per CLAUDE.md §8.5)**: default **sonnet @ high**. **opus** on the architectural / high-blast-radius tasks: **005** (ADR authoring), **010** (enrichment architecture), **011** (engine refactor over normalized envelope — serial spine), **051 / 052** (edit shared `Services/Ai/` internals via PublicContracts). **effort: xhigh** on brownfield/high-consequence: **007** (retire OOB), **011**, **015** (auto-file), **051**, **052**. All others sonnet @ high.

## Critical Path
`001 → 005 → 006 → 010 → 011 → 012 → 015 → 030/031 → 050(gate) → 051 → 052 → 054 → 080 → 090`

## High-Risk / Watch Items
- **050 gate** (⛔): W5 blocked on `spaarke-ai-architecture-redesign-r2` coordination (Services/Ai sole-owner). Run `/conflict-check`; consume `PublicContracts` seams; no internal fork.
- **007** (OOB retire): verify no references remain; report publish-size reduction.
- **070** (compliance): two hard Microsoft deadlines (EWS 2026-10-01, `Mail-Advanced` 2026-12-31).
- **015** (auto-file): owner override — auto-file ON for deterministic ≥0.85; kill-switch must flip without redeploy; AI rungs never auto-file.
- Every BFF-touching task: `/conflict-check` before PR + publish-size + CVE report (root §10).

## FR Coverage
FR-01→001 · FR-02→002 · FR-03→003 · FR-04→004 · FR-05→005 · FR-06→006 · FR-07→007 · FR-08→010 · FR-09→011 · FR-10→012,013,014 · FR-11→015 · FR-12→020,021 · FR-13→022 · FR-14→030 · FR-15→031 · FR-16→040,041 · FR-17→042 · FR-18→051 · FR-19→052,054 · FR-20→053 · FR-21→060,061 · FR-22→062 · FR-23→070 · FR-24→071 · FR-25→072,073,074 · FR-26→075 · FR-27→080,081,082. NFRs distributed (016,017,023,032,043).
