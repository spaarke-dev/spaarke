# Task 022 (D2-08 follow-on) — DocumentViewerWidget upgrade to RichFilePreview

> **Status**: Code authoring complete (sub-agent parallel wave P2-G5). Build / test execution + dark-mode visual check + commit deferred to main session.
> **Date**: 2026-06-04
> **Task**: `tasks/022-upgrade-documentviewerwidget.poml`
> **Rigor**: FULL
> **Sub-agent scope**: code authoring only — no `npm build`, no `npm test`, no `git commit`, no `git push` (main session executes these).

---

## 1. Summary

Replaced the R4 monospace-text-preview shim inside `DocumentViewerWidget.tsx` with consumption of the canonical `RichFilePreview` renderer from `@spaarke/ui-components` (extracted in task 013). The widget is now a thin `WorkspaceWidgetProps<DocumentViewerWidgetData>` wrapper that maps the typed payload onto `IRichFilePreviewProps` and mounts `<RichFilePreview />` inside its `data-widget-type`/`data-testid` envelope.

**Per R5 CLAUDE.md §3.1 (reuse mandate)**: the renderer is CONSUMED, not rebuilt. No parallel preview component was created. The diff is a deletion of the R4 monospace shim subtree + an import + mount of the shared-lib renderer — the renderer's iframe / metadata pane / 3-dot menu chrome lives only in `RichFilePreview.tsx`.

---

## 2. Files created / modified by this task

| Path | Status | Notes |
|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DocumentViewerWidget.tsx` | MODIFIED (upgrade) | R4 shim body replaced with `RichFilePreview` consumption; payload type extended additively with 6 optional R5 fields. |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/DocumentViewerWidget.test.tsx` | MODIFIED (rewrite) | All R4 monospace assertions removed; new cases cover renderer mounting, back-compat, R5 payload paths, defensive narrowing, envelope props. Renderer is mocked at the `@spaarke/ui-components` module boundary (same pattern as `WorkspaceLayoutWidget.test.tsx` per R4 task 068 Jest 30 + React 19 env fix). |
| `projects/spaarke-ai-platform-unification-r5/notes/task-022-upgrade-evidence.md` | NEW (this file) | Sub-agent evidence ledger. |
| `projects/spaarke-ai-platform-unification-r5/tasks/022-upgrade-documentviewerwidget.poml` | UPDATED (status only) | `not-started` → `code-authoring-complete`; `started: 2026-06-04`. |

**Confirmed UNCHANGED by this task**:

