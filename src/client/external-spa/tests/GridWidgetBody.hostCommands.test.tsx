/**
 * GridWidgetBody.hostCommands.test — the real `createGridWidgetBody(configId)` forwards its optional `hostCommands`
 * prop to the shared grid wrapper (`<ExternalDataGrid>`), which is where the grid's toolbar slot is.
 *
 * `../src/widgets/ExternalDataGrid` is mocked (the pattern in RecordTemplateSections.test.tsx) with a stand-in that
 * records the props it is mounted with, so the test sees exactly what the body hands the grid. Control: with no
 * `hostCommands` the grid is handed none. The body's other wiring (config id, row-open callbacks) is checked in the
 * same render so a passing forward is not a vacuous one.
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';

vi.mock('@spaarke/ui-components/utils/themeStorage', () => ({
  resolveCodePageTheme: () => null,
  setupCodePageThemeListener: () => () => undefined,
}));
vi.mock('../src/services/gridDataverseClient', () => ({ gridDataverseClient: {} }));

interface SeenProps {
  configId: string;
  hostCommands?: ReadonlyArray<{ id: string; label: string; onInvoke: () => void }>;
  onRecordOpen?: unknown;
}
const seen: SeenProps[] = [];

vi.mock('../src/widgets/ExternalDataGrid', async () => {
  const R = await import('react');
  return {
    ExternalDataGrid: (props: SeenProps) => {
      seen.push(props);
      return R.createElement('div', { 'data-testid': 'fake-grid' });
    },
  };
});

import { createGridWidgetBody } from '../src/widgets/GridWidgetBody';

const CONFIG_ID = '3ff4102c-1092-f111-b8dc-7ced8ddc4a05';

beforeEach(() => {
  seen.length = 0;
});

describe('createGridWidgetBody — hostCommands', () => {
  it('forwards hostCommands to the grid, by identity', () => {
    const Body = createGridWidgetBody(CONFIG_ID);
    const hostCommands = [{ id: 'do-it', label: 'Do it', onInvoke: vi.fn() }];

    render(React.createElement(Body, { title: 'Things', hostCommands }));

    expect(screen.getByTestId('fake-grid')).toBeTruthy();
    const last = seen[seen.length - 1];
    expect(last.configId).toBe(CONFIG_ID);
    expect(last.hostCommands).toBe(hostCommands);
    // The body's own wiring is still there.
    expect(typeof last.onRecordOpen).toBe('function');
  });

  it('control: without hostCommands the grid is handed none', () => {
    const Body = createGridWidgetBody(CONFIG_ID);

    render(React.createElement(Body, { title: 'Things' }));

    expect(seen.length).toBeGreaterThan(0);
    expect(seen.every(p => p.hostCommands === undefined)).toBe(true);
  });
});
