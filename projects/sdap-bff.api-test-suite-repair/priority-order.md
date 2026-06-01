# Priority Order — Phase 2+3 Repair Sequencing

> **Project**: `sdap-bff.api-test-suite-repair`
> **Created**: 2026-05-31 by Phase 0 task 005 (`tasks/005-priority-order.poml`)
> **Binding constraints**: FR-04 (this file's existence + sibling sign-offs), FR-20 (LOW-tier start gate), design.md §4.7 (coordinate, don't lock), §2.3 (sibling-project risk matrix), NFR-01 (no production code), NFR-09 (`repair-not-rewrite: true`)
> **Source of truth for measured numbers**: [`baseline/test-baseline-2026-05-31.trx`](baseline/test-baseline-2026-05-31.trx) (342 failures, parsed by area below) + [`baseline/README.md`](baseline/README.md) (deviation analysis vs. design.md §3)
> **File-count source**: design.md §3.3 (~35 / ~70 / ~25 / ~88 files per tier) — failure counts from TRX. See "Per-tier numbers" notes for derivation.

---

## 🔔 Owner action required

**Contact 3 sibling-project owners and fill in the sign-off cells** in the per-tier tables and the [Owner Outreach Status](#owner-outreach-status) section below:

1. **`ai-spaarke-action-engine-r1`** owner — HIGH-risk overlap; flag any in-flight workspace/finance/endpoint work
2. **`ai-spaarke-insights-engine-r1`** owner — MEDIUM-risk overlap; in-flight tests under `Services/Ai/*`
3. **`x-email-communication-solution-r2`** owner — MEDIUM-risk overlap; Communications test files in the failing-test set

**If a sibling-project owner does not respond within 1 business day** (per [`spec.md`](spec.md) Assumptions: "sibling owners respond to coordination sync within 1 business day"), default the priority order to "active areas last" without sign-off and proceed. Outreach is **awareness, not veto** — sibling owners do NOT have authority to delay this project; they have authority to flag conflicts so we sequence around them.

This file is a **coordination artifact**, not a deployment gate. The AI agent (task 005) drafted the structure + pre-filled what could be derived from data; the **owner fills the sign-off cells** during outreach.

---

## §4.7 Principle — Coordinate, don't lock

Per [`design.md`](design.md) §4.7 (locked decision): **this project does NOT freeze sibling BFF projects during repair.** Instead, the priority order sequences in-flight areas LAST so the disruptive portion of repair happens against quieter code. The sibling-owner sign-off mechanism informs owners "we are about to touch test files in your area; flag any conflict." It is NOT a veto — it is awareness.

Counter-intuitively, this is the *robust* choice (per the §5 owner direction "robust over easy"): freezing siblings would create coordination cost without proportional risk reduction; sequencing-with-awareness gets ~80% of the benefit at ~10% of the cost.

---

## Tier ordering at a glance

| Order | Tier | File-count target (design.md §3.3) | Measured failures (TRX 2026-05-31) | Start gate |
|---|---|---|---|---|
| 1 | **HIGH — pure algorithm + safety** | ~35 files | ~19 failures (Services/Ai/Safety; Workspace/Scorecard/Finance/Email currently 0 failures — verify pre-execution) | Phase 1 exit (P1.A compile + P1.C factory + P1.B helper all green) |
| 2 | **MEDIUM — service orchestration** | ~70 files | ~81 failures (53 Communication + 28 Ai/Chat/Capabilities/Nodes/other) | Same as HIGH; runs in parallel per design.md §7.0 Phase 2+3 unlock |
| 3 | **INTEGRATION** | ~25 files (Sprk.Bff.Api.Tests/Integration/) + `Spe.Integration.Tests` (per D-03 / FR-13) | ~72 failures (54 Workspace + 9 Communication + 8 SseStreaming + 1 PlaybookExecution; `Spe.Integration.Tests` compile-broken per task 002) | Same as HIGH; runs in parallel per design.md §7.0 |
| 4 | **LOW — endpoint pipeline** | ~88 files | ~143 failures (Api/Ai/* 89 + Api/Reporting 17 + Api/Office 10 + Api/Agent 7 + remainder spread across Api/Upload, Api/Files, Api/User, etc.) | **FR-20: starts only after HIGH + MEDIUM 50% complete** |

**Counts caveat**: the failing-test counts above are from `baseline/test-baseline-2026-05-31.trx` parsed by namespace prefix (`grep`-aggregated). The per-tier *file* counts (~35 / ~70 / ~25 / ~88) come from design.md §3.3 — they may not match exactly after task 008's per-class inventory; refine here when task 008 publishes its formal area-counts. The baseline shows 342 total failures (vs. design.md §3's 269) — see [`baseline/README.md`](baseline/README.md) for the +73 delta interpretation.

---

## HIGH Tier — `Pure algorithm + safety` (~35 files, ~19 measured failures)

**Rationale (design.md §3.3)**: deterministic algorithms with silent-bug + real-money/safety cost; highest defect-prevention value; repair first while context is fresh.

**Scope (design.md §3.3 + §7 P23.H)**:
- `Services/Workspace/*` (scoring, ~2,300 LOC; e.g., `PriorityScoringServiceTests` 772 LOC, `EffortScoring` 872 LOC)
- `Services/Scorecard*` (4 files)
- `Services/Finance/SignalEvaluation*` (~780 LOC)
- `Services/Email/EmailAssociation*` (~863 LOC)
- `Services/Ai/Safety/*` (groundedness, citations, prompt shield, confidence scoring)
- `Filters/*`
- `Infrastructure/Json/*`
- `Infrastructure/Resilience/*`

### HIGH-tier area annotations (sibling-owner sign-offs)

| Area | Measured failures | File count | Sibling project | Sibling owner | Sign-off date |
|---|---|---|---|---|---|
| `Services/Workspace/*` | 0 (TRX) — verify post-task-008 | ~8 (design.md §3.3) | `ai-spaarke-action-engine-r1` (workspace endpoints/services in flight) | TBD | TBD |
| `Services/Scorecard*` | 0 (TRX) | 4 | no in-flight overlap | N/A | N/A |
| `Services/Finance/SignalEvaluation*` | 0 (TRX) | ~3 | no in-flight overlap | N/A | N/A |
| `Services/Email/EmailAssociation*` | 0 (TRX) | ~2 | `x-email-communication-solution-r2` (Email subdomain) | TBD | TBD |
| `Services/Ai/Safety/*` | **19** | ~6 | `ai-spaarke-insights-engine-r1` (Phase 2 adds Services/Ai/* tests) | TBD | TBD |
| `Filters/*` | 0 (TRX) | ~6 | no in-flight overlap | N/A | N/A |
| `Infrastructure/Json/*` | 0 (TRX) | ~3 | no in-flight overlap | N/A | N/A |
| `Infrastructure/Resilience/*` | 0 (TRX) | ~3 | no in-flight overlap | N/A | N/A |

**HIGH-tier note**: design.md §3 estimated higher HIGH-tier failure counts; the TRX shows most HIGH-tier failures concentrate in `Services/Ai/Safety/*` (19). This is consistent with the design's "deterministic algorithm bugs are mostly mechanical drift" framing — the algorithms themselves are stable; safety scoring assertions need updates.

---

## MEDIUM Tier — `Service orchestration (mocked)` (~70 files, ~81 measured failures)

**Rationale (design.md §3.3)**: mocked-dependency tests verifying orchestration; medium defect-prevention value; repair second.

**Scope (design.md §3.3 + §7 P23.M)**:
- `Services/Ai/Chat/*` (30 files)
- `Services/Ai/Capabilities/*`
- `Services/Ai/Nodes/*`
- `Services/Communication/*` (16 files; HIGHEST sibling-coordination risk per design.md §2.3)

### MEDIUM-tier area annotations (sibling-owner sign-offs)

| Area | Measured failures | File count | Sibling project | Sibling owner | Sign-off date |
|---|---|---|---|---|---|
| `Services/Ai/Chat/*` | **4** | ~30 | `ai-spaarke-insights-engine-r1` (Phase 2 in flight) | TBD | TBD |
| `Services/Ai/Capabilities/*` | **2** | ~10 | `ai-spaarke-insights-engine-r1` | TBD | TBD |
| `Services/Ai/Nodes/*` | **5** | ~10 | `ai-spaarke-insights-engine-r1` | TBD | TBD |
| `Services/Ai/*` (other: Sessions, Feedback, RagService, Insights, WorkingDocument) | **17** | ~10 | `ai-spaarke-insights-engine-r1` | TBD | TBD |
| `Services/Communication/*` | **53** (Communication: 53 = AssociationMapping 29 + DataverseRecordCreation 23 + 1 other) | ~16 | **`x-email-communication-solution-r2`** — HIGHEST coordination risk per design.md §2.3 + project CLAUDE.md Implementation Notes | TBD | TBD |

**MEDIUM-tier note**: `Services/Communication/*` is the single most active sibling-overlap area in the entire repair scope. design.md §2.3 calls this out specifically ("ArchivalFlow, AssociationMapping, AttachmentValidation, CommunicationService, DataverseRecordCreation, EmailAttachmentExtraction"). The Communications sibling project owner MUST sign off before MEDIUM-tier Communications batches start. Per project CLAUDE.md: "Owner-aligned for Phase 1 task 011 + Phase 2+3 tasks 055, 056."

---

## INTEGRATION Tier — `Sprk.Bff.Api.Tests/Integration/` + `Spe.Integration.Tests` (~25 files + Spe.Integration.Tests, ~72 measured failures + Spe build-broken)

**Rationale (design.md §3.3 + D-03 lock-in)**: WireMock-based + workspace fixtures; closer to real behavior than mocked unit tests; medium-high defect-prevention value; repair third. **`Spe.Integration.Tests` is IN scope per D-03** (locked decision 2026-05-31 by Phase 0 task 006).

**Scope (design.md §7 P23.I + FR-18)**:
- `Sprk.Bff.Api.Tests/Integration/*` (25 files)
- `tests/integration/Spe.Integration.Tests/*` (failure classification deferred to Phase 1 P1.E / FR-13; currently build-broken per task 002 — 4× CS1739 in `ExternalAccessIntegrationTests.cs`)

### INTEGRATION-tier area annotations (sibling-owner sign-offs)

| Area | Measured failures | File count | Sibling project | Sibling owner | Sign-off date |
|---|---|---|---|---|---|
| `Integration/Workspace/*` (WorkspaceEndpointsTests 31 + WorkspaceLayoutEndpointTests 23) | **54** | ~6 | `ai-spaarke-action-engine-r1` (workspace endpoints in flight) | TBD | TBD |
| `Integration/CommunicationIntegrationTests` | **9** | 1 | `x-email-communication-solution-r2` | TBD | TBD |
| `Integration/SseStreamingIntegrationTests` | **8** | 1 | `ai-spaarke-insights-engine-r1` (streaming = AI surface) | TBD | TBD |
| `Integration/PlaybookExecutionTests` | **1** | 1 | `ai-spaarke-insights-engine-r1` (playbook = AI surface) | TBD | TBD |
| `Integration/*` (remainder: ~16 files) | 0 (TRX — verify) | ~16 | no in-flight overlap | N/A | N/A |
| **`Spe.Integration.Tests`** (per D-03; build currently broken — 4× CS1739) | N/A — project does not compile | ~unknown (Phase 1 P1.E baselines) | no in-flight sibling | N/A — compile recovery is FR-13 scope | N/A |

**INTEGRATION-tier note**: 54 of the 72 measured INTEGRATION failures are in `Integration/Workspace/*`. This is the SECOND-highest sibling-overlap concentration after Communications and intersects directly with `ai-spaarke-action-engine-r1` (which adds new workspace endpoints/services per project CLAUDE.md "Related Projects" table). Action Engine sign-off is binding for this section. **Dependabot coordination**: per project CLAUDE.md Implementation Notes, "PRs #287 (FluentAssertions), #265 (coverlet.collector), #236 (Microsoft.AspNetCore.Mvc.Testing) touch test infrastructure. Coordinate merge timing with Phase 2+3 P23.I (integration tests) — don't merge during active P23.I work."

---

## LOW Tier — `Endpoint pipeline` (~88 files, ~143 measured failures) — START-GATED

**🚪 START GATE (FR-20)**: **LOW-tier repair begins ONLY after HIGH + MEDIUM tier repair is 50% complete.**

**Rationale (design.md §3.3 + §7 P23.L)**: 88 endpoint pipeline files; most duplicate synthetic-smoke coverage; low defect-prevention value individually. Triage discipline: keep response-shape contract tests; archive route-registration duplicates. The 50%-complete gate prevents over-investment in low-value triage while HIGH/MEDIUM regression risk is still material.

**Scope (design.md §3.3 + §7 P23.L + FR-19)**:
- `Api/*` (75 files)
- Top-level `*EndpointTests` (13 files)
- Default action per design.md §7 P23.L: "archive if duplicates synthetic smoke; repair if tests a response-shape contract that smoke doesn't"
- **Owner approval triggered if archive count exceeds 10 files** in this tier (per design.md §5.4 refinement + NFR-04)

### LOW-tier area annotations (sibling-owner sign-offs)

| Area | Measured failures | File count | Sibling project | Sibling owner | Sign-off date |
|---|---|---|---|---|---|
| `Api/Ai/*` (PlaybookRun 20 + StandaloneChatContext 18 + Handler 11 + Node 10 + AnalysisChatContext 10 + Model 8 + ChatSessionPlan 5 + ChatRefine 4 + DailyBriefing 2 + Agent/Conversation 3) | **89** (largest LOW-tier cluster) | ~35 | `ai-spaarke-insights-engine-r1` + `ai-spaarke-action-engine-r1` (both touch Ai endpoints) | TBD | TBD |
| `Api/Reporting/*` (ReportingEndpoints 12 + ReportingAuthorizationFilter 5) | **17** | ~3 | no in-flight overlap | N/A | N/A |
| `Api/Office/*` (OfficeEndpoints 10) | **10** | ~2 | no in-flight overlap | N/A | N/A |
| `Api/Agent/*` (HandoffUrlBuilder 3 + AgentConversationService 3) | **6** | ~3 | `ai-spaarke-action-engine-r1` | TBD | TBD |
| Top-level `UserEndpointsTests`, `UploadEndpointsTests`, `ListingEndpointsTests`, `FileOperationsTests`, `HealthAndHeadersTests`, `PipelineHealthTests`, `CorsAndAuthTests`, `EndpointGroupingTests`, `SpeAdmin/SearchItemsTests` | ~22 spread thinly (1-7 per class) | ~13 | `ai-spaarke-action-engine-r1` (any new endpoint adds here) | TBD | TBD |
| Remaining `Api/*` (uncovered above) | 0 (TRX) | ~32 | no in-flight overlap | N/A | N/A |

**LOW-tier note (start gate enforcement)**: per FR-20, P23.L wave kickoff is gated on the running tally of HIGH + MEDIUM tier completion. The project-pipeline / TASK-INDEX.md wave structure encodes this; the gate is enforced at wave dispatch time, NOT at file-write time of this document. If the owner needs to override the gate (e.g., LOW-tier blocker for a sibling project), document the override in `ledgers/archive-ledger.md` per design.md §5.4.

---

## Owner Outreach Status

> **Fill these cells during owner outreach.** Update status as: `TBD` → `contacted` → `signed-off` OR `no-in-flight-overlap` (if sibling owner confirms their project is not currently active in any of the tiered areas).

| # | Sibling project | Risk (design.md §2.3) | Coordination action (design.md / project CLAUDE.md) | Status | Owner contact | Sign-off date |
|---|---|---|---|---|---|---|
| 1 | **`ai-spaarke-action-engine-r1`** | **HIGH** — new BFF endpoints/services | Phase 0 task 005 (THIS file): priority-order sign-off + commitment to use test convention this project establishes. Affects HIGH `Services/Workspace`, INTEGRATION `Integration/Workspace`, LOW `Api/Ai`, LOW `Api/Agent`, LOW top-level endpoints | **signed-off** | `dev@spaarke.com` | 2026-06-01 |
| 2 | **`ai-spaarke-insights-engine-r1`** | MEDIUM — Phase 2 adds tests under `Services/Ai/` | Daily sync during Phase 2+3 P23.M; priority order sequences Insights-active files last (per project CLAUDE.md "Related Projects"). Affects HIGH `Services/Ai/Safety`, MEDIUM `Services/Ai/*`, INTEGRATION `Integration/SseStreaming`+`Integration/PlaybookExecution`, LOW `Api/Ai` | **signed-off** | `dev@spaarke.com` | 2026-06-01 |
| 3 | **`x-email-communication-solution-r2`** | MEDIUM — Communications test files in failing-test set | Owner-aligned for Phase 1 task 011 + Phase 2+3 tasks 055, 056 (per project CLAUDE.md). Affects HIGH `Services/Email/EmailAssociation`, MEDIUM `Services/Communication/*` (the single highest sibling-overlap area), INTEGRATION `Integration/CommunicationIntegrationTests` | **signed-off** | `dev@spaarke.com` | 2026-06-01 |

**Sign-off semantics**:
- **`signed-off`**: sibling owner acknowledged the area+timing; flagged any specific files to avoid; agreed on coordination cadence
- **`no-in-flight-overlap`**: sibling owner confirmed their project is NOT currently touching the listed test areas during the repair window
- **`TBD`**: outreach not yet completed (default; cells start here)
- **`contacted`**: outreach in flight; awaiting response

---

## Cross-references

| Reference | Purpose |
|---|---|
| [`spec.md`](spec.md) FR-04 | Binding source for this file's existence + sibling sign-off requirement |
| [`spec.md`](spec.md) FR-20 | Binding source for LOW-tier 50%-complete start gate |
| [`spec.md`](spec.md) Assumptions | "Sibling owners respond within 1 business day" — drives the default-without-sign-off fallback at file top |
| [`design.md`](design.md) §2.3 | Sibling-project risk matrix + coordination matrix |
| [`design.md`](design.md) §3 | Baseline test counts (per-tier file estimates: ~35 / ~70 / ~25 / ~88) |
| [`design.md`](design.md) §4.7 | "Coordinate, don't lock" — the principle this file enforces |
| [`design.md`](design.md) §5.4 | Triage authority (agent judges, archive >10 files in tier triggers owner) |
| [`design.md`](design.md) §7 P23.H / P23.M / P23.I / P23.L | Per-tier scope groupings + start-gate semantics |
| [`CLAUDE.md`](CLAUDE.md) | Project binding rules; "Related Projects" table is the source for sibling-owner identification |
| [`baseline/test-baseline-2026-05-31.trx`](baseline/test-baseline-2026-05-31.trx) | Source of all measured failure counts in this file |
| [`baseline/README.md`](baseline/README.md) | Deviation analysis (342 vs. design.md §3's 269) + compile-error 0/17 deviation |
| [`decisions/D-03-integration-in-scope.md`](decisions/D-03-integration-in-scope.md) | Locked decision: `Spe.Integration.Tests` IS in INTEGRATION tier scope |

---

## Change log

| Date | Change | By |
|---|---|---|
| 2026-05-31 | Initial draft per task 005 (`tasks/005-priority-order.poml`); 4 tier sections + 3 sibling-owner sign-off slots (all TBD); per-area failure counts parsed from `baseline/test-baseline-2026-05-31.trx`; file-count estimates from design.md §3.3 (to be refined by task 008 area-counts) | AI agent (task-execute) |
| 2026-06-01 | **FR-06 satisfied** — 3 sibling-owner sign-off slots populated with `dev@spaarke.com` (consolidated contact per r2 owner clarification 2026-06-01). All three siblings (Action Engine, Insights, Communications) signed-off by the same owner; per-area annotation tables (HIGH/MEDIUM/INTEGRATION/LOW) retain `TBD` cell text per-area, but the Owner Outreach Status table (the FR-06 authoritative source) is fully resolved. See `projects/sdap.bff.api-test-suite-repair-r2/decisions/owner-responses/consolidated-sibling-contact-2026-06-01.md`. | r2 task 002 (task-execute) |

---

*This file is a coordination artifact. The AI agent drafted structure + pre-filled data-derivable fields; the owner fills sign-off cells during outreach. Sign-off is awareness, not veto. Per design.md §4.7: coordinate, don't lock.*
