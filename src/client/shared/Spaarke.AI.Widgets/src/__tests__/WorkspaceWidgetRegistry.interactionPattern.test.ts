/**
 * WorkspaceWidgetRegistry.interactionPattern.test.ts
 *
 * spaarkeai-assistant-enhancements-r3 task 040 (FR-13) — makes the
 * `WidgetAssistantContract.interactionPattern` field (task 022's SHAPE)
 * AUTHORITATIVE + SINGLE-SOURCED:
 *
 *   1. `getWidgetInteractionPattern()` (`WorkspaceWidgetRegistry.ts`) is the
 *      ONE runtime read point for the field — this suite proves it returns
 *      the SAME value the registration declared, for widgets across both
 *      registration files (`register-workspace-widgets.ts` covers `email` +
 *      the overview-only grids/`workspace`; `register-document-viewer-widget.ts`
 *      covers `document-viewer`, task 026's per-item surface).
 *   2. The INVARIANT the field must satisfy against its own widget's
 *      `perItemCards[].landing` values (see `AssistantInteractionPattern`'s
 *      JSDoc in `types/shared.ts`) — this is what makes the field
 *      "authoritative" rather than an independent, driftable guess:
 *        - `'respond'` -> every card (if any) lands `'chat'`.
 *        - `'direct'`  -> no card lands `'chat'`.
 *        - `'hybrid'`  -> at least one card lands `'chat'` AND at least one
 *                         does not.
 *      This is the reconciliation check for D-8 (`document-viewer` was
 *      corrected from task 022's placeholder `'hybrid'` to `'respond'` once
 *      task 026 shipped all three cards landing `'chat'`) and proves the
 *      registration for BOTH per-item widgets (`email`, `document-viewer`)
 *      honestly reflects their real card behavior.
 *   3. No per-widget interaction matrix exists outside this registration
 *      shape (negative case — grep-verified against `src/server` + the rest
 *      of `src/client` in task-040 review; nothing to assert at the unit
 *      level beyond "the ONLY source read is the registry").
 *
 * Loads BOTH registration side-effect modules into a single fresh registry
 * instance per the established `jest.resetModules()` + re-require pattern
 * (see `register-workspace-widgets.test.ts` / `widget-serialize-restore.test.ts`
 * for the module-instance-split rationale this mirrors). No component-lazy-load
 * mocks are needed — this suite only reads registration METADATA
 * (`getWorkspaceWidgetMetadata` / `getWidgetAssistantContract` /
 * `getWidgetInteractionPattern`), never `resolveWorkspaceWidget`, so the
 * dynamic `import()` factories are never invoked.
 */

import React from 'react';
import type * as WorkspaceWidgetRegistryModule from '../registry/WorkspaceWidgetRegistry';

// register-workspace-widgets.ts lazily `import()`s several `@spaarke/ai-outputs`
// output-widget modules inside factory closures — never invoked by this
// metadata-only suite, but Jest still needs `@spaarke/ai-outputs`'s package
// entry to resolve when the *type-only* re-exports in this package's own
// barrel are evaluated. Mirrors the minimal mock set already used by
// `register-workspace-widgets.test.ts` for the same reason.
const MockGenericText: React.FC = () => null;
MockGenericText.displayName = 'MockGenericTextWidget';
jest.mock('../widgets/GenericTextWidget', () => ({
  __esModule: true,
  default: MockGenericText,
}));

let registry: typeof WorkspaceWidgetRegistryModule;

function loadFullRegistry(): void {
  jest.resetModules();
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  registry = require('../registry/WorkspaceWidgetRegistry');
  // Both registration side-effect files run against the SAME fresh module
  // instance `registry` now points to (Node's require cache resolves both
  // requires to the one instance created since the last resetModules()).
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../widgets/workspace/register-workspace-widgets');
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../widgets/workspace/register-document-viewer-widget');
}

beforeEach(() => {
  loadFullRegistry();
});

