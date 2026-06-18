/**
 * SendToWorkspaceButton.test.tsx — R6 task 057 / D-C-08 unit tests.
 *
 * Verifies the four behaviour contracts of the affordance:
 *   1. Click dispatches a `workspace.widget_load` PaneEventBus event carrying
 *      the message content + the resolved widget type + display name.
 *   2. Click invokes the optional `onSent` observer callback with the same
 *      payload.
 *   3. Disabled state when `content` is empty/whitespace.
 *   4. Accessibility: `aria-label="Send to workspace"` is on the button.
 *
 * @see SendToWorkspaceButton.tsx — component under test
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// Import from the `events` subpath (not the barrel) — see test 057
// PinToMatterButton.test.tsx for the rationale (workspace-widget side-effect
// chain pulls `@spaarke/ui-components/components/CreateMatterWizard` →
// `@spaarke/sdap-client` which isn't resolvable from SpaarkeAi). The
// affordance only needs the bus + provider + event-type.
// (R6 Wave C-G3 gap-fill, 2026-06-11.)
import {
  PaneEventBus,
  PaneEventBusProvider,
  type WorkspacePaneEvent,
} from '@spaarke/ai-widgets/events';

import { SendToWorkspaceButton } from '../SendToWorkspaceButton';

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

describe('SendToWorkspaceButton', () => {
  it('renders with aria-label "Send to workspace"', () => {
    renderWithBus(<SendToWorkspaceButton content="Hello world" />);
    const button = screen.getByRole('button', { name: /send to workspace/i });
    expect(button).toBeInTheDocument();
  });

  it('is disabled when content is empty', () => {
    renderWithBus(<SendToWorkspaceButton content="" />);
    const button = screen.getByRole('button', { name: /send to workspace/i });
    expect(button).toBeDisabled();
  });

  it('is disabled when content is whitespace only', () => {
    // JSX attribute strings do NOT interpret backslash escapes — use a JS
    // expression so \n / \t become actual whitespace characters. The component
    // calls content.trim().length === 0 to detect "only whitespace".
    renderWithBus(<SendToWorkspaceButton content={'   \n\t  '} />);
    const button = screen.getByRole('button', { name: /send to workspace/i });
    expect(button).toBeDisabled();
  });

  it('dispatches workspace.widget_load on click with default Summary widget type', async () => {
    const events: WorkspacePaneEvent[] = [];
    const { bus } = renderWithBus(
      <SendToWorkspaceButton content="Hello world" />,
    );
    bus.subscribe('workspace', (ev) => events.push(ev));

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /send to workspace/i }));

    expect(events).toHaveLength(1);
    expect(events[0]?.type).toBe('widget_load');
    expect(events[0]?.widgetType).toBe('Summary');
    expect(events[0]?.displayName).toBe('From Chat');

    // widgetData embeds the user content in `body` (Summary convention).
    const data = events[0]?.widgetData as
      | { kind?: string; body?: string }
      | undefined;
    expect(data?.kind).toBe('Summary');
    expect(data?.body).toBe('Hello world');
  });

  it('uses caller-supplied displayName + widgetType when provided', async () => {
    const events: WorkspacePaneEvent[] = [];
    const { bus } = renderWithBus(
      <SendToWorkspaceButton
        content="row,col\n1,2"
        widgetType="Table"
        displayName="Imported Table"
      />,
    );
    bus.subscribe('workspace', (ev) => events.push(ev));

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /send to workspace/i }));

    expect(events).toHaveLength(1);
    expect(events[0]?.widgetType).toBe('Table');
    expect(events[0]?.displayName).toBe('Imported Table');
  });

  it('invokes onSent callback after dispatch', async () => {
    const onSent = jest.fn();
    renderWithBus(
      <SendToWorkspaceButton content="Hello world" onSent={onSent} />,
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /send to workspace/i }));

    expect(onSent).toHaveBeenCalledTimes(1);
    expect(onSent).toHaveBeenCalledWith({
      content: 'Hello world',
      widgetType: 'Summary',
      displayName: 'From Chat',
    });
  });

  it('does NOT dispatch or call onSent when disabled', async () => {
    const events: WorkspacePaneEvent[] = [];
    const onSent = jest.fn();
    const { bus } = renderWithBus(
      <SendToWorkspaceButton content="" onSent={onSent} />,
    );
    bus.subscribe('workspace', (ev) => events.push(ev));

    const user = userEvent.setup();
    // userEvent.click on a disabled button is a no-op (matches real DOM).
    await user.click(screen.getByRole('button', { name: /send to workspace/i }));

    expect(events).toHaveLength(0);
    expect(onSent).not.toHaveBeenCalled();
  });
});
