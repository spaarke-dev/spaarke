/**
 * ManageWorkspacesPane per-row menu — characterization test (task 052, C-9).
 *
 * The row's three-dot (more) menu moved onto the shared RowActionMenu. This pins its observable contract, which holds
 * on the previous hand-rolled <Menu> too (the suite was run against both):
 *  - six items in order: Pin/Unpin, Set as default, Move up, Move down, Edit, Delete; each with its test id
 *  - a click on the trigger or inside the popover does NOT bubble to the row (which would open the workspace)
 *  - Pin toggles localStorage; Set as default moves to the top; Move up/down reorder; items are disabled exactly as before
 *    (default already first; not pinned; at the ends; Delete on a system layout)
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const mockDispatch = jest.fn();
const mockRefetch = jest.fn();
let mockLayouts: unknown[] = [];

jest.mock('@spaarke/ai-widgets', () => ({
  useAiSession: () => ({ authenticatedFetch: jest.fn(), bffBaseUrl: 'https://bff.test', isAuthenticated: true }),
  useDispatchPaneEvent: () => mockDispatch,
}));

jest.mock('../../../hooks/useWorkspaceLayouts', () => ({
  useWorkspaceLayouts: () => ({ layouts: mockLayouts, isLoading: false, refetch: mockRefetch }),
}));

jest.mock('../../../services/workspaceLayoutMutations', () => ({
  renameWorkspaceLayout: jest.fn(),
  deleteWorkspaceLayout: jest.fn(),
}));

// The pane needs four things from the shared barrel; load the REAL RowActionMenu by deep path (the barrel pulls in
// the whole library) and stub the rest.
jest.mock('@spaarke/ui-components', () => ({
  OOB_MODAL_SIZES: {},
  formatRelativeTime: () => 'just now',
  getXrm: () => null,
  RowActionMenu: jest.requireActual('@spaarke/ui-components/components/RowActionMenu/RowActionMenu').RowActionMenu,
}));

import { ManageWorkspacesPane } from '../ManageWorkspacesPane';
import { getPinnedWorkspaces, pinWorkspace } from '../../../services/pinnedWorkspaces';

const layout = (id: string, name: string, over: Record<string, unknown> = {}) => ({
  id,
  name,
  layoutTemplateId: 't',
  sectionsJson: '[]',
  isDefault: false,
  sortOrder: null,
  isSystem: false,
  modifiedOn: '2026-10-01T10:00:00+00:00',
  ...over,
});

const renderPane = () =>
  render(
    <FluentProvider theme={webLightTheme}>
      <ManageWorkspacesPane open onOpenChange={jest.fn()} />
    </FluentProvider>
  );

const openRowMenu = (id: string) => fireEvent.click(screen.getByTestId(`manage-more-${id}`));

beforeEach(() => {
  window.localStorage.clear();
  mockDispatch.mockClear();
  mockRefetch.mockClear();
  mockLayouts = [layout('a', 'Alpha'), layout('b', 'Bravo'), layout('s', 'System', { isSystem: true })];
});

describe('ManageWorkspacesPane row menu', () => {
  it('lists the six actions in order with their test ids', () => {
    renderPane();
    openRowMenu('a');
    const items = screen.getAllByRole('menuitem');
    expect(items.map(i => i.textContent)).toEqual(['Pin', 'Set as default', 'Move up', 'Move down', 'Edit', 'Delete']);
    expect(items.map(i => i.getAttribute('data-testid'))).toEqual([
      'manage-menu-pin-a',
      'manage-menu-default-a',
      'manage-menu-up-a',
      'manage-menu-down-a',
      'manage-menu-edit-a',
      'manage-menu-delete-a',
    ]);
  });

  it('opening the menu does not bubble to the row (the workspace is not opened)', () => {
    renderPane();
    openRowMenu('a');
    fireEvent.click(screen.getByTestId('manage-menu-edit-a'));
    expect(mockDispatch).not.toHaveBeenCalled();
  });

  it('Pin pins the workspace; the label becomes Unpin and Unpin removes it', () => {
    renderPane();
    openRowMenu('a');
    fireEvent.click(screen.getByTestId('manage-menu-pin-a'));
    expect(getPinnedWorkspaces().map(p => p.layoutId)).toEqual(['a']);
    openRowMenu('a');
    expect(screen.getByTestId('manage-menu-pin-a')).toHaveTextContent('Unpin');
    fireEvent.click(screen.getByTestId('manage-menu-pin-a'));
    expect(getPinnedWorkspaces()).toEqual([]);
  });

  it('an unpinned workspace cannot move up or down', () => {
    renderPane();
    openRowMenu('a');
    expect(screen.getByTestId('manage-menu-up-a')).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByTestId('manage-menu-down-a')).toHaveAttribute('aria-disabled', 'true');
  });

  it('Move down / Move up reorder the pinned list and respect the ends', () => {
    pinWorkspace('a', 'Alpha');
    pinWorkspace('b', 'Bravo');
    renderPane();
    openRowMenu('a');
    expect(screen.getByTestId('manage-menu-default-a')).toHaveAttribute('aria-disabled', 'true'); // already default
    expect(screen.getByTestId('manage-menu-up-a')).toHaveAttribute('aria-disabled', 'true'); // at the top
    fireEvent.click(screen.getByTestId('manage-menu-down-a'));
    expect(getPinnedWorkspaces().map(p => p.layoutId)).toEqual(['b', 'a']);
    openRowMenu('a');
    expect(screen.getByTestId('manage-menu-down-a')).toHaveAttribute('aria-disabled', 'true'); // now at the bottom
    fireEvent.click(screen.getByTestId('manage-menu-up-a'));
    expect(getPinnedWorkspaces().map(p => p.layoutId)).toEqual(['a', 'b']);
  });

  it('Set as default moves the workspace to the top of the pinned list', () => {
    pinWorkspace('a', 'Alpha');
    pinWorkspace('b', 'Bravo');
    renderPane();
    openRowMenu('b');
    fireEvent.click(screen.getByTestId('manage-menu-default-b'));
    expect(getPinnedWorkspaces().map(p => p.layoutId)).toEqual(['b', 'a']);
  });

  it('Delete is disabled on a system layout and a disabled item does nothing', () => {
    renderPane();
    openRowMenu('s');
    const del = screen.getByTestId('manage-menu-delete-s');
    expect(del).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(del);
    expect(screen.queryByText(/Delete workspace/i)).toBeNull();
  });

  it('Delete on an editable layout is enabled', () => {
    renderPane();
    openRowMenu('a');
    // (the old hand-rolled item rendered aria-disabled="false", RowActionMenu omits the attribute: both mean enabled)
    expect(screen.getByTestId('manage-menu-delete-a').getAttribute('aria-disabled')).not.toBe('true');
  });

  it('the trigger is labelled for the row', () => {
    renderPane();
    const row = screen.getByTestId('manage-workspaces-row-a');
    expect(within(row).getByRole('button', { name: 'Actions for Alpha' })).toBeInTheDocument();
  });
});
