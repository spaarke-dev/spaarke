/**
 * worklistSectionRegistration.test.tsx - spaarke-ontology-platform-r1 task 156.
 *
 * Pins the contract of LegalWorkspace's `worklist.registration.ts` section shim, the thing that makes the Worklist reachable from the
 * Console's workspace dropdown (the `sprk_workspacelayout` "Worklist" row mounts the `worklist` section by id):
 *   1. The registration is `worklist` / "Worklist" and its factory yields a content section.
 *   2. The section renders the shared `WorklistWidget` with a service built by `createWorklistService` from LegalWorkspace's
 *      `authenticatedFetch` and `getBffBaseUrl()` (D-15), and with `launchDecisionWizard` as the wizard launcher (D-26: in-app host).
 *   3. The widget's `lookup` slot (the wizard's Person / Lookup search - without it Revise budget / Reassign / Add To Do / Assign Work
 *      cannot be completed) and `aggregateRow` slot (task 054's card, opening the Email Review tab on the pane bus) are filled as the
 *      direct widget fills them; with no Xrm `lookup` is undefined and the section still renders.
 *   4. Negative: the shim never reads `sprk_signal` / `sprk_decisionrecord` (D-15), statically and at runtime. Other Dataverse reads
 *      (the lookup's contacts / budgets / organizations) are allowed.
 *
 * This test lives in this package because LegalWorkspace has no Jest setup and this package already imports its sources
 * (see `legalWorkspaceSectionRegistry.test.ts`, which also covers "the registry includes `worklist`").
 *
 * ADR-038 class: MAINTAIN - the section id / wiring is the contract a deployed layout row depends on.
 */

import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render } from '@testing-library/react';

const mockDispatch = jest.fn();
const mockLookup = jest.fn();
const mockOpenLookup = jest.fn();
const mockOpenRecord = jest.fn();

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    WorklistWidget: jest.fn(() => null),
    createWorklistService: jest.fn(() => ({ marker: 'worklist-service' })),
    launchDecisionWizard: jest.fn(),
  };
});
jest.mock('@spaarke/ai-widgets', () => ({
  createWorklistLookup: jest.fn(() => mockLookup),
  createWorklistOpenLookup: jest.fn(() => mockOpenLookup),
  createWorklistOpenRecord: jest.fn(() => mockOpenRecord),
  useOptionalDispatchPaneEvent: () => mockDispatch,
}));
// `virtual`: this package does not depend on communication-components by name (the shim resolves it from LegalWorkspace's node_modules).
jest.mock('@spaarke/communication-components', () => ({ ReconciliationAggregateCard: jest.fn(() => null) }), {
  virtual: true,
});
jest.mock('../../../../solutions/LegalWorkspace/src/services/authInit', () => ({
  authenticatedFetch: jest.fn(),
}));
jest.mock('../../../../solutions/LegalWorkspace/src/config/runtimeConfig', () => ({
  getBffBaseUrl: jest.fn(() => 'https://bff.example.test'),
}));

import { WorklistWidget, createWorklistService, launchDecisionWizard } from '@spaarke/ui-components';
import type { SectionFactoryContext } from '@spaarke/ui-components';
import { createWorklistLookup, createWorklistOpenLookup, createWorklistOpenRecord } from '@spaarke/ai-widgets';
import { worklistRegistration } from '../../../../solutions/LegalWorkspace/src/sections/worklist.registration';
import { authenticatedFetch } from '../../../../solutions/LegalWorkspace/src/services/authInit';

const SHIM_PATH = path.resolve(__dirname, '../../../../solutions/LegalWorkspace/src/sections/worklist.registration.ts');

const widgetMock = WorklistWidget as unknown as jest.Mock;
const serviceMock = createWorklistService as unknown as jest.Mock;
const lookupFactoryMock = createWorklistLookup as unknown as jest.Mock;
const openLookupFactoryMock = createWorklistOpenLookup as unknown as jest.Mock;
const openRecordFactoryMock = createWorklistOpenRecord as unknown as jest.Mock;

/** The shim's code with block and line comments removed, so prose in the docblock cannot satisfy or fail a code check. */
const shimCode = (): string =>
  fs
    .readFileSync(SHIM_PATH, 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/^\s*\/\/.*$/gm, '');

const mountSection = () => {
  const config = worklistRegistration.factory({} as SectionFactoryContext);
  if (config.type !== 'content') throw new Error('the worklist section must be a content section');
  return render(React.createElement(React.Fragment, null, config.renderContent()));
};

/** Run `body` with `window.Xrm` set to a fake whose WebApi spy is passed in; always cleans up. */
const withXrm = (body: (retrieve: jest.Mock) => void) => {
  const retrieve = jest.fn().mockResolvedValue({ entities: [] });
  const w = window as unknown as { Xrm?: unknown };
  w.Xrm = { WebApi: { retrieveMultipleRecords: retrieve, retrieveRecord: retrieve } };
  try {
    body(retrieve);
  } finally {
    delete w.Xrm;
  }
};

