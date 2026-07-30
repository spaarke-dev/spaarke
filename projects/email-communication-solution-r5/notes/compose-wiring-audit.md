# Compose-Wiring Audit — code-page/widget composer vs PCF composer

> **Type**: Investigation (defect → root-cause → fix map).
> **Date**: 2026-07-29
> **Scope**: Why the Email code-page/widget composer (opened via `useEmailComposeActions`) was missing
> features the PCF composer (`CommunicationActionsApp`) has.

---

## ✅ VERIFICATION UPDATE — 2026-07-29 (all 6 defects RESOLVED)

**This document was written as a PRE-FIX investigation. A re-verify pass on 2026-07-29 confirms
all six defects are now implemented end-to-end in committed code** (commit `031123f40`
"compose popup wiring" + the two subsequent UAT-round commits). The body below is retained as the
original root-cause analysis; the table here is the current, authoritative state.

| # | Defect | Status | Evidence (file:line) |
|---|--------|--------|----------------------|
| 1 | Recipient lookup button | ✅ Fixed | `SendEmailDialog.tsx:104` · `useEmailComposeActions.tsx:196` · `createXrmEmailComposeHandlers.ts:103` · mounts `EmailPage/main.tsx:228`, `EmailWorkspaceWidget.tsx:104` |
| 2 | Recipient type-ahead | ✅ Fixed | both mounts build `searchUsersAndContacts(dataService, q)` — `EmailPage/main.tsx:202,227`; `EmailWorkspaceWidget.tsx:78,103` |
| 3 | Parent attachments carry-over | ✅ Fixed | hook calls `fetchSourceAttachments` `useEmailComposeActions.tsx:111`, passes `initialAttachments:201`; `dataverseUrl` threaded from both mounts |
| 4 | "Related to" inheritance | ✅ Fixed | `EmailWorkspace.tsx:133-155` builds `parentAssociations` and passes into the hook |
| 5 | Body toolbar tools | ✅ Fixed | factory `onLookupRecord`/`onAddRelationship` `createXrmEmailComposeHandlers.ts:87,138`; threaded via deps + both mounts |
| 6 | "Reply: {subject}" title | ✅ Fixed | hook `titleOverride` `useEmailComposeActions.tsx:172`; wrapper `SendEmailDialog.tsx:123`; engine `EmailComposer.tsx:780` |

The compose handler factory `createXrmEmailComposeHandlers` is a REAL Xrm-backed builder
(`getXrm().Utility.lookupObjects`) — not a stub. Nothing remains open on the compose path.

