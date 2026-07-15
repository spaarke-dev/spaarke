# Email Communication Solution R4 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-14
> **Source**: `projects/email-communication-solution-r4/design.md` (rev 2, 2026-07-14)
> **Absorbs**: `x-email-communication-solution-r3` (SUPERSEDED — designed, 79 tasks, never executed; send-side design preserved at [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md))

## Executive Summary

R4 turns Spaarke's already-live, 100%-Graph inbound email pipeline into a **channel-extensible Communication Intelligence layer**. A single direction-agnostic `ICommunicationEnrichmentService` gives inbound *and* outbound communications identical treatment (fixing outbound's missing auto-association and RAG indexing), wrapping a **unified Association Engine** that operates over a normalized message envelope — deterministic-first, then semantic and LLM — with confidence, provenance, and eight target entities. Assessed communications feed **Responsive Intelligence** (completing the stubbed OutputRouter `record`/`notification` legs) so declarative rules create Events/Tasks, notify assignees, and emit summaries/checklists. A **channel-aware Communication Code Page** (consuming R3's `<EmailComposer />` for email compose) replaces the OOB form and hosts the `RegardingResolver`-based review path. Everything uses Spaarke Auth v2 primitives; a mandatory hardening track closes the Microsoft-2026 Graph/EWS/add-in gaps. The legacy OOB-`email`-activity subsystem (`Services/Email/`) is retired.

R4 also absorbs R3's send-side client consolidation (canonical `<EmailComposer />` engine + wrappers + caller migration), executed in parallel with R4's server work (C# vs TS, disjoint by file and language).

## Scope

### In Scope

- **W0 — Shared foundation**: one `sprk_communication` schema pass (R3 reply-thread columns + R4 association columns/statuses); one new **Communication ADR (ADR-045)**; BFF server send-path changes (`AttachmentDriveItemIds` rename, `Internet-Message-Id` capture); **retire the OOB-`email` subsystem** (`Services/Email/`, `/api/v1/emails/*`, `EmailAssociationService`).
- **W1 — Server enrichment + deterministic engine**: `ICommunicationEnrichmentService` (both directions, + missing outbound RAG); refactor `IncomingAssociationResolver` → Association Engine over normalized envelope; extend targets to eight entities; structural detectors; confidence→status + **auto-file threshold**; channel seams (email impl); central auth.
- **W2 — Client composer engine + wrappers** (absorbed R3): `<EmailComposer />` engine + `SendEmailStep`/`SendEmailDialog`/`SendEmailPage` wrappers; `sendCommunication()` refinements.
- **W3 — Server semantic + AI rungs (4–5)**: `RecordSearchService` rung 4; new JPS extraction/classification action → `AppOnlyAnalysisService` rung 5.
- **W4 — Channel-aware Communication Code Page**: view/record surface generalized by `sprk_communicationtype`; mounts `<EmailComposer />` for email compose; Form Component Control swap; embeds `RegardingResolver` PCF + suggestion/confidence review; "Communications Awaiting Association" view.
- **W5 — Responsive Intelligence**: complete OutputRouter `record`/`notification` dispositions; wire enrichment → `EventRulesService` → CreateEvent/CreateTask/CreateNotification; new "Communication Triage" JPS Action → `DeliverComposite`; rules via Binding + `sprk_matchconditions`.
- **W6 — Client caller migration + retirements** (absorbed R3): migrate SummarizeFilesDialog, FilePreviewDialog, DocumentEmailWizard, 5 create-record wizards; retire LegalWorkspace forks + `sprk_communication_send.js`.
- **W7 — Microsoft hardening + auth + index** (parallel track): Graph `Mail-Advanced.*`/EWS audit; subscription lifecycle-notification + `delta` reconciliation backstop; **Outlook add-in NAA/`@spaarke/auth` migration + apply the stubbed BFF Office auth filters + unified JSON manifest + org-URL fix + surface engine suggestions in save pane**; index-config tokenization + read/write consolidation; refresh `knowledge/work-iq`.
- **W8 — Documentation**: new `communication-intelligence-architecture.md`; update `sprk_communication.md`, `email-processing-architecture.md`, `communication-service-architecture.md`; mark OOB-`email` + fragmented-send docs RETIRED; update `EMAIL-TRIAGE-MODULE-DESIGN.md` (fix stale refs, point at R4 substrate, sequence after R4).

### Out of Scope

- Teams / Slack / Gmail / SMS **channel implementations** — R4 ships only the seams (`ICommunicationChannelSender`/`ICommunicationArchiver`, email impl only) (DEC-11).
- The full **Email Triage Workbench** product (queue UI, bulk disposition, SLA, Daily Briefing tile, MCP exposure) — downstream product consuming R4 (DEC-10). R4 review surface is the minimum to close the loop, not the Workbench.
- **Work IQ** integration (delegated-only, consumption-billed, prose-oriented — not an app-only classifier) (DEC-7, requirement #12).
- Server-Side Sync and OOB Dataverse `email`/activity entities — R4 stands only on the Graph + `sprk_communication` path; does not unify, re-enable, or extend SSS (requirement #1).
- Finishing Outlook **Share / Grant Access** and in-Outlook triage (backlog §12).
- Platform-wide Dataverse client-secret drift consolidation (`DataverseServiceClientImpl`/`DataverseWebApiService`) — noted, not owned.
- Feedback-learning loop from reviewer overrides (override reasons captured as signal only).

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Communication/` — `IncomingCommunicationProcessor`, `IncomingAssociationResolver`, `CommunicationService`; new `ICommunicationEnrichmentService`, Association Engine, `Detectors/`, channel seams.
- `src/server/api/Sprk.Bff.Api/Services/Email/` + `Api/EmailEndpoints.cs` (`/api/v1/emails/*`) — **retire** (W0).
- `src/server/api/Sprk.Bff.Api/Services/Ai/` — `AppOnlyAnalysisService`, `AnalysisOrchestrationService`, `OutputRouter`/`DispositionRoutability.cs`, `EventRulesService`, node executors (`CreateNotification`/`CreateTask`/`DeliverComposite`).
- `src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs` — apply the stubbed `.AddOfficeAuthFilter()/.AddJobOwnershipFilter()/.AddEntityAccessFilter()` (currently `// TODO: Task 033`).
- `src/client/shared/Spaarke.UI.Components/src/` — `<EmailComposer />` engine + wrappers + sub-components; `PolymorphicResolverService`; `FieldMappingService`.
- `src/client/office-addins/shared/` — migrate off deprecated `NaaAuthService`/`DialogAuthService`/`authConfig` onto `@spaarke/auth` `OfficeNaaStrategy`.
- `src/client/pcf/RegardingResolver/` — review/override surface (reused; catalog extended, no new branches).
- `src/client/code-pages/` — new channel-aware Communication Code Page (exemplar: `DocumentRelationshipViewer/`).
- Dataverse: `sprk_communication` schema; author `docs/data-model/sprk_servicerequest.md`; catalog/priority/`RegardingLookupMap` extensions.
- `appsettings.template.json` (`AiSearch` section `:234-248`) — tokenize index names + `AllowedIndexes`.

## Requirements

### Functional Requirements

**W0 — Foundation**
1. **FR-01**: One `sprk_communication` schema pass adds R3 reply-thread columns (`sprk_inreplyto`, `sprk_internetmessageid`), R4 association columns (`sprk_associationprovenance` JSON, `sprk_regardingservicerequest` lookup), and confirms `sprk_receiveddate`/`sprk_associationstatus`. — Acceptance: all columns exist in the solution; `docs/data-model/sprk_communication.md` updated (closes §1.2 doc drift).
2. **FR-02**: Add `Suggested` and `Ambiguous` values to the `sprk_associationstatus` option set, integers **verified via Dataverse MCP** before assignment (suggested ~100000002/3) (DEC-5). — Acceptance: option-set values created with no integer collision; verification recorded in task notes.
3. **FR-03**: Author `sprk_servicerequest` schema doc and wire it as an association target: add `sprk_regardingservicerequest` lookup, `RegardingLookupMap` entry, `TODO_REGARDING_CATALOG` + `RegardingFieldPriority` entries (requirement #14). — Acceptance: `sprk_servicerequest` resolvable end-to-end through the regarding machinery.
4. **FR-04**: Add `sprk_event` to the regarding catalog/priority; **correct the organization association target to `sprk_organization`** (not OOB `account`, not OOB `organization`) — fix the sender-domain match that currently writes `account` (DEC-3, requirement #15). — Acceptance: domain-match writes `sprk_regardingorganization`→`sprk_organization`; no path writes `account` for org association.
5. **FR-05**: Author **ADR-045** (single Communication ADR): client canonical send + server Association Engine + enrichment service + channel seams. Supersedes the retired R3 "ADR-033" plan (ADR-033 number is already occupied by an unrelated accepted ADR). — Acceptance: ADR-045 concise + full versions committed; cross-referenced from design.
6. **FR-06**: BFF send-path changes — non-breaking `AttachmentDriveItemIds` rename (`[Obsolete]` alias on `AttachmentDocumentIds`); capture real `Internet-Message-Id` post-send and stamp `sprk_internetmessageid` (feeds W1 thread rung); `reply`/`forward` stamp `sprk_inreplyto`. — Acceptance: existing callers compile; thread columns populated on send.
7. **FR-07**: **Retire the OOB-`email` subsystem** — delete `Services/Email/` async remnants, `Api/EmailEndpoints.cs` (`/api/v1/emails/*`), `EmailAssociationService`, `EmailToEmlConverter`, and the `PrimaryEntityName=="email"` webhook registration (DEC-2). Reuse only the confidence-scoring *signal design* (reimplemented in W1). — Acceptance: no references to retired types remain; build + tests green; one ADR-028 auth-drift point (self-built `ConfidentialClientApplication`) removed.

**W1 — Server enrichment + deterministic engine**
8. **FR-08**: `ICommunicationEnrichmentService` — direction-agnostic, signature `(communicationId, direction, NormalizedMessage, archivedDocumentId?)`, invoked by **both** `IncomingCommunicationProcessor` and `CommunicationService` outbound creators; owns association → categorization → AI analysis → RAG indexing → Responsive-Intelligence trigger, in order (requirement #6). — Acceptance: outbound send now auto-associates and RAG-indexes (both prior gaps closed); best-effort non-fatal invariant preserved.
9. **FR-09**: Refactor `IncomingAssociationResolver` into the **Association Engine** operating over a **normalized envelope** (`{ direction, from, to[], cc[], subject, bodyText, bodyHtml, internetMessageId, inReplyTo, references[], conversationId, sentAt, attachments[] }`), never `Microsoft.Graph.Message`; rung interface + per-attribute confidence + provenance; **rungs 0–2 behavior preserved under test before extension** (R-7). — Acceptance: existing inbound matches unchanged; engine consumes envelope only.
10. **FR-10**: Deterministic rungs 0–3 across eight targets (matter, project, invoice, service request, work assignment, event, contact, organization): rung 0 explicit-ref/caller-supplied; rung 1 thread continuity (`inReplyTo`/`references`/`conversationId`, reimplemented `conversationindex` logic); rung 2 participant correlation (extend `QueryContactByEmailAsync` to membership + org-by-domain); rung 3 structural detectors in new `Detectors/` (calendar-invite, e-sign completion, invoice #, court/e-filing). — Acceptance: each rung has tests + direction-symmetry tests; provenance recorded per match.
11. **FR-11**: Confidence→status mapping with **auto-file enabled at launch** for deterministic rungs 0–3 at ≥0.85 (→ `Resolved`, auto-file); 0.50–0.85 or **any AI rung** → `Suggested`; <0.50/none → `Pending Review`; conflicting high-confidence → `Ambiguous`. Auto-file governed by a per-tenant **ADR-018 kill-switch** (config, default-on for deterministic ≥0.85) (DEC-4, owner override — see Owner Clarifications). — Acceptance: statuses assigned per ladder; kill-switch flips auto-file to suggest-only without redeploy; AI-rung matches never auto-file.

**W2 — Client composer** (absorbed R3; detail §8.5 + [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md))
12. **FR-12**: Canonical `<EmailComposer />` engine in `@spaarke/ui-components` (injected `authenticatedFetch`, React 18, Fluent v9) with modes `compose|view|reply|forward|draft`, mounts `inline|dialog|page`, three thin semantic wrappers (`SendEmailStep`/`SendEmailDialog`/`SendEmailPage`), and canonical sub-components (`RecipientField`, `BodyEditor`, `AttachmentList` caps 150/35 MB, `SendModeRadio`, `AssociationChips`, `ComposerActionBar`). — Acceptance: wrappers render in all three mounts; unit tests pass.
13. **FR-13**: `sendCommunication()` refinements — add `SendCommunicationError` (parses ProblemDetails: status/code/detail/correlationId, replaces 3 divergent shapes); use canonical `attachmentDriveItemIds` (SPE driveItem IDs, not `sprk_document` GUIDs). — Acceptance: single typed error path; `DocumentEmailWizard.tsx:494` no longer sends wrong IDs.

**W3 — Semantic + AI rungs**
14. **FR-14**: Rung 4 — `RecordSearchService` / `spaarke-records-index` semantic match (extracted terms → hybrid search; `confidenceScore` + `matchReasons`) for fuzzy matter/project/invoice/org. — Acceptance: rung 4 emits `Suggested`-tier results with match reasons in provenance.
15. **FR-15**: Rung 5 — new JPS extract+classify Action (`$choices` → record types; category, urgency, obligations[], suggested actions[]) run via `AppOnlyAnalysisService` (app-only/MI; ADR-016 budget, ADR-014 cache); emits `Suggested`/`Ambiguous` only, with rationale. Per-rung telemetry (DEC-8). — Acceptance: AI rung fires only on ambiguity/miss; never auto-files; telemetry per rung recorded.

**W4 — Communication Code Page**
16. **FR-16**: Channel-aware view/record Code Page generalized by `sprk_communicationtype` (email interactive today; Teams/SMS/Notification render read-only later); mounts `<EmailComposer />` for email compose; Form Component Control swap (standard OOB form retained as admin fallback); `@spaarke/auth` v2 (exemplar `DocumentRelationshipViewer/`) (requirement #7). — Acceptance: page replaces OOB form for `sprk_communication`; layout keys off communication type, not "email".
17. **FR-17**: Review surface — embed `RegardingResolver` PCF (via `PolymorphicResolverService.applyResolverFields`) pre-filled with the engine's top suggestion + confidence + provenance rationale; 1-click accept or pick another; "Communications Awaiting Association" Dataverse view (`sprk_associationstatus in (Suggested, Pending Review, Ambiguous)`); optional Field Mapping on accept; override reasons captured as feedback signal. — Acceptance: reviewer can confirm/override a suggestion in ≤1 click; view is matter-level auth-scoped (R-6).

**W5 — Responsive Intelligence**
18. **FR-18**: Complete OutputRouter `record` + `notification` dispositions (remove `NotSupportedException` in `DispositionRoutability.cs`) (requirement #9). — Acceptance: both dispositions route; no `NotSupportedException` remains for these legs.
19. **FR-19**: Wire enrichment assessment → `EventRulesService.FireAsync("communication_assessed")` → ordered Bindings/Actions → OutputRouter `record`/`notification` → `CreateEvent`(`sprk_event`)/`CreateTask`(`task`)/`CreateNotification`(`appnotification` → Daily Briefing). Rules configured as Binding rows + `sprk_matchconditions`; reuse EventRules deterministic gates (cost cap, opt-out, explicit-command supersede, M4 confidence gate). — Acceptance: an assessed communication creates the rule-configured artifacts; gates enforced; privilege only *flagged*, never decided (ADR-015).
20. **FR-20**: New "Communication Triage" JPS Action (structured output: category, urgency, obligations[], suggested actions[], response checklist) delivered via `DeliverComposite`, following `SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md` (ADR-037 section-name-keyed streaming). — Acceptance: assessed communication emits a summary/checklist via `DeliverComposite`.

**W6 — Caller migration + retirements** (absorbed R3; detail §8.5(e))
21. **FR-21**: Migrate ad-hoc/inline send implementations to the canonical composer: SummarizeFilesDialog (inline fetch), FilePreviewDialog (LegalWorkspace), DocumentEmailWizard (attachment-ID bug), and the shared `SendEmailStep` consumed by CreateProject/Event/Todo/WorkAssignment + the `CreateMatter` LegalWorkspace fork. — Acceptance: no caller uses inline `fetch`/ad-hoc composer; `WorkAssignmentWizardDialog.tsx:31` cross-package import resolved.
22. **FR-22**: Retire `sprk_communication_send.js` webresource (~1,150 LOC × 2 copies) after auditing ribbon references. — Acceptance: webresource removed; ribbons re-pointed; send still works from all entry surfaces.

**W7 — Hardening** (parallel track, deadline-driven)
23. **FR-23**: Graph compliance audit — confirm the only non-draft sensitive-property write is `IsRead=true` PATCH (`IncomingCommunicationProcessor` line 687) and its `Mail-Advanced.*` exemption before **2026-12-31** (DEC-6); confirm no EWS in scripts/plugins before **2026-10-01** (none in `src/`). — Acceptance: written finding for each deadline; remediation task filed if exposed.
24. **FR-24**: Subscription resilience — add lifecycle-notification subscription + `delta`-query reconciliation backstop to `GraphSubscriptionManager` (Microsoft-required belt-and-suspenders; polling backup is partial cover today) (R-5). — Acceptance: missed-event reconciliation demonstrable; lifecycle notifications handled.
25. **FR-25**: Outlook add-in hardening — migrate off deprecated `NaaAuthService`/`DialogAuthService`/`authConfig` onto `@spaarke/auth` `OfficeNaaStrategy`; **apply the stubbed BFF Office auth filters** (`.AddOfficeAuthFilter`/`.AddJobOwnershipFilter`/`.AddEntityAccessFilter` at `OfficeEndpoints.cs` lines 172/471/490/761 — real security gap); converge on the unified JSON manifest; fix hardcoded org URL (`spaarkedev1.crm.dynamics.com`); make the save task pane a consumer of the Association Engine (show suggestions + confidence, accept/override) (requirement #13). — Acceptance: add-in save flow works end-to-end under NAA; Office endpoints protected; smoke-tested in dev (R-8).
26. **FR-26**: Index-config hardening — tokenize `AiSearch` index names + 8-entry `AllowedIndexes` in `appsettings.template.json:234-248` (mirror `#{...}#`); consolidate the `AiSearch:KnowledgeIndexName` (reads) / `Analysis:SharedIndexName` (writes) split-brain (FAILURE-MODES G-9); route all communication indexing through `SearchIndexNameResolver` (requirement #8). — Acceptance: deploy packages carry no hardcoded index names; single read/write setting.

**W8 — Documentation**
27. **FR-27**: Author `docs/architecture/communication-intelligence-architecture.md`; update `sprk_communication.md`, `email-processing-architecture.md`, `communication-service-architecture.md`; mark OOB-`email` + fragmented-send docs RETIRED; update `EMAIL-TRIAGE-MODULE-DESIGN.md` per DEC-10 (fix stale component refs, point classification ladder at R4 substrate, sequence after R4). — Acceptance: docs match code; Triage module doc references only live components.

### Non-Functional Requirements

- **NFR-01 (Microsoft compliance — MUST, deadline)**: No EWS anywhere in the shipping solution (enforced-off 2026-10-01); non-draft sensitive-property writes limited to admin-consented `Mail-Advanced.*` or confirmed-exempt (enforced 2026-12-31). Compliance is first-class, not advisory (requirement #11).
- **NFR-02 (BFF publish size — MUST, §10 governance)**: Every BFF-touching task measures compressed publish output and reports absolute + diff vs baseline (~49.63 MB incl. PDBs as of 2026-07-08). Ceiling ≤60 MB; ≥+5 MB single-task delta requires justification; ≥55 MB cumulative → architecture review. Retiring `Services/Email/` should *reduce* size — report the delta.
- **NFR-03 (Auth alignment — MUST, ADR-028)**: Server injects central `TokenCredential` + `IGraphClientFactory` (`ForApp`/`ForUserAsync`) + canonical Dataverse interfaces; **never** `new` a credential or `ConfidentialClientApplication`. Client uses `@spaarke/auth` only (`useAuth`/`authenticatedFetch`/`buildBffApiUrl`; `OfficeNaaStrategy` for add-in); no token snapshots, no `accessToken` props (requirement #5).
- **NFR-04 (Channel extensibility — MUST, seams only)**: Association Engine, enrichment service, regarding model, and review UI MUST NOT change to add a future channel. New channels = new sender/archiver/ingestor adapters + a normalizer to the envelope. Define (not implement) `ICommunicationChannelSender` + `ICommunicationArchiver`; email is the only R4 implementation (requirement #2, DEC-11).
- **NFR-05 (AI cost containment)**: Deterministic-first ladder; AI rungs 4–5 fire only on ambiguity/miss; ADR-014 cache + ADR-016 budget enforced; per-rung telemetry to measure before broadening (R-3, DEC-8).
- **NFR-06 (Best-effort, non-fatal)**: Enrichment/association failures MUST NOT fail the send or inbound-capture path (preserve current invariant).
- **NFR-07 (Auth/privilege in review queue — MUST)**: "Communications Awaiting Association" and the review surface are matter-level auth-scoped (ADR-003/008); AI flags privilege, never decides it (ADR-015) (R-6).
- **NFR-08 (Test obligations)**: PRs modifying `Sprk.Bff.Api/Services/` add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`; unconditionally-mapped endpoints have unconditional service registration (ADR-032 Null-Object for feature-gated services). Test additions follow ADR-038 KEEP-path categories.

## Technical Constraints

### Applicable ADRs

- **ADR-045** (NEW — this project): Communication ADR — client canonical send + server Association Engine + enrichment + channel seams.
- **ADR-024**: Regarding family — engine MUST keep writing ADR-024 typed regarding lookups + denormalized fields.
- **ADR-028**: Spaarke Auth v2 — central `TokenCredential` + `IGraphClientFactory`; no self-built credentials.
- **ADR-018**: Kill-switch config — auto-file per-tenant enable/disable.
- **ADR-032**: BFF Null-Object Kill-Switch — feature-gated services consumed by unconditional endpoints.
- **ADR-016 / ADR-014**: AI budget + cache — bound AI-rung cost.
- **ADR-015**: AI may flag privilege, never decide.
- **ADR-013**: AI architecture — PublicContracts facade; capability invocation.
- **ADR-037**: DeliverComposite — section-name-keyed streaming for the Triage summary/checklist.
- **ADR-003 / ADR-008**: Auth seams + endpoint filters — review-queue authorization; apply Office endpoint filters.
- **ADR-006 / ADR-026 / ADR-021 / ADR-022 / ADR-012**: Code Page + Fluent v9 + React version + shared-component-library rules for the Communication Code Page and `<EmailComposer />`.
- **ADR-029**: BFF publish hygiene — size ratchet.
- **ADR-038**: Testing strategy — KEEP-path test categories.

### MUST Rules

- ✅ MUST operate the Association Engine over the **normalized envelope**, never `Microsoft.Graph.Message`.
- ✅ MUST invoke enrichment from **both** inbound and outbound paths (direction symmetry).
- ✅ MUST retain the OOB-`email`-activity retirement — no re-introduction of SSS or `email` activity dependencies.
- ✅ MUST keep AI rungs from auto-filing (always `Suggested`/`Ambiguous`).
- ✅ MUST verify new option-set integers via Dataverse MCP before assignment.
- ✅ MUST correct the org target to `sprk_organization` (not `account`/OOB `organization`).
- ❌ MUST NOT add a new regarding mechanism — extend `RegardingResolver`/`RegardingLookupMap`/`TODO_REGARDING_CATALOG` (Component Justification §11).
- ❌ MUST NOT `new` a credential or `ConfidentialClientApplication` anywhere.
- ❌ MUST NOT build Teams/Slack/Gmail/SMS channel code (seams only).

### Existing Patterns to Follow

- Regarding: `PolymorphicResolverService.applyResolverFields`, `RegardingResolver` PCF, `TODO_REGARDING_CATALOG`, `CommunicationService.RegardingLookupMap` (lines 921-931), `IncomingAssociationResolver.RegardingFieldPriority`/`PopulateResolverFieldsAsync`.
- AI fan-out: `AppOnlyAnalysisService.AnalyzeEmailAsync`, `EventRulesService.FireAsync`, `OutputRouter`/`DispositionRoutability.cs`, `CreateNotificationNodeExecutor`/`CreateTaskNodeExecutor`, `DailyBriefingCompositeService`.
- Code Page exemplar: `src/client/code-pages/DocumentRelationshipViewer/`.
- Send-side detail: [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md) §5–§8.
- Field Mapping: `FieldMappingService.ts` (client) + `IFieldMappingDataverseService` (server).

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-018 kill-switch / R-1 (design DEC-4) | Design DEC-4 recommends "Suggest-only first; enable auto-file only after measuring" | Owner directs **auto-file ON at launch** for deterministic rungs ≥0.85 (see Owner Clarifications). This is a deliberate deviation from the design's conservative default, not from an ADR MUST — §5.4's ladder already sanctions ≥0.85 → `Resolved`. | **A (project-scoped exception)** | Auto-file is gated behind the ADR-018 per-tenant kill-switch and limited to deterministic rungs (AI rungs never auto-file); misfile is re-file (audited), never delete (R-1). Owner accepts the risk for launch velocity; kill-switch is the escape hatch. Documented here + in FR-11. |
| ADR-024 regarding | Engine must resolve eight targets incl. new `sprk_servicerequest`/`sprk_event`/`sprk_organization` | None — extension, not replacement; keeps writing ADR-024 fields | **C (comply)** | Extend the catalog/map/priority; no new mechanism. |
| ADR-013 AI / §10 BFF Hygiene | New JPS action + engine add to BFF `Services/` | Adds AI surface to the BFF | **C (comply)** | Reuses `AppOnlyAnalysisService` runtime + PublicContracts facade; retiring `Services/Email/` offsets size; Placement Justification required in project `design.md`/PR. |

> No further ADR tensions surfaced at design time. All listed ADRs otherwise apply without exception. This section may be updated if tensions emerge during implementation.

## Success Criteria

1. [ ] Inbound **and** outbound communications run through `ICommunicationEnrichmentService`; outbound now auto-associates and RAG-indexes. — Verify: send a test email → `sprk_communication` shows association status + document is indexed.
2. [ ] Association Engine resolves all eight target entities with confidence + provenance over the normalized envelope; rungs 0–2 behavior unchanged from pre-refactor. — Verify: per-rung + regression tests green.
3. [ ] Deterministic ≥0.85 matches auto-file to `Resolved`; AI-rung matches land as `Suggested`; kill-switch flips auto-file to suggest-only without redeploy. — Verify: seeded fixtures across the ladder + config toggle test.
4. [ ] OOB-`email` subsystem fully retired; no references remain; build + tests green; publish size reported (expected reduction). — Verify: grep clean + `dotnet publish` size diff.
5. [ ] Assessed communication triggers rule-configured Event/Task/Notification + Triage summary/checklist via EventRules + DeliverComposite; gates enforced. — Verify: end-to-end rule fire in dev.
6. [ ] Channel-aware Communication Code Page replaces the OOB form and hosts the `RegardingResolver` review path; reviewer confirms/overrides a suggestion in ≤1 click. — Verify: manual review-loop walkthrough.
7. [ ] Canonical `<EmailComposer />` in use by all migrated callers; `sprk_communication_send.js` retired; `attachmentDriveItemIds` bug fixed. — Verify: caller audit + send from each entry surface.
8. [ ] Outlook add-in save flow works under `@spaarke/auth`/NAA with Office endpoint filters applied; suggestions shown in save pane. — Verify: dev smoke test.
9. [ ] W7 compliance findings written for both Graph deadlines; subscription `delta`/lifecycle backstop demonstrable; index names tokenized. — Verify: findings docs + reconciliation test + template diff.
10. [ ] Docs updated; Triage module doc points at R4 substrate and references only live components. — Verify: doc-drift check.

## Dependencies

### Prerequisites

- **R2 server foundation** — live (Graph subscriptions, OBO send, `.eml` archival, `IncomingAssociationResolver`). ✅
- **Spaarke Auth v2 primitives** — `@spaarke/auth` `OfficeNaaStrategy` + BFF filter classes exist on `master`; R4 owns the *last-mile* add-in/endpoint wiring in W7 (no incomplete external project blocks it — see Owner Clarifications). ✅ (foundation)
- **`sprk_servicerequest`** — exists in Dataverse (zero repo footprint); R4 authors its schema doc + regarding wiring in W0.
- **Dataverse MCP access** — required in W0 to verify new option-set integers (FR-02).

### External Dependencies

- Microsoft Graph beta endpoint (app-only) + v1.0 (OBO) — unchanged.
- Azure OpenAI (primary AI rung) + Azure AI Content Understanding (attachment parsing) — in-stack.
- Graph deadlines: EWS enforced-off **2026-10-01**; `Mail-Advanced.*` enforced **2026-12-31**.

## Owner Clarifications

*Captured during design-to-spec interview (2026-07-14):*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Communication ADR number | Design says supersede "ADR-033", but ADR-033 is taken. Which number? | **ADR-045** (next free) | FR-05 authors ADR-045; design's "ADR-033" reference treated as stale R3 carryover. |
| Add-in auth scope | Does R4 own the add-in NAA/auth-filter wiring, or depend on external Auth-v2 tasks 081/082? | **"Auth-v2 should already be complete — is it not?"** → Verified: foundation (`@spaarke/auth` `OfficeNaaStrategy` + BFF filter *classes*) IS complete on `master`; add-in still on deprecated services + Office endpoints still have filters stubbed (`// TODO: Task 033`). **R4 owns the last-mile wiring in W7.** | FR-25 is in-scope R4 work with **no external blocker**; risk R-8 downgraded (target primitives ready). |
| Auto-file default | Ship suggest-only, or enable auto-file for high-confidence deterministic matches at launch? | **Enable auto-file for deterministic ≥0.85 at launch** (overrides design DEC-4's conservative "suggest-only first") | FR-11 ships auto-file ON for deterministic rungs 0–3 ≥0.85, gated by ADR-018 kill-switch; AI rungs still never auto-file; recorded as ADR Tension Path A + adjusts R-1 mitigation. |

## Assumptions

- **ADR numbering**: The new Communication ADR is **ADR-045** (038–044 occupied). Confirmed by owner.
- **Option-set integers**: `Suggested`/`Ambiguous` provisionally ~100000002/3 — **subject to Dataverse MCP verification in W0** (FR-02); actual integers may differ.
- **Add-in dependency**: W7 add-in wiring proceeds against the *already-shipped* `@spaarke/auth`/filter primitives; if a gap in those primitives is found, it escalates (does not silently expand R4 scope).
- **Auto-file scope**: Auto-file applies to deterministic rungs 0–3 only; rung 4 (semantic) and rung 5 (AI) always land as `Suggested`/`Ambiguous` regardless of score, per FR-11.
- **`Mail-Advanced.*` exemption**: `IsRead=true` PATCH is *likely* exempt; treated as an audit item (FR-23/DEC-6), not a blocker, unless the W7 audit finds otherwise.
- **Triage Workbench + other channels**: Remain downstream/backlog; R4 ships substrate + seams only.

## Unresolved Questions

*To be resolved during implementation (none blocking `/project-pipeline`):*

- [ ] **DEC-6 (W7)**: Confirm `IsRead=true` PATCH is `Mail-Advanced.*`-exempt; if not, file remediation before 2026-12-31. — Blocks: W7 compliance sign-off only.
- [ ] **DEC-8 (W3)**: Real-volume measurement target that would gate broadening AI-rung usage / adjusting the auto-file threshold post-launch. — Blocks: post-launch tuning, not the build.
- [ ] **EWS in scripts/plugins**: Audit confirmed none in `src/`; confirm none in scripts/plugins. — Blocks: W7 EWS sign-off only.
- [ ] **`sprk_communication_send.js` ribbon references**: Full ribbon-reference audit before webresource retirement (FR-22). — Blocks: W6 retirement step only.

---

*AI-optimized specification. Original design: `projects/email-communication-solution-r4/design.md` (rev 2). Absorbs R3 send-side design ([`reference/r3-send-side-design.md`](reference/r3-send-side-design.md)).*
