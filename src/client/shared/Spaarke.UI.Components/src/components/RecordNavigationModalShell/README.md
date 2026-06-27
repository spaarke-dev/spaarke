# RecordNavigationModalShell

> **Status**: New — smart-todo-r4 task R4-010 (Phase 1 shared-lib hoist)
> **Spec source**: `projects/smart-todo-r4/spec.md` FR-12, FR-13, FR-14
> **Constraints**: ADR-021 (Fluent UI v9) · ADR-012 (Shared component library)

Universal modal shell for cross-record navigation around an embedded record
surface (typically an iframe hosting an OOB MDA form). Renders chrome
(`<` / `>` nav + "N of M" counter + title + action-bar slot) and orchestrates
the cross-frame dirty-check protocol so unsaved-change prompts surface BEFORE
the iframe `src` swaps.

The shell does **not** own the modal envelope (`Dialog` / `DialogSurface`).
Callers wrap it in their own modal surface — either a Fluent v9 `Dialog`
(matter-ui-style preview dialogs) or a Code Page launched via
`Xrm.Navigation.navigateTo` (smart-todo-r4 SmartTodo modal per FR-13 / FR-17).

---

## Quick start

```tsx
import { RecordNavigationModalShell } from '@spaarke/ui-components';

const SmartTodoModal: React.FC<Props> = ({ ids, currentId, onClose, environmentUrl }) => {
  const [iframeWindow, setIframeWindow] = React.useState<Window | null>(null);
  const index = ids.indexOf(currentId);

  const handleNavigate = React.useCallback(async (dir: 'prev' | 'next') => {
    const nextId = ids[dir === 'next' ? index + 1 : index - 1];
    // Re-route the parent to load the next record (iframe src rebuilds).
    setCurrentId(nextId);
  }, [ids, index]);

  const iframeSrc = `${environmentUrl}/main.aspx?pagetype=entityrecord&etn=sprk_todo&id=${currentId}&navbar=off`;

  return (
    <Dialog open onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface>
        <RecordNavigationModalShell
          currentIndex={index}
          navigationTotal={ids.length}
          onNavigate={handleNavigate}
          title="Edit To Do"
          dirtyCheckTargetWindow={iframeWindow}
          actionBar={
            <Button appearance="subtle" icon={<DismissRegular />} onClick={onClose} />
          }
        >
          <iframe
            ref={(node) => setIframeWindow(node?.contentWindow ?? null)}
            src={iframeSrc}
            title={`Record ${index + 1} of ${ids.length}`}
          />
        </RecordNavigationModalShell>
      </DialogSurface>
    </Dialog>
  );
};
```

---

## Props

See `types.ts` for the authoritative TypeScript definition.

| Prop | Type | Default | Notes |
|---|---|---|---|
| `currentIndex` | `number` | — | 0-based position in the navigation set. |
| `navigationTotal` | `number` | — | Total record count. |
| `onNavigate` | `(dir: "prev" \| "next") => void \| Promise<void>` | — | Called on confirmed prev/next; may be async. |
| `title` | `string` | — | Header title (typically current record name). |
| `actionBar` | `React.ReactNode` | — | Optional right-side action slot. |
| `children` | `React.ReactNode` | — | Content area (typically the iframe). |
| `dirtyCheckTargetWindow` | `Window \| null` | `null` | Iframe `contentWindow` to query; `null` skips dirty-check. |
| `dirtyCheckTargetOrigin` | `string` | `"*"` | Outbound `postMessage` target origin. |
| `allowedOrigins` | `string[]` | `["https://*.dynamics.com"]` + `window.location.origin` | Inbound-response origin allow-list. |
| `dirtyCheckTimeout` | `number` | `1000` | ms before timeout fallback (treat as clean). |
| `onDirtyDiscard` | `() => void` | — | Fires when user confirms discard, before `onNavigate`. |
| `className` | `string` | — | Root override class. |

Prev is disabled when `currentIndex === 0`; next is disabled when
`currentIndex === navigationTotal - 1`.

---

## Cross-frame dirty-check protocol (FR-14)

The shell coordinates a request/response handshake with the iframe before
allowing nav to proceed. This lets the iframe-hosted MDA form veto navigation
if it has unsaved changes (per FR-14).

### Message shapes

```ts
// Outbound — parent → iframe
interface IDirtyCheckRequest {
  type: 'request-dirty-check';
  correlationId: string;   // shell-generated, must echo in response
}

// Inbound — iframe → parent
interface IDirtyCheckResponse {
  type: 'dirty-check-result';
  correlationId: string;   // must match the request that triggered it
  dirty: boolean;          // true = has unsaved changes
}
```

Type discriminators are exported as constants for iframe-side authors:

```ts
import { DIRTY_CHECK_REQUEST_TYPE, DIRTY_CHECK_RESULT_TYPE } from '@spaarke/ui-components';
```

### Round-trip flow

1. User clicks `<` or `>` (or the equivalent enabled affordance).
2. Shell calls `window.postMessage({type: 'request-dirty-check', correlationId}, dirtyCheckTargetOrigin)` on `dirtyCheckTargetWindow`.
3. The iframe-side listener (authored separately; see smart-todo-r4 task 041)
   inspects its `formContext.data.entity.getIsDirty()` (or equivalent) and
   responds via `window.parent.postMessage({type: 'dirty-check-result', correlationId, dirty}, '*')`.
4. The shell validates the response's `event.origin` against
   `allowedOrigins` AND validates `correlationId`. Untrusted responses are
   silently dropped.
