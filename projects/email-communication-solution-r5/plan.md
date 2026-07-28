# Implementation Plan — Email Workspace (Outlook-style) · `email-communication-solution-r5`

> **Source**: `spec.md` (19 FR / 7 NFR) + `design.md` (6 lenses).
> **Generated**: 2026-07-27 via `/project-pipeline`.
> **Type**: UI/surface project (dual-use Pattern D) + ONE new BFF endpoint (`.eml` render) + ONE config change (archiving default-on).
> **Hot-path**: BFF=Y · SpaarkeAi=Y · CI=N · Skills=N.

---

## 1. Goal

Ship a dedicated Outlook-style **Email** surface inside Spaarke — flat card list of Email-type `sprk_communication` records + a reading pane that renders the selected email **as sent** (full quoted history + inline images from the archived `.eml`) — mounted BOTH as a SpaarkeAi workspace widget and a standalone code page from one shared React 19 component. Reuse the canonical composer, association review, attachments, header, and tracking flags; keep the OOB form + its 4 PCFs regression-free by sharing **React-agnostic logic** (not components) across the PCF boundary.

## 2. Graduation Criteria (from spec §Success Criteria)

1. Email surface opens as widget AND code page, rendering identically (dual-mount parity).
2. Left list shows only Email-type records for the selected saved view; view switch re-populates.
3. Email with quoted history renders the full chain **as sent** (inline images resolved) from the `.eml`.
4. Archive-less email degrades to `sprk_body` + "full history unavailable" note (no error).
5. Reply / Reply All / Forward / New open the canonical composer with correct recipient prefill; send via existing path.
6. Association review is interactive + writes additively; a reply shows inherited parent regarding.
7. Malicious HTML (in `.eml` + field body) executes no script; `.eml` in sandboxed iframe.
8. OOB `sprk_communication` form + 4 PCFs regression-free after Layer-1 extraction.
9. BFF publish ≤60 MB, delta reported; `eml-render` endpoint has tests.
10. No React-version cast introduced on new code-page work; Layer-1 cores React-agnostic.

## 3. Architecture Context

### Two-layer split across the PCF ↔ code-page boundary (the load-bearing design decision)
- **Layer 1 — React-agnostic logic** (`provenance.ts`, reducers, `*Service`/API adapters, types, write handlers): **shared by both** the OOB-form PCF and the code page. Pure TS → zero React-version conflict, no cast.
- **Layer 2 — React 19 views**: authored in React 19 for the code page/widget; the **OOB-form PCFs keep their React 16/17 views** untouched.
- Rationale: PCF virtual controls run under platform React (manifest 16.14 / runtime 17.0.2); a virtual control cannot bundle its own React. Code pages bundle React 19. Fluent is uniformly v9 everywhere. Sharing *logic* (not components) is the only zero-friction reuse — ADR-022 slim-first; already the `CommunicationConnections`/`CommunicationAttachments` pattern.

### Discovered Resources (project-pipeline Step 2)

**Applicable ADRs**
- **ADR-022** — PCF platform libraries (two-layer split complies; slim-first). *Path B minor factual-currency amendment noted.*
- **ADR-006** — PCF vs Code Page boundary.
- **ADR-012** — Shared components (slim PCF↔shared surface).
- **ADR-021** — Fluent v9 + dark mode across all surfaces.
- **ADR-028** — Spaarke Auth v2 (code-page bootstrap; `authenticatedFetch`).
- **ADR-045** — Communication / Association architecture (association owned server-side; additive write path).
- **ADR-038** — Testing strategy (endpoint tests; seam tests as DoD for BFF read/render).

**Constraints / governance**
- `.claude/constraints/bff-extensions.md` — §10 BFF Hygiene (placement justification, publish ≤60 MB, endpoint tests) for the `eml-render` endpoint.
- Root CLAUDE.md §11 — component justification (five §11 rows in spec; Layer-1 extraction is COMPLETE-class, no §11 row).

