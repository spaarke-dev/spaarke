# Task 033 — EmailBodyView (reading-pane `.eml` render branch)

**Status**: Complete. Build green (`tsc`), jest green (10/10 new tests; 90/90 package-wide). Gates clean (code-review + adr-check, 0 findings).

## What was built

New component folder `@spaarke/communication-components/src/components/EmailBody/**` — the reading-pane BODY sub-view that fills the task-032 `EmailReadingPaneShell` `renderBody(selectedId)` slot (`EmailPaneSlotRenderer`):

- `EmailBody/EmailBodyView.tsx` — the body branch. Given a selected `sprk_communication`:
  1. **`.eml` path** — when the record has an archived `.eml` doc (`emlDocumentId` prop, resolved host-side from the related document flagged `sprk_isemailarchive`), calls `authenticatedFetch('/documents/{emlDocumentId}/eml-render')` (`@spaarke/auth`, ADR-028; relative path → `buildBffApiUrl` owns `/api`) and renders the returned SERVER-sanitized HTML in a **sandboxed iframe** via `srcDoc` with `sandbox=""` — NO `allow-scripts`, NO `allow-same-origin` (NFR-03 defense-in-depth). Inline `cid:` images arrive pre-resolved to `data:` server-side. The `.eml` HTML is NEVER passed to `dangerouslySetInnerHTML`.
  2. **Degradation path** — no `emlDocumentId` OR a non-2xx/failed render ⇒ render `sanitizeEmailHtml(sprk_body)` (client-sanitized, task 001) via `dangerouslySetInnerHTML` + a token-styled "Full history unavailable" note. Archive-less is a NORMAL state — no error banner.
  3. **Loading** — a Fluent `Skeleton` while the render is in flight (header-first paint preserved; the shell already painted the header from the record — NFR-02, the body never blocks it).
  4. **Error** — ONLY for a host record-LOAD failure (`recordLoadError` prop) with a Retry affordance (`onRetryRecord`). An archive-less email is degradation, not error.
- `EmailBody/EmailBodyView.types.ts` — `EmailBodyViewProps` + `AuthenticatedFetchFn`.
- `EmailBody/index.ts` — folder barrel.
- `EmailBody/__tests__/EmailBodyView.test.tsx` — 10 RTL tests incl. the required dark-mode + XSS-negative cases.

## BARREL EXPORT — action required by the orchestrator (post-wave)

Per the P3b guardrail (avoid a 4-way race on `src/components/index.ts`), this task did NOT edit the components barrel. The orchestrator must append, alongside the other Wave-5/P3b barrel additions:

```ts
export * from './EmailBody';
```

## Host wiring (task 040 assembly)

The shell's `renderBody(selectedId)` slot passes only the id. The host (the `EmailWorkspace` assembly / code page that already loads the `sprk_communication` rows + related docs for the card list) resolves the archive doc id + body and supplies them:

```tsx
<EmailReadingPaneShell
  items={cards}
  renderBody={(id) => {
    const rec = recordsById[id];
    return (
      <EmailBodyView
        selectedId={id}
        emlDocumentId={rec?.emlArchiveDocId ?? null}  // related doc flagged sprk_isemailarchive
        body={rec?.sprk_body ?? ''}
        recordLoadError={rec == null && loadFailed}
        onRetryRecord={reloadSelectedRecord}
      />
    );
  }}
  /* ...other slots 034/035/036... */
/>
```

## Deviations / notes for reviewers

1. **Archive-doc-id + `sprk_body` are HOST-resolved props**, not resolved inside the component. Resolution stays in the host that already loads the records (ADR-012 presentational boundary) — this deliberately avoids a second BFF surface / a new Graph call, which would have fired task-033 escalation trigger #1 (spec MUST NOT: no new BFF surface beyond `eml-render`). No escalation needed.
2. **Sandboxed iframe sizing** — a `sandbox=""` iframe cannot be auto-height-sized from the parent (no `allow-same-origin` → can't measure content height) without relaxing the sandbox, which is forbidden (NFR-03). Resolved by filling the pane (`flex: 1 1 auto`, `minHeight: 360px`) with the iframe scrolling internally — a documented deviation, NOT a sandbox relaxation, so escalation trigger #2 did not fire.
3. **Note uses a token-styled `div`, not Fluent `MessageBar`** — `MessageBar`'s `useMessageBarReflow` needs a `ResizeObserver` that jsdom lacks (crashes tests). The token-styled note themes correctly in light + dark (ADR-021, dark-mode test green) and carries `role="note"`.
4. **Did NOT edit** `EmailReadingPaneShell/**` (consumes its exported slot types only), `src/components/index.ts` (barrel — see above), other sub-views, or `src/logic/**` — per the P3b guardrail. No `@spaarke/ui-components` / PCF / BFF source touched.
5. **File paths** — built under `src/components/EmailBody/**` (matching the shipped task-032 layout at `src/components/EmailReadingPaneShell/**`), not the `src/reading-pane/**` paths the POML `<relevant-files>` listed (those were stale relative to what task 032 actually landed).

## Quality gates (Step 9.5)

- `code-review`: Clean — 0 Critical / 0 Warning / 1 Suggestion (accepted, documented: iframe internal scroll). 0 AI code smells.
- `adr-check`: Clean — 0 Violations / 0 Warnings across ADR-021 (Fluent v9/dark, no hardcoded colors), ADR-028 (auth via `authenticatedFetch`, relative URL), ADR-022/NFR-05 (no `as React.ComponentType` cast), ADR-012 (presentational, no `Xrm`/`WebApi`).

## Acceptance criteria — verified

- ✅ `.eml` archive → full chain + inline images in an iframe with `sandbox=""` (no `allow-scripts`/`allow-same-origin`).
- ✅ Archive-less → client-sanitized `sprk_body` + "full history unavailable" note, NO error.
- ✅ Negative (XSS, `sprk_body`): `<script>`/`onerror=`/`javascript:` neutralized by `sanitizeEmailHtml`; no script executes.
- ✅ Negative (XSS, `.eml`): iframe never carries `allow-scripts`/`allow-same-origin`.
- ✅ Negative (fetch failure): non-2xx / throw / `!res.ok` → degrades to `sprk_body`, no crash.
- ✅ Header-first: body shows a skeleton while the render is in flight (does not block the header).
- ✅ Dark mode chrome (ADR-021); no `as React.ComponentType` cast (NFR-05); build + typecheck green.
