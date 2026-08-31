/**
 * Unit tests for the client surface-launch registry (spaarkeai-assistant-enhancements-r1).
 *
 * The registry is the single source of truth for "which surface does a capability's
 * Binding open." These tests lock in:
 *  - resolveSurfaceLaunch resolves known consumerTypes to the right kind/surface.
 *  - the `workspace-tab` kind (grids/widgets, e.g. list-tasks) is registered and
 *    distinguished from the sessionStorage hand-off kinds (wizard / oob-form) — this
 *    is what lets ConversationPane.handleSurfaceLaunch branch on kind instead of a
 *    hardcoded consumerType literal.
 *  - unknown / empty consumerTypes resolve to undefined (graceful "nothing opens").
 *  - every entry is structurally sound.
 */

import { SURFACE_LAUNCH_REGISTRY, resolveSurfaceLaunch } from '../surfaceLaunchRegistry';

describe('surfaceLaunchRegistry', () => {
  it('resolves list-tasks to a workspace-tab grid surface (event-bus kind)', () => {
    const entry = resolveSurfaceLaunch('list-tasks');
    expect(entry).toBeDefined();
    expect(entry!.kind).toBe('workspace-tab');
    expect(entry!.surface).toBe('my-tasks-list'); // registered widget type
    expect(entry!.title).toBe('My Tasks');
  });

  it('resolves wizard consumerTypes to the wizard kind + web-resource surface', () => {
    expect(resolveSurfaceLaunch('create-matter')).toMatchObject({ kind: 'wizard', surface: 'sprk_creatematterwizard' });
    expect(resolveSurfaceLaunch('create-task')).toMatchObject({ kind: 'wizard', surface: 'sprk_createeventwizard' });
  });

  it('resolves create-todo to an oob-form entity surface', () => {
    expect(resolveSurfaceLaunch('create-todo')).toMatchObject({ kind: 'oob-form', surface: 'sprk_todo' });
  });

  it('resolves nda-review to the compose workspace-tab surface (ai-advanced-capabilities-nda-r1 task 022)', () => {
    const entry = resolveSurfaceLaunch('nda-review');
    expect(entry).toBeDefined();
    expect(entry!.kind).toBe('workspace-tab');
    expect(entry!.surface).toBe('compose');
    expect(entry!.title).toBe('Compose');
  });

  it('resolves daily-briefing to the workspace layout-dispatcher surface (spaarkeai-assistant-enhancements-r4 task 022, FR-06)', () => {
    const entry = resolveSurfaceLaunch('daily-briefing');
    expect(entry).toBeDefined();
    expect(entry!.kind).toBe('workspace-tab');
    expect(entry!.surface).toBe('workspace'); // registered widget type (generic layout dispatcher)
    expect(entry!.title).toBe('Daily Briefing');
    expect(entry!.widgetData).toMatchObject({ layoutName: 'Daily Briefing' });
  });

  it('resolves smart-todo to the workspace layout-dispatcher surface (spaarkeai-assistant-enhancements-r4 task 022, FR-06)', () => {
    const entry = resolveSurfaceLaunch('smart-todo');
    expect(entry).toBeDefined();
    expect(entry!.kind).toBe('workspace-tab');
    expect(entry!.surface).toBe('workspace');
    expect(entry!.title).toBe('Smart To Do');
    expect(entry!.widgetData).toMatchObject({ layoutName: 'Smart To Do List' });
  });

  it('returns undefined for unknown / empty consumerTypes (graceful, no throw)', () => {
    expect(resolveSurfaceLaunch('does-not-exist')).toBeUndefined();
    expect(resolveSurfaceLaunch('')).toBeUndefined();
    expect(resolveSurfaceLaunch(null)).toBeUndefined();
    expect(resolveSurfaceLaunch(undefined)).toBeUndefined();
  });

  it('separates event-bus kinds from sessionStorage hand-off kinds', () => {
    const eventBusKinds = new Set(['workspace-tab', 'layout']);
    const handoffKinds = new Set(['wizard', 'oob-form']);
    for (const [consumerType, entry] of Object.entries(SURFACE_LAUNCH_REGISTRY)) {
      // Every entry is exactly one of the two transport families.
      expect(eventBusKinds.has(entry.kind) || handoffKinds.has(entry.kind)).toBe(true);
      // Structural soundness — a non-empty title + surface id for every entry.
      expect(typeof entry.title).toBe('string');
      expect(entry.title.length).toBeGreaterThan(0);
      expect(typeof entry.surface).toBe('string');
      expect(entry.surface.length).toBeGreaterThan(0);
      expect(consumerType.length).toBeGreaterThan(0);
    }
  });
});
