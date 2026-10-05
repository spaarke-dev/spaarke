/**
 * documentChangeDetectionService.ts
 *
 * spaarkeai-word-add-in-r1 task 094 (owner, 2026-10-04 — "Re-enable on document edits"): bridges
 * `IHostAdapter.registerDocumentChangeHandler` (async registration, resolving to an unsubscribe
 * function) to a synchronous, React-`useEffect`-friendly subscribe/unsubscribe pair, so a caller can
 * write:
 *
 *   useEffect(() => subscribeToDocumentChanges(hostAdapter, onChange), [hostAdapter, onChange]);
 *
 * without juggling the registration promise itself. Capability-gated (NFR-10) — never a `hostType`
 * check: a host whose `canDetectDocumentChanges` is `false` (Outlook; a Word host below WordApi 1.6)
 * gets a no-op unsubscribe and `onChange` is never invoked. A registration failure (the adapter
 * rejects for some reason other than the capability gate already ruled out) is logged and swallowed —
 * per the owner's binding rule, detection is a bonus the Save button's "never block a save" promise
 * does not depend on.
 */
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';

/**
 * Subscribe to content-change notifications on the open document. Returns an unsubscribe function
 * safe to call at any time — including before the underlying async registration has resolved, in
 * which case the registration is unwound as soon as it lands rather than left dangling.
 */
export function subscribeToDocumentChanges(
  adapter: Pick<IHostAdapter, 'getCapabilities' | 'registerDocumentChangeHandler'>,
  onChange: () => void
): () => void {
  if (!adapter.getCapabilities().canDetectDocumentChanges) {
    return () => undefined;
  }

  let cancelled = false;
  let unsubscribe: (() => void) | null = null;

  adapter
    .registerDocumentChangeHandler(onChange)
    .then(fn => {
      if (cancelled) {
        fn();
      } else {
        unsubscribe = fn;
      }
    })
    .catch(error => {
      console.warn('[Spaarke] Could not register document change detection', error);
    });

  return () => {
    cancelled = true;
    unsubscribe?.();
  };
}
