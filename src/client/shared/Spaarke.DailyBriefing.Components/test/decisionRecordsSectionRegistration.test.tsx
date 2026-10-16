/**
 * decisionRecordsSectionRegistration.test.tsx - spaarke-ontology-platform-r1 task 045 (D-47).
 *
 * Pins the contract of LegalWorkspace's `decisionRecords.registration.ts` section shim, the thing that makes the Decision Record tab
 * reachable from the Console's workspace dropdown (the `sprk_workspacelayout` "Decision Records" row mounts the `decisionRecords`
 * section by id):
 *   1. The registration is `decisionRecords` / "Decision Records" and its factory yields a content section.
 *   2. The section renders the shared `DecisionRecordsWidget` with a service built by `createWorklistService` from LegalWorkspace's
 *      `authenticatedFetch` and `getBffBaseUrl()` (D-15), and with `launchDecisionWizard` as the wizard launcher (D-26: in-app host).
 *   3. Negative: the shim never reads `sprk_signal` / `sprk_decisionrecord` (D-15), statically and at runtime; it uses no Xrm at all.
 *
 * This test lives in this package for the reason `worklistSectionRegistration.test.tsx` does: LegalWorkspace has no Jest setup.
 *
 * ADR-038 class: MAINTAIN - the section id / wiring is the contract a deployed layout row depends on.
 */

import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render } from '@testing-library/react';

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    DecisionRecordsWidget: jest.fn(() => null),
    createWorklistService: jest.fn(() => ({ marker: 'decision-records-service' })),
    launchDecisionWizard: jest.fn(),
  };
});
jest.mock('../../../../solutions/LegalWorkspace/src/services/authInit', () => ({
  authenticatedFetch: jest.fn(),
}));
jest.mock('../../../../solutions/LegalWorkspace/src/config/runtimeConfig', () => ({
  getBffBaseUrl: jest.fn(() => 'https://bff.example.test'),
}));

import { DecisionRecordsWidget, createWorklistService, launchDecisionWizard } from '@spaarke/ui-components';
import type { SectionFactoryContext } from '@spaarke/ui-components';
import { decisionRecordsRegistration } from '../../../../solutions/LegalWorkspace/src/sections/decisionRecords.registration';
import { authenticatedFetch } from '../../../../solutions/LegalWorkspace/src/services/authInit';

const SHIM_PATH = path.resolve(
  __dirname,
  '../../../../solutions/LegalWorkspace/src/sections/decisionRecords.registration.ts'
);

const widgetMock = DecisionRecordsWidget as unknown as jest.Mock;
const serviceMock = createWorklistService as unknown as jest.Mock;

/** The shim's code with block and line comments removed, so prose in the docblock cannot satisfy or fail a code check. */
const shimCode = (): string =>
  fs
    .readFileSync(SHIM_PATH, 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/^\s*\/\/.*$/gm, '');

const mountSection = () => {
  const config = decisionRecordsRegistration.factory({} as SectionFactoryContext);
  if (config.type !== 'content') throw new Error('the decisionRecords section must be a content section');
  return render(React.createElement(React.Fragment, null, config.renderContent()));
};

describe('decision records section shim (task 045)', () => {
  beforeEach(() => {
    widgetMock.mockClear();
    serviceMock.mockClear();
  });

  test('registers the `decisionRecords` section labelled "Decision Records"', () => {
    expect(decisionRecordsRegistration.id).toBe('decisionRecords');
    expect(decisionRecordsRegistration.label).toBe('Decision Records');
    const config = decisionRecordsRegistration.factory({} as SectionFactoryContext);
    expect(config.id).toBe('decisionRecords');
    expect(config.type).toBe('content');
  });

  test('renders DecisionRecordsWidget over a BFF-route service built from authenticatedFetch + getBffBaseUrl()', () => {
    const { getByTestId } = mountSection();

    expect(getByTestId('decision-records-section-host')).toBeInTheDocument();
    expect(serviceMock).toHaveBeenCalledTimes(1);
    expect(serviceMock).toHaveBeenCalledWith({ fetch: authenticatedFetch, baseUrl: 'https://bff.example.test' });
    expect(widgetMock).toHaveBeenCalled();
    expect(widgetMock.mock.calls[0][0].service).toEqual({ marker: 'decision-records-service' });
  });

  test('opens a record through launchDecisionWizard (the in-app host)', () => {
    mountSection();
    expect(widgetMock.mock.calls[0][0].launchWizard).toBe(launchDecisionWizard);
  });

  test('negative: never reads sprk_signal / sprk_decisionrecord and uses no Xrm (static)', () => {
    expect(shimCode()).not.toMatch(/sprk_signal|sprk_decisionrecord|Xrm|WebApi/i);
  });

  test('negative: reads nothing at runtime even with Xrm present', () => {
    const retrieve = jest.fn().mockResolvedValue({ entities: [] });
    const w = window as unknown as { Xrm?: unknown };
    w.Xrm = { WebApi: { retrieveMultipleRecords: retrieve, retrieveRecord: retrieve } };
    try {
      mountSection();
      expect(retrieve).not.toHaveBeenCalled();
    } finally {
      delete w.Xrm;
    }
  });
});
