# Email Communication Intelligence R1 — AI Implementation Specification (Phase 1)

> **Status**: Ready for Implementation
> **Created**: 2026-07-28
> **Source**: `design.md` rev-3 (§0 authoritative; §0.7–§0.11 supersede mechanism/scope claims in §1–§13)
> **Scope**: **Phase 1 only** — the backend intelligence + record-write spine. Surfaces are owned by the completed `email-communication-solution-r5` (coordination: `notes/email-intelligence-r1-coordination.md`).

## Executive Summary

r1 is the intelligence and record-currency layer over Spaarke's shipped communication engine (r4). It **activates the already-produced-but-dark AI classification** (category / urgency / obligations), computes a real **RI-confidence** score (closing a hardcoded-0 gap that leaves the notification path inert), extends deterministic **email-to-record association** to all 7 core record types, and makes matched records **current from email** (human-confirmed, cited, audited field updates — Job B). All work is code-directed (Action + Binding, ADR-039) on shipped infrastructure; the node-graph playbook engine is frozen and not used. Review/reading surfaces are r5's (complete); r1 feeds them via a shared contract.

## Scope

### In Scope (Phase 1)
- **Deterministic identifier association** across 7 core records (matter, project, invoice, work assignment, budget, service request, report card) — catalog-driven, value-based, reinforcement-gated (§0.8).
- **Auto-file policy narrowing (C-1):** auto-file only on rung 0 (explicit ID) + rung 1 (thread inheritance); rung 2/3 → `Suggested`.
- **RI-confidence scorer** — email-specific (triage urgency + deterministic-rung agreement); unblocks the shipped Task/notification path.
- **`TRIAGE-EMAIL` Action + Binding** — categorize / summarize / extract-obligations / priority, RAG-grounded in the matter's correspondence.
- **Triage fields** on `sprk_communication` + **`sprk_emailreviewlog`** audit entity.
- **Job B (FULL)** — propose → confirm → apply (`IActionSeam.UpdateRecordAsync`, OBO) → audit; allow-listed fields; cited; confirm/apply rendered on r5 surfaces via a stored-proposal + apply endpoint.
- **Job C** — email-triggered tasks/events via the shipped create-task pattern (cited).
- **Regarding-vs-related intent** (§0.9a) + **attachment-grounded action extraction** (§0.9b).
- **Shared + M365 group mailbox** capture coverage (D-07).
- **Review-outcome** vocabulary (distinct from ADR-040 "disposition").

### Out of Scope
- **IP Auto-Docketing** — removed entirely (D-12/D-13); no docketing entity, deadline-cascade engine, or IP playbook.
- **Reading/review UI** — owned by r5 (complete); r1 feeds it, does not build it.
- **SprkChat conversational review over mail** (D-11c) — Phase 2.
- **Daily Briefing triage channel** — Phase 2.
- **The node-graph playbook engine** — frozen; not used.
- **New `sprk_triageitem` entity** — triage state hangs off `sprk_communication` (D-01).

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/Rungs/` — new identifier rung
- `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/RegardingFieldMap.cs` — report-card entry
- `src/server/api/Sprk.Bff.Api/Services/Communication/` — RI-confidence scorer; `CommunicationAssessedSignal` wiring; auto-file policy (`AssociationStatusMapper` / `AutoFileGate`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/` — consume `IActionSeam` (Job B apply); no new AI internals
- `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` — apply endpoint + queue-feed (extends existing suggest-associations)
- Dataverse **catalog data**: `sprk_analysisaction` (TRIAGE-EMAIL) + `sprk_playbookconsumer` (Binding) rows; `infra/dataverse/inputschemas/`; golden-utterance eval case
- Dataverse **schema**: triage fields on `sprk_communication`; `sprk_emailreviewlog`; `sprk_regardingreportcard` (added by operator); Job B allow-list config
- Capture layer (communication-service domain): shared/group mailbox subscription coverage

## Requirements

### Functional Requirements

