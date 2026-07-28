# Email Workspace (Outlook-style) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-27
> **Source**: `projects/email-communication-solution-r5/design.md` (produced via `/use-case-to-design`)
> **Round**: r5 (Communication surface line — successor to email-communication-solution-r4)

## Executive Summary

A dedicated Outlook-style **Email** surface inside Spaarke: a flat card list of `sprk_communication` email records on the left (driven by Dataverse saved views), and a reading pane on the right that shows the selected email **as the sender sent it** — the full quoted reply/forward history and inline images — with reply/reply-all/forward/compose via the canonical composer, plus inline attachment and record-association review. It ships in two mounts from one shared component (dual-use Pattern D): a SpaarkeAi **workspace widget** and a standalone **code page**. The OOB `sprk_communication` model-driven main form and its PCFs are kept; r5 extracts the controls' React-agnostic **logic** into shared cores and adds a reusable `.eml`→HTML render capability.

## Scope

### In Scope
- Dual-use `email` surface (SpaarkeAi workspace widget + standalone code page) via Pattern D.
- Flat left card list of Email-type `sprk_communication` records; view-selector dropdown over Dataverse saved views; optional "View by List / View by Thread" toggle.
- Reading pane rendering the email **as sent** from the archived `.eml` (full history + inline `cid:` images) in a sandboxed iframe; graceful degradation to `sprk_body` when no archive exists.
- New BFF endpoint `GET /api/documents/{id}/eml-render` (MimeKit HTML-preserving parse + `cid:`→`data:` + server-side sanitize).
- Full-width reading-pane toolbar: Reply / Reply All / Forward / New / Archive / Create.
- Canonical compose reuse for reply/reply-all/forward/new (`EmailComposer` + `SendEmailDialog`).
- Reused envelope header, attachments (with preview), interactive record-association review (confirm/change/dismiss/link-another), tracking flags.
- Shared hardened `sanitizeEmailHtml` client util + retrofit of the two existing permissive call sites.
- Two-layer extraction of the four controls' React-agnostic logic into shared cores; React 19 views for the code page; PCFs keep their views.
- Config change: archiving default-on for monitored email accounts (forward-only).

### Out of Scope
- Replacing the OOB `sprk_communication` main form or its PCFs (kept for MDA contexts).
- Chat/Teams/SMS/ACS channels (the existing `communications-list` widget remains the multi-channel surface).
- Spaarke-side thread reconstruction from linked records (the pane shows the single email as sent, not an assembled conversation).
- Thread-entity association inheritance enhancement (server association-engine change — deferred).
- Retroactive `.eml` backfill for historical mail.
- `EmailProcessingMonitor` admin diagnostics; new send/archive/attachment endpoints; new AI Actions/Bindings.
- Remote-image privacy gate; bespoke server-side `.eml` render cache (both fast-follow).

