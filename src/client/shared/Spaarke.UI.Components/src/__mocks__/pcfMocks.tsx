/**
 * PCF Mock Utilities for Unit Tests
 * Provides mock implementations of PCF framework types
 */

import React from 'react';
import { render, RenderOptions } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

export const createMockWebAPI = (): any => ({
  createRecord: jest.fn().mockResolvedValue({ id: "new-record-id" }),
  updateRecord: jest.fn().mockResolvedValue(undefined),
  deleteRecord: jest.fn().mockResolvedValue(undefined),
  retrieveRecord: jest.fn().mockResolvedValue({
    id: "record-id",
    name: "Test Record"
  }),
  retrieveMultipleRecords: jest.fn().mockResolvedValue({
    entities: [
      { id: "1", name: "Record 1" },
      { id: "2", name: "Record 2" }
    ],
    nextLink: null
  }),
  execute: jest.fn().mockResolvedValue({ Success: true })
});

export const createMockNavigation = (): any => ({
  openForm: jest.fn().mockResolvedValue(undefined),
  openUrl: jest.fn(),
  openAlertDialog: jest.fn().mockResolvedValue({ confirmed: true }),
  openConfirmDialog: jest.fn().mockResolvedValue({ confirmed: true }),
  openErrorDialog: jest.fn().mockResolvedValue(undefined)
});

export const createMockContext = (overrides?: any): any => ({
  webAPI: createMockWebAPI(),
  navigation: createMockNavigation(),
  mode: {
    isControlDisabled: false,
    isVisible: true
  },
  parameters: {},
  utils: {
    getEntityMetadata: jest.fn().mockResolvedValue({
      EntitySetName: "accounts",
      LogicalName: "account",
      PrimaryIdAttribute: "accountid",
      PrimaryNameAttribute: "name"
    })
  },
  ...overrides
});

export const createMockRecord = (id: string, entityName = "account"): any => ({
  id,
  entityName,
  getFormattedValue: jest.fn((column: string) => `Formatted ${column}`),
  getValue: jest.fn((column: string) => `Value ${column}`),
  getNamedReference: jest.fn(() => ({
    id: { guid: id },
    name: `Record ${id}`,
    entityType: entityName
  }))
});

export const createMockDataset = (
  recordIds = ["1", "2", "3"],
  entityName = "account"
): ComponentFramework.PropertyTypes.DataSet => {
  const records: any = {};
  recordIds.forEach(id => {
    records[id] = createMockRecord(id, entityName);
  });

  return {
    loading: false,
    error: false,
    errorMessage: "",
    sortedRecordIds: recordIds,
    records,
    columns: [
      {
        name: "name",
        displayName: "Name",
        dataType: "SingleLine.Text",
        alias: "name",
        order: 0,
        visualSizeFactor: 1
      } as any,
      {
        name: "primarycontactid",
        displayName: "Primary Contact",
        dataType: "Lookup.Simple",
        alias: "primarycontactid",
        order: 1,
        visualSizeFactor: 1
      } as any
    ],
    paging: {
      pageSize: 25,
      totalResultCount: recordIds.length,
      hasNextPage: false,
      hasPreviousPage: false,
      loadNextPage: jest.fn(),
      loadPreviousPage: jest.fn(),
      reset: jest.fn(),
      setPageSize: jest.fn()
    } as any,
    sorting: [],
    filtering: {
      clearFilter: jest.fn(),
      getFilter: jest.fn(),
      setFilter: jest.fn()
    } as any,
    linking: {
      addLinkedEntity: jest.fn(),
      getLinkedEntities: jest.fn().mockReturnValue([])
    } as any,
    security: {
      editable: true,
      readable: true,
      secured: false
    } as any,
    addColumn: jest.fn(),
    getTargetEntityType: jest.fn().mockReturnValue(entityName),
    getTitle: jest.fn().mockReturnValue("Test Dataset"),
    getViewId: jest.fn().mockReturnValue("view-id"),
    openDatasetItem: jest.fn(),
    refresh: jest.fn(),
    clearSelectedRecordIds: jest.fn(),
    getSelectedRecordIds: jest.fn().mockReturnValue([]),
    setSelectedRecordIds: jest.fn()
  } as any;
};

export const createMockColumn = (
  name: string,
  displayName: string,
  dataType: string = "SingleLine.Text"
): any => ({
  name,
  displayName,
  dataType,
  alias: name,
  order: 0,
  visualSizeFactor: 1,
  isHidden: false,
  isPrimary: name === "name"
});

export const createMockEntityPrivileges = (
  overrides?: Partial<any>
): any => ({
  canCreate: true,
  canRead: true,
  canWrite: true,
  canDelete: true,
  canAppend: true,
  canAppendTo: true,
  ...overrides
});

export const createMockCommandContext = (overrides?: any): any => ({
  selectedRecords: [],
  entityName: "account",
  webAPI: createMockWebAPI(),
  navigation: createMockNavigation(),
  refresh: jest.fn(),
  emitLastAction: jest.fn(),
  ...overrides
});

/**
 * Render component with Fluent UI provider
 */
export const renderWithProviders = (
  ui: React.ReactElement,
  options?: Omit<RenderOptions, 'wrapper'>
) => {
  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <FluentProvider theme={webLightTheme}>
      {children}
    </FluentProvider>
  );

  return render(ui, { wrapper: Wrapper, ...options });
};
