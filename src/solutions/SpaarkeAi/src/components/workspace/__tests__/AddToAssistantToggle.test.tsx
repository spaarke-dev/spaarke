/**
 * AddToAssistantToggle.test.tsx — R6 task 057 / D-C-09 unit tests.
 *
 * Verifies the five behaviour contracts of the affordance:
 *   1. Renders as a controlled Switch driven by `visibleToAssistant`.
 *   2. Toggling fires `onChange` with the NEW boolean value.
 *   3. Toggling dispatches `workspace.tab_edited` with `editedFields:
 *      ['visibleToAssistant']` per ADR-015 (field NAMES, not values).
 *   4. Disabled state when `tabId` is empty OR `disabled` prop is true.
 *   5. Accessibility: dynamic `aria-label` reflects the next-action verb
 *      ("Add to assistant" when off, "Hide from assistant" when on).
 *
 * @see AddToAssistantToggle.tsx — component under test
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import {
  PaneEventBus,
  PaneEventBusProvider,
  type WorkspacePaneEvent,
} from '@spaarke/ai-widgets';

import { AddToAssistantToggle } from '../AddToAssistantToggle';

// ---------------------------------------------------------------------------
// Helper — render with FluentProvider + PaneEventBus context.
// ---------------------------------------------------------------------------

function renderWithBus(node: React.ReactNode): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>{node}</PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus };
}

describe('AddToAssistantToggle', () => {
  it('shows "Add to assistant" aria-label when visibleToAssistant=false', () => {
    renderWithBus(
      <AddToAssistantToggle
        tabId="tab-1"
        sessionId="session-1"
        visibleToAssistant={false}
        onChange={jest.fn()}
      />,
    );
    expect(
      screen.getByRole('switch', { name: /add to assistant/i }),
    ).toBeInTheDocument();
  });

  it('shows "Hide from assistant" aria-label when visibleToAssistant=true', () => {
    renderWithBus(
      <AddToAssistantToggle
        tabId="tab-1"
        sessionId="session-1"
        visibleToAssistant={true}
        onChange={jest.fn()}
      />,
    );
    expect(
      screen.getByRole('switch', { name: /hide from assistant/i }),
    ).toBeInTheDocument();
  });

  it('fires onChange with NEW value when toggled on', async () => {
    const onChange = jest.fn();
    renderWithBus(
      <AddToAssistantToggle
        tabId="tab-1"
        sessionId="session-1"
        visibleToAssistant={false}
        onChange={onChange}
      />,
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole('switch', { name: /add to assistant/i }));

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it('fires onChange with false when toggled off', async () => {
    const onChange = jest.fn();
    renderWithBus(
      <AddToAssistantToggle
        tabId="tab-1"
        sessionId="session-1"
        visibleToAssistant={true}
        onChange={onChange}
      />,
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole('switch', { name: /hide from assistant/i }));

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(false);
  });

  it('dispatches workspace.tab_edited with editedFields=[visibleToAssistant] only', async () => {
    const events: WorkspacePaneEvent[] = [];
    const { bus } = renderWithBus(
      <AddToAssistantToggle
        tabId="tab-1"
        sessionId="session-1"
        visibleToAssistant={false}
        onChange={jest.fn()}
      />,
    );
    bus.subscribe('workspace', (ev) => events.push(ev));

    const user = userEvent.setup();
    await user.click(screen.getByRole('switch', { name: /add to assistant/i }));

    expect(events).toHaveLength(1);
    expect(events[0]?.type).toBe('tab_edited');
    expect(events[0]?.tabId).toBe('tab-1');
    expect(events[0]?.sessionId).toBe('session-1');
    expect(events[0]?.editedFields).toEqual(['visibleToAssistant']);
    // ADR-015 binding: no field value smuggled onto the event.
    expect(events[0]?.timestamp).toMatch(/^\d{4}-\d{2}-\d{2}T/);
  });

  it('is disabled when tabId is empty', () => {
    renderWithBus(
      <AddToAssistantToggle
        tabId=""
        sessionId="session-1"
        visibleToAssistant={false}
        onChange={jest.fn()}
      />,
    );
    expect(screen.getByRole('switch')).toBeDisabled();
  });

  it('is disabled when disabled prop is true', () => {
    renderWithBus(
      <AddToAssistantToggle
        tabId="tab-1"
        sessionId="session-1"
        visibleToAssistant={false}
        disabled
        onChange={jest.fn()}
      />,
    );
    expect(screen.getByRole('switch')).toBeDisabled();
  });

  it('does NOT dispatch or call onChange when disabled', async () => {
    const events: WorkspacePaneEvent[] = [];
    const onChange = jest.fn();
    const { bus } = renderWithBus(
      <AddToAssistantToggle
        tabId=""
        sessionId="session-1"
        visibleToAssistant={false}
        onChange={onChange}
      />,
    );
    bus.subscribe('workspace', (ev) => events.push(ev));

    const user = userEvent.setup();
    await user.click(screen.getByRole('switch'));

    expect(events).toHaveLength(0);
    expect(onChange).not.toHaveBeenCalled();
  });
});
