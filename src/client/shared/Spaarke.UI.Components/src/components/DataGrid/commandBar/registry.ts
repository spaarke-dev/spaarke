/**
 * commandBar/registry — host-extensible command handler registry.
 *
 * Lets hosts register named handlers for `CommandBarItem.action === 'custom'`
 * (or to override a built-in by re-registering its id). The `<CommandBar />`
 * dispatches by `customHandlerId` lookup against this registry; misses fall
 * back to `DEFAULT_ACTION_HANDLERS`.
 *
 * **Conflict policy** (per OC-34 + FR-DG-08): last-write-wins. Re-registering
 * an id emits `console.warn` so the conflict is visible during development but
 * never throws — hot-reload scenarios + multiple hosts on the same page must
 * remain unblocked.
 *
 * **No React.** This module is pure shared state — a single Map living at module
 * scope. Hosts register handlers once (typically during their bootstrap) and
 * the `<CommandBar />` reads them lazily on each invocation. Safe across React
 * 16 + 18 + SSR.
 *
 * @see DefaultHandler
 * @see CommandBar
 */

import type { DefaultHandler } from './defaults';

// ─────────────────────────────────────────────────────────────────────────────
// The single shared registry — Map, not plain object, so we can iterate + clear
// without prototype-key collisions.
// ─────────────────────────────────────────────────────────────────────────────

const commandHandlers = new Map<string, DefaultHandler>();

// ─────────────────────────────────────────────────────────────────────────────
// Public API
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Register a command handler under `id`.
 *
 * If `id` is already registered, the previous handler is replaced and a
 * `console.warn` is emitted with both IDs so the conflict is visible. The new
 * handler wins (last-write-wins per OC-34).
 *
 * @param id      A stable string id — by convention `'verb-noun'` like
 *                `'mark-paid'`, `'send-to-foundry'`. The `<CommandBar />` matches
 *                `CommandBarItem.customHandlerId === id`.
 * @param handler Async function invoked when the user clicks the registered button.
 *
 * @example
 * ```ts
 * registerCommandHandler('mark-paid', async (ctx) => {
 *   await Promise.all(ctx.selectedIds.map(id => updateInvoice(id, { status: 'paid' })));
 *   ctx.refresh();
 * });
 * ```
 */
export function registerCommandHandler(id: string, handler: DefaultHandler): void {
  if (commandHandlers.has(id)) {
    // eslint-disable-next-line no-console
    console.warn(
      `[CommandRegistry] Handler "${id}" already registered. Overwriting (last-write-wins per OC-34).`,
    );
  }
  commandHandlers.set(id, handler);
}

/**
 * Resolve a registered handler by id. Returns `undefined` for unknown ids;
 * callers (the `<CommandBar />`) decide whether to fall back to a default
 * handler or surface a warning.
 */
export function getCommandHandler(id: string): DefaultHandler | undefined {
  return commandHandlers.get(id);
}

/**
 * Remove a registered handler by id. No-op for unknown ids.
 *
 * Primarily useful in tests + hot-module-reload scenarios where the same
 * handler id must be re-registered without triggering the conflict warning.
 *
 * @returns `true` if a handler was actually removed, `false` otherwise.
 */
export function unregisterCommandHandler(id: string): boolean {
  return commandHandlers.delete(id);
}

/**
 * Clear every registered handler. Useful in test teardown to keep the registry
 * isolated between test cases.
 *
 * **Not part of the public host API** — intended for internal test use only.
 */
export function clearCommandHandlers(): void {
  commandHandlers.clear();
}

/**
 * Snapshot of every currently-registered handler id. Stable order is NOT
 * guaranteed (matches Map insertion order, but consumers should not depend on it).
 *
 * Useful for debugging + Storybook overlays — not part of the production hot path.
 */
export function listCommandHandlers(): ReadonlyArray<string> {
  return Array.from(commandHandlers.keys());
}