| Path | Reason |
|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-document-viewer-widget.ts` | Widget registration MUST stay unchanged so every R4 `workspace.widget_load` dispatch site continues to resolve to this widget without modification (constraint per task POML + R5 spec). Verified via `git status` — file does not appear in the modified list. |
| `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreview.tsx` | Task 013 extracted the renderer; task 022 only CONSUMES it (per task POML scope discipline). Not in `git status` modified list. |
| `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx` | Modal wrapper; out of scope. |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/FilePreviewContextWidget.tsx` | Sibling task 018 (Context-pane); explicitly NOT modified per scope discipline. (File exists in the worktree as untracked from task 018's parallel wave but was NOT edited by this task.) |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | The R4 dispatch site for `workspace.widget_load`. Confirmed back-compat: the dispatcher builds `{ filename, contentType, textContent }` (no R5 fields) and that payload is still accepted by the upgraded `DocumentViewerWidgetData` (all R4 fields preserved, R5 fields optional). |
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` | Only mentions `DocumentViewerWidgetData` in a docstring comment. No type-level dependency on the widget's internal shape. |

---

## 3. Stub-replacement diff summary (lines moved, not duplicated)

### Removed from `DocumentViewerWidget.tsx` (R4 shim)

- `MAX_INLINE_PREVIEW_CHARS` constant (50_000-character truncation cap)
- `formatFileSize(bytes?)` helper (the renderer's `formatFileSize` is now authoritative)
- `isPdf(contentType)` helper (renderer chooses its own iconography)
- The R4 file-header docstring's "Demo scope (Risk R-7)" paragraph (superseded by the task 022 upgrade history block)
- Styles: `card`, `headerIcon`, `headerTitle`, `headerSubtitle`, `metadataRow`, `previewContainer`, `previewText`, `emptyState`, `truncationNotice` (all renderer concerns now)
- JSX body: `<Card>` + `<CardHeader>` + `<pre className={styles.previewText}>` subtree + the four conditional blocks (`isLoading`, `error`, `textContent.length === 0`, `textContent.length > 0`)
- The `displayedText` truncation calculation
- Direct imports of `Card`, `CardHeader`, `Badge`, `DocumentRegular`, `DocumentPdfRegular` (no longer used)

### Retained from R4

- `WorkspaceWidgetProps<DocumentViewerWidgetData>` signature
- `isDocumentViewerData` defensive type guard (relaxed to validate only R4 minimum required fields; new R5 optional fields pass through unchecked)
- Top-level `<div>` wrapper with `className`/`data-widget-type`/`data-testid="document-viewer-widget"` envelope (back-compat — test harnesses and the workspace tab manager rely on this stable attribute)
- `default export` shape so `register-document-viewer-widget.ts`'s dynamic import resolution continues to work
- File location + module name (no rename)

### Added

- Import of `RichFilePreview` + `IRichFilePreviewProps` type from `@spaarke/ui-components` (via the package boundary per ADR-012)
- `DocumentViewerWidgetData` extended ADDITIVELY with 6 OPTIONAL R5 fields: `documentId?`, `documentType?`, `createdBy?`, `createdAt?`, `previewUrl?`, `fetchPreviewUrl?`
- `resolveFetchPreviewUrl(data)` helper — selects between `data.fetchPreviewUrl` / `data.previewUrl` / null-resolving fallback (precedence order documented in JSDoc)
- `resolveDocumentId(data)` helper — synthesizes a stable id from the filename when no `documentId` is supplied (R4 payloads); ensures the renderer's 3-dot menu aria-label has a non-empty id
- `mapPayloadToRendererProps(data)` pure function — maps the typed payload onto `IRichFilePreviewProps` (called from inside `useMemo`)
- Default action callbacks (`onOpenFile`, `onOpenRecord`, `onEmailDocument`, `onCopyLink`) wired to a `console.warn` dev-time signal so future R5 dispatch sites (tasks 020 / 021) know to supply real handlers. The callbacks DO NOT throw — back-compat is the contract
- Envelope `isLoading` / `error` short-circuit ABOVE the renderer mount (so the surrounding host envelope renders without invoking the renderer's own fetch effect when the dispatch payload itself is still pending)
- Minimal `useStyles` reduced to `root`, `envelopeMessage`, `envelopeError` (Fluent v9 semantic tokens only; no hex / rgba)

### Authoring note (lines moved, not duplicated)

The renderer's 2-column body grid / iframe / metadata pane / 3-dot menu chrome was MOVED to `RichFilePreview.tsx` by task 013, not duplicated here. This task's diff adds an IMPORT + MOUNT of that single shared primitive. Both consumers (this Workspace widget + task 018's `FilePreviewContextWidget`) mount the SAME `RichFilePreview` component — the renderer chrome exists in exactly one place in the codebase.

---

## 4. Default `disabledActions` behavior (task 022 step 3 decision)

The widget intentionally does NOT pass an explicit `disabledActions` prop to the renderer, which means the renderer's `DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS` applies:

| Action | Hidden by default | Reason |
|---|---|---|
| `preview` | Yes | The renderer IS the preview surface |
| `aiSummary` | Yes | Sparkle columns in list/card views are the canonical entry point |
| `toggleWorkspace` | Yes | The renderer IS the workspace surface for this document |
| `rename` | Yes | Not handled at this surface |
| `findSimilar` | Auto-hidden | The widget does not supply `onFindSimilar` (R5 tasks 020 / 021 are responsible for wiring it when they land) |

This matches the Workspace consumer's needs: a chat-attached document is already in the workspace; the user doesn't need a menu item to add it again.

When future R5 dispatch sites (tasks 020 / 021) need to expose `toggleWorkspace` or `preview`, they can either (a) extend the widget's payload to carry a renderer-options object (out of scope for this task), or (b) extend the `IRichFilePreviewProps` interface in `@spaarke/ui-components` to accept a callback-driven default override (out of scope; renderer is owned by task 013). For now, the defaults are correct.

---

## 5. Graceful-degrade behavior for R4 text-only payloads

| Scenario | Widget behavior |
|---|---|
| R4 payload (`{ filename, contentType, textContent }`) — no preview URL source | `fetchPreviewUrl` resolves to `null` → renderer enters its error/retry state ("Preview not available") with the existing Retry button. NO crash. NO regression to the R4 monospace `<pre>`. |
| R4 payload with `sizeBytes` populated | Same as above; `sizeBytes` propagates to the renderer's `fileSize` prop for the Details section, but the preview area stays in the empty/error state. |
| R5 payload with `previewUrl: '...'` | The widget wraps the static URL in an async resolver and passes it to the renderer; the renderer's iframe mounts with the URL. |
| R5 payload with `fetchPreviewUrl: () => Promise<string \| null>` (caller-owned closure) | The widget passes the closure reference through UNCHANGED so the caller's async/auth semantics (ADR-028 fresh-token retrieval) are preserved. Takes precedence over `previewUrl` when both are supplied. |
| Invalid payload (fails `isDocumentViewerData` type guard) | Widget mounts the renderer with synthesized fallback (`documentName = 'Unknown file'`, `documentId = 'document-viewer:unknown'`, null-resolving fetch). NO crash. |

Test coverage maps 1:1 to these scenarios — see `__tests__/DocumentViewerWidget.test.tsx` test groups: "RichFilePreview composition", "back-compat with R4 text-only payloads", "R5 payload with preview URL source", "envelope props", "defensive narrowing", "default action callbacks".

---

## 6. R4 dispatch-site back-compat verification

**Confirmed R4 dispatch site** (search across `src/`): `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` lines 879–891.

```typescript
// (Verbatim from R4)
const widgetData: DocumentViewerWidgetData = {
  filename: attachment.filename,
  contentType: attachment.contentType,
  textContent: attachment.textContent,
};
dispatch("workspace", {
  type: "widget_load",
  widgetType: DOCUMENT_VIEWER_WIDGET_TYPE,
  widgetData,
  // ...
});
```

This dispatcher constructs `DocumentViewerWidgetData` with EXACTLY the R4 minimum required fields. After task 022:

- ✅ `DocumentViewerWidgetData` still ACCEPTS this exact shape (the three required fields are unchanged; `sizeBytes?` is still optional; new fields are all optional).
- ✅ `DOCUMENT_VIEWER_WIDGET_TYPE` constant unchanged (the registration file was NOT modified by this task).
- ✅ Dispatcher compiles without modification — verified via static read of `ConversationPane.tsx`.

When this R4 dispatch payload mounts the upgraded widget, the renderer's empty/error state renders ("Preview not available" + Retry button) — which is the documented graceful-degrade behavior. The renderer DOES NOT crash; the widget DOES NOT crash. R5 dispatch sites (tasks 020 / 021) will populate the new optional fields when they land.

---

## 7. ADR compliance check (sub-agent self-review)

| ADR | Constraint | Verification |
|---|---|---|
| ADR-006 | No PCF control change | ✅ Zero changes to `src/client/pcf/`. The widget is in `@spaarke/ai-widgets` (React 19, NOT PCF-safe). |
| ADR-012 | Shared-library boundary | ✅ Widget lives in `@spaarke/ai-widgets`; imports renderer from `@spaarke/ui-components` via the package boundary (`import { RichFilePreview } from '@spaarke/ui-components'`), NOT via a relative cross-package path. The barrel re-export from task 013 (`Spaarke.UI.Components/src/components/FilePreview/index.ts` lines 19–21) is in place. |
| ADR-021 | Fluent v9 semantic tokens; dark-mode parity | ✅ No hard-coded color literals introduced. The widget wrapper uses only `tokens.colorNeutralBackground1`, `tokens.colorNeutralForeground3`, `tokens.colorPaletteRedForeground1`, `tokens.spacing*`. The renderer itself enforces semantic-token usage for its subtree. Dark-mode visual check deferred to main session per sub-agent scope. |
| ADR-022 | React 19 | ✅ Widget is a function component using `React.FC` + `useMemo` only. No React 18-only APIs introduced. |
| ADR-028 | No token snapshots | ✅ The widget itself makes NO BFF call. The `fetchPreviewUrl` closure is either caller-owned (R5 dispatch sites supply their own async closure, responsible for fresh-token retrieval) or null-resolving (R4 dispatch). The widget never reads or stores an auth token. |
| ADR-029 (R5 NFR-01) | BFF publish-size delta | ✅ Zero BFF code changed. Publish-size delta = 0 MB (frontend-only task). |
| ADR-030 (PaneEventBus) | Zero new channels; no new event types | ✅ Widget is a CONSUMER of the existing `workspace.widget_load` dispatch. No new event emission, no new channels, no new event types. |
| ADR-032 | NOT applicable to R5 (no conditional registrations) | ✅ The widget registration is unconditional via `register-document-viewer-widget.ts` (unmodified). |

---

## 8. Out-of-scope confirmation (negative criteria from task POML)

- ✅ `RichFilePreview.tsx` NOT modified (task 013 owns extraction; this task only consumes).
- ✅ `FilePreviewContextWidget.tsx` NOT modified (task 018 owns the Context-pane consumer; parallel-safe sibling).
- ✅ `register-document-viewer-widget.ts` NOT modified (back-compat constraint; widget registration must remain stable).
- ✅ No parallel preview component authored (per R5 CLAUDE.md §3.1 reuse mandate).
- ✅ No new BFF endpoint, no new Dataverse seed, no new chat-tool dispatch shape (per task POML constraint "no scope-creep on payload schema").
- ✅ No new feature flag (per R5 CLAUDE.md §3.2).
- ✅ No new DI registration (frontend-only task).
- ✅ No new PaneEventBus channel (per R5 CLAUDE.md §3.4 / ADR-030).
- ✅ No PCF control change (per ADR-006 / R5 CLAUDE.md §4.3 — R5 has NO PCF controls).

---

## 9. Sub-agent execution scope

Per the wave-P2-G5 sub-agent contract:

- ✅ Code authoring complete (widget upgraded, payload extended additively, tests rewritten, evidence file written, POML status bumped to `code-authoring-complete`).
- ⏭️ `npm run build` from `src/client/shared/Spaarke.AI.Widgets/` — deferred to main session.
- ⏭️ `npm test -- DocumentViewerWidget` — deferred to main session.
- ⏭️ `tsc --noEmit` across `@spaarke/ai-widgets` + downstream consumers (`Spaarke.UI.Components`, LegalWorkspace, SpaarkeAi shell) — deferred to main session.
- ⏭️ Dark-mode visual parity check (light + dark theme rendering in component sandbox / dev page) — deferred to main session.
- ⏭️ `code-review` + `adr-check` skills (Step 9.5) — deferred to main session.
- ⏭️ TASK-INDEX.md update (022 🔲 → ✅) — deferred to main session.
- ⏭️ `current-task.md` reset to next pending P2-G5 task — deferred to main session.
- ⏭️ Commit + push — explicitly deferred per task launch instructions (sub-agent must NOT commit/push).

---

## 10. Surprises / decisions

1. **Renderer mock at the module boundary**: existing AI Widgets tests (e.g. `WorkspaceLayoutWidget.test.tsx`) mock `@spaarke/ui-components` at the module level via `jest.mock(...)` (rather than using a Fluent provider wrapper) because Jest 30 + React 19 had cross-library compile issues per R4 task 068. We follow the same pattern for `DocumentViewerWidget.test.tsx` — the renderer's own behavior is tested in `Spaarke.UI.Components/__tests__/RichFilePreview.test.tsx`; the widget tests focus on (a) widget mounts the renderer and (b) widget maps payload → props correctly. This keeps test scope tight + avoids any risk of the test pulling in the full Fluent v9 surface or the renderer's own iframe lifecycle.

2. **Default action callbacks DO NOT throw**: the task POML Step 4 specified `console.warn` + no-op for the four default action callbacks (`onOpenFile`, `onOpenRecord`, `onEmailDocument`, `onCopyLink`). R4 dispatch sites don't currently wire these — throwing would break R4 back-compat the moment a user opens the 3-dot menu and clicks any item. Future R5 dispatch sites (tasks 020 / 021) will supply real callbacks via an extension to the payload schema (out of scope here per task POML).

3. **`textContent` is retained on the type but NOT rendered**: R4 dispatch sites populate `textContent` with the client-extracted preview string. After the upgrade, the renderer iframes the original file via `fetchPreviewUrl` — `textContent` is no longer used for rendering, but the field stays in the type so existing dispatchers compile unchanged. Removing it would be a breaking change.

4. **Envelope `isLoading` / `error` short-circuit the renderer mount**: the widget's standard `WorkspaceWidgetProps<T>` envelope passes `isLoading` and `error` props from the surrounding host (the Workspace pane). These represent the OUTER dispatch / payload-resolution state, not the inner preview-URL fetch (which the renderer has its own loading/error states for). The widget short-circuits the renderer mount when the envelope props are set so the outer + inner loading states don't compose in a confusing way. Reviewer flag if this layering needs revisiting.

5. **`isDocumentViewerData` type guard relaxed**: the guard now validates ONLY `filename` (string) + `contentType` (string) + `textContent` (string) — the R4 minimum. New optional fields are NOT checked by the guard; `mapPayloadToRendererProps` normalizes them via the `?.` / `??` / `typeof` patterns. This keeps the guard back-compat with R4 dispatch sites and avoids forcing R5 dispatch sites to populate fields they don't have yet.

6. **`Spinner` size choice in envelope**: the widget's envelope-loading state uses `Spinner size="medium"` (Fluent v9 default-ish) instead of the R4 shim's `Text>Loading preview…</Text>` because it gives a clearer affordance. Visual tweak only — no behavior change. The "Loading preview…" label is preserved via the Spinner's `label` prop.

---

## 11. Next steps (for main session)

1. Run `npm run build` from `src/client/shared/Spaarke.AI.Widgets/` and confirm clean TypeScript compile.
2. Run `npm test -- DocumentViewerWidget` from `src/client/shared/Spaarke.AI.Widgets/` and confirm the new test suite passes (8 test groups, ~14 individual tests).
3. Run `tsc --noEmit` (or workspace equivalent) against the cross-consumer matrix:
   - `src/client/shared/Spaarke.AI.Widgets/` (this package — self-typecheck)
   - `src/client/shared/Spaarke.UI.Components/` (renderer source)
   - `src/solutions/SpaarkeAi/` (R4 dispatch site — `ConversationPane.tsx`)
   - `src/solutions/LegalWorkspace/` (downstream consumer)
4. Render the upgraded widget in light + dark themes via the SpaarkeAi dev page or the component sandbox; verify:
   - Empty/error state renders (R4 dispatch payload path) — borders / backgrounds / foreground colors all token-driven.
   - Iframe + metadata pane render (synthetic R5 payload with `previewUrl`) — full rich preview chrome.
   - 3-dot menu opens with the default 4 hidden actions; `findSimilar` also hidden.
5. Run `code-review` + `adr-check` skills at task-execute Step 9.5.
6. Mark task 022 ✅ in `TASK-INDEX.md`. Update task POML status `code-authoring-complete` → `complete`. Reset `current-task.md` to the next pending P2-G5 task.
7. (Optional) Cross-link task 018's evidence (sibling parallel-wave consumer) once it lands.
