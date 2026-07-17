/**
 * surfaceLaunchRegistry.test.ts — the client launch registry mapping (task 012).
 * Pins the surface-launch-mechanism §3 routing table.
 */
import {
  SURFACE_LAUNCH_REGISTRY,
  EVENT_SUBTYPE_TASK_GUID,
  resolveSurfaceLaunch,
} from '../surfaceHandoff/surfaceLaunchRegistry';

describe('surfaceLaunchRegistry (surface-launch-mechanism §3)', () => {
  it('maps create-matter → custom wizard sprk_creatematterwizard', () => {
    const e = resolveSurfaceLaunch('create-matter');
    expect(e).toBeDefined();
    expect(e!.kind).toBe('wizard');
    expect(e!.surface).toBe('sprk_creatematterwizard');
    expect(e!.preset).toBeUndefined();
  });

  it('maps create-task → sprk_createeventwizard with the Task-subtype preset', () => {
    const e = resolveSurfaceLaunch('create-task');
    expect(e).toBeDefined();
    expect(e!.kind).toBe('wizard');
    expect(e!.surface).toBe('sprk_createeventwizard');
    expect(e!.preset).toEqual({ sprk_eventtype_ref: EVENT_SUBTYPE_TASK_GUID });
    // The exact GUID is a contract with the Dataverse Event subtype row.
    expect(EVENT_SUBTYPE_TASK_GUID).toBe('124f5fc9-98ff-f011-8406-7c1e525abd8b');
  });

  it('maps create-todo → OOB entity form sprk_todo (no custom wizard)', () => {
    const e = resolveSurfaceLaunch('create-todo');
    expect(e).toBeDefined();
    expect(e!.kind).toBe('oob-form');
    expect(e!.surface).toBe('sprk_todo');
  });

  it('returns undefined for an unmapped / empty / nullish consumertype', () => {
    expect(resolveSurfaceLaunch('create-widget')).toBeUndefined();
    expect(resolveSurfaceLaunch('')).toBeUndefined();
    expect(resolveSurfaceLaunch(null)).toBeUndefined();
    expect(resolveSurfaceLaunch(undefined)).toBeUndefined();
  });

  it('registry covers exactly the three R1 create intents', () => {
    expect(Object.keys(SURFACE_LAUNCH_REGISTRY).sort()).toEqual(['create-matter', 'create-task', 'create-todo']);
  });
});