**Standards / guides**
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` — dual-use Pattern D.
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — Pattern D worked example (Calendar).
- `docs/standards/MODAL-DECISION-CRITERIA.md` — "Open full form" 85% `navigateTo` modal.
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` — `Xrm.WebApi` (record metadata) vs BFF (`.eml` render).
- `docs/data-model/sprk_communication.md` — entity schema + `sprk_communicationtype` / `sprk_body` / `sprk_isemailarchive`.

**Canonical implementations to copy (from spec §Existing Patterns + design Lens 4)**
- Dual-use widget: `CalendarWorkspaceWidget` / `CommunicationsWorkspaceWidget` + `communications.registration.ts`.
- Code-page mount: `src/solutions/DailyBriefing/src/main.tsx`, `EventsPage`.
- `.eml` build (reverse of parse): `Services/Communication/.../GraphMessageToEmlConverter.cs`.
- Server MIME parse (extend, HTML-preserving): `Services/.../TextExtractorService.cs` (`using MimeKit`).
- Client sanitize/render (harden + share): `CommunicationTimeline/.../MessageRow.tsx`; `ConversationView/.../MessageBubble.tsx`.
- Additive association write: `ConnectionsEditor.tsx` → `ConnectionsWriteHandler.applyRegardingSelection`.
- View selector: `DataGrid/ViewSelector.tsx` + `IDataverseClient.retrieveSavedQueriesForEntity`.
- Split pane: `PanelSplitter/PanelSplitter.tsx`. Compose: `EmailComposer/` + `wrappers/SendEmailDialog.tsx`.

## 4. Phase Breakdown (WBS)

### Phase 0 — Foundations: security util + archiving config
| Task | Title | FR/NFR | Parallel |
|---|---|---|---|
| 001 | Shared hardened `sanitizeEmailHtml` in `@spaarke/ui-components` + retrofit `MessageRow`/`MessageBubble` | FR-16, NFR-03 | group P0 |
| 002 | Archiving default-on for monitored email accounts (`ArchiveIncomingOptIn`) | FR-17 | serial (shared `Services/Communication/`, `parallel-safe:false`) |

### Phase 1 — BFF `.eml` render endpoint
| Task | Title | FR/NFR | Parallel |
|---|---|---|---|
| 010 | `GET /api/documents/{id}/eml-render` — MimeKit HTML-preserving parse + `cid:`→`data:` + server sanitize + immutable cache + tests | FR-07, NFR-01/03 | serial |

### Phase 2 — Layer-1 logic extraction (React-agnostic cores; OOB PCFs regression-free)
| Task | Title | FR/NFR | Parallel |
|---|---|---|---|
| 020 | Extract **production** `ConnectionsEditor` Layer-1 logic (`provenance` + `ConnectionsWriteHandler.applyRegardingSelection`) → shared; PCF consumes | FR-13, FR-18 | serial (barrel + most complex; replaces stale stub) |
| 021 | Extract `CommunicationAttachments` Layer-1 (list core + data/BFF adapters); promote `AttachmentList` | FR-12, FR-18 | group P2 |
| 022 | Extract `CommunicationActions` Layer-1 (action-bar / prefill / suggested-create logic) | FR-08, FR-18 | group P2 |
| 023 | Lift `TrackingFieldTrio` generic core → `@spaarke/ui-components` (options passed in) | FR-14, FR-18 | group P2 |

### Phase 3 — Layer-2 React 19 views + reading-pane shell
| Task | Title | FR/NFR | Parallel |
|---|---|---|---|
| 030 | `EmailCardList` flat card list (from/subject/preview/date/unread) + loading/empty states | FR-03, FR-19 | group P3a |
| 031 | `ViewSelector` integration over `sprk_communication` saved views + default "Email — Inbox" + optional List/Thread toggle | FR-04 | group P3a |
| 032 | Reading-pane shell — `PanelSplitter` 2-pane + full-width toolbar composition | FR-05, FR-08 | serial (shell all others compose into) |
| 033 | `.eml` render branch — fetch `eml-render` → sandboxed iframe; degrade to `sprk_body` + note; error/loading states | FR-06, FR-19, NFR-02/03 | group P3b |
| 034 | Envelope header (`CommunicationHeader` reuse) + `AttachmentList` view + `RichFilePreviewDialog` | FR-11, FR-12 | group P3b |
| 035 | `ConnectionsEditor` review view (interactive) + `TrackingFieldTrio` view (consume 020/023 cores) | FR-13, FR-14, FR-19 | group P3b |
| 036 | Compose reuse — Reply/ReplyAll/Forward/New via `EmailComposer`+`SendEmailDialog` (recipient prefill) + "Open full form" 85% modal | FR-09, FR-10, FR-15 | group P3b |

