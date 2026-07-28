# Email Workspace (Outlook-style) — Design

> **Program**: standalone (Communication surface line — successor to email-communication-solution-r4)
> **Project**: `email-communication-solution-r5`  ·  **Round**: r5
> **Date**: 2026-07-27  ·  **Owner**: ralph.schroeder
> **Driver**: Use case (vertical). Value is defined by the user-facing Email surface, not by a horizontal AI capability. Primarily a **UI/surface** project that assembles the existing communication stack into an Outlook-style master/detail experience; it pulls in **one new BFF endpoint** (`.eml` render) and **one config change** (archiving default-on) to satisfy the "show the email as sent" requirement.

---

## Lens 1 — Use Case Definition

- **Jobs to be done**: triage / read / respond to / compose email that is associated with legal records (matters, projects, organizations, etc.). This is the *mail metaphor* — a dedicated, standalone Email destination — not the existing chat/thread ("communications-list") metaphor.
- **Personas**: legal professionals (associate, paralegal, partner, ops) whose anchoring activity is working a matter and who expect a familiar Outlook-like inbox to read and act on `sprk_communication` email records without leaving Spaarke.
- **Triggers**: user opens the **Email** workspace widget in SpaarkeAi, OR navigates to the standalone **Email** code page from Spaarke navigation. Selecting a card opens that email on the right.
- **Inputs → Outputs**: `sprk_communication` rows (filtered to `sprk_communicationtype = 100000000 Email`) → an Outlook-style two-pane surface where the user reads a selected email **as it was sent** (full quoted history + inline images) and can reply / reply-all / forward / compose new.
- **Concrete sub-tasks** (the closed list):
  1. Browse emails as **cards in a flat left-pane list**, choosing among **views via a dropdown** (Dataverse saved views on `sprk_communication`); optional "View by List / View by Thread" toggle.
  2. Select a card → **read the full email on the right** (reading pane): full-width toolbar, from/to/cc/bcc, subject, sent/received dates, the **email body as sent** (the embedded reply/forward history + inline images — rendered from the archived `.eml`, see Lens 2), attachments, related-record associations, tracking flags.
  3. **Reply / Reply All / Forward** from the open email → opens the canonical email composer modal, recipients pre-filled.
  4. **Compose new email** → opens the canonical compose modal (the single compose used across *all* new-email surfaces).
  5. Review/confirm the email's **record associations** (Association Engine "regarding" review) inline in the reading pane — **fully interactive** (confirm / change / dismiss / link-another), reusing the existing additive write path. Reply-chain auto-association is already applied at ingestion.
- **What "email thread" means here (aligned 2026-07-27)**: the **quoted reply/forward history embedded in the message as the sender sent it** — a property of the single email, preserved in the archived `.eml`. This is distinct from (a) Outlook "Conversations" UI grouping (NOT used for the left list), and (b) Spaarke's `sprk_communicationthread` record-linkage (used for **association**, not display).
- **Scope boundaries / non-goals**:
  - OUT: replacing the OOB `sprk_communication` model-driven main form + its PCFs — **those are kept** for MDA contexts (opening a communication from a matter subgrid, admin).
  - OUT: chat/Teams/SMS/ACS channels — **Email-only** (the existing `communications-list` widget remains the multi-channel/thread surface).
  - OUT: **Spaarke-side thread reconstruction from linked records** — the reading pane shows the *single selected* email rendered as sent (from its `.eml`), NOT a conversation assembled from multiple `sprk_communication` rows.
  - OUT: **thread-entity association inheritance enhancement** — closing the server gap where *every* `sprk_communicationthread` member inherits the thread's current regarding (see Lens 4) is deferred to a separate association-engine round. r5 reuses the reply-chain inheritance that already works.
  - OUT: `EmailProcessingMonitor` admin diagnostics; new send/archive/attachment BFF endpoints (reuse r4/messaging-app); net-new AI Actions/Bindings.