1. **FR-01 (7-entity identifier rung)**: Extend the deterministic association ladder with a rung that, for each of the 7 core records, extracts identifier-shaped tokens from subject/body and **exact-matches values** against the entity's number field (roster + fields read from `sprk_recordtype_ref`). — Acceptance: an email quoting `PRJT.10001.01` / `CMRCL-441482` / `INV-002` associates to the correct record; numbering schemes are not hardcoded; onboarding a tenant requires only `sprk_recordtype_ref` config.
2. **FR-02 (Report-card enablement)**: Add `sprk_reportcard`→`sprk_regardingreportcard` to `RegardingFieldMap.cs` (regarding lookup + `sprk_recordtype_ref` row already added by operator). — Acceptance: engine can write a report-card association.
3. **FR-03 (Auto-file policy C-1)**: Auto-file only on rung 0 + rung 1; rung 2 (participant) and rung 3 (structural) matches resolve to `Suggested`. — Acceptance: a sender-only or structural-only match never auto-files; a thread reply or explicit-ID match does; bare-numeric identifier alone never auto-files.
4. **FR-04 (RI-confidence scorer)**: Compute an email-specific RI-confidence from triage urgency + deterministic-rung agreement and pass it into `CommunicationAssessedSignal` (replacing the hardcoded 0). — Acceptance: a high-urgency, well-associated email produces a score ≥ the rule-gate threshold and fires the shipped `CommunicationRiActionService` (Task + appnotification + ping); noise does not.
5. **FR-05 (TRIAGE-EMAIL Action + Binding)**: Author a `prompted` Action + Binding producing `{category, summary(2-line), obligations[], priority, reviewOutcome}` by reusing the existing classification signal; triggered on the enrichment/event path via the `PublicContracts` facade (ADR-013). — Acceptance: an inbound email yields the structured triage output on `sprk_communication` with no second full LLM pass where the classification rung already ran.
6. **FR-06 (RAG grounding)**: The triage Action grounds classification/summary in the matter's own prior correspondence (already indexed). — Acceptance: classification improves with matter context vs. a context-free pass (eval-case demonstrated).
7. **FR-07 (Triage fields)**: Add triage fields to `sprk_communication`: category, priority, summary, obligations (**lean JSON**), RI-confidence, review-outcome. — Acceptance: fields populate from FR-05; JSON shape documented for future promotion to child records.
8. **FR-08 (Review audit)**: New append-only `sprk_emailreviewlog`: item, actor (user OR rule/model id), action, prior AI suggestion + confidence, timestamp — **machine-review and human-review both as rows**. — Acceptance: every AI proposal and every human decision is queryable per matter.
9. **FR-09 (Job B — propose)**: The triage Action proposes **allow-listed** field updates on the associated record, each with **old→new value, cited source text (email or attachment + locator), and confidence**; stored as pending proposals. — Acceptance: "closing moved to Aug 15" yields a proposed `sprk_closingdate` update, cited, on the matched matter; a non-allow-listed field is never proposed.
10. **FR-10 (Job B — apply)**: An apply endpoint applies a confirmed proposal via `IActionSeam.UpdateRecordAsync` under the confirming user's OBO, and writes the audit row; rendered on r5's shipped surface. — Acceptance: on confirm, the record updates under OBO and an `sprk_emailreviewlog` row records "AI proposed / human approved / from email X / confidence".
11. **FR-11 (Job B allow-list config — `sprk_emailupdatefield`)**: Per-`(entity, field)` allow-list in the new `sprk_emailupdatefield` table (`sprk_targetentity` → `sprk_recordtype_ref`, `sprk_targetfield`, `sprk_enabled`, `sprk_fieldtype` coercion hint, `sprk_requireconfirm`=true in P1, `sprk_extractionguidance`). Job B may propose an update ONLY to an enabled row here. — Acceptance: the allow-list governs what FR-09 may propose; a non-listed/disabled field is never proposed; tenant-configurable + per-field kill-switch. *(Table created by operator; r1 reads it.)*
12. **FR-12 (Regarding-vs-related intent)**: Classify intent `file-to-existing | update-existing | new-record-related-to`; phrasing like "new filing based on X" **demotes** the identifier from *regarding* to *related* and suppresses auto-file; on `new-record-related-to`, propose creating a record (gated `dataverse.create_record`) linking the referenced record as related. — Acceptance: "new filing based on PAT-908068" does NOT auto-file onto PAT-908068; it offers create-new / file-onto / link-related.
13. **FR-13 (Attachment-grounded action extraction)**: The triage/action Action grounds on extracted attachment text (existing text-extraction → SPE → child-`sprk_document`), deterministically gated to likely action-triggers. — Acceptance: an action stated only in an attachment PDF is extracted and cited to the attachment + locator.
14. **FR-14 (Job C — email-triggered work)**: Email content triggers tasks/events via the shipped create-task pattern (`CREATE-TASK@v1` → gated `dataverse.create_record` → `sprk_event`), cited to the source communication. — Acceptance: an email implying a task creates one on the right record, cited; deadline-bearing entries require confirm.
15. **FR-15 (Shared/group mailbox coverage)**: Extend capture (Graph subscriptions + Exchange `ApplicationAccessPolicy`) to cover shared mailboxes and M365 group mailboxes. **r1 owns this** (r4 capture is archived, no active owner). **Spike-first**: task 1 confirms the subscription/permission model for shared vs group mailboxes + the `GraphSubscriptionManager` / permission-grant delta; implementation is gated on that finding. Runs **parallel to the intelligence spine** (independent). — Acceptance: an email to a shared/group mailbox is captured, associated, and triaged like a user mailbox.
16. **FR-16 (Category taxonomy + priority weights)**: Category taxonomy and priority weights are Dataverse-configurable with a seeded starter set (D-03). — Acceptance: an admin can add/reweight categories without code.
17. **FR-17 (Surface-agnostic feed — NOT the UI)**: r1 exposes surface-agnostic contracts (triage data, ranked association suggestions, proposals, and a **queue-feed** endpoint returning the ranked exceptions) that **r5 renders** in both surfaces (Code Page + SpaarkeAi widget). **C-3 resolved: r5 builds the Exceptions Queue *surface*; r1 supplies the *feed* only.** — Acceptance: the r1 feed contract is consumable by both r5 surfaces without a fork; r1 builds no UI.

