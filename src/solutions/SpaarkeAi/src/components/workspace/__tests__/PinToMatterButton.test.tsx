/**
 * PinToMatterButton.test.tsx — R6 task 057 / D-C-10 unit tests.
 *
 * Verifies the four behaviour contracts of the affordance:
 *   1. Render in light + dark theme (ADR-021 dark-mode parity).
 *   2. Click invokes the onPin callback with the NEXT toggle state.
 *   3. Disabled when matterId is empty, tabId is empty, or disabled prop set.
 *   4. Dispatches `workspace.tab_edited` with editedFields=['isPinned',
 *      'matterContext'] only (ADR-015 binding: field NAMES, not values).
 *
 * @see PinToMatterButton.tsx — component under test
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
} from '@fluentui/react-components';

// Import from the `events` subpath (not the barrel) so the workspace-widget
// registration side-effects (CreateMatterWizardWidget → ui-components/
// CreateMatterWizard → sdap-client) don't transitively need to resolve in
// the SpaarkeAi Jest workspace. The affordance only needs the bus + provider
// + event-type — all live in the `events` slice. (R6 Wave C-G3 gap-fill.)
import {
  PaneEventBus,
  PaneEventBusProvider,
  type WorkspacePaneEvent,
} from '@spaarke/ai-widgets/events';

import { PinToMatterButton } from '../PinToMatterButton';

// ---------------------------------------------------------------------------
// Helper — render with FluentProvider + PaneEventBus context.
// ---------------------------------------------------------------------------

function renderWithBus(
  node: React.ReactNode,
  theme: typeof webLightTheme = webLightTheme,
): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider bus={bus}>{node}</PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus };
}

describe('PinToMatterButton', () => {
  // -------------------------------------------------------------------------
  // Render + accessibility
  // -------------------------------------------------------------------------

  it('renders with aria-label "Pin to matter" when not pinned', () => {
    renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId="matter-1"
        isPinned={false}
        onPin={jest.fn()}
      />,
    );
    expect(
      screen.getByRole('button', { name: /pin to matter/i }),
    ).toBeInTheDocument();
  });

  it('renders with aria-label "Unpin from matter" when pinned', () => {
    renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId="matter-1"
        isPinned={true}
        onPin={jest.fn()}
      />,
    );
    expect(
      screen.getByRole('button', { name: /unpin from matter/i }),
    ).toBeInTheDocument();
  });

  it('reflects pinned state via aria-pressed', () => {
    renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId="matter-1"
        isPinned={true}
        onPin={jest.fn()}
      />,
    );
    const button = screen.getByRole('button', { name: /unpin from matter/i });
    expect(button).toHaveAttribute('aria-pressed', 'true');
  });

  // -------------------------------------------------------------------------
  // Dark-mode parity (ADR-021)
  // -------------------------------------------------------------------------

  it('renders in dark theme without crashing (ADR-021 token usage)', () => {
    // Smoke test: the component uses Fluent v9 semantic tokens (tokens.* from
    // @fluentui/react-components) per ADR-021. Rendering under webDarkTheme
    // confirms no light-only hex colors are baked in. Token-level visual
    // verification is enforced by Storybook / VRT in CI; this test is the
    // structural floor.
    renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId="matter-1"
        isPinned={false}
        onPin={jest.fn()}
      />,
      webDarkTheme,
    );
    expect(
      screen.getByRole('button', { name: /pin to matter/i }),
    ).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Click handler
  // -------------------------------------------------------------------------

  it('calls onPin with true when clicked from unpinned state', async () => {
    const onPin = jest.fn();
    renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId="matter-1"
        isPinned={false}
        onPin={onPin}
      />,
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /pin to matter/i }));

    expect(onPin).toHaveBeenCalledTimes(1);
    expect(onPin).toHaveBeenCalledWith(true);
  });

  it('calls onPin with false when clicked from pinned state', async () => {
    const onPin = jest.fn();
    renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId="matter-1"
        isPinned={true}
        onPin={onPin}
      />,
    );

    const user = userEvent.setup();
    await user.click(
      screen.getByRole('button', { name: /unpin from matter/i }),
    );

    expect(onPin).toHaveBeenCalledTimes(1);
    expect(onPin).toHaveBeenCalledWith(false);
  });

  // -------------------------------------------------------------------------
  // PaneEventBus dispatch (ADR-015 + ADR-030)
  // -------------------------------------------------------------------------

  it('dispatches workspace.tab_edited with editedFields=[isPinned, matterContext] only', async () => {
    const events: WorkspacePaneEvent[] = [];
    const { bus } = renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId="matter-1"
        isPinned={false}
        onPin={jest.fn()}
      />,
    );
    bus.subscribe('workspace', (ev) => events.push(ev));

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /pin to matter/i }));

    expect(events).toHaveLength(1);
    expect(events[0]?.type).toBe('tab_edited');
    expect(events[0]?.tabId).toBe('tab-1');
    expect(events[0]?.sessionId).toBe('session-1');
    expect(events[0]?.editedFields).toEqual(['isPinned', 'matterContext']);
    // ADR-015: ISO timestamp only; no field values smuggled onto the event.
    expect(events[0]?.timestamp).toMatch(/^\d{4}-\d{2}-\d{2}T/);
  });

  it('does NOT carry matterId value on the dispatched event (ADR-015)', async () => {
    const events: WorkspacePaneEvent[] = [];
    const { bus } = renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId="secret-matter-id-7f3a"
        isPinned={false}
        onPin={jest.fn()}
      />,
    );
    bus.subscribe('workspace', (ev) => events.push(ev));

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /pin to matter/i }));

    expect(events).toHaveLength(1);
    // The matterId is a prop input but MUST NOT appear in the dispatched
    // event payload per ADR-015 (deterministic field NAMES only).
    const serialized = JSON.stringify(events[0]);
    expect(serialized).not.toContain('secret-matter-id-7f3a');
  });

  // -------------------------------------------------------------------------
  // Disabled state
  // -------------------------------------------------------------------------

  it('is disabled when matterId is empty', () => {
    renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId=""
        isPinned={false}
        onPin={jest.fn()}
      />,
    );
    expect(
      screen.getByRole('button', { name: /pin to matter/i }),
    ).toBeDisabled();
  });

  it('is disabled when tabId is empty', () => {
    renderWithBus(
      <PinToMatterButton
        tabId=""
        sessionId="session-1"
        matterId="matter-1"
        isPinned={false}
        onPin={jest.fn()}
      />,
    );
    expect(
      screen.getByRole('button', { name: /pin to matter/i }),
    ).toBeDisabled();
  });

  it('is disabled when disabled prop is true', () => {
    renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId="matter-1"
        isPinned={false}
        disabled
        onPin={jest.fn()}
      />,
    );
    expect(
      screen.getByRole('button', { name: /pin to matter/i }),
    ).toBeDisabled();
  });

  it('does NOT dispatch or call onPin when disabled', async () => {
    const events: WorkspacePaneEvent[] = [];
    const onPin = jest.fn();
    const { bus } = renderWithBus(
      <PinToMatterButton
        tabId="tab-1"
        sessionId="session-1"
        matterId=""
        isPinned={false}
        onPin={onPin}
      />,
    );
    bus.subscribe('workspace', (ev) => events.push(ev));

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /pin to matter/i }));

    expect(events).toHaveLength(0);
    expect(onPin).not.toHaveBeenCalled();
  });
});