- **Done-criteria**: a user can open the Email surface (widget AND code page), pick a saved view, see email cards, click one, read the full email **as sent** on the right (with quoted history + inline images), act on its associations, and reply/forward/compose — with the OOB main form still working unchanged.
- **Business value**: a familiar, matter-aware email surface inside Spaarke removes the context-switch to Outlook for reading/triaging matter email; consolidates the read+act loop next to the record; and — by extracting the form controls' **React-agnostic logic** into shared cores and adding a reusable `.eml` render capability — makes email UI reusable across future surfaces without duplicating business logic.

## Lens 2 — Surface / UX

- **Target surfaces** (dual-use **Pattern D**, per `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`):
  1. **SpaarkeAi workspace widget** — new widget type `email` (sibling of `communications-list`), registered in `Spaarke.AI.Widgets/.../register-workspace-widgets.ts` + a LegalWorkspace section shim.
  2. **Standalone Email code page** — its own `main.tsx` + auth bootstrap (the `DailyBriefing` / `EventsPage` mount pattern), reachable from Spaarke navigation.
  Both mount the **same** React 19 shared-lib component (single source of truth for the code-page surface).
- **Interaction walk-through** (concrete, end-to-end):
  ```
  User opens Email (widget or code page)
    → left pane: ViewSelector dropdown (reused) defaulted to "Email — Inbox" saved view
    → left pane renders sprk_communication Email rows as FLAT CARDS
      (from/subject/preview/date/unread) — one card per email; no conversation grouping
      (optional "View by List / View by Thread" toggle groups by sprk_communicationthread)
  User clicks a card
    → right reading pane loads that sprk_communication by id (Xrm.WebApi) for metadata
    → BODY: calls GET /api/documents/{emlDocId}/eml-render → server returns sanitized
      full HTML (quoted history + inline cid: images inlined as data: URIs) → rendered
      in a SANDBOXED IFRAME. This is the email "as sent."
      Degradation: if no .eml archive exists, render sprk_body (clean latest message,
      client-sanitized) with a "full history unavailable" note.
    → also renders: full-width toolbar (Reply / Reply All / Forward / New / Archive / Create…)
      + CommunicationHeader (from/to/cc/bcc, subject, dates)
      + AttachmentList (inline images filtered; click → RichFilePreviewDialog)
      + ConnectionsEditor (associations — interactive; reply-chain auto-assoc already applied)
      + TrackingFieldTrio (monitor / priority / access flags)
      + "Open full form" → 85% navigateTo modal (escalation)
  User clicks Reply / Reply All / Forward (toolbar)
    → SendEmailDialog opens the canonical EmailComposer in reply|forward mode,
      recipients pre-filled → Send → existing BFF send path → reading pane refreshes
  User clicks New Email → SendEmailDialog opens the canonical EmailComposer in compose mode
  User changes the view dropdown → new saved-view FetchXML runs → card list re-populates
  ```