### Affected Areas
- `src/client/shared/Spaarke.Communication.Components/**` — new `EmailCardList`, reading-pane shell, `.eml` render branch; Layer-1 logic (connections `provenance` + write handler, attachment adapters, action-bar logic); widget export.
- `src/client/shared/Spaarke.UI.Components/**` — `TrackingFieldTrio` generic core; shared `sanitizeEmailHtml`; retrofit `CommunicationTimeline/.../MessageRow.tsx` + `ConversationView/.../MessageBubble.tsx`.
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` — `email` widget registration.
- `src/solutions/LegalWorkspace/src/sections/email.registration.ts` + `sectionRegistry.ts` — section shim.
- `src/solutions/EmailPage/**` (new) — standalone code page `main.tsx` + auth bootstrap.
- `src/server/api/Sprk.Bff.Api/**` — new `eml-render` endpoint (`FileAccessEndpoints.cs`/`DocumentsEndpoints.cs` area, MimeKit); `Services/Communication/IncomingCommunicationProcessor.cs` archiving default.
- `src/client/pcf/{CommunicationActions,CommunicationAttachments,CommunicationConnections,TrackingFieldTrio}/**` — refactor to consume Layer-1 logic (views unchanged).
- `scripts/system-layouts.json` + `Deploy-SystemWorkspaceLayouts.ps1` — widget seed.
- `tests/unit/Sprk.Bff.Api.Tests/**` — `eml-render` endpoint tests.

## Requirements

### Functional Requirements

1. **FR-01 — Dual-use surface (Pattern D)**: one shared React 19 component mounts both as SpaarkeAi widget type `email` and as a standalone code page. Acceptance: the same component renders identically in both mounts (dual-mount parity); one bug fix propagates to both.
2. **FR-02 — Standalone Email code page**: own `main.tsx` with ADR-028 auth bootstrap (DailyBriefing/EventsPage pattern), reachable from Spaarke navigation. Acceptance: page loads standalone, authenticates via `@spaarke/auth`, renders the surface.
3. **FR-03 — Flat left card list**: one card per `sprk_communication` row filtered to `sprk_communicationtype = 100000000 (Email)`; card shows from / subject / preview / date / unread. Acceptance: non-Email types never appear; cards render for the active view.
4. **FR-04 — View-selector dropdown**: reuse `ViewSelector` + `IDataverseClient.retrieveSavedQueriesForEntity` over `sprk_communication`; default "Email — Inbox"; changing the view re-runs its FetchXML. Optional "View by List / View by Thread" toggle (thread = group by `sprk_communicationthread`). Acceptance: switching views re-populates the list; default view loads on open.
5. **FR-05 — Two-pane reading layout**: reuse `PanelSplitter`; selecting a card loads that email into the right pane; resizable. Acceptance: selection drives the right pane; splitter persists width.
6. **FR-06 — Email rendered as sent**: the reading-pane body renders the archived `.eml` (full quoted history + inline `cid:` images) in a **sandboxed iframe**. If no `.eml` archive exists, degrade to `sprk_body` (clean latest message) with a "full history unavailable" note. Acceptance: an email with quoted history displays the full chain; an archive-less email shows the latest message + the note (no error).
7. **FR-07 — `.eml` render endpoint**: `GET /api/documents/{id}/eml-render` downloads the `.eml` from SPE, parses with MimeKit **preserving the HTML body**, rewrites `cid:` refs to `data:` URIs from the `multipart/related` parts, **sanitizes server-side**, and returns ready-to-render safe HTML. Response is marked immutable / long-lived cacheable. Acceptance: endpoint returns sanitized HTML with inline images resolved; malicious markup neutralized; unauthorized document → fails closed.
8. **FR-08 — Full-width toolbar**: Reply / Reply All / Forward / New / Archive / Create, spanning the reading pane width; reuse extracted `CommunicationActionBar` logic. Acceptance: actions dispatch correctly for an Email-type record.
9. **FR-09 — Reply / Reply All / Forward**: open the canonical `EmailComposer` via `SendEmailDialog`, recipients pre-filled (`initialTo`/`initialCc`; reply-all includes all recipients). Send uses the existing BFF path; reading pane refreshes. Acceptance: recipients pre-fill correctly per mode; send succeeds via existing path.
10. **FR-10 — New Email**: opens the canonical `EmailComposer` in compose mode via `SendEmailDialog` — the same compose used across all new-email surfaces. Acceptance: compose opens empty; no separate/forked composer introduced.
11. **FR-11 — Envelope header**: reuse `CommunicationHeader` (from / to / cc / bcc, subject, sent/received dates). Acceptance: header reflects the selected record.
12. **FR-12 — Attachments**: promote `AttachmentList` to shared; inline images filtered; click opens `RichFilePreviewDialog`. Acceptance: attachments list correctly; inline images excluded; preview opens.
13. **FR-13 — Interactive association review**: reuse extracted `ConnectionsEditor`; confirm / change / dismiss / link-another via the existing additive write path (`applyRegardingSelection`). Reply-chain auto-association (`ThreadContinuityRung`) is already applied at ingestion and displayed. Acceptance: user actions persist additively (siblings preserved); a reply shows its parent's inherited regarding.
14. **FR-14 — Tracking flags**: lift `TrackingFieldTrio` generic core to `@spaarke/ui-components` (options passed in, not hardcoded); render monitor / high-priority / access-permission. Acceptance: flags read/write correctly; core is entity-agnostic.
15. **FR-15 — Open full form**: explicit "Open full form" escalates to the OOB Email main form as an 85% `navigateTo` modal. Acceptance: modal opens the correct record/form.
16. **FR-16 — Shared hardened sanitizer**: build `sanitizeEmailHtml` (allow-list tags/attrs, no scripts/handlers, restrict URL schemes to http/https/mailto, link `rel=noopener target=_blank`) in `@spaarke/ui-components`; use it for field-rendered bodies; **retrofit** `MessageRow`/`MessageBubble` off the permissive `USE_PROFILES:{html:true}`. Acceptance: malicious HTML neutralized on all field-render paths; the two retrofitted components use the shared util.
17. **FR-17 — Archiving default-on**: set `ArchiveIncomingOptIn` default-on for monitored email accounts (forward-only). Acceptance: new inbound mail to monitored accounts produces an `.eml` archive.
18. **FR-18 — Two-layer control extraction**: extract each in-scope control's React-agnostic logic (Layer 1) into shared cores consumed by both the PCF and the code page; build React 19 views (Layer 2) for the reading pane; PCFs keep their existing React 16/17 views; OOB form unchanged. Acceptance: no duplicated business logic; PCFs and OOB form regression-free.
19. **FR-19 — Surface states**: loading (skeletons; header paints from record, body swaps in when `.eml` render returns), empty ("No emails" / "Select an email"), error (view-load banner; `.eml` failure → `sprk_body` fallback; record-load retry), uncertainty (association "Needs your decision"/"Suggested" groups). Acceptance: each state renders as specified.

### Non-Functional Requirements
- **NFR-01 — BFF publish size**: ≤60 MB compressed ceiling; MimeKit is already referenced (no new package). Measure + report absolute size + delta per §10 on every BFF-touching task.
- **NFR-02 — Performance**: reading-pane header paints < 300 ms (from the loaded record); `.eml` body renders < 1.5 s; view switch < 1 s.
- **NFR-03 — Security (XSS)**: no script execution from any email HTML. `.eml` sanitized server-side and rendered in a sandboxed iframe (`sandbox`, no `allow-scripts`/`allow-same-origin`); field bodies sanitized client-side via the shared util.
- **NFR-04 — Zero regression**: the OOB `sprk_communication` main form and its four PCFs function unchanged after Layer-1 extraction.
- **NFR-05 — React version boundary (ADR-022)**: Layer-1 cores are React-agnostic (no React-18/19 runtime APIs); code page = React 19; PCFs = platform React; no `as React.ComponentType` cast introduced on new code-page work.
- **NFR-06 — Dual-mount parity**: widget and code page render identically from the shared component.
- **NFR-07 — Auth**: reuse `@spaarke/auth` `authenticatedFetch`; per ADR-028; no new auth surface.

## Technical Constraints

### Applicable ADRs
- **ADR-022** — PCF platform libraries (platform React 16.14/runtime 17.0.2; Fluent v9); the two-layer split complies.
- **ADR-006** — PCF vs Code Page boundary.
- **ADR-012** — Shared components (slim PCF↔shared surface).
- **ADR-021** — Fluent v9 across all surfaces.
- **ADR-028** — Spaarke Auth v2 (code-page bootstrap).
- **ADR-045** — Communication / Association architecture (association owned server-side).
- **§10 BFF Hygiene** — new endpoint governance (placement justification, publish-size, tests).
- **§11 Component Justification** — reuse-first; new components justified below.

### MUST Rules
- ✅ MUST share **React-agnostic logic** (not React components) across the PCF boundary; PCFs keep platform React; code page uses React 19 (ADR-022 slim-first).
- ✅ MUST sanitize all email HTML before display (server-side for `.eml`; client-side shared util for field bodies); MUST render `.eml` in a sandboxed iframe.
- ✅ MUST reuse the canonical `EmailComposer`/`SendEmailDialog` for every compose/reply/forward path — MUST NOT fork a new composer.
- ✅ MUST use the existing additive association write path (`applyRegardingSelection`) — MUST NOT clear-and-set.
- ✅ MUST keep the OOB `sprk_communication` form + PCFs working (regression-free).
- ✅ MUST use the extracted **production** `ConnectionsEditor` (PCF version) — MUST NOT reuse the stale `CommunicationPage` stub.
- ✅ MUST measure + report BFF publish size on BFF-touching tasks; MUST add endpoint tests.
- ❌ MUST NOT use React 18/19 runtime APIs (`createRoot`, `use()`, Actions) in any core a PCF renders.
- ❌ MUST NOT introduce a new BFF surface beyond the single `eml-render` endpoint.

### Existing Patterns to Follow
- Dual-use widget: `CalendarWorkspaceWidget` / `CommunicationsWorkspaceWidget` + `communications.registration.ts`.
- Code-page mount: `src/solutions/DailyBriefing/src/main.tsx`, `EventsPage`.
- `.eml` build (reverse of what we parse): `Services/Communication/…/GraphMessageToEmlConverter.cs` (self-contained multipart/related + `ContentId`).
- Server MIME parse (to extend, HTML-preserving): `TextExtractorService.cs` (`using MimeKit`).
- Client sanitize/render pattern (to harden + share): `CommunicationTimeline/.../MessageRow.tsx`.
- Additive association write: `ConnectionsWriteHandler.applyRegardingSelection`.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>            <!-- NEW endpoint: GET /api/documents/{id}/eml-render -->
  <spaarkeai>Y</spaarkeai> <!-- new 'email' workspace widget + section shim -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
**BFF Placement Justification (§10 / `.claude/constraints/bff-extensions.md`)**: the `.eml`→HTML render belongs in the BFF because MimeKit already lives server-side, the SPE download facade is server-side, and server-side sanitization is the trusted single point for untrusted email HTML. A client-side MIME parser would be net-new JS and would ship raw `.eml` bytes to the browser. No new NuGet package (MimeKit present); ≤60 MB publish ceiling applies per task; endpoint requires tests in `tests/unit/Sprk.Bff.Api.Tests/`.

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `EmailCardList` (flat mail card column) | `ThreadList.tsx` (thread rows); DataGrid body (tabular) | No — thread-oriented / tabular-only; card render would require a DataGrid fork | No Outlook card list; surface degrades to a chat list or a grid |
| Email reading-pane shell (2-pane + full-width toolbar + Layer-2 views) | `ConversationView` (chat bubbles); `CommunicationLayout` (single-record page) | No — bubbles are chat; `CommunicationLayout` is full-page with no split/toolbar-across-width | No side-by-side read-while-browsing; loses the Outlook reading pane |
| `GET /api/documents/{id}/eml-render` endpoint | `TextExtractorService.ExtractFromEmlAsync` (strips HTML→text); `/preview-url` (generic SPE iframe) | No — text-extractor discards HTML; preview iframe can't render `.eml` as email | Cannot show the email "as sent"; the core reading-pane requirement fails |
| `sanitizeEmailHtml` (shared hardened client util) | permissive `DOMPurify.sanitize(…html:true)` in `MessageRow`/`MessageBubble` | **Yes — extend**: factor + harden existing usage into one util; retrofit both call sites | Rendering raw email HTML unhardened = XSS; current usage is a latent hole |
| Email surface (`email` widget + code page) | `communications-list`; `CommunicationPage` | No — different metaphor/entry/expectation; `CommunicationPage` is single-record | No standalone mailbox destination |

**Layer-1 logic extraction** (`provenance` + write handler, attachment adapters, action-bar logic, tracking logic) is **COMPLETE (extraction of existing code)**, not net-new surface — it reduces duplication and needs no §11 row.

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-022 (PCF Platform Libraries) | PCF uses platform React; shared PCF surface stays slim | Resolved — no violation. Two-layer split shares logic (not React components); React 19 views for code page only. | **C (comply)** | ADR-022's own slim-first guidance; matches `CommunicationConnections`/`CommunicationAttachments`. |
| ADR-022 (factual currency) | Text says "React 16 / React 18 unavailable as of May 2026" | MDA runtime is now 17.0.2; Fluent runtime 9.68.0; MUST-rules unchanged | **B (minor amendment)** | Non-blocking follow-up to refresh version facts; does not affect r5 code. |
| §10 BFF Hygiene | New endpoints require placement justification + publish-size + tests | New `eml-render` endpoint | **C (comply)** | Justified above; MimeKit already present; tests + size report required. |
| ADR-045 (Communication/Association) | Association logic owned by the server Association Engine | Reading pane only displays associations + reuses the additive write path; no new client association logic | **C (comply)** | Reuse; thread-entity inheritance gap deferred, not worked around. |
| ADR-039 / BFF §10 (surface identity in code) | Surface wiring stays in code | Widget registration + shim in code | **C (comply)** | No server-side surface identity added. |

## Success Criteria
1. [ ] Email surface opens as both a SpaarkeAi widget and a standalone code page, rendering identically — Verify: mount both, compare.
2. [ ] Left list shows only Email-type records for the selected saved view; view switch re-populates — Verify: exercise views incl. a non-Email negative check.
3. [ ] Selecting an email with quoted history renders the full chain **as sent** (inline images resolved) from the `.eml` — Verify: reference-screenshot-style email.
4. [ ] An archive-less email degrades to `sprk_body` + "full history unavailable" (no error) — Verify: record with no `.eml`.
5. [ ] Reply / Reply All / Forward / New open the canonical composer with correct recipient prefill; send via existing path — Verify: each mode.
6. [ ] Association review is interactive and writes additively; a reply shows inherited parent regarding — Verify: ambiguous + inherited cases.
7. [ ] Malicious HTML (in `.eml` and field body) executes no script; `.eml` in sandboxed iframe — Verify: XSS payload test.
8. [ ] OOB `sprk_communication` form + 4 PCFs regression-free after Layer-1 extraction — Verify: open form, exercise each PCF.
9. [ ] BFF publish size ≤60 MB, delta reported; `eml-render` endpoint has tests — Verify: publish + test run.
10. [ ] No React-version cast introduced on new code-page work; Layer-1 cores React-agnostic — Verify: code review + build under both React majors.

## Dependencies

### Prerequisites
- Archiving default-on config (FR-17) landed before/with the reading pane, so `.eml` exists to render.
- Extracted production `ConnectionsEditor` logic available before the reading pane's association section.

### External Dependencies
- MimeKit (already referenced server-side).
- DOMPurify (already a client dependency).
- SPE document download facade + document identity for the `.eml` archive (`sprk_isemailarchive`).
- Graph (only via the existing ingestion path — no new Graph calls in r5).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Archiving rollout | New accounts only, all accounts, or + backfill? | **All monitored accounts, forward-only; no backfill** | `.eml` reading works for all new inbound; historical mail uses degradation |
| Historical mail | Degradation acceptable, or backfill old `.eml`? | **Degradation acceptable; no backfill** | Pre-archive records show latest message + note; backfill is a separate future effort |
| `.eml` caching | Parse-on-open vs cache? | **Production: render-on-open, immutable/long-cache response**; server cache only if metrics require | Simple, correct, production-valid; leverages HTTP cache (content immutable) |
| Thread-entity inheritance | Include server enhancement or defer? | **Defer** | Reply-chain inheritance (built) covers r5; enhancement is a separate association round |
| Remote images | Privacy gate now or later? | **Later (render normally in r5)** | Scripts still sanitized; block-by-default images is a fast-follow |

## Assumptions
- **Reading pane = single email as sent** (from `.eml`), not a Spaarke-reconstructed multi-record conversation — per owner alignment 2026-07-27.
- **Left list flat by default**; the List/Thread toggle is included only if inexpensive.
- **`sprk_communicationthread`** surfaced as context/optional grouping, not a conversation view.
- **Server-side sanitize in `eml-render`** is authoritative for the `.eml` path; the client sandboxed iframe is defense-in-depth.

## Unresolved Questions
- None blocking. Documented follow-ups (not r5 scope): server-side `.eml` render cache (if perf requires), remote-image privacy gate, thread-entity association inheritance (server round), historical `.eml` backfill.

---
*AI-optimized specification. Original design: `design.md`.*