### Non-Functional Requirements
- **NFR-01 (Publish size)**: BFF publish ≤ 60 MB compressed; report delta per BFF-touching task. Triage adds no new heavy dependency (reuses the AI stack).
- **NFR-02 (CVE)**: No new HIGH-severity CVE (`dotnet list package --vulnerable`).
- **NFR-03 (AI facade discipline, ADR-013)**: No `IOpenAiClient`/`IPlaybookService` injected into Communication code — reach AI only via `Services/Ai/PublicContracts/`.
- **NFR-04 (Best-effort/non-fatal)**: Triage / RI-confidence / proposal generation MUST NOT fail the capture or send path (ADR-045 NFR-06).
- **NFR-05 (Auth)**: Background triage runs app-only; Job B apply runs under the confirming user's OBO (ADR-028).
- **NFR-06 (Trust & audit, D-5.5)**: Every AI proposal carries a plain-language reason + citation to exact source text, and the system verifies the cited text exists; nothing deadline-bearing / privilege-adjacent / record-mutating auto-finalizes; privilege is flagged, never decided (ADR-015).
- **NFR-07 (Eval gate, ADR-039)**: Every new Action/Binding adds a golden-utterance eval case (blocking merge gate).
- **NFR-08 (Rung cost)**: The identifier reverse-lookup is gated to not-yet-resolved emails and cached; report per-message query count.

## Technical Constraints

### Applicable ADRs
- **ADR-045** — Communication architecture (engine over normalized envelope; direction-symmetric enrichment; channel seams). Extend, never fork.
- **ADR-024** — Polymorphic regarding family; write only via the resolver; one regarding mechanism.
- **ADR-039** — Grounded execution / closed catalogs / Action + Binding. New capability = catalog data + `coded`/`prompted`, never the frozen node engine.
- **ADR-013** — BFF AI facade; `PublicContracts` seams only.
- **ADR-040** — Session ledger; store-before-render; owns the word "disposition".
- **ADR-041 / ADR-043 / ADR-047** — judgment gate / execution spine / notification spine — **Proposed, in-flight**; pin to current shape and check charters.
- **ADR-015** — privilege flagged, never decided.
- **ADR-018 / ADR-014 / ADR-016** — kill-switch; AI caching; per-tenant budgets.
- **ADR-028** — auth (app-only background; OBO writes).
- **ADR-004 / ADR-036** — job contract (if any async work).

