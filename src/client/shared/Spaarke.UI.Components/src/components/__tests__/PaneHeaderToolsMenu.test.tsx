/**
 * PaneHeaderToolsMenu.test.tsx — smoke coverage for the shared pane-header
 * "⋮ tools" trigger + dropdown extracted for C-12 (spaarke-ontology-platform-r1
 * reuse audit, item D5).
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { PaneHeaderMenuTriggerButton, PaneHeaderToolsMenu, type PaneHeaderToolsMenuItem } from '../PaneHeaderToolsMenu';

describe('PaneHeaderMenuTriggerButton', () => {
  it('renders an icon-only ⋮ button with the given aria-label and calls onClick', () => {
    const onClick = jest.fn();
    render(<PaneHeaderMenuTriggerButton ariaLabel="Workspaces" onClick={onClick} testId="ws-trigger" />);

    const button = screen.getByTestId('ws-trigger');
    expect(button).toHaveAttribute('aria-label', 'Workspaces');

    fireEvent.click(button);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('renders a highlight badge when highlight is true, and none otherwise', () => {
    const { rerender } = render(<PaneHeaderMenuTriggerButton ariaLabel="Assistant tools" testId="a-trigger" />);
    expect(screen.queryByTestId('a-trigger-badge')).not.toBeInTheDocument();

    rerender(<PaneHeaderMenuTriggerButton ariaLabel="Assistant tools" highlight testId="a-trigger" />);
    expect(screen.getByTestId('a-trigger-badge')).toBeInTheDocument();
  });
});

describe('PaneHeaderToolsMenu', () => {
  type ToolId = 'alpha' | 'beta';

  const items: readonly PaneHeaderToolsMenuItem<ToolId>[] = [
    { id: 'alpha', label: 'Alpha Tool', testId: 'tool-alpha' },
    { id: 'beta', label: 'Beta Tool', testId: 'tool-beta' },
  ];

  it('opens the popover on trigger click and shows every item', async () => {
    const onSelect = jest.fn();
    render(
      <PaneHeaderToolsMenu
        triggerAriaLabel="Assistant tools"
        groupHeader="Assistant Tools"
        items={items}
        onSelect={onSelect}
        triggerTestId="assistant-trigger"
      />
    );

    fireEvent.click(screen.getByTestId('assistant-trigger'));

    await waitFor(() => expect(screen.getByText('Alpha Tool')).toBeInTheDocument());
    expect(screen.getByText('Beta Tool')).toBeInTheDocument();
    expect(screen.getByText('Assistant Tools')).toBeInTheDocument();
  });

  it('calls onSelect with the item id and closes the popover on selection', async () => {
    const onSelect = jest.fn();
    render(
      <PaneHeaderToolsMenu
        triggerAriaLabel="Context tools"
        items={items}
        onSelect={onSelect}
        triggerTestId="context-trigger"
      />
    );

    fireEvent.click(screen.getByTestId('context-trigger'));
    await waitFor(() => expect(screen.getByTestId('tool-beta')).toBeInTheDocument());

    fireEvent.click(screen.getByTestId('tool-beta'));

    expect(onSelect).toHaveBeenCalledWith('beta');
    await waitFor(() => expect(screen.queryByTestId('tool-beta')).not.toBeInTheDocument());
  });
});
