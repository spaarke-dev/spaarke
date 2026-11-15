/**
 * Shared mounting helpers for the wizard tests in the widget (task 032 fix round): the whole widget (`App`) over a host double, as the
 * widget runs it. Not a test file (no `.test.`), so jest does not run it.
 */
import * as React from 'react';
import { configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App } from '../App';
import { DISCONNECTED, McpHostContext, type McpHostConnection } from '../mcp/mcpHostContext';
import { CONSOLE_BASE, guid, installResponseStandIn } from './laneFixtures';
import type { WizardHost } from './wizardFixtures';

installResponseStandIn();
configure({ asyncUtilTimeout: 4000 });

export const LANE_OPEN_KEY = 'spaarke.worklist.laneOpen';

export const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
export const button = (name: string) => screen.getByRole('button', { name: exact(name) });
export const lane = () => screen.getByTestId('worklist-lane-Decide');
export const lines = () => within(lane()).getAllByTestId('issue-line');
export const lineIds = () => lines().map(l => l.getAttribute('data-signal-id'));
export const rowOf = (n: number) => lines().find(l => l.getAttribute('data-signal-id') === guid('5', n))!;
export const count = () => screen.getByTestId('lane-count-Decide');

export function mount(host: WizardHost, connection: Partial<McpHostConnection> = {}, strict = false) {
  const value: McpHostConnection = {
    ...DISCONNECTED,
    isConnected: true,
    hostContext: { theme: 'light', availableDisplayModes: ['inline', 'fullscreen'] },
    callTool: host.callTool,
    openLink: host.openLink,
    requestDisplayMode: host.requestDisplayMode,
    toolResult: { consoleBaseUrl: CONSOLE_BASE },
    ...connection,
  };
  const tree = (
    <McpHostContext.Provider value={value}>
      <App />
    </McpHostContext.Provider>
  );
  return render(strict ? <React.StrictMode>{tree}</React.StrictMode> : tree);
}

/** Opens the lane's Work Item `n`; resolves when the wizard shows What was found. */
export async function openItem(n: number) {
  await screen.findByTestId('decide-lane');
  fireEvent.click(rowOf(n));
  return screen.findByTestId('step-found');
}

/** Waits for Confirm's Record button to be usable (it is held for the Confirm guard). */
export async function recordButtonReady(name: RegExp = /^(record decision|dismiss and record)$/i) {
  await screen.findByTestId('step-confirm');
  await waitFor(() => expect(screen.getByRole('button', { name })).toBeEnabled());
  return screen.getByRole('button', { name });
}

/** The browser's Enter on the focused element: keydown, then a click when it is an enabled button (what holding Enter repeats). */
export function pressEnter(repeat: boolean) {
  const target = document.activeElement ?? document.body;
  const proceeded = fireEvent.keyDown(target, { key: 'Enter', code: 'Enter', repeat });
  if (proceeded && target instanceof HTMLButtonElement && !target.disabled) fireEvent.click(target);
}
