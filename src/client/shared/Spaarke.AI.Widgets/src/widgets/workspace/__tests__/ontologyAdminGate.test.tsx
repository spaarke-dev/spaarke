/**
 * ontologyAdminGate (spaarke-ontology-platform-r1 task 104; D-22, FR-56): the "Ontology admin" tab is registered ONLY when the
 * capability probe says the caller can author policies.
 *
 *  - importing the registration module does not register it (every other widget registers at module load; this one must not);
 *  - an administrator's `true` registers it once, single-instance, with an explicit Assistant opt-out, resolving to the real widget;
 *  - a Console User's `false` (or a failure, which the probe client reports as false) leaves it unregistered;
 *  - the probe is asked once per page load; the hook re-renders when it answers; the restore wait is bounded.
 *
 * The probe client itself (`fetchOntologyAdminCapability`: true only for a 2xx saying `canAuthorPolicies: true`) is covered in
 * `@spaarke/ui-components` (`OntologyAdmin.test.tsx`); here it is a stand-in so the gate's decision is what is tested.
 *
 * `@spaarke/ui-components` is the runtime-values stand-in for the same reason `register-workspace-widgets.test.ts` uses it (task
 * 092: re-requiring the real barrel after `jest.resetModules()` runs the heap out).
 */

import React from 'react';
import { act, renderHook } from '@testing-library/react';
import type * as WorkspaceWidgetRegistryModule from '../../../registry/WorkspaceWidgetRegistry';
import type * as GateModule from '../ontologyAdminGate';

const MockGenericText: React.FC = () => null;
jest.mock('../../../widgets/GenericTextWidget', () => ({ __esModule: true, default: MockGenericText }));

const TYPE = 'ontology-admin';
const OPTIONS = { fetch: jest.fn(), baseUrl: 'https://bff.example' };

let registry: typeof WorkspaceWidgetRegistryModule;
let gate: typeof GateModule;
let probe: jest.Mock<Promise<boolean>, [unknown]>;

function load(answer: () => Promise<boolean>): void {
  jest.resetModules();
  probe = jest.fn(answer);
  // The renderer (imported above) and the re-required gate must share one React instance.
  jest.doMock('react', () => React);
  jest.doMock('@spaarke/ui-components', () => ({
    ...require('../../../__mocks__/spaarke-ui-components-runtime'),
    fetchOntologyAdminCapability: probe,
  }));
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  registry = require('../../../registry/WorkspaceWidgetRegistry');
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('../register-workspace-widgets');
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  gate = require('../ontologyAdminGate');
}