### MUST Rules
- ✅ MUST extend the Association Engine (add one rung); MUST NOT add a second engine or regarding mechanism.
- ✅ MUST build intelligence + writes on Action + Binding via `PublicContracts`; MUST NOT land capability on the frozen node-graph engine or `IInvokePlaybookAi` (deleted).
- ✅ MUST auto-file only on rung 0 + rung 1 (C-1); rung 2/3 → `Suggested`.
- ✅ MUST human-confirm all record-mutating Job B writes; cite source; audit; allow-list fields.
- ✅ MUST keep numbering schemes out of code (value-based match; roster from `sprk_recordtype_ref`).
- ❌ MUST NOT auto-finalize anything deadline-bearing/privilege-adjacent; MUST NOT silently drop unmatched email (explicit queue/status).
- ❌ MUST NOT re-use "disposition" for the review outcome (use "review outcome").

### Existing Patterns to Follow
- Association rung: `Services/Communication/Engine/Rungs/ThreadContinuityRung.cs` / `ExplicitReferenceRung.cs`.
- Record write: `IActionSeam.UpdateRecordAsync` → `UpdateRecordActionCore` (`Services/Ai/Nodes/ActionCore/`).
- Task creation: the shipped `create-task` capability (`CREATE-TASK@v1`).
- Consumer wiring: `docs/guides/ai-guide-consumer-wiring.md`.
- Notification: `CommunicationRiActionService` + `ICommunicationAssessedProducer`.

## Placement & New Components (CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>            <!-- Services/Communication rung + scorer; Services/Ai via PublicContracts; new endpoints -->
  <spaarkeai>N</spaarkeai> <!-- surfaces are r5's; r1 feeds them, builds no widget -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
Placement: all server work lands in `Sprk.Bff.Api` inside the existing Communication + Ai domains (no new top-level service). AI reached via `PublicContracts` facade. Publish-size + CVE verified per BFF-touching task; ceiling ≤ 60 MB (baseline ~49.63 MB incl. PDBs).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| 7-entity identifier rung | `ExplicitReferenceRung` (matter-only) | Extends the ladder (new rung class mirroring rung 0) | Without it, only matter numbers deterministically associate — project/invoice/etc. never auto-match; G-1 fails |
| `sprk_emailreviewlog` entity | `AuditEnrichmentMiddleware` (request logs) | No — request logs can't prove per-email human+machine review | Without it there is no defensible per-email review record — the compliance differentiator (G-5) fails |
| RI-confidence scorer | `CommunicationAssessedSignal.Confidence` (hardcoded 0) | Replaces the hardcoded value with a computed one | Without it the notification path denies under any threshold and creates nothing (Pillar 1 stays dark) |
| Triage fields on `sprk_communication` | `sprk_associationstatus` | **Yes — extend the entity** | N/A (extension) |
| `TRIAGE-EMAIL` Action + Binding | none (catalog data) | No existing triage capability | Without it there is no category/summary/obligations/priority — Pillar 1 fails |
| `sprk_emailupdatefield` (Job B allow-list) | Field Mapping Framework (creation-time value copy) | Evaluated; poor fit (different intent) → **new config table** | Without it Job B can propose arbitrary column writes — unsafe |
| Apply + queue-feed endpoints | `suggest-associations` (read-only) | Extends the endpoint family; thin minimal-APIs | Without them r5 has no way to apply a confirmed proposal or read the exceptions queue |