describe('worklist section shim (task 156)', () => {
  beforeEach(() => {
    widgetMock.mockClear();
    serviceMock.mockClear();
    lookupFactoryMock.mockClear();
    openLookupFactoryMock.mockClear();
    openRecordFactoryMock.mockClear();
    mockDispatch.mockClear();
  });

  test('registers the `worklist` section labelled "Worklist"', () => {
    expect(worklistRegistration.id).toBe('worklist');
    expect(worklistRegistration.label).toBe('Worklist');
    const config = worklistRegistration.factory({} as SectionFactoryContext);
    expect(config.id).toBe('worklist');
    expect(config.type).toBe('content');
  });

  test('renders WorklistWidget over a BFF-route service built from authenticatedFetch + getBffBaseUrl()', () => {
    const { getByTestId } = mountSection();

    expect(getByTestId('worklist-section-host')).toBeInTheDocument();
    expect(serviceMock).toHaveBeenCalledTimes(1);
    expect(serviceMock).toHaveBeenCalledWith({ fetch: authenticatedFetch, baseUrl: 'https://bff.example.test' });
    expect(widgetMock).toHaveBeenCalled();
    expect(widgetMock.mock.calls[0][0].service).toEqual({ marker: 'worklist-service' });
  });

  test('opens a line through launchDecisionWizard (the in-app host)', () => {
    mountSection();
    expect(widgetMock.mock.calls[0][0].launchWizard).toBe(launchDecisionWizard);
  });

  test('with Xrm: WorklistWidget gets the wizard lookup and the aggregate row', () => {
    withXrm(retrieve => {
      mountSection();
      const props = widgetMock.mock.calls[0][0];
      expect(lookupFactoryMock).toHaveBeenCalledTimes(1);
      expect(lookupFactoryMock.mock.calls[0][0].retrieveMultipleRecords).toBe(retrieve);
      expect(props.lookup).toBe(mockLookup);
      expect(props.aggregateRow).toBeDefined();
      expect(props.aggregateRow.props.dataverseClient).toBeDefined();
    });
  });

  test('with Xrm: WorklistWidget gets the side-pane lookup and the record modal (task 158, D-182)', () => {
    withXrm(() => {
      mountSection();
      const props = widgetMock.mock.calls[0][0];
      expect(openLookupFactoryMock).toHaveBeenCalledTimes(1);
      expect(openRecordFactoryMock).toHaveBeenCalledTimes(1);
      expect(props.openLookup).toBe(mockOpenLookup);
      expect(props.openRecord).toBe(mockOpenRecord);
    });
  });

  test('with Xrm: the side-pane lookup also gets Xrm.WebApi, to read a picked contact address (task 160, D-179)', () => {
    withXrm(retrieve => {
      mountSection();
      expect(openLookupFactoryMock.mock.calls[0][1]?.retrieveMultipleRecords).toBe(retrieve);
    });
  });

  test('aggregate row opens the Email Review tab through the pane bus', () => {
    withXrm(() => {
      mountSection();
      widgetMock.mock.calls[0][0].aggregateRow.props.onOpen();
      expect(mockDispatch).toHaveBeenCalledWith('workspace', {
        type: 'widget_load',
        widgetType: 'communications-reconciliation',
      });
    });
  });

  test('without Xrm: lookup is undefined and the section still renders', () => {
    const { getByTestId } = mountSection();
    expect(getByTestId('worklist-section-host')).toBeInTheDocument();
    const props = widgetMock.mock.calls[0][0];
    expect(props.lookup).toBeUndefined();
    expect(lookupFactoryMock).not.toHaveBeenCalled();
    // D-182: no Xrm, no side pane and no record modal - the wizard keeps its inline type-ahead and shows the subject as text.
    expect(props.openLookup).toBeUndefined();
    expect(props.openRecord).toBeUndefined();
    expect(openLookupFactoryMock).not.toHaveBeenCalled();
    expect(openRecordFactoryMock).not.toHaveBeenCalled();
    expect(props.aggregateRow.props.dataverseClient).toBeUndefined();
  });

  test('negative: never reads sprk_signal / sprk_decisionrecord (static)', () => {
    expect(shimCode()).not.toMatch(/sprk_signal|sprk_decisionrecord/i);
  });

  test('negative: never reads sprk_signal / sprk_decisionrecord (runtime, Xrm present)', () => {
    withXrm(retrieve => {
      mountSection();
      widgetMock.mock.calls[0][0].aggregateRow.props.onOpen();
      const entities = retrieve.mock.calls.map(call => String(call[0]).toLowerCase());
      expect(entities).not.toContain('sprk_signal');
      expect(entities).not.toContain('sprk_decisionrecord');
    });
  });
});
