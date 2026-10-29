/**
 * ManageWorkspacesPane — the gated "Ontology admin" entry (spaarke-ontology-platform-r1 task 104; D-22, FR-56).
 *
 * The entry is listed only when the Ontology admin gate says the tab is available (the BFF capability probe answered, as the
 * caller, that they can author policies, and the tab was registered). The administrator sees it and opening it dispatches the
 * registered type; a Console User (gate false) sees nothing of it. The gate's own decision is covered in `@spaarke/ai-widgets`
 * (`ontologyAdminGate.test.tsx`); here it is a stand-in.
 *
 * Classification (ADR-038 §7): MAINTAIN — the only Console entry point to the Ontology admin tab.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

const mockDispatch = jest.fn();
let mockAdminAvailable = false;

jest.mock('@spaarke/ai-widgets', () => ({
  useAiSession: () => ({ authenticatedFetch: jest.fn(), bffBaseUrl: 'https://bff.test', isAuthenticated: true }),
  useDispatchPaneEvent: () => mockDispatch,
  useOntologyAdminTabAvailable: () => mockAdminAvailable,
  ONTOLOGY_ADMIN_WIDGET_TYPE: 'ontology-admin',
}));

jest.mock('../../../hooks/useWorkspaceLayouts', () => ({
  useWorkspaceLayouts: () => ({
    layouts: [
      {
        id: 'a',
        name: 'Alpha',
        layoutTemplateId: 't',
        sectionsJson: '[]',
        isDefault: false,
        sortOrder: null,
        isSystem: false,
        modifiedOn: '2026-10-01T10:00:00+00:00',
      },
    ],
    isLoading: false,
    refetch: jest.fn(),
  }),
}));

jest.mock('../../../services/workspaceLayoutMutations', () => ({
  renameWorkspaceLayout: jest.fn(),
  deleteWorkspaceLayout: jest.fn(),
}));

jest.mock('@spaarke/ui-components', () => ({
  OOB_MODAL_SIZES: {},
  formatRelativeTime: () => 'just now',
  getXrm: () => null,
  RowActionMenu: jest.requireActual('@spaarke/ui-components/components/RowActionMenu/RowActionMenu').RowActionMenu,
}));

import { ManageWorkspacesPane } from '../ManageWorkspacesPane';

const onOpenChange = jest.fn();

const renderPane = (theme = webLightTheme) =>
  render(
    <FluentProvider theme={theme}>
      <ManageWorkspacesPane open onOpenChange={onOpenChange} />
    </FluentProvider>
  );

beforeEach(() => {
  window.localStorage.clear();
  mockDispatch.mockClear();
  onOpenChange.mockClear();
  mockAdminAvailable = false;
});

describe('ManageWorkspacesPane — Ontology admin entry (task 104)', () => {
  it('the administrator sees "Ontology admin" under Administration, apart from the layouts', () => {
    mockAdminAvailable = true;
    renderPane();

    expect(screen.getByTestId('manage-section-admin-header')).toHaveTextContent('Administration');
    expect(screen.getByTestId('manage-workspaces-row-ontology-admin')).toHaveTextContent('Ontology admin');
    expect(screen.getByTestId('manage-workspaces-list')).not.toContainElement(
      screen.getByTestId('manage-workspaces-row-ontology-admin')
    );
  });

  it('opening it dispatches the registered tab type and closes the drawer', () => {
    mockAdminAvailable = true;
    renderPane();

    fireEvent.click(screen.getByTestId('manage-workspaces-row-ontology-admin'));

    expect(mockDispatch).toHaveBeenCalledWith('workspace', {
      type: 'widget_load',
      widgetType: 'ontology-admin',
      displayName: 'Ontology admin',
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('opens from the keyboard (Enter)', () => {
    mockAdminAvailable = true;
    renderPane();

    fireEvent.keyDown(screen.getByTestId('manage-workspaces-row-ontology-admin'), { key: 'Enter' });

    expect(mockDispatch).toHaveBeenCalledWith('workspace', expect.objectContaining({ widgetType: 'ontology-admin' }));
  });

  it('a Console User (gate false) sees no Administration section and no Ontology admin entry', () => {
    renderPane();

    expect(screen.queryByTestId('manage-section-admin')).toBeNull();
    expect(screen.queryByText('Ontology admin')).toBeNull();
    expect(screen.queryByText('Administration')).toBeNull();
    expect(screen.getByText('Alpha')).toBeInTheDocument();
  });

  it('renders under the dark theme (ADR-021)', () => {
    mockAdminAvailable = true;
    renderPane(webDarkTheme);
    expect(screen.getByTestId('manage-workspaces-row-ontology-admin')).toBeInTheDocument();
  });
});