## ADR Tensions (CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-040 | "disposition" is the AI-output-delivery term | Triage needs a term for the human review outcome | **A (exception)** | Use "review outcome" for the triage decision; leave "disposition" to ADR-040. Naming disambiguation, no rule violation |
| ADR-024 | regarding family expresses "regarding" only | The "new-filing-based-on-X" case needs a distinct "related-to" relationship | **A (exception)** | Represent "related-to" distinctly for intent FR-12 without a second regarding mechanism; document the relationship model |
| ADR-045 | ships auto-file for deterministic ≥ 0.85 (rungs 0–3) | C-1 narrows auto-file to rungs 0+1 | **A (exception)** | Conservative per research (misfiling = #1 trust-killer); a tightening of the shipped policy, not a violation; kill-switch-governed |
| ADR-041/043/047 | Proposed, in-flight | r1 builds against the gate/spine seams | coordinate | Pin to current shape; `/conflict-check` before BFF PRs; check charters |

## Success Criteria (operator-executed browser UAT on dev)

1. [ ] An email quoting a project/invoice/work-assignment/budget/service-request/report-card number associates to the right record — Verify: browser, cross-tenant numbering.
2. [ ] A thread reply inherits the parent's association (rung 1); a sender-only match lands `Suggested`, not auto-filed (C-1) — Verify: browser.
3. [ ] "New filing based on X" does NOT auto-file onto X; it offers create-new / file-onto / link-related — Verify: browser.
4. [ ] Opening an email shows category, 2-line summary, obligations, priority — Verify: r5 surface.
5. [ ] A high-signal email produces a Task + appnotification + real-time ping (RI-confidence fix) — Verify: browser, no new UI needed.
6. [ ] A fact-bearing email proposes an allow-listed record update, cited; on confirm the record updates and an audit row is written — Verify: r5 surface + record + `sprk_emailreviewlog`.
7. [ ] An action stated only in an attachment is extracted and cited to the attachment — Verify: browser.
8. [ ] A shared/group-mailbox email is captured, associated, and triaged — Verify: browser.
9. [ ] Every AI proposal + human decision is queryable per matter (`sprk_emailreviewlog`) — Verify: query.

## Dependencies

### Prerequisites
- Shipped: communication engine (r4), association ladder, notification spine, `IActionSeam` write cores, `sprk_recordtype_ref` catalog (codes + number fields populated by operator), r5 surfaces.
- Operator-completed: `sprk_regardingreportcard` lookup + `sprk_recordtype_ref` RPTC row.
- `sprk_recordtype_ref` data-hygiene fix (typos in some `sprk_regardingfield` values; `contact`-row anomaly) — read defensively or clean first.

### External / Coordination
- **r5** — **builds** the review surfaces (association-suggestion cards, proposed-update/action cards, the **Exceptions Queue surface**) consuming r1's feed; thread grouping key aligned with engine inheritance (coordination doc §2/§3). r1 supplies feed + apply endpoints, not UI (C-3).
- **`Services/Ai` seams** — consume `PublicContracts`; ADR-041/043/047 in-flight; `/conflict-check` before BFF PRs.
- **Operator** — creates the `sprk_emailupdatefield` table (FR-11 schema) + seeds a starter allow-list.

## Owner Clarifications (2026-07-28)

| Topic | Decision | Impact |
|---|---|---|
| Triage unit (D-01) | `sprk_communication` | No new entity |
| Legacy Stack B (D-02/D-04) | Moot — retired by r4 | Build only on `sprk_communication`/`.eml` |
| Taxonomy/priority (D-03) | Dataverse-tuneable + starter set | FR-16 |
| Review-outcome term (D-05) | "review outcome" | Avoids ADR-040 clash |
| Obligation storage (D-06) | Lean JSON | FR-07 |
| Mailbox coverage (D-07) | Shared + group | FR-15 |
| Priority scorer (D-08) | Email-specific, not portfolio | FR-04 |
| Surface placement (D-10) | Dual-use (both) | FR-17 |
| AI in P1 (D-11) | Triage + RAG in; chat P2 | FR-05/06 |
| IP docketing (D-12/13) | Removed | Out of scope |
| Auto-file (C-1) | rung 0 + rung 1 only | FR-03 |
| Job B depth | FULL (not propose-only) | FR-09/10 |
| **Allow-list home (C-4)** | **New `sprk_emailupdatefield` table** (operator creates; schema in FR-11) | FR-11 |
| **Exceptions Queue (C-3)** | **r5 builds surface; r1 supplies feed** | FR-17 |
| **Shared/group mailbox (FR-15)** | **r1 owns; spike-first, parallel to spine** | FR-15 |

## Assumptions

- **RI-confidence formula (C-5)**: urgency × deterministic-rung-agreement blend; exact weights tuned during implementation with an eval set.
- **Review-outcome value set**: File / Update / Route / Dismiss (+ Suggested/Ambiguous statuses from the engine) — confirm the closed set at task time.

## Unresolved Questions

*All prior blocking items (C-3, C-4, FR-15) resolved 2026-07-28 — see Owner Clarifications. Remaining items are tuning-during-implementation, not blockers:*
- [ ] RI-confidence exact weights (C-5) — tuned with an eval set at implementation, not a blocker.
- [ ] Review-outcome closed value set — confirmed at task time.

---
*AI-optimized specification. Original design: `design.md` (rev-3).*
