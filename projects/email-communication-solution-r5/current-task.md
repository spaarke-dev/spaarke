# Current Task State — `email-communication-solution-r5`

> **Last Updated**: 2026-07-30 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This is a **UAT-iteration** session on the
> shipped Email surface (tasks 040/041/042/050 already ✅), NOT a fresh POML task.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work** | Email reading-pane + composer UAT iteration (owner-driven, live in harness) |
| **Last checkpoint** | Pushed commit **`643f63c00`** → `origin/work/email-communication-solution-r5` (resolver redesign + reading-pane polish; all green) |
| **Status** | Checkpoint pushed. NEXT wave = compose-engine items **9/10/11/12** (fully scoped below) |
| **Next Action** | Implement **10 + 11 first** (compose "Related to": deletable parent chips + Link tile; remove connect icon), then **9a/12** (verify), then **9b** (wire `onUploadLocalAttachment`). Start by reading `EmailComposer.tsx` L721–745 (Related-to render) + L432/L490 (connect gating). |
| **Harness** | `http://localhost:5175/` — run `SPAARKE_REPO_ROOT="c:/code_files/spaarke-wt-email-communication-solution-r5" npm run dev` in `c:/code_files/spaarke-prototype/projects/email-communication-solution-r5-uat` (HMR views this worktree's source). |

### Verify/build commands (this package)
- Typecheck: `cd src/client/shared/Spaarke.Communication.Components && npx tsc --noEmit -p tsconfig.json`
- Tests (jest, NOT vitest): `npx jest <pattern>` in that package.

### Critical context
Harness renders the **real production components** via alias. Native Xrm record-lookup
(recipient/record/connect) works in the deployed MDA (proven by the wizard) but no-ops in
the standalone harness (stub `Xrm.Utility.lookupObjects`). Widget header elevation + viewport
are host-provided; harness only approximates them.

---

## Design decisions LOCKED this session

### Resolver (single-primary redesign — SHIPPED in 643f63c00)
- **Model A**: the reading-pane resolver sets the ONE primary regarding (owns the denorm
  `sprk_regardingrecord*` fields incl. `sprk_regardingrecordnumber`). The engine's multi-lookup
  auto-writes are UNCHANGED. No clear-and-set.
- **One merged "Related to" section** (removed the old separate "Association" section + read-only pills).
- **3-state, dot-driven**: 🟢 confirmed (human-confirmed only, any %/path) · 🟡 needs-confirmation
  (autoFiled/100% awaiting confirm) · 🔴 requires-review (below auto-match → pick from candidates).
- **Cards**: always 3 slots, blank below **70%**; 2-line (`{REC#} : {name}` + %-tag / reason);
  click-to-select → Confirm appears under the selected card; switchable (incl. down from 100%).
- **Confirmed chip** lives in the **section header** (`{Type}: {number}`), clickable to open the
  record (navigationService.openRecordModal) + × to remove.
- **100% auto-write**: server-side at ingest; UI reflects + allows switch.
- **Link another record**: an in-grid tile (after the last card), non-bold, search icon (standard
  `Search20Regular`); click opens the type dropdown + right-pane lookup IN PLACE via `PolymorphicPicker`.

### Reading-pane polish (SHIPPED in 643f63c00)
No horizontal scroll (readingPaneScroll clips X); circular scroll-down FAB (scrollbar hidden,
appears only when content below fold — mirrors ComposeEditor); card sender **bold** + subject
**blue** + association **review dot**; shorter title bar; taller toolbar; **removed** Open-full-form
icon; recipient labels copied from compose `RecipientField.labelBox` + values **Segoe UI 14px**;
end-of-body **(i)** on a faint centered divider; toolbar right-icon spacing; widget-header elevation
(component approximation).

### Item-1 framing (owner asked)
Make PURE-COMPONENT items exact (fonts/spacing/cards/labels/colors). Host/platform items only
approximate in harness: native Xrm lookup (deploy-validated), widget header elevation, viewport.

---

## NEXT WAVE — Compose-engine items 9/10/11/12 (fully scoped, decisions captured)

**All live in the shared `EmailComposer` engine** (`src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/`) — consumed by the PCF (`SendEmailPage`), the wizard (`SendEmailStep`), and the reading-pane code page (`SendEmailDialog` via `useEmailComposeActions`). Mind blast radius; prefer per-caller wiring where possible.

### 10 + 11 (a PAIR — do together first)
- **10**: compose "Related to" shows inherited **parent associations as chips, each DELETABLE (×)**
  (owner: "may not be related to the parent"), PLUS a **"Link another record" tile**; when empty →
  show ONLY the Link tile. Lighter than the reading-pane resolver (chips + link, no cards/dots).
- **11**: **remove the connect/network icon** from the compose toolbar (relating now via the Related-to
  Link tile). MUST land with 10 or the composer loses its only way to relate.
- Engine anchors: Related-to render `EmailComposer.tsx` **L721–745** (`AssociationChips` at L745);
  connect icon `showConnector` gate **L432**, render **L490** (`Connector20Regular`);
  `handleAddRelationship` **L345–363** (reflects picked record into `state.associations`).
- Parent associations already flow in via the `associations` prop (reply/forward carry-over — verified working).

### 9a (attach existing document) — VERIFY, likely already works
- Engine `canLinkDocument = !!props.onLookupRecord` (`EmailComposer.tsx` L424); reading-pane compose
  passes `onLookupRecord` (createXrmEmailComposeHandlers). Confirm the paperclip's "Link documents"
  shows + works in the reading-pane compose.

### 12 (Attach | Link per row) — VERIFY, already exists
- `AttachmentList.tsx` L163: `showIncludeToggles = !readOnly && !!item.documentId`. Local files
  (no documentId) = **Attach-only**; document-backed items = **Attach + Link**. Already implemented.

### 9b (new-file upload) — CLIENT WIRING ONLY (NO new BFF endpoint — corrected)
- **Decision**: **Attach = default** for new local files (bytes, universal, works for all recipients).
  **Link = opt-in** — wire `onUploadLocalAttachment(file) => Promise<{documentId,...}>` to an EXISTING
  upload endpoint so the file gets a `documentId` (+ `linkUrl`) → the Link toggle lights up (gated on
  documentId). **External-recipient access = MATCH existing "Link a document" behavior** (no new sharing
  policy now).
- Engine seam already exists: `EmailComposer.tsx` L381–415 (`onUploadLocalAttachment` → patches
  `documentId`); a passing test exists (`__tests__/attachOnCompose.test.tsx`). **No production consumer
  wires it yet** (grep: only the test). So: unwired, not unbuilt.
- **Existing upload endpoints** (pick one; NO new BFF surface): `POST /api/spe/containers/{id}/items/upload`
  (ContainerItemEndpoints:157) · `PUT /api/drives/{driveId}/upload` (DocumentsEndpoints:307) ·
  `POST /api/containers/{containerId}/upload` + `PUT .../files/{*path}` (UploadEndpoints) · OBO chunked
  (OBOEndpoints:106/141). Open scoping Q: which endpoint + whether it also creates the governed
  `sprk_document` or that's a second call. TRACE how existing surfaces create the governed doc before wiring.

---

## Deferred / known follow-ups
- **External-recipient sharing of Linked SPE docs** = a deliberate, likely ADR-worthy decision (SPE links
  are Entra-auth-gated; external recipients need Graph sharing links). Out of scope now (match existing).
- **Old resolver-state tests** already rewritten to the single-primary model (green).
- Prototype harness seed (`_infra/seed/presets/email-r5-uat.ts`) still models old states; the 🟡
  needs-confirmation state needs an `autoFiled:true` seed to render cleanly (optional demo polish).
- (i) info affordance is at body top-right→now bottom divider; owner wanted "in the toolbar" — placed in
  body (toolbar integration needs the degraded flag lifted to the shell; offered as follow-up).

## Formal pipeline remainder (separate from UAT)
- **051 Deploy** 🔲 (BFF + code page + widget seed; publish-size report) → **090 Wrap-up** 🔲.
  UAT polish should settle before 051.

---

## How to continue
Say **"continue"** or **"start 10 and 11"**. First read `EmailComposer.tsx` L721–745 + L432/L490,
then implement deletable chips + Link tile + remove connect icon; typecheck + jest; view in harness.