5. If the response arrives within `dirtyCheckTimeout` ms:
   - `dirty === true` → render Fluent v9 `Dialog`: **"Discard unsaved changes?"**
     - **Discard and continue** → fire `onDirtyDiscard?.()`, then `onNavigate(direction)`.
     - **Cancel** → dialog dismisses, no state change.
   - `dirty === false` → `onNavigate(direction)` invoked immediately.
6. If no response arrives within the timeout, shell treats the iframe as
   clean and invokes `onNavigate(direction)`. This protects against
   iframe-load races and listener-registration gaps.

### Origin allow-list

Inbound `dirty-check-result` messages are validated against `allowedOrigins`
(plus `window.location.origin`, appended at runtime). Untrusted-origin
responses are dropped silently; the timeout fallback then fires (clean).

Default allow-list:
- `https://*.dynamics.com` (matches all customer/MDA origins; the wildcard
  requires at least one subdomain label, so bare `dynamics.com` is rejected)
- `window.location.origin` (auto-appended; covers same-origin Code Page
  embeddings).

Override via the `allowedOrigins` prop when embedding in non-Dataverse
contexts. Patterns support a single leading `*.` wildcard subdomain.

### Iframe-side reference implementation

The iframe-side listener is **NOT** part of this component — it is implemented
separately as a Dataverse JS Web Resource registered on the embedded form.

**Status (2026-06-24, smart-todo-r4 R4-113)**: no current consumer ships a
form-side responder. The original `sprk_todo_dirty_check.js` v1.1.0 script
was deployed to Dataverse but never registered on the To Do form designer's
OnLoad event, so it never ran. Per the "no shims, complete or delete" rule
the script + its test + its bind-instructions doc were removed in R4-113.
The parent-side dirty-check protocol (types + `dirtyCheckTargetWindow` prop)
remains in this shared lib as **available infrastructure** — future consumers
that want true cross-frame dirty-checking can implement a form-side handler
matching the contract below.

Minimal handler shape future consumers should implement:

```ts
// Inside the iframe (e.g. the SmartTodo form-script web resource):
window.addEventListener('message', (ev) => {
  if (ev.data?.type !== 'request-dirty-check') return;
  // Production script validates ev.origin against an allow-list
  // (https://*.dynamics.com + same-origin) BEFORE responding.
  const dirty = Boolean(formContext.data.entity.getIsDirty());
  ev.source?.postMessage(
    { type: 'dirty-check-result', correlationId: ev.data.correlationId, dirty },
    ev.origin   // echo origin — never use "*" for responses
  );
});
```

**Origin allow-list (production script)** — mirrors the parent shell's default:

- `https://*.dynamics.com` — any Spaarke MDA / customer tenant
- `window.location.origin` — same-origin embeds (Code Page → Code Page)

Untrusted-origin requests are silently dropped — the parent shell's timeout
fallback then fires (treats no-response as clean per spec FR-14).

### Opting out of dirty-check

Pass `dirtyCheckTargetWindow={null}` (or omit it) to skip the round-trip
entirely. The shell will invoke `onNavigate` immediately on prev/next click.
Useful for non-form embeds (e.g. read-only document viewers) where unsaved
changes are not possible.

---

## Accessibility (WCAG 2.1 AA)

- `<` / `>` controls are Fluent v9 `Button` elements with `aria-label` and a
  `Tooltip` (`relationship="label"`). They expose keyboard-activation via
  default Fluent button semantics.
- The nav group has `role="group"` + `aria-label="Record navigation"`.
- The counter has `aria-live="polite"` and a verbose `aria-label`
  ("Record N of M") so screen readers announce position changes.
- The discard-confirm dialog uses Fluent v9 `Dialog`, which provides
  focus-trap, ESC dismissal, and announced title/content per WAI-ARIA
  Authoring Practices.
- The shell adds NO inline `style={}` props and uses only semantic tokens
  (`tokens.colorNeutralForeground1`, `tokens.spacingHorizontalM`, …) so
  high-contrast and dark modes inherit automatically.

---

## Performance notes

- Style hook (`useRecordNavigationModalShellStyles`) is module-scoped per
  Fluent v9 conventions — no per-render style recomputation.
- Dirty-check round-trip listener is registered ONLY for the duration of an
  in-flight `request-dirty-check`; on timeout or response, it is removed.
- No persistent global `message` listener — the shell does not interfere
  with caller-installed message handlers outside its own correlation ids.

---

## Test surface

`__tests__/RecordNavigationModalShell.test.tsx` covers:

- Chrome render: header, prev, next, counter, action-bar slot, children slot.
- Disabled boundaries: prev disabled at index 0; next disabled at
  `navigationTotal - 1`.
- Counter format: `"N of M"` (1-based) and the `0 of 0` fallback when total
  is `0`.
- Clean path: when no `dirtyCheckTargetWindow` is supplied,
  `onNavigate("next")` fires immediately.
- Dirty-check round trip: `dirty=true` → discard dialog → cancel aborts
  nav; confirm fires `onDirtyDiscard` and `onNavigate`.
- Origin allow-list: untrusted-origin responses are ignored (treated as no
  response → timeout fallback fires).
- Timeout fallback: when no response arrives within `dirtyCheckTimeout`,
  shell proceeds as clean.

---

## Related

- `RichFilePreview.tsx` / `RichFilePreviewDialog.tsx` — original navigation
  chrome the shell generalizes. Task 011 (smart-todo-r4) will refactor the
  dialog to consume the shell.
- `BUILD-A-NEW-WORKSPACE-WIDGET.md` — companion archetype guidance.
- `code-page-wizard-wrapper.md` — pattern for hosting the shell inside an
  `Xrm.Navigation.navigateTo` Code Page modal (FR-17 launch context).
