/**
 * worklistSectionRegistration.test.tsx - spaarke-ontology-platform-r1 task 156.
 *
 * Pins the contract of LegalWorkspace's `worklist.registration.ts` section shim, the thing that makes the Worklist reachable from the
 * Console's workspace dropdown (the `sprk_workspacelayout` "Worklist" row mounts the `worklist` section by id):
 *   1. The registration is `worklist` / "Worklist" and its factory yields a content section.
 *   2. The section renders the shared `WorklistWidget` with a service built by `createWorklistService` from LegalWorkspace's
 *      `authenticatedFetch` and `getBffBaseUrl()` (D-15) - the Signal read route, nothing else - and with `launchDecisionWizard`
 *      as the wizard launcher (D-26: in-app host, never navigateTo / WizardModal).
 *   3. Negative: the shim reads no `sprk_signal` / `sprk_decisionrecord` through Xrm or Dataverse (statically, and at runtime with an
 *      Xrm present).
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

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    WorklistWidget: jest.fn(() => null),
    createWorklistService: jest.fn(() => ({ marker: 'worklist-service' })),
    launchDecisionWizard: jest.fn(),
  };
});
jest.mock('../../../../solutions/LegalWorkspace/src/services/authInit', () => ({
  authenticatedFetch: jest.fn(),
}));
jest.mock('../../../../solutions/LegalWorkspace/src/config/runtimeConfig', () => ({
  getBffBaseUrl: jest.fn(() => 'https://bff.example.test'),
}));

import { WorklistWidget, createWorklistService, launchDecisionWizard } from '@spaarke/ui-components';
import type { SectionFactoryContext } from '@spaarke/ui-components';
import { worklistRegistration } from '../../../../solutions/LegalWorkspace/src/sections/worklist.registration';
import { authenticatedFetch } from '../../../../solutions/LegalWorkspace/src/services/authInit';

const SHIM_PATH = path.resolve(
  __dirname,
  '../../../../solutions/LegalWorkspace/src/sections/worklist.registration.ts'
);

const widgetMock = WorklistWidget as unknown as jest.Mock;
const serviceMock = createWorklistService as unknown as jest.Mock;

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

describe('worklist section shim (task 156)', () => {
  beforeEach(() => {
    widgetMock.mockClear();
    serviceMock.mockClear();
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

  test('negative: reads no sprk_signal / sprk_decisionrecord through Xrm or Dataverse (static)', () => {
    const code = shimCode();
    expect(code).not.toMatch(/sprk_signal|sprk_decisionrecord/i);
    expect(code).not.toMatch(/\bXrm\b|getXrm|XrmDataverseClient|WebApi|retrieveMultipleRecords/);
  });

  test('negative: reads no sprk_signal / sprk_decisionrecord through Xrm or Dataverse (runtime, Xrm present)', () => {
    const retrieveMultipleRecords = jest.fn();
    const w = window as unknown as { Xrm?: unknown };
    w.Xrm = { WebApi: { retrieveMultipleRecords, retrieveRecord: retrieveMultipleRecords } };
    try {
      mountSection();
      expect(retrieveMultipleRecords).not.toHaveBeenCalled();
    } finally {
      delete w.Xrm;
    }
  });
});