> **Still deferred (unchanged):** functional local-file upload (`onUploadLocalAttachment`) — needs a
> BFF document-upload path. Defect 6's subject-in-title is opt-in via `titleOverride`, so the PCF/wizard
> consumers are unaffected (they don't pass it).

---

---

## (a) PCF-vs-code-page composer relationship — SAME engine, different wrappers + wiring

**`sameComposer: true`.** There is exactly ONE composer engine — the canonical
`EmailComposer` (`src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/EmailComposer.tsx`).
Both surfaces mount it; neither forks it. **Every feature the owner reports as "missing" already exists
in the engine, gated behind optional props.** The divergence is entirely in (1) which thin wrapper each
surface uses and (2) which props each caller supplies.

| | PCF action bar (works) | Email code-page + widget (broken) |
|---|---|---|
| Composition root | `CommunicationActionsApp.tsx` | `EmailWorkspace.tsx` |
| Wrapper | **`SendEmailPage`** (`mount='page'`) | **`SendEmailDialog`** (`mount='dialog'`) via `useEmailComposeActions` |
| Engine | `EmailComposer` (identical) | `EmailComposer` (identical) |

**Key structural fact:** the two wrappers are NOT equally capable.

- `ISendEmailPageProps` (`wrappers/SendEmailPage.tsx` L29–75) **declares the full rich surface**:
  `initialAttachments`, `onSearchRecipients`, `onLookupRecipients`, `recordLookupCatalog`,
  `onLookupRecord`, `onAddRelationship`.
- `ISendEmailDialogProps` (`wrappers/SendEmailDialog.tsx` L71–135) **declares only a thin subset**:
  `onSearchRecipients`, `associations`, `attachmentSources`, `initialTo/Cc/Subject/Body`, `regarding`,
  `sourceRecord`. It **omits** `onLookupRecipients`, `recordLookupCatalog`, `onLookupRecord`,
  `onAddRelationship`, `initialAttachments`, `onUploadLocalAttachment`.

Because `SendEmailDialog` forwards `...composerProps` to the engine (L166), any prop it *declared* would
reach the engine at runtime — but TypeScript blocks the caller from passing props the wrapper type omits.
So the dialog wrapper is a real capability ceiling for the code-page/widget path.

### The widget prop pipeline (where things drop)

```
Mount                         →  EmailWorkspace          →  useEmailComposeActions  →  SendEmailDialog     →  EmailComposer
EmailPage/main.tsx L202-210      props L118-129 /            deps L52-62 /              ISendEmailDialogProps   (engine — has
EmailWorkspaceWidget L74-83      compose call L147-154       dialog element L133-147    L71-135 (thin)          all features)
```

The PCF path (`CommunicationActionsApp` → `SendEmailPage`) fills every prop from `Xrm` handlers
(L544–585). The widget path supplies only `authenticatedFetch`, `bffBaseUrl`, `dataService`,
`navigationService`, `onSearchRecipients` (optional — and neither mount actually passes it), and `onSent`.

### Exact files

| Role | Path |
|---|---|
| Canonical engine (all features live here) | `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/EmailComposer.tsx` |
| Rich wrapper (PCF) | `.../EmailComposer/wrappers/SendEmailPage.tsx` |
| Thin wrapper (widget) | `.../EmailComposer/wrappers/SendEmailDialog.tsx` |
| Recipient field (typeahead + lookup) | `.../EmailComposer/subcomponents/RecipientField.tsx` |
| PCF wiring (reference impl — full) | `src/client/pcf/CommunicationActions/CommunicationActions/CommunicationActionsApp.tsx` |
| Widget hook (thin wiring) | `src/client/shared/Spaarke.Communication.Components/src/components/EmailComposeActions/useEmailComposeActions.tsx` |
| Widget hook deps type | `.../EmailComposeActions/EmailComposeActions.types.ts` |
| Widget prefill read | `.../EmailComposeActions/fetchCommunicationPrefill.ts` |
| Derive prefill fields (pure) | `.../logic/actions/composerPrefill.ts` |
| Source-attachment enumerator (exists, unused by widget) | `.../logic/actions/attachmentsSource.ts` (`fetchSourceAttachments`) |
| Composition root | `.../components/EmailWorkspace/EmailWorkspace.tsx` (+ `EmailWorkspace.types.ts`) |
| Code-page mount | `src/solutions/EmailPage/src/main.tsx` |
| Widget mount | `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/EmailWorkspaceWidget.tsx` |

> Note: a *third*, older code-page slot `src/client/code-pages/CommunicationPage/src/components/EmailComposerSlot.tsx`
> mounts `SendEmailPage` directly and is ALSO under-wired (no lookup/relationship/attachment handlers). It is a
> different surface from the `EmailWorkspace` the owner is testing; fixes below target the `EmailWorkspace` path.

---

## (b) Defect → root cause → fix → blast radius

| # | Symptom | Root cause (file:line) | Proposed fix | Blast radius |
|---|---------|------------------------|--------------|--------------|
| 1 | To/Cc/Bcc label doesn't open a contact picker | Engine renders the label as a lookup button ONLY when `onLookupRecipients` is set (`RecipientField.tsx:412`; wired at `EmailComposer.tsx:649,658,667`). PCF supplies `onLookupRecipients` (`CommunicationActionsApp.tsx:558`, handler `:433`). Widget path never supplies it: not declared on `ISendEmailDialogProps` (`SendEmailDialog.tsx:71`), not passed by the hook (`useEmailComposeActions.tsx:133`), not on `EmailComposeActionsDeps` (`EmailComposeActions.types.ts:43`), not on `EmailWorkspaceProps`, not built at either mount. | Add optional `onLookupRecipients` to `ISendEmailDialogProps` (forward-through already works via the `...composerProps` spread); add to deps + `EmailWorkspaceProps`; pass from the hook (record-scoped only); build the handler at each mount from `getXrm().Utility.lookupObjects` — copy `CommunicationActionsApp.tsx:433-473` verbatim (Xrm IS reachable in an MDA code page/widget via `window.Xrm`, same `getXrm()` the widget already uses). | **SHARED** (additive optional prop on `SendEmailDialog` type — behavior-neutral to existing callers) + our-wiring |
| 2 | Recipient type-ahead does nothing | Engine's debounced search runs only when `onSearch` is set (`RecipientField.tsx:372-397` early-returns on `!onSearch`); `onSearch` = `props.onSearchRecipients` (`EmailComposer.tsx:648`). `onSearchRecipients` is plumbed end-to-end (`EmailWorkspaceProps` L80 → deps L63 → `SendEmailDialog` L85 → engine) — but **neither mount passes it**: `EmailPage/main.tsx:202-210` and `EmailWorkspaceWidget.tsx:74-83` both omit it, so it arrives `undefined`. | At each mount build `handleSearchRecipients = (q) => searchUsersAndContacts(createXrmDataService(), q)` and pass `onSearchRecipients={...}` to `<EmailWorkspace>`. Both pieces already exist in the codebase (`EmailComposerSlot.tsx:81-85` shows the exact idiom). No shared-lib edit. | **OUR-WIRING** (2 mount files only) — lowest |
| 3 | Parent attachments not carried into reply/forward | PCF enumerates source attachments via `fetchSourceAttachments(context.webAPI, id, url)` (`CommunicationActionsApp.tsx:374`) and passes `initialAttachments` (`:565`). Widget path: `fetchCommunicationPrefill.ts:17` `$select` omits attachments; `deriveComposerFields` (`composerPrefill.ts:60-107`) never derives attachments; the hook never calls `fetchSourceAttachments` and never passes `initialAttachments`; `ISendEmailDialogProps` doesn't declare `initialAttachments`. | In `useEmailComposeActions.openComposer`, for record-scoped modes call `fetchSourceAttachments(...)` (already exported from `@spaarke/communication-components/logic/actions`) — its `IActionsWebApi` is satisfied by `dataService.retrieveMultipleRecords` — store on `DialogState`, pass `initialAttachments` to the dialog; add `initialAttachments` to `ISendEmailDialogProps`. Needs a Dataverse base-url for `buildDocumentLinkUrl` (resolve at mount, thread via deps). | **SHARED** (additive `initialAttachments` on `SendEmailDialog` type) + our-wiring (hook + deps + mounts) |
| 4 | Parent "Related to" associations not carried | PCF reads the denormalized `_sprk_regarding*_value` lookups (`CommunicationActionsApp.tsx:330-366`, list `:104-114`) into `parentAssociations` and passes `associations` (`:564`). Widget path: `fetchCommunicationPrefill.ts:17` doesn't `$select` any regarding field; the hook forwards `associations` = `deps.associations` (`useEmailComposeActions.tsx:142`) — but `EmailWorkspace.tsx:147-154` never passes `associations` into the deps and `EmailWorkspaceProps` doesn't expose it → always `undefined`. | Read the parent's regarding associations (reuse the PCF's `COMMUNICATION_REGARDING_FIELDS` loop) — best done inside the hook's prefill read (extend `fetchCommunicationPrefill` `$select` + map). Recommend lifting the regarding-field map + mapper into `logic/actions` as ONE shared helper (avoids a third copy). The `associations` prop already flows hook→dialog→engine, so **no engine/wrapper change**. | **OUR-WIRING** (hook + prefill read; optional additive shared helper in `logic/actions`) — no `SendEmailDialog`/engine change |
| 5 | Body toolbar missing paperclip/search/connect | `toolbarSlot` gates each control (`EmailComposer.tsx:423-432`): `canLinkDocument`/`showRecordSearch` need `props.onLookupRecord` (+ `recordLookupCatalog`); `showConnector` needs `props.onAddRelationship`. PCF supplies all three (`CommunicationActionsApp.tsx:559-561`, handlers `:406`/`:479`). Widget path supplies none, and `ISendEmailDialogProps` declares none of `recordLookupCatalog`/`onLookupRecord`/`onAddRelationship`/`onUploadLocalAttachment` (`SendEmailDialog.tsx:71-135`). (Paperclip "Add files" branch — `canAddLocal` — IS on by default, but with no `onUploadLocalAttachment` local picks are display-only, so the whole toolbar reads as broken.) | Add optional `recordLookupCatalog`, `onLookupRecord`, `onAddRelationship` (and later `onUploadLocalAttachment`) to `ISendEmailDialogProps`; thread via deps + `EmailWorkspaceProps`; build the Xrm handlers at each mount (copy `CommunicationActionsApp.tsx:406-499` + the `RECORD_LOOKUP_CATALOG`/`REGARDING_ENTITY_TYPES` consts). Functional file-upload (`onUploadLocalAttachment`) is a larger add (needs a BFF document-upload) — defer. | **SHARED** (additive optional props on `SendEmailDialog` type) + our-wiring |
| 6 | Header says only "Reply" (want "Reply: {subject}") | The engine header hardcodes the mode word from `state.mode` with no subject (`EmailComposer.tsx:780-789`: reply→`'Reply'`, forward→`'Forward'`, …). This is the SHARED engine, so the PCF action-bar dialog shows the SAME bare word — this is NOT a PCF-vs-code-page divergence; both are bare. | Prefer a prop-gated change: add optional `titleOverride?: string` (or `showSubjectInTitle?: boolean`) so callers opt in; the widget passes `Reply: {subject}` / `Forward: {subject}` / `Reply All: {subject}`. Avoid an unconditional inline change unless the owner wants it app-wide (it would change the PCF + wizard dialog + every consumer). | **SHARED ENGINE** — touches ALL composer consumers (PCF action bar, wizard `SendEmailStep`, code page). Flag for owner sign-off. |