describe('ontologyAdminGate', () => {
  it('importing the registrations does NOT register the Ontology admin tab', () => {
    load(async () => true);
    expect(registry.hasWorkspaceWidget(TYPE)).toBe(false);
    expect(registry.getAllWorkspaceWidgetTypes()).not.toContain(TYPE);
    expect(gate.isOntologyAdminTabAvailable()).toBe(false);
    expect(probe).not.toHaveBeenCalled();
  });

  it('the administrator: a true answer registers the tab once, single-instance, with an Assistant opt-out', async () => {
    load(async () => true);

    await expect(gate.startOntologyAdminGate(OPTIONS)).resolves.toBe(true);

    expect(probe).toHaveBeenCalledWith(OPTIONS);
    expect(registry.getAllWorkspaceWidgetTypes().filter(t => t === TYPE)).toEqual([TYPE]);
    const meta = registry.getWorkspaceWidgetMetadata(TYPE)!;
    expect(meta.displayName).toBe('Ontology admin');
    expect(meta.allowMultiple).toBe(false);
    expect((meta.assistantContract as { optOut?: boolean }).optOut).toBe(true);
    expect(gate.isOntologyAdminTabAvailable()).toBe(true);
  });

  it('the registered tab resolves to the real OntologyAdminWorkspaceWidget, not the unknown-type fallback', async () => {
    load(async () => true);
    await gate.startOntologyAdminGate(OPTIONS);

    const resolved = await registry.resolveWorkspaceWidget(TYPE);

    // eslint-disable-next-line @typescript-eslint/no-require-imports
    expect(resolved).toBe(require('../OntologyAdminWorkspaceWidget').OntologyAdminWorkspaceWidget);
    expect(resolved).not.toBe(MockGenericText);
  });

  it('its defaultOrder does not collide with any other registration', async () => {
    load(async () => true);
    await gate.startOntologyAdminGate(OPTIONS);
    const order = registry.getWorkspaceWidgetMetadata(TYPE)!.defaultOrder;
    expect(
      registry.getAllWorkspaceWidgetTypes().filter(t => registry.getWorkspaceWidgetMetadata(t)?.defaultOrder === order)
    ).toEqual([TYPE]);
  });

  it('a Console User: a false answer (also what the probe client returns for a failure) leaves the tab unregistered', async () => {
    load(async () => false);

    await expect(gate.startOntologyAdminGate(OPTIONS)).resolves.toBe(false);

    expect(registry.hasWorkspaceWidget(TYPE)).toBe(false);
    expect(gate.isOntologyAdminTabAvailable()).toBe(false);
  });

  it('asks the probe once per page load, however often it is started', async () => {
    load(async () => true);
    const first = gate.startOntologyAdminGate(OPTIONS);
    const second = gate.startOntologyAdminGate(OPTIONS);
    expect(second).toBe(first);
    await first;
    await gate.startOntologyAdminGate(OPTIONS);
    expect(probe).toHaveBeenCalledTimes(1);
  });

  it("useOntologyAdminTabAvailable re-renders from false to true when the administrator's answer arrives", async () => {
    let answer!: (value: boolean) => void;
    load(() => new Promise<boolean>(resolve => (answer = resolve)));
    const { result } = renderHook(() => gate.useOntologyAdminTabAvailable());
    const started = gate.startOntologyAdminGate(OPTIONS);
    expect(result.current).toBe(false);

    await act(async () => {
      answer(true);
      await started;
    });

    expect(result.current).toBe(true);
  });

  describe('resolveRestorableWorkspaceWidget (the tab-restore resolver)', () => {
    it('skips a persisted admin tab (null) for a Console User, and resolves every other type as before', async () => {
      load(async () => false);
      await gate.startOntologyAdminGate(OPTIONS);

      await expect(gate.resolveRestorableWorkspaceWidget(TYPE)).resolves.toBeNull();
      const other = await gate.resolveRestorableWorkspaceWidget('worklist');
      expect(other).toBe(await registry.resolveWorkspaceWidget('worklist'));
      expect(other).not.toBeNull();
    });

    it('skips it while the probe has not answered (an administrator whose probe outlasted the restore wait)', async () => {
      load(() => new Promise<boolean>(() => undefined));
      void gate.startOntologyAdminGate(OPTIONS);
      await expect(gate.resolveRestorableWorkspaceWidget(TYPE)).resolves.toBeNull();
    });

    it('restores it as the real widget for the administrator', async () => {
      load(async () => true);
      await gate.startOntologyAdminGate(OPTIONS);

      // eslint-disable-next-line @typescript-eslint/no-require-imports
      const real = require('../OntologyAdminWorkspaceWidget').OntologyAdminWorkspaceWidget;
      await expect(gate.resolveRestorableWorkspaceWidget(TYPE)).resolves.toBe(real);
    });
  });

  describe('whenOntologyAdminGateSettled (the tab-restore wait)', () => {
    afterEach(() => jest.useRealTimers());

    it('resolves at once when the gate was never started', async () => {
      load(async () => true);
      await expect(gate.whenOntologyAdminGateSettled(10_000)).resolves.toBeUndefined();
    });

    it('waits for the answer, so a restore after it sees the registered tab', async () => {
      let answer!: (value: boolean) => void;
      load(() => new Promise<boolean>(resolve => (answer = resolve)));
      void gate.startOntologyAdminGate(OPTIONS);
      let settled = false;
      const wait = gate.whenOntologyAdminGateSettled(10_000).then(() => (settled = true));

      await Promise.resolve();
      expect(settled).toBe(false);
      answer(true);
      await wait;

      expect(settled).toBe(true);
      expect(registry.hasWorkspaceWidget(TYPE)).toBe(true);
    });

    it('is bounded: a probe that never answers does not hold the restore', async () => {
      jest.useFakeTimers();
      load(() => new Promise<boolean>(() => undefined));
      void gate.startOntologyAdminGate(OPTIONS);
      const wait = gate.whenOntologyAdminGateSettled(3000);

      jest.advanceTimersByTime(3000);

      await expect(wait).resolves.toBeUndefined();
      expect(registry.hasWorkspaceWidget(TYPE)).toBe(false);
    });
  });
});
