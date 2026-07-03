# `Xrm.Navigation.navigateTo` Popup ↔ Opener Result Bridge

> **Last Reviewed**: 2026-07-03
> **Status**: Current
> **Severity**: Medium — silent data loss (opener never sees the wizard's result); no error, just missing follow-up behavior

## When

Any wizard or dialog opened from a Dataverse-hosted client surface via `Xrm.Navigation.navigateTo({ pageType: "webresource", ... }, { target: 2 })` that needs to signal a result (saved id, confirmed flag, changed fields) back to the opener when it closes.

Common cases:
- Wizard saves a new record → opener auto-opens or selects the new record
- Wizard edits a record → opener refreshes its view
- Wizard cancels vs confirms → opener behaves differently

## The trap

`Xrm.Navigation.navigateTo({ ... }, { target: 2 })` **opens a separate window** (via `window.open` under the hood). The two windows share the same origin (both are web resources on the org's `crm.dynamics.com` domain), but their `window` objects are DISTINCT execution contexts.

**`window.*` globals do NOT cross the boundary.** A common (and wrong) pattern:

```typescript
// ❌ Wizard writes to its OWN window on save
(window as any).__dialogResult = { confirmed: true, layoutId: savedId };
```

```typescript
// ❌ Opener awaits navigateTo, reads its OWN window
await xrm.Navigation.navigateTo({ ... }, { target: 2 });
const result = (window as any).__dialogResult;
// result is ALWAYS undefined — different window
```

Symptoms: the wizard saves successfully (BFF returns 200, the record is created), but the opener never fires its follow-up action. No error. No console warning. Just missing behavior.

## The pattern (sessionStorage bridge with age-gated result)

Use `sessionStorage` as a shared per-origin per-tab-set channel. Ship a timestamp with every result so a stale prior result can't be mistaken for a fresh save.

### Shared constants

Both writer (wizard) and reader (opener) need the same storage key. Declare it as a constant in each file OR (better) in a shared module both consume:

```typescript
const WIZARD_RESULT_STORAGE_KEY = "spaarke:{consumer}-wizard:last-result";
const WIZARD_RESULT_MAX_AGE_MS = 60_000; // 60s — anything older is "stale"
```

Naming: `spaarke:{consumer}-wizard:last-result` — namespace by consumer (workspace-layout, matter, event, etc.) so multiple wizards don't collide.

### Writer side (in the wizard's `handleFinish`)

```typescript
// After successful save
try {
  window.sessionStorage?.setItem(
    WIZARD_RESULT_STORAGE_KEY,
    JSON.stringify({
      confirmed: true,
      layoutId: savedId,     // or matterId, or eventId, ...
      // Any other fields the opener needs
      at: Date.now(),         // MANDATORY — used for staleness gate
    })
  );
} catch { /* storage may be disabled — degrade gracefully */ }
```

Do this ONLY on the success path. On cancel, do NOT write — the reader treats "no result" as "user cancelled".

### Reader side (in the opener, after `navigateTo` Promise resolves)

```typescript
function readWizardDialogResult(): WizardDialogResult | null {
  try {
    const raw = window.sessionStorage?.getItem(WIZARD_RESULT_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as WizardDialogResult;
    if (typeof parsed.at === "number" &&
        Date.now() - parsed.at > WIZARD_RESULT_MAX_AGE_MS) {
      return null; // stale — ignore
    }
    return parsed;
  } catch {
    return null;
  }
}

function consumeWizardDialogResult(): void {
  try {
    window.sessionStorage?.removeItem(WIZARD_RESULT_STORAGE_KEY);
  } catch { /* storage may be disabled */ }
}

// In the opener's handler
async function handleOpenWizard() {
  await xrm.Navigation.navigateTo({ ... }, { target: 2 });

  const result = readWizardDialogResult();
  if (result?.confirmed && result.layoutId) {
    // Consume IMMEDIATELY so a subsequent cancel doesn't re-fire
    consumeWizardDialogResult();
    // Now do the follow-up action
    dispatch("workspace", { type: "widget_load", ... });
  }
}
```

## Why age-gating matters

Without `at:` + `MAX_AGE_MS`, this failure mode is real: user saves wizard #1 (writes result), opener reads + consumes correctly. User later opens wizard #2, cancels it. If wizard #1's result is still in sessionStorage (e.g., consume step was skipped for any reason), wizard #2's Promise resolves and the reader mistakes wizard #1's stale result for wizard #2's fresh save. Opener fires follow-up with wrong data.

60 seconds is safe: a user won't successfully complete a wizard AND cancel a follow-on wizard within 60s without the intermediate read consuming the prior result. Adjust downward if your wizards are typically shorter.

## Verification

After implementing, in DevTools:
1. Open wizard, save → check Application → Session Storage → verify the key is present with fresh `at:`
2. Close wizard → the opener's handler reads → verify follow-up fires
3. Check sessionStorage again → key should be gone (consumed)

If the follow-up doesn't fire:
- Check both windows' Origin in DevTools — they must match (both `crm.dynamics.com`).
- Check sessionStorage in BOTH windows — verify the wizard actually wrote it.
- Check the reader's `at:` gate — if it's rejecting as stale, adjust `MAX_AGE_MS` or investigate why the writer's clock is off.

## Alternatives (and why not)

| Approach | Why not |
|---|---|
| `window.__dialogResult` | Does NOT cross window boundary (this is the bug this pattern fixes) |
| `postMessage` | Works but requires wiring a listener before `navigateTo`, then removing after. sessionStorage is simpler for one-shot signal. |
| `localStorage` | Persists across sessions. Overkill for transient signal. Also creates cross-tab noise if user has multiple SpaarkeAi tabs open. |
| `BroadcastChannel` | Modern + clean, but requires listener setup + is not available in all embedded contexts (older WebViews). |
| Direct `window.opener` access | Works within same-origin but blocked when `navigateTo` opens with `noopener` semantics; behavior is version-dependent. |

sessionStorage is the "boring works everywhere" choice for one-shot signals in Dataverse embedded contexts.

## Do NOT

- Store secrets or PII in sessionStorage — it's readable by any script on the origin. Only opaque ids (layoutId, matterId) and non-sensitive flags.
- Forget the `at:` timestamp — without it, stale results cause silent wrong-action bugs.
- Skip the `consume` step — leaving old results in sessionStorage causes cross-invocation contamination.
- Use `window.__dialogResult` even as a "backup" alongside sessionStorage — it's dead code and misleads future readers. Delete it.

## Live examples

- Writer: `src/solutions/WorkspaceLayoutWizard/src/App.tsx:717-731` — `handleFinish` writes to sessionStorage
- Reader (create flow): `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx:571-597` — auto-opens new workspace as tab after wizard save
- Reader (edit flow): `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManagerComponent.tsx:335-397` — refreshes affected tab after wizard save
- Shared constants: both consumer files define `WIZARD_RESULT_STORAGE_KEY = "spaarke:workspace-wizard:last-result"` + `WIZARD_RESULT_MAX_AGE_MS = 60_000`

## Related

- [`.claude/FAILURE-MODES.md#g-11`](../../FAILURE-MODES.md#g-11-xrmnavigationnavigateto-target-2-opens-a-separate-window--cross-window-signaling-requires-sessionstorage-not-window) — the failure-mode entry that led here
- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — surrounding architecture for the workspace wizard flow