---

## (c) Recommended fix ordering (low blast radius first)

1. **Defect 2 (type-ahead)** — mount-only, zero shared-lib touch, restores the most visible behavior.
   Two 3-line edits (`EmailPage/main.tsx`, `EmailWorkspaceWidget.tsx`).
2. **Defect 4 (Related to inheritance)** — hook + prefill read only; `associations` already flows to the
   engine, so no wrapper/engine change. Optionally lift the regarding-map into `logic/actions` (additive).
3. **Defects 1 + 5 + 3 (recipient lookup + toolbar tools + attachments) — batch together.** They share ONE
   enabling change: widen `ISendEmailDialogProps` with the additive optional handler props, then thread
   `deps → EmailWorkspaceProps → both mounts` and build the Xrm handlers by copying the PCF's proven
   `CommunicationActionsApp` handlers. Doing them as one pass avoids touching `SendEmailDialog.tsx` three times.
   (Defer functional local-file upload `onUploadLocalAttachment` — needs a BFF upload path.)
4. **Defect 6 (title)** — last; it is a SHARED-ENGINE change affecting every composer consumer. Recommend the
   prop-gated approach and get owner sign-off on whether the subject-in-title should also apply to the PCF/wizard.

### Blast-radius summary

- **OUR-WIRING only (safe, isolated):** Defects 2, 4.
- **SHARED wrapper type — additive/optional, behavior-neutral to existing callers:** Defects 1, 3, 5 (each
  widens `ISendEmailDialogProps`; existing `SendEmailPage`/PCF callers unaffected).
