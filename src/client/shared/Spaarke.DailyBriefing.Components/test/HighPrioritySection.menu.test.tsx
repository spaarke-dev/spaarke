/**
 * HighPrioritySection row menu — characterization test (task 052, C-9).
 *
 * The per-row three-dot menu moved onto the shared RowActionMenu. Its observable contract is pinned here and holds on
 * the previous hand-rolled <Menu> too: the menu exists only when the host wires onOpenRecord and/or onEmailItem; items
 * appear per callback in the order Open record, Email; the trigger is labelled per item; choosing an item calls the
 * host's callback with the right arguments.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { HighPrioritySection } from '../src/components/HighPrioritySection';
import type { HighPriorityItemResult } from '../src/services/briefingService';

const item = (over: Partial<HighPriorityItemResult> = {}): HighPriorityItemResult => ({
  entityType: 'sprk_matter',
  entityId: 'm-1',
  name: 'Acme v. Beta',
  highPriority: true,
  monitor: false,
  kindLabel: 'Matter',
  action: 'Overdue',
  ...over,
});

const renderSection = (props: Partial<React.ComponentProps<typeof HighPrioritySection>> = {}) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <HighPrioritySection items={[item()]} {...props} />
    </FluentProvider>
  );

describe('HighPrioritySection row menu', () => {
  it('has no menu when neither onOpenRecord nor onEmailItem is wired', () => {
    renderSection();
    expect(screen.queryByRole('button', { name: /More actions/ })).toBeNull();
  });

  it('labels the trigger per item, falling back to "item" for an unnamed one', () => {
    renderSection({ onOpenRecord: jest.fn(), items: [item(), item({ entityId: 'm-2', name: '' })] });
    expect(screen.getByRole('button', { name: 'More actions for Acme v. Beta' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'More actions for item' })).toBeInTheDocument();
  });

  it('shows Open record then Email when both are wired, and calls the right callback with the right arguments', () => {
    const onOpenRecord = jest.fn();
    const onEmailItem = jest.fn();
    const it1 = item();
    renderSection({ onOpenRecord, onEmailItem, items: [it1] });
    fireEvent.click(screen.getByRole('button', { name: 'More actions for Acme v. Beta' }));
    expect(screen.getAllByRole('menuitem').map(m => m.textContent)).toEqual(['Open record', 'Email']);

    fireEvent.click(screen.getByRole('menuitem', { name: 'Email' }));
    expect(onEmailItem).toHaveBeenCalledWith(it1);
    expect(onOpenRecord).not.toHaveBeenCalled();
  });

  it('Open record calls onOpenRecord(entityType, entityId)', () => {
    const onOpenRecord = jest.fn();
    renderSection({ onOpenRecord, onEmailItem: jest.fn() });
    fireEvent.click(screen.getByRole('button', { name: 'More actions for Acme v. Beta' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Open record' }));
    expect(onOpenRecord).toHaveBeenCalledWith('sprk_matter', 'm-1');
  });

  it('only the wired action appears', () => {
    renderSection({ onEmailItem: jest.fn() });
    fireEvent.click(screen.getByRole('button', { name: 'More actions for Acme v. Beta' }));
    expect(screen.getAllByRole('menuitem').map(m => m.textContent)).toEqual(['Email']);
  });
});