### Phase 4 — Dual-use surface wiring (Pattern D)
| Task | Title | FR/NFR | Parallel |
|---|---|---|---|
| 040 | Assemble the single shared React 19 `EmailWorkspace` component (dual-mount source of truth) | FR-01, NFR-06 | serial |
| 041 | SpaarkeAi `email` widget registration + LegalWorkspace section shim + `system-layouts.json` seed | FR-01 | group P4 |
| 042 | Standalone Email code page `src/solutions/EmailPage/**` `main.tsx` + ADR-028 auth bootstrap | FR-02, NFR-07 | group P4 |

### Phase 5 — Integration, verification, deploy, wrap-up
| Task | Title | FR/NFR | Parallel |
|---|---|---|---|
| 050 | Verification sweep — dual-mount parity + OOB-form/4-PCF regression + XSS security cases (UI + endpoint tests) | NFR-03/04/06, SC 7/8 | serial |
| 051 | Deploy — code page + widget seed + BFF; publish-size report (≤60 MB, delta) | NFR-01 | serial |
| 090 | Project wrap-up — README status → Complete, lessons-learned, `/test-diet`, archive | — | serial |

## 5. Critical Path

`002 (archiving) → 010 (eml-render) → 033 (.eml render branch)` is the "email as sent" spine.
`020 (Connections Layer-1) → 035 (association view)` and `001 (sanitizer) → 033/field bodies` feed in.
`030/031/032 (list + shell) → 033/034/035/036 → 040 (shared component) → 041/042 (mounts) → 050 → 051 → 090`.

**Longest chain**: 002 → 010 → 032 → 033 → 040 → 041/042 → 050 → 051 → 090.

## 6. Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| P0 | 001 | — | Sanitizer util is independent of BFF work. 002 runs serial (shared file). |
| P2 | 021, 022, 023 | 020 landed (barrel established) | Distinct PCFs/cores; coordinate `@spaarke/*` barrel exports. |
| P3a | 030, 031 | 001 | Left-pane list + view selector; independent of shell internals. |
| P3b | 033, 034, 035, 036 | 032 (shell) landed | Distinct reading-pane sub-views; wire into the shell. |
| P4 | 041, 042 | 040 (shared component) landed | Widget mount + code-page mount. |

**`parallel-safe:false`**: 002 (shared `Services/Communication/`), 010 (BFF endpoint area), 032/040 (shell/assembly all views compose into). All BFF-touching tasks run `/conflict-check` before PR.

## 7. Risks

- **Communication-cluster contention** — `Services/Communication/` + `@spaarke/communication-components` shared with notification-spine-r1, messaging-r1/r2/r3, email-r4. Mitigate: `/conflict-check` before each BFF/shared-lib PR; sequence merge after email-r4.
- **`.eml` render fidelity** — inline `cid:`→`data:` resolution + sanitize must not strip legitimate markup. Mitigate: reference `GraphMessageToEmlConverter.cs` (the writer we parse); closed test set incl. quoted-history reference email.
- **XSS** — untrusted email HTML on two paths. Mitigate: server-side sanitize (`.eml`) + sandboxed iframe (no `allow-scripts`/`allow-same-origin`) + shared hardened client util (field bodies). Malicious-HTML test cases mandatory.
- **OOB regression** — Layer-1 extraction must not change PCF behavior. Mitigate: PCFs keep their views; regression pass in task 050.

## 8. References
- `spec.md` · `design.md` · root `CLAUDE.md` §10/§11 · `.claude/constraints/bff-extensions.md`
- `projects/INDEX.md` (hot-path registry — this project's row)
- Predecessor: `projects/email-communication-solution-r4/`
