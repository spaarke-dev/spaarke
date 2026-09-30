# Task 027 (FR-10): the record-open mechanism — branch taken and rationale

> **Task**: `027-open-record-from-pane.poml`
> **Branch taken**: **Option 3** — read-only detail in-pane (task 026's `RelatedRecordCard`) + a
> browser-tab escape hatch via `Office.context.ui.openBrowserWindow` (`OpenBrowserWindowApi` 1.1).
> **NOT** the Office Dialog API.
> **Source of the decision**: `notes/spikes/spike-2-dialog-api.md`'s own Recommendation section
> (task 003). This task implements that recommendation; it does not re-derive it.

---

## Spike-2's four answers, restated

Per the task's first acceptance criterion, restating Spike-2's findings before any code was written:

- **(a) Framing** — DESK-RESEARCHED: no framing refusal is expected on either Word desktop or Word
  on the web in the Dialog API's *default* (non-iframe) mode; `displayInIframe` is silently ignored
  outside Office-on-the-web, and Dataverse's `frame-ancestors` CSP only restricts genuine embedding,
  which the default mode never does. One residual AMBER item (a WebView2-specific navigation
  allow-list beyond standard CSP) is undocumented and unresolved by desk research.
- **(b) Auth context** — no new Entra SPA redirect URI is required under any of the three options
  compared. The dialog cannot share the pane's `@spaarke/auth` `sessionStorage` cache by any
  verified mechanism (AMBER — untested whether a dialog's browsing context inherits a clone of the
  opener's session storage); an MDA form's own sign-in is architecturally independent of
  `@spaarke/auth`'s BFF-scoped token entirely, using Dataverse's own Entra app registration via the
  browser/WebView2 profile's existing AAD session (silent) or an interactive prompt (AMBER —
  untested on Word desktop's WebView2 profile specifically).
- **(c) Usability at dialog size** — UNVERIFIED (AMBER) on all three interactions the task requires
  reported separately: saving the record, opening a lookup picker, and using a subgrid, all at the
  Dialog API's default 80%×80% dimension. No Microsoft documentation confirms or denies MDA
  functions correctly inside a Dialog-API window at any size.
- **(d) Propagation** — `messageParent` is architecturally available cross-domain (Dialog Origin
  1.1), but an unmodified Dataverse `main.aspx` page never calls it — Microsoft does not instrument
  MDA to participate in Office Dialog messaging, and there is no documented extensibility hook
  (form script / web resource / ribbon command) through which it could. This is not an origin-policy
  wall; the far end simply never opts in. Consequently propagation must use a
  close/focus-triggered refetch for **every** option Spike-2 compared, not a shortcut specific to
  Option 3.

## Why Option 3, restated

Spike-2's own Recommendation section picked Option 3 because it is the only one of the three with
**zero remaining platform risk that only a live probe could resolve** — every mechanism it depends
on (browser-tab MDA sign-in, the documented unconditional `OpenBrowserWindowApi` 1.1 call) is
either already how Spaarke users interact with Dataverse today, or has no origin constraint left to
verify. Options 1 (MDA directly in the dialog) and 2 (a Spaarke-hosted code page in the dialog) each
carry AMBER items — (c)'s three usability interactions for Option 1, and a structural
maintenance/fidelity cost for Option 2 — that this task does not attempt to resolve or build
against, per the spike's own guardrail against building ahead of verification.

This task implements Option 3 exactly as recommended. No escalation fired: FR-10's acceptance
criterion is satisfiable by Option 3 (trigger 1 does not apply), no new Entra registration/redirect
URI class/admin-consent permission is required by any path in this report (trigger 2 does not
apply), and the design never iframe-embeds `main.aspx` (trigger 3, the
`MODAL-DECISION-CRITERIA.md` anti-pattern, does not apply).

---

## What was built

### The launcher

`shared/taskpane/services/openRecordLauncher.ts` — a pure URL builder (`buildOpenRecordUrl`) plus a
thin, injectable side-effecting wrapper (`openRecord`), following the same shape as the existing
`createTodoLauncher.ts` precedent (see that file's header for the shared discipline). It is a
**separate** service, not an extension of `createTodoLauncher`: that launcher owns a create-form
payload and a `window.open` popup lifecycle; this one owns an EXISTING-record deep link and the
`OpenBrowserWindowApi` lifecycle Spike-2 selected. They share no state (root CLAUDE.md §11
justification).

- Default opener: `Office.context.ui.openBrowserWindow(url)` — never `window.open`, never the
  Dialog API.
- URL shape reuses the existing "Quick Create" precedent (`App.tsx` `onQuickCreate`,
  `main.aspx?etn=...&pagetype=entityrecord`), extended with `id=` to open an EXISTING record —
  matching Spike-2's own Probe 1 URL exactly (`notes/spikes/spike-2-dialog-api.md` Operator
  Verification Recipe). This does not invent a second URL-building shape.
- Unset `ORG_URL` (or an empty record id) is a safe no-op with a stated reason, logged via
  `console.warn` and returned as `{ opened: false, reason }` — never a broken window, mirroring the
  Quick Create precedent exactly.
- Every record GUID is canonicalized via the package's local `cleanGuid` (ADR-044, the
  `todoChoices.ts`/ADR-012 Path A precedent) at the point the URL is built, regardless of whether
  the caller's id was already clean.
- No auth token is read, constructed, or passed anywhere in this file — the opened tab
  authenticates against Dataverse using the browser's own AAD session (or an interactive prompt),
  independent of `@spaarke/auth`'s BFF-scoped token entirely (Spike-2 §b, item 2).

### The click seams

- `RelatedRecordCard`'s existing `onOpenRecord` prop (task 026's click seam) is now filled by
  `SaveFlow`'s `handleOpenRelatedRecord`, gated on the new `canOpenRecord` prop.