- **"Reading pane" definition (aligned 2026-07-27)**: the right pane shows the **single selected email, rendered as the sender sent it** — the embedded reply/forward history and inline images, sourced from the archived `.eml` (which preserves the full `body.content`; the queryable `sprk_body` field stores only Graph's stripped `uniqueBody`). Not a Spaarke-reconstructed conversation.
- **Reused vs. new UI**:
  - **Reused as-is**: `ViewSelector`, `PanelSplitter`, canonical `EmailComposer` + `SendEmailDialog`/`SendEmailPage`, `RichTextEditor`, `RichFilePreviewDialog`, `IDataverseClient.retrieveSavedQueriesForEntity`, `CommunicationHeader`, the DOMPurify dependency, MimeKit (server).
  - **Shared LOGIC extracted (React-agnostic — Layer 1)**: `provenance.ts` + connections write-handler, attachment data/service adapters, action-bar/prefill/suggested-create logic, tracking-trio value logic. Consumed by **both** the OOB-form PCFs and the new code page. See **Reuse architecture** below.
  - **Net-new (§11)**: `EmailCardList` (flat card list), the **email reading-pane shell** (two-pane layout + full-width toolbar + React 19 Layer-2 views), the **`.eml` render pipeline** (new BFF endpoint + client sandboxed-iframe render branch + `cid:` resolution), the **shared hardened `sanitizeEmailHtml` client util**, and the two thin **mount registrations**. The **OOB-form PCFs keep their existing React 16/17 view code** unchanged.
- **Required states**:
  - **loading**: skeleton cards (left); reading pane paints header/metadata instantly from the record, then swaps in the `.eml` body when the render call returns.
  - **empty**: "No emails in this view" (left); "Select an email" placeholder (right).
  - **error**: view-load failure banner (left); `.eml` render failure → fall back to `sprk_body` + note; record-load failure → error state with retry.
  - **uncertainty**: `ConnectionsEditor` renders low-confidence association matches as **"Needs your decision" / "Suggested"** groups (Association Engine provenance).
- **Citations/provenance UX**: N/A for email reading; the association-provenance review (why a record was auto-linked vs suggested) is handled by the reused `ConnectionsEditor`.

### Reuse architecture — two-layer split across the PCF ↔ code-page boundary (resolved 2026-07-27)

**Constraint (verified 2026-07-27 vs MS Learn):** PCF *virtual* controls execute under the platform's React (manifest `16.14.0`; model-driven **runtime 17.0.2**); **no React 18/19 platform library, no MS roadmap**; a virtual control cannot bundle its own React. Code pages bundle **React 19**. **Fluent is uniformly v9 everywhere** — not a conflict; only the React runtime/types version conflicts.

**Decision:** do **not** share React *components* across the PCF boundary — share React-agnostic *logic* only.
- **Layer 1 — React-agnostic logic** (`provenance.ts`, reducers, `*Service`/API adapters, types, write handlers): **shared by both** the OOB-form PCF and the code page. Pure TS → zero React-version conflict, no cast.
- **Layer 2 — React views**: authored in **React 19** for the code page/widget; the **OOB-form PCFs keep their React 16/17 views** untouched.

This is ADR-022-compliant ("slim-first": share utils/logic, not components) and already the pattern `CommunicationConnections` + `CommunicationAttachments` follow. React 19 authoring buys a virtual PCF nothing (it still runs under platform React 17), so sharing logic — not components — is the only zero-friction reuse.

## Lens 3 — AI Capabilities Required

> Surface project; **no new AI Actions/Bindings/Tools**. It *surfaces* the existing Association Engine (consumed through reused logic) and reuses existing send/AI paths.

| Capability need | Primitive type | Description |
|---|---|---|
| Show/confirm record associations for an email ("regarding" review incl. AI-suggested matches) | Existing Association Engine + provenance (surfaced via `ConnectionsEditor` logic) | Read `sprk_associationprovenance` / `sprk_associationstatus`; render grouped review; **write** chosen regarding via the existing additive path. No new Action. |
| Reply-chain auto-association (reply inherits parent message's regarding) | Existing Association Engine `ThreadContinuityRung` (server, at ingestion) | Already applied on inbound; the reading pane just displays the result. |
| Suggested creates (Create Event / To Do / Link Invoice from an email) | Existing provenance→suggested-creates parsing (action-bar logic) | Carried into shared Layer-1 logic. |

*(No new prompted analysis, RAG, memory, grounding, or redline. "Summarize this email" / "suggest a reply" would be a separate design.)*

## Lens 4 — Have vs. Gap

> Verdicts verified against live code via seven Explore/researcher audits (2026-07-27): DataGrid framework, communication components, dual-use widget pattern, 5-PCF migration, PCF/code-page React+Fluent versions, email-ingestion body storage, HTML rendering/sanitization, `.eml` renderer feasibility, association-engine inheritance. Precedence **REUSE > ACTIVATE > COMPLETE > BUILD**.

| Capability | Verdict | Evidence (file) | Note / what's needed |
|---|---|---|---|
| Dual-use widget + code-page mount (Pattern D) | REUSE | `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`, `CalendarWorkspaceWidget.tsx` | Apply the pattern. |
| View-selector dropdown + saved-view resolution | REUSE | `DataGrid/ViewSelector.tsx`; `IDataverseClient.retrieveSavedQueriesForEntity` | Standalone; usable outside DataGrid. |
| Resizable two-pane split | REUSE | `PanelSplitter/PanelSplitter.tsx` | Master/detail skeleton. |
| Canonical compose / reply / reply-all / forward / new | REUSE | `EmailComposer/` + `wrappers/SendEmailDialog.tsx`, `SendEmailPage.tsx` | The one compose for ALL new-email triggers (r4-consolidated). |
| Attachment preview modal | REUSE | `RichFilePreviewDialog` / `RichFilePreview.tsx` | Reuse for attachments. NOTE: its generic SPE iframe does NOT render `.eml` as email — see `.eml` row. |
| Open the record (escalate to full form) | REUSE | `xrmNavigationServiceAdapter.ts` `openRecordModal` | 85% modal via `navigateTo`. |
| `sprk_communication` schema + Email envelope header | REUSE | `docs/data-model/sprk_communication.md`; `CommunicationPage/.../CommunicationHeader.tsx` | Header reused in reading pane. |
| **Reply-chain auto-association** (reply inherits parent's regarding) | REUSE | `Communication/Engine/Rungs/ThreadContinuityRung.cs` (`CopyParentRegarding`); `IncomingAssociationResolver.cs` | Runs server-side at ingestion; display-only in the pane. |
| **Explicit "link another record"** | REUSE | `pcf/CommunicationConnections/.../ConnectionsEditor.tsx` `onLinkAnother` → `ConnectionsWriteHandler.applyRegardingSelection` (additive) | Lives in ConnectionsEditor, NOT RegardingResolver. Extract Layer-1 logic. |
| Server MIME parser (for `.eml`) | REUSE (dep) | `Services/…/TextExtractorService.cs` (`using MimeKit`) | MimeKit already referenced; currently strips HTML→text for AI. New HTML-preserving path needed. |
| Client HTML-sanitize + render pattern | REUSE (dep) | `CommunicationTimeline/.../MessageRow.tsx` (DOMPurify + `dangerouslySetInnerHTML`); `dompurify ^3.4` | Pattern exists but **permissive** (no scheme/link/`cid:` hardening) — harden + share. |
| Action bar — LOGIC | COMPLETE | `pcf/CommunicationActions/.../CommunicationActionsApp.tsx` | Extract Layer-1 logic; build React 19 toolbar view; PCF keeps its view. |
| Attachment list — presentational core + LOGIC | COMPLETE | `pcf/CommunicationAttachments/.../AttachmentList.tsx` (PCF-free) + services | Promote list + data/BFF adapters (Layer 1). |
| Associations review — LOGIC + write path | COMPLETE | `ConnectionsEditor.tsx` + `provenance.ts` (Xrm-free) + `ConnectionsWriteHandler` | Extract Layer-1 logic; build React 19 review view. ⚠ `CommunicationPage`'s copy is a **stale stub** — replace. |
| Tracking flags trio | COMPLETE | `pcf/TrackingFieldTrio/.../TrackingFieldTrioApp.tsx` | Lift generic core to `@spaarke/ui-components`; pass options in. Net-new to the form. |
| **`.eml` → sanitized-HTML render** (email as sent) | BUILD | RichFilePreview iframe won't render `.eml`; `GraphMessageToEmlConverter.cs` confirms self-contained multipart/related + `ContentId` | **New BFF endpoint** `GET /api/documents/{id}/eml-render`: MimeKit parse, keep HTML, `cid:`→`data:` rewrite, **server-sanitize**, return safe HTML → client renders in sandboxed iframe. |
| **Archiving reliability** (the `.eml` must exist) | COMPLETE/CONFIG | `IncomingCommunicationProcessor.cs` `ArchiveIncomingOptIn` (opt-out); `ArchiveEmlAsync` (full body) | Flip archiving to **default-on** for monitored email accounts; else reading pane degrades to `sprk_body`. |
| Shared hardened `sanitizeEmailHtml` (client, field bodies) | BUILD | — (only permissive inline usage today) | One shared util (allow-list, no scripts/handlers, URL-scheme restriction, link hardening); retrofit `MessageRow`/`MessageBubble`. |
| Email **card list** (flat) | BUILD | — (`ThreadList` is thread-oriented) | Net-new React 19 presentation (§11). |
| Email **reading-pane shell + full-width toolbar** | BUILD | — (right pane today = `ConversationView` bubbles) | Net-new React 19 composition (§11). |
| Email **surface** (code page `main.tsx` + widget shims) | BUILD (thin) | — | New dual-use surface (§11). |
| **Thread-entity association inheritance** (member inherits thread's regarding on later join / subject-only threading / thread-regarding change) | GAP (deferred) | `ThreadContinuityRung.cs` keys on RFC-2822 ancestry, not the `sprk_communicationthread` anchor; runs only at capture | **Out of r5.** Server association-engine enhancement; log as follow-up. |
| Admin email-processing dashboard | OUT | `pcf/EmailProcessingMonitor/` | Excluded. |

**Verdict legend**: REUSE = wired · ACTIVATE = built-but-dark · COMPLETE = partial/extract-existing · BUILD = net-new · CONFIG = configuration change · GAP = absent, deferred.

## Lens 5 — Configuration

- **Actions / Bindings / Tools**: **none new.**
- **Dataverse saved views** (`savedquery` on `sprk_communication`, pre-filtered `sprk_communicationtype = Email`): **Email — Inbox / Sent / Drafts / By Matter / All Email**. Makers author as system views; FetchXML gives the filtering.
- **View dropdown source policy**: list all `sprk_communication` Email views (or `Email — *` subset). Optional lightweight allowlist later (borrow the `sprk_gridconfiguration` `availableViews` *concept*, not the full record). Left list flat by default; optional thread-grouped mode.
- **Archiving config**: set `ArchiveIncomingOptIn` **default-on** for monitored email accounts so the `.eml` exists to render.
- **New BFF endpoint**: `GET /api/documents/{id}/eml-render` (MimeKit HTML-preserving parse + `cid:`→`data:` + server sanitize). Placement justification required (§10).
- **Reading-pane "open full form" target**: OOB `sprk_communication` Email main form GUID (escalate-to-modal). Kept intact.
- **Widget registration**: `registerWorkspaceWidget('email', …, () => import('@spaarke/communication-components'))` + LegalWorkspace `email.registration.ts` shim + `system-layouts.json` seed.
- **Shared-lib layering (two-layer split)**:
  - **Layer 1 (React-agnostic logic)** → `@spaarke/communication-components` (connections `provenance` + write handler, attachment adapters, action-bar logic); `TrackingFieldTrio` logic + `sanitizeEmailHtml` → `@spaarke/ui-components` (generic).
  - **Layer 2 (React 19 views)** → `@spaarke/communication-components` (`EmailCardList`, reading-pane shell, `.eml` render branch, toolbar/attachment/connections/tracking views). PCFs keep their React 16/17 views.
- **Model tiers / License**: N/A.

## Lens 6 — Acceptance & Evaluation

- **Closed test set**:
  1. Inbound email associated to one matter (clean) → reads correctly; associations show "Filed automatically".
  2. Ambiguous associations → `ConnectionsEditor` shows "Needs your decision"; confirm → additive write persists.
  3. Attachments incl. `.eml`/inline image → `AttachmentList` filters inline images; preview opens.
  4. **Reply/forward email with quoted history (like the reference screenshot)** → reading pane renders the full body **as sent** (all prior messages + inline images) from the `.eml`.
  5. **Email with no `.eml` archive** → reading pane degrades to `sprk_body` (clean latest message) + "full history unavailable" note; no error.
  6. **Reply inherits parent's associations** → a reply to an already-filed email shows the parent's regarding auto-applied (ThreadContinuityRung).
  7. Draft email → appears in "Drafts" view; opens the canonical composer in draft mode.
  8. HTML-body + plaintext-body → both render correctly.
  9. Reply-All on a multi-recipient email → composer pre-fills To + Cc.
- **Negative / authorization cases**:
  - Non-Email `sprk_communication` (Teams/SMS) → excluded from every Email view.
  - Email with no user access (wrong tenant/role) → not returned; direct-id load fails closed with error state.
  - **Malicious inbound HTML** (`<script>`, `onerror=`, `javascript:` link, tracking pixel) in both the `.eml` (server-sanitized) and a `sprk_body` field render (client-sanitized) → no script execution; sandboxed iframe contains the `.eml`.
- **Eval harness**: N/A for LLM metrics. Acceptance is **functional/UAT**: dual-mount parity, OOB-form regression (PCFs work after Layer-1 extraction), `.eml` render fidelity + graceful degradation, sanitization (no XSS), and the cases above.
- **Success metrics**: reading pane header paints < 300 ms (from record); `.eml` body renders < 1.5 s; view switch < 1 s; **zero regression** on the OOB form; shared Layer-1 logic (no duplicated business logic); **zero XSS** on the malicious-HTML cases.

---

## Governance Seeds (for design-to-spec handoff)

### Hot-Path Declaration (per CLAUDE.md §10)
```xml
<hot-path-declaration>
  <bff>Y</bff>            <!-- NEW endpoint: GET /api/documents/{id}/eml-render (MimeKit parse + sanitize) -->
  <spaarkeai>Y</spaarkeai> <!-- new 'email' workspace widget + section shim -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
> **BFF placement justification (§10)**: the `.eml`→HTML render belongs in the BFF because MimeKit already lives server-side, the SPE download facade is server-side, and server-side sanitization is the trusted single point for untrusted email HTML. A client-side MIME parser would be net-new JS + ship raw `.eml` bytes to the browser. Publish-size impact: MimeKit is already referenced (no new package); measure + report per NFR-01. New endpoint requires tests in `tests/unit/Sprk.Bff.Api.Tests/`.

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `EmailCardList` (flat mail card column) | `ThreadList.tsx` (thread rows), DataGrid body (tabular) | No — thread-oriented / tabular-only; would need a fork to render mail cards | No Outlook card list; the surface degrades to a chat list or a grid |
| Email **reading-pane shell** (2-pane + full-width toolbar + Layer-2 views) | `ConversationView` (bubbles), `CommunicationLayout` (single-record page) | No — bubbles are chat; `CommunicationLayout` is full-page, no split/toolbar-across-width | No side-by-side read-while-browsing; loses the Outlook reading pane |
| `.eml` **render endpoint** `GET /api/documents/{id}/eml-render` | `TextExtractorService.ExtractFromEmlAsync` (strips HTML→text for AI); `/preview-url` (generic SPE iframe) | No — text-extractor discards HTML; preview iframe can't render `.eml` as email | Cannot show the email "as sent"; the core reading-pane requirement fails |
| `sanitizeEmailHtml` (shared hardened client util) | permissive `DOMPurify.sanitize(...html:true)` in `MessageRow`/`MessageBubble` | Extend → **yes**: factor + harden the existing usage into one util and retrofit both call sites | Rendering raw email HTML without hardening = **XSS**; current usage is a latent hole |
| Email **surface** (`email` widget + code page) | `communications-list`; `CommunicationPage` | No — different metaphor/entry/expectation; `CommunicationPage` is single-record | No standalone mailbox destination |

> **Note on "extract-to-shared" Layer-1 logic** (`provenance` + write handler, attachment adapters, action-bar logic, tracking logic): **COMPLETE (extraction of existing code)**, not net-new surface → no §11 row. Reduces duplication by collapsing PCF-local + stale code-page copies into one shared React-agnostic core.

### Platform-Enabler Flag (demand-pull discipline)
- **`.eml`→HTML render endpoint + `sanitizeEmailHtml`** — this use case demand-pulls both as **reusable** capabilities (any future surface needing to show an archived email / render untrusted HTML). Minimal increment: build only the render endpoint + one shared sanitizer; do not pre-build a general mail-rendering framework.
- **Shared-lib reusable logic cores** — extract only the in-scope control logic; leave PCFs' views untouched.
- **Canonical compose** — consumed, not extended.
- No scheduler / gate / model-tier / results-table enablers pulled.

### Candidate ADR Tensions (per CLAUDE.md §6.5)
| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-022 (PCF Platform Libraries) | PCF uses platform React; shared surface stays slim | **Resolved — no violation.** Two-layer split shares logic (not React components) across the PCF boundary; React 19 views for code page only. | **C (comply)** | ADR-022's own slim-first guidance; matches `CommunicationConnections`/`CommunicationAttachments`. |
| ADR-022 (factual currency) | Text says React 16 / "React 18 unavailable as of May 2026" | MDA runtime now 17.0.2; Fluent 9.68.0. MUST-rules unchanged. | **B (minor amendment)** | Non-blocking follow-up. |
| §10 BFF Hygiene | New endpoints require placement justification + publish-size + tests | New `eml-render` endpoint added to BFF | **C (comply)** | Justified above; MimeKit already present (no new package); tests + size report required. |
| ADR-045 (Communication/Association architecture) | Association logic owned by the server Association Engine | Reading pane only **displays** associations + reuses the additive write path; no new client-side association logic | **C (comply)** | Reuse; thread-entity inheritance gap deferred, not worked around. |
| ADR-039 / BFF §10 (surface identity in code) | Surface wiring stays in code | Widget registration + shim in code — compliant | **C (comply)** | No server-side surface identity. |

### Resolved Decisions (2026-07-27)
- **"Email thread" = the email as sent** (embedded quoted history), rendered from the archived `.eml` — **Path 1** chosen over persist-full-body (Dataverse size cap) and Graph read-time assembly (latency/retention). Ranking: `.eml` > persist-body > Graph-assembly.
- **Left list = flat** (one card per email); optional "View by List / Thread" toggle. No Outlook-Conversations grouping.
- **Associations**: reading pane fully interactive; reply-chain auto-association is **already built** (ThreadContinuityRung, server); explicit link-another is **already built** (ConnectionsEditor). Thread-entity inheritance is a deferred server gap. (Correction: inheritance is NOT in RegardingResolver — that's a single-parent picker.)
- **Sanitization**: server-side in `eml-render` (the `.eml` path) + shared hardened client `sanitizeEmailHtml` for field bodies (retrofit `MessageRow`/`MessageBubble`).
- **PCF ↔ code-page**: two-layer split (share logic, not components).
- **New Email**: canonical `EmailComposer`/`SendEmailDialog`; `NewThreadModal` dropped.
- **Card → full-form escalation**: yes, explicit "Open full form" → 85% modal.

### Resolved Owner Clarifications (2026-07-27)
- **Archiving rollout** → **default-on for all monitored email accounts, forward-only** (no retroactive backfill). `.eml` reading works for all new inbound mail immediately.
- **Historical emails without `.eml`** → **accept `sprk_body`-only degradation** ("full history unavailable"); **no backfill** in r5.
- **`.eml` render caching** → **production posture: render-on-open, response marked immutable / long-lived cacheable** (archived `.eml` is immutable, so browser/gateway caching serves repeat opens). No bespoke server-side cache unless perf metrics require it (additive).
- **Thread-entity association inheritance** → **deferred** to a separate association-engine round (out of r5).
- **Remote-image privacy gate** → **render remote images normally in r5**; block-by-default "load remote images?" gate is a fast-follow. (Sanitizer still neutralizes scripts regardless.)

### Unresolved Questions
- None blocking. All design-time questions resolved above; remaining items (server-side `.eml` cache, remote-image gate, thread-entity inheritance, historical backfill) are documented follow-ups, not r5 scope.

---
*Design produced by use-case-to-design. Next: `/design-to-spec projects/email-communication-solution-r5`.*