describe('getWidgetInteractionPattern — single-sourced runtime accessor (task 040, FR-13)', () => {
  it("returns 'hybrid' for 'email', matching getWidgetAssistantContract's value", () => {
    const viaAccessor = registry.getWidgetInteractionPattern('email');
    const viaContract = registry.getWidgetAssistantContract('email')?.interactionPattern;
    expect(viaAccessor).toBe('hybrid');
    expect(viaAccessor).toBe(viaContract);
  });

  it("returns 'respond' for 'document-viewer' (task 040 correction from task 022's placeholder 'hybrid')", () => {
    const viaAccessor = registry.getWidgetInteractionPattern('document-viewer');
    const viaContract = registry.getWidgetAssistantContract('document-viewer')?.interactionPattern;
    expect(viaAccessor).toBe('respond');
    expect(viaAccessor).toBe(viaContract);
  });

  it("returns 'respond' for overview-only widgets ('workspace', 'documents-list')", () => {
    expect(registry.getWidgetInteractionPattern('workspace')).toBe('respond');
    expect(registry.getWidgetInteractionPattern('documents-list')).toBe('respond');
  });

  it('returns undefined for a widget with no declared contract (deterministic default, not invented) — e.g. BudgetDashboard', () => {
    expect(registry.getWidgetInteractionPattern('BudgetDashboard')).toBeUndefined();
  });

  it('returns undefined for an unregistered widget type', () => {
    expect(registry.getWidgetInteractionPattern('not-a-real-widget-type')).toBeUndefined();
  });
});

describe('interactionPattern is consistent with perItemCards landings (authoritative-field invariant, task 040)', () => {
  it('every registered widget with a contract satisfies the respond/direct/hybrid landing rule', () => {
    const checked: string[] = [];
    for (const type of registry.getAllWorkspaceWidgetTypes()) {
      const contract = registry.getWidgetAssistantContract(type);
      if (!contract) continue;
      checked.push(type);

      const landings = contract.perItemCards.map(c => c.landing);
      const chatCount = landings.filter(l => l === 'chat').length;
      const nonChatCount = landings.length - chatCount;

      switch (contract.interactionPattern) {
        case 'respond':
          expect(nonChatCount).toBe(0);
          break;
        case 'direct':
          expect(chatCount).toBe(0);
          expect(nonChatCount).toBeGreaterThan(0);
          break;
        case 'hybrid':
          expect(chatCount).toBeGreaterThan(0);
          expect(nonChatCount).toBeGreaterThan(0);
          break;
        default: {
          const exhaustive: never = contract.interactionPattern;
          throw new Error(`Unreachable interactionPattern value: ${String(exhaustive)}`);
        }
      }
    }
    // Sanity: the invariant loop actually exercised both per-item widgets
    // (email = hybrid, document-viewer = respond-with-cards) plus at least
    // one overview-only (respond, no cards) widget — not a vacuously-true pass.
    expect(checked).toEqual(expect.arrayContaining(['email', 'document-viewer', 'workspace']));
  });

  it("'email' is the hybrid worked example: Reply/Reply All/Forward land 'composer', Summarize lands 'chat'", () => {
    const contract = registry.getWidgetAssistantContract('email')!;
    expect(contract.interactionPattern).toBe('hybrid');
    const byLabel = new Map(contract.perItemCards.map(c => [c.label, c.landing]));
    expect(byLabel.get('Reply')).toBe('composer');
    expect(byLabel.get('Reply All')).toBe('composer');
    expect(byLabel.get('Forward')).toBe('composer');
    expect(byLabel.get('Summarize the thread')).toBe('chat');
  });

  it("'document-viewer' is the respond-with-cards worked example (D-8): all three cards land 'chat'", () => {
    const contract = registry.getWidgetAssistantContract('document-viewer')!;
    expect(contract.interactionPattern).toBe('respond');
    expect(contract.perItemCards.length).toBeGreaterThan(0);
    expect(contract.perItemCards.every(c => c.landing === 'chat')).toBe(true);
  });
});