- A new Document-record affordance ("Open document record") renders next to
  `DocumentProfileSection` inside `SaveFlow`, calling `handleOpenDocumentRecord` for the
  `sprk_document` record itself — the SAME capability gate, the SAME launcher.

### Capability gating (NFR-10)

`HostCapabilities.canOpenBrowserWindow` (new field, `shared/adapters/types.ts`) is decided at
runtime in both `WordAdapter` and `OutlookAdapter` via
`Office.context.requirements.isSetSupported('OpenBrowserWindowApi', '1.1')` — never declared as a
manifest requirement (that would stop the add-in loading on hosts without it). `SaveView.tsx`
reads it from the live `hostAdapter` and threads it to `SaveFlow` as `canOpenRecord`; there is no
`hostType === 'word'`/`'outlook'` branch anywhere in this task's view code. When the capability is
`false`, the related-record card renders as plain, non-interactive text (its existing, already-built
fallback from task 026) and the Document-record affordance does not render at all — both are
honest fallback surfaces, not errors.

(`outlook/OutlookHostAdapter.ts`, an orphaned/unused adapter referenced only from a doc-comment
example in `HostAdapterFactory.ts` — no production import — also received the same field, purely to
keep `npm run typecheck` at 0 production errors; it is dead code independent of this task, per the
project CLAUDE.md task-010 finding that `HostAdapterFactory` now only constructs the two live
adapters.)

### The return path

Spike-2 §d found that MDA never calls `messageParent`, so every option — including Option 3 —
needs a focus/visibility-triggered re-read rather than a push notification. `SaveFlow` tracks
whether the user has opened any record via `hasOpenedExternalRecordRef` (set on a successful
`openRecord()` call) and, once true, listens for `window` `focus` and `document` `visibilitychange`
events. On either firing (guarded to `document.visibilityState === 'visible'` for the
`visibilitychange` case):

1. `onRetryDocumentIdentity?.()` re-runs task 013's identity resolution, which re-derives the
   related-record card's data through the SAME `useRelatedRecord` derivation it already uses (no
   second, divergent read path) — task 033 already built `retryDocumentIdentity` in `App.tsx`;
   this task reuses it exactly as the correction instructed.
2. `profileRefreshSignal` increments, which `DocumentProfileSection` observes and calls task 033's
   existing `useDocumentProfile().refetch()` — reused, not re-implemented. `useDocumentProfile.ts`
   itself was **not edited**.

This is an explicit, tested mechanism (`SaveFlow.openRecord.test.tsx`'s "focus/visibility return
path" suite), not a "the user will just refresh manually" assumption. It is weaker than a push
notification (it fires on "the user came back," not "the user saved a change") — this is the exact
limitation Spike-2 recorded for every option it compared, not a shortcut specific to this branch.

---

## Consequence for FR-13 (confirmed, not reopened)

As Spike-2 itself flagged: FR-13's "finish editing in the record" premise weakens under Option 3 —
the user leaves Word entirely to edit the full record in a browser tab. This task does not reopen
the deferred "+More fields on create" request (design.md §4.2); it is recorded here, as the spike's
own consequence section anticipated, for the Phase 3 creation-completeness tasks to account for.

---

## Deviations from the POML

- The POML's `<relevant-files>` names `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs`
  as a file this task might modify. No BFF file was touched (this task is entirely client-side — see
  the publish-size statement below) so no contract test was added or needed; ADR-038 only requires a
  contract test for a NEW or MODIFIED endpoint, and none exists here.
- The Office Dialog API branch (the POML's preferred-by-default framing) was **not** built. This is
  not a deviation from Spike-2 — it is Spike-2's own recommendation, restated above.

## Publish-size statement (client-only task)

No file under `src/server/api/Sprk.Bff.Api/` (or `Spaarke.Core`/`Spaarke.Dataverse`) was touched by
this task. Zero-delta: the BFF publish artifact is unaffected by this task's changes. Per root
CLAUDE.md §10 bullet 4 / the POML's publish-size constraint, this is the explicit zero-delta record
in lieu of a `dotnet publish` + `Compress-Archive` measurement, since there is nothing to measure.

## What is UNVERIFIED

The six `<ui-tests>` in the POML all require a live Office host (Word desktop/web with a real
signed-in Dataverse tenant) and are **not** run or claimed as passing by this task:

1. Open the related record from the card
2. Open the Document record from the pane
3. Edits propagate back to the pane
4. Capability gating, not host branching (live-host observation)
5. ADR-021 dark-mode check via the Office theme toggle
6. Keyboard-only operation (NFR-11)

Unit-level coverage exists for the underlying logic (capability computation, launcher URL-building
and canonicalization, the no-op paths, the focus/visibility wiring, and the no-capability render
path) — see `openRecordLauncher.test.ts`, the `WordAdapter.test.ts`/`OutlookAdapter.test.ts`
capability additions, and `SaveFlow.openRecord.test.tsx`. None of that substitutes for the live-host
ui-tests above.
