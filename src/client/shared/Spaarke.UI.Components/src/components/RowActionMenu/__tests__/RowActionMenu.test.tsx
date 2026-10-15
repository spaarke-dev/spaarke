/**
 * RowActionMenu + DocumentRowMenu — unit tests (task 052, C-9).
 *
 * Two jobs:
 *  1. RowActionMenu's own contract: descriptor-driven items, hidden / disabled / tooltip, dividers only between groups
 *     that each have a visible item, opt-in click propagation stops, a custom trigger, and an item test id.
 *  2. DocumentRowMenu CHARACTERIZATION: DocumentRowMenu was rebuilt on RowActionMenu, so its observable behaviour (12
 *     actions in FR-DOC-01 order, the `disabledActions` hiding, divider placement, the trigger label, the trigger's
 *     stopPropagation) is pinned here. These assertions were also run against the pre-extraction implementation.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { RowActionMenu } from '..';
import type { RowActionDescriptor } from '..';
import { DocumentRowMenu } from '../../DocumentRowMenu';
import type { DocumentRowAction } from '../../DocumentRowMenu';

const themed = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

// Count the MenuDividers rendered in the popover portal (Fluent renders them role="presentation", so not by role).
const dividers = () => Array.from(document.body.querySelectorAll('.fui-MenuDivider'));

const openMenu = (name: RegExp | string) => fireEvent.click(screen.getByRole('button', { name }));

describe('RowActionMenu', () => {
  type K = 'a' | 'b' | 'c' | 'd';
  const groups: RowActionDescriptor<K>[][] = [
    [
      { key: 'a', label: 'Alpha' },
      { key: 'b', label: 'Bravo' },
    ],
    [
      { key: 'c', label: 'Charlie' },
      { key: 'd', label: 'Delta', testId: 'delta-item' },
    ],
  ];

  it('renders one item per descriptor in order, with a single divider between the two groups', () => {
    themed(<RowActionMenu<K> groups={groups} onAction={jest.fn()} ariaLabel="More" />);
    openMenu('More');
    expect(screen.getAllByRole('menuitem').map(i => i.textContent)).toEqual(['Alpha', 'Bravo', 'Charlie', 'Delta']);
    expect(dividers()).toHaveLength(1);
  });

  it('passes the chosen key to onAction', () => {
    const onAction = jest.fn();
    themed(<RowActionMenu<K> groups={groups} onAction={onAction} ariaLabel="More" />);
    openMenu('More');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Charlie' }));
    expect(onAction).toHaveBeenCalledTimes(1);
    expect(onAction).toHaveBeenCalledWith('c');
  });

  it('omits hidden items, and drops the divider when a whole group is hidden (no orphan, no double, no leading divider)', () => {
    const hideGroup2: RowActionDescriptor<K>[][] = [groups[0], groups[1].map(a => ({ ...a, hidden: true }))];
    themed(<RowActionMenu<K> groups={hideGroup2} onAction={jest.fn()} ariaLabel="More" />);
    openMenu('More');
    expect(screen.getAllByRole('menuitem').map(i => i.textContent)).toEqual(['Alpha', 'Bravo']);
    expect(dividers()).toHaveLength(0);
  });

  it('does not render a leading divider when the first group is hidden', () => {
    const hideGroup1: RowActionDescriptor<K>[][] = [groups[0].map(a => ({ ...a, hidden: true })), groups[1]];
    themed(<RowActionMenu<K> groups={hideGroup1} onAction={jest.fn()} ariaLabel="More" />);
    openMenu('More');
    expect(screen.getAllByRole('menuitem')).toHaveLength(2);
    expect(dividers()).toHaveLength(0);
  });

  it('a disabled item is aria-disabled and its click does not call onAction', () => {
    const onAction = jest.fn();
    const g: RowActionDescriptor<K>[][] = [
      [
        { key: 'a', label: 'Alpha', disabled: true },
        { key: 'b', label: 'Bravo' },
      ],
    ];
    themed(<RowActionMenu<K> groups={g} onAction={onAction} ariaLabel="More" />);
    openMenu('More');
    const alpha = screen.getByRole('menuitem', { name: 'Alpha' });
    expect(alpha).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(alpha);
    expect(onAction).not.toHaveBeenCalled();
    // an enabled item has no aria-disabled attribute
    expect(screen.getByRole('menuitem', { name: 'Bravo' })).not.toHaveAttribute('aria-disabled');
  });

  it('puts the testId on the item', () => {
    themed(<RowActionMenu<K> groups={groups} onAction={jest.fn()} ariaLabel="More" />);
    openMenu('More');
    expect(screen.getByTestId('delta-item')).toHaveTextContent('Delta');
  });

  it('default trigger is an icon-only button labelled by ariaLabel; the trigger click does NOT stop propagation unless asked', () => {
    const rowClick = jest.fn();
    themed(
      <div onClick={rowClick}>
        <RowActionMenu<K> groups={groups} onAction={jest.fn()} ariaLabel="More" />
      </div>
    );
    openMenu('More');
    expect(rowClick).toHaveBeenCalledTimes(1);
  });

  it('stopTriggerPropagation keeps the trigger click from reaching the row', () => {
    const rowClick = jest.fn();
    themed(
      <div onClick={rowClick}>
        <RowActionMenu<K> groups={groups} onAction={jest.fn()} ariaLabel="More" stopTriggerPropagation />
      </div>
    );
    openMenu('More');
    expect(rowClick).not.toHaveBeenCalled();
    // the menu still opened
    expect(screen.getAllByRole('menuitem')).toHaveLength(4);
  });

  it('stopPopoverPropagation keeps an item click from reaching the row; by default it does reach it', () => {
    const rowClick = jest.fn();
    const { unmount } = themed(
      <div onClick={rowClick}>
        <RowActionMenu<K>
          groups={groups}
          onAction={jest.fn()}
          ariaLabel="More"
          stopTriggerPropagation
          stopPopoverPropagation
        />
      </div>
    );
    openMenu('More');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Alpha' }));
    expect(rowClick).not.toHaveBeenCalled();
    unmount();

    const rowClick2 = jest.fn();
    themed(
      <div onClick={rowClick2}>
        <RowActionMenu<K> groups={groups} onAction={jest.fn()} ariaLabel="More" stopTriggerPropagation />
      </div>
    );
    openMenu('More');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Alpha' }));
    expect(rowClick2).toHaveBeenCalled();
  });

  it('a custom trigger replaces the default one and still opens the menu', () => {
    themed(
      <RowActionMenu<K>
        groups={groups}
        onAction={jest.fn()}
        trigger={
          <button type="button" data-testid="custom-trigger">
            Pick one
          </button>
        }
      />
    );
    expect(screen.queryByRole('button', { name: /more/i })).toBeNull();
    fireEvent.click(screen.getByTestId('custom-trigger'));
    expect(screen.getAllByRole('menuitem')).toHaveLength(4);
  });
});

describe('DocumentRowMenu (characterization: unchanged by the RowActionMenu extraction)', () => {
  const ALL: DocumentRowAction[] = [
    'preview',
    'aiSummary',
    'openFile',
    'findSimilar',
    'download',
    'copyLink',
    'email',
    'openRecord',
    'toggleWorkspace',
    'pinToTop',
    'rename',
    'delete',
  ];
  const LABELS = [
    'Preview',
    'AI summary',
    'Open file',
    'Find similar',
    'Download',
    'Copy link',
    'Email',
    'Open record',
    'Toggle workspace',
    'Pin to top',
    'Rename',
    'Delete',
  ];
  const doc = { id: 'd1', name: 'Lease.pdf' };

  it('trigger is labelled "More actions for <name>"', () => {
    themed(<DocumentRowMenu document={doc} onAction={jest.fn()} />);
    expect(screen.getByRole('button', { name: 'More actions for Lease.pdf' })).toBeInTheDocument();
  });

  it('renders the 12 actions in FR-DOC-01 order with exactly two dividers', () => {
    themed(<DocumentRowMenu document={doc} onAction={jest.fn()} />);
    openMenu('More actions for Lease.pdf');
    expect(screen.getAllByRole('menuitem').map(i => i.textContent)).toEqual(LABELS);
    expect(dividers()).toHaveLength(2);
  });

  it('dispatches each action key to onAction', () => {
    const onAction = jest.fn();
    themed(<DocumentRowMenu document={doc} onAction={onAction} />);
    openMenu('More actions for Lease.pdf');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Find similar' }));
    expect(onAction).toHaveBeenCalledWith('findSimilar');
  });

  it('disabledActions HIDES the listed actions and only places dividers between groups that still have an item', () => {
    themed(
      <DocumentRowMenu
        document={doc}
        onAction={jest.fn()}
        disabledActions={['download', 'copyLink', 'email', 'openRecord', 'delete']}
      />
    );
    openMenu('More actions for Lease.pdf');
    expect(screen.getAllByRole('menuitem').map(i => i.textContent)).toEqual([
      'Preview',
      'AI summary',
      'Open file',
      'Find similar',
      'Toggle workspace',
      'Pin to top',
      'Rename',
    ]);
    // group B is gone: one divider (A | C), not two
    expect(dividers()).toHaveLength(1);
  });

  it('hiding groups A and B leaves a single group and no divider', () => {
    themed(<DocumentRowMenu document={doc} onAction={jest.fn()} disabledActions={ALL.slice(0, 8)} />);
    openMenu('More actions for Lease.pdf');
    expect(screen.getAllByRole('menuitem').map(i => i.textContent)).toEqual([
      'Toggle workspace',
      'Pin to top',
      'Rename',
      'Delete',
    ]);
    expect(dividers()).toHaveLength(0);
  });

  it("the trigger click stops propagation so a parent row's onClick does not fire", () => {
    const rowClick = jest.fn();
    themed(
      <div onClick={rowClick}>
        <DocumentRowMenu document={doc} onAction={jest.fn()} />
      </div>
    );
    openMenu('More actions for Lease.pdf');
    expect(rowClick).not.toHaveBeenCalled();
  });

  it('applies className to the trigger', () => {
    themed(<DocumentRowMenu document={doc} onAction={jest.fn()} className="my-trigger" />);
    expect(screen.getByRole('button', { name: 'More actions for Lease.pdf' })).toHaveClass('my-trigger');
  });
});