- **SHARED engine — app-wide behavior change, needs owner sign-off:** Defect 6.

No fix requires forking the composer. The engine already supports all six behaviors; the work is (mostly)
supplying the props the widget path currently drops, plus one additive widening of the thin dialog wrapper's
type and one prop-gated engine header tweak.

---

{ "sameComposer": true, "defects": [ { "id": 1, "rootCause": "onLookupRecipients handler not plumbed to the dialog path; ISendEmailDialogProps omits it and neither mount builds an Xrm lookupObjects handler (RecipientField.tsx:412 gates the lookup button on it)", "blastRadius": "shared", "fixFile": "src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/wrappers/SendEmailDialog.tsx" }, { "id": 2, "rootCause": "onSearchRecipients is plumbed end-to-end but neither mount passes it (EmailPage/main.tsx:202 and EmailWorkspaceWidget.tsx:74 omit it), so RecipientField.tsx:372 early-returns", "blastRadius": "our-wiring", "fixFile": "src/solutions/EmailPage/src/main.tsx" }, { "id": 3, "rootCause": "useEmailComposeActions never calls fetchSourceAttachments and never passes initialAttachments; fetchCommunicationPrefill $select omits attachments and ISendEmailDialogProps omits initialAttachments", "blastRadius": "shared", "fixFile": "src/client/shared/Spaarke.Communication.Components/src/components/EmailComposeActions/useEmailComposeActions.tsx" }, { "id": 4, "rootCause": "parent regarding lookups are never read (fetchCommunicationPrefill.ts:17 $select omits them) and EmailWorkspace.tsx:147 never supplies deps.associations, so the already-flowing associations prop is always undefined", "blastRadius": "our-wiring", "fixFile": "src/client/shared/Spaarke.Communication.Components/src/components/EmailComposeActions/useEmailComposeActions.tsx" }, { "id": 5, "rootCause": "toolbarSlot controls are gated on onLookupRecord/recordLookupCatalog/onAddRelationship (EmailComposer.tsx:423-432) which the dialog wrapper type omits and neither mount supplies", "blastRadius": "shared", "fixFile": "src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/wrappers/SendEmailDialog.tsx" }, { "id": 6, "rootCause": "engine header hardcodes the mode word with no subject (EmailComposer.tsx:780-789) — shared by every consumer including the PCF", "blastRadius": "shared", "fixFile": "src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/EmailComposer.tsx" } ], "reportPath": "projects/email-communication-solution-r5/notes/compose-wiring-audit.md" }
