/**
 * ReconciliationWorkspaceWidget — confirmed-regarding gate wiring.
 *
 * The widget hands `<ReconciliationWorkspace />` a `resolveRegarding` adapter that decides whether the Fields/Tasks
 * tabs un-gate (NFR-10). It must be the SHARED resolver (`logic/connections/reconciliationResolvers`), which reads the
 * row's typed regarding lookups, so the gate never depends on a candidate display name the viewer may not be able to
 * read. `ReconciliationWorkspace` is mocked at the module boundary to capture the props the widget passes.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { act, render } from '@testing-library/react';

type ResolveRegarding = (record: Record<string, unknown>) => { entityType: string; recordId: string } | null;

let captured: { resolveRegarding?: ResolveRegarding } = {};

jest.mock('@spaarke/communication-components', () => {
  const connections = '../../../../../Spaarke.Communication.Components/src/logic/connections';
  return {
    __esModule: true,
    ...jest.requireActual(`${connections}/provenance`),
    ...jest.requireActual(`${connections}/reconciliationResolvers`),
    bffRefile: () => () => Promise.resolve(),
    ReconciliationWorkspace: (props: { resolveRegarding?: ResolveRegarding }) => {
      captured = { resolveRegarding: props.resolveRegarding };
      return React.createElement('div', { 'data-testid': 'mock-reconciliation-workspace' });
    },
  };
});

jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  XrmDataverseClient: class {},
  CreateRecordChooserModal: () => null,
  getXrm: () => ({ WebApi: {}, Utility: { getGlobalContext: () => ({ getClientUrl: () => '' }) } }),
}));

jest.mock('../../../providers/useAiSession', () => ({
  __esModule: true,
  useAiSession: () => ({
    authenticatedFetch: jest.fn(),
    bffBaseUrl: 'https://bff.example',
    chatSessionId: null,
    setChatSessionId: jest.fn(),
  }),
}));

import { ReconciliationWorkspaceWidget } from '../ReconciliationWorkspaceWidget';

const MATTER = '11111111-2222-3333-4444-555555555555';
const RESOLVED = 100000000;

/** A Resolved row whose stored provenance names the confirmed candidate by id only (no name). */
function resolvedRow(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    sprk_communicationid: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
    sprk_associationstatus: RESOLVED,
    sprk_associationprovenance: JSON.stringify({
      version: 2,
      direction: 'Incoming',
      decision: { status: 'Resolved', autoFiled: true, killSwitchEnabled: true, aiInvolved: false },
      rungsFired: ['RecordNameMatch'],
      candidates: [
        {
          field: 'sprk_regardingmatter',
          targetEntity: 'sprk_matter',
          targetId: MATTER,
          reinforcedConfidence: 0.97,
          deterministicConfidence: 0.97,
          written: true,
          conflict: false,
          contributors: [{ rung: 'RecordNameMatch', confidence: 0.97, provenance: 'record-name-match:matched=number' }],
        },
      ],
      signals: [],
    }),
    sprk_regardingrecordid: MATTER,
    sprk_regardingrecordname: 'Display Name',
    sprk_regardingrecordtypename: 'Matter',
    _sprk_regardingmatter_value: MATTER,
    ...overrides,
  };
}

describe('ReconciliationWorkspaceWidget — confirmed-regarding gate', () => {
  beforeEach(() => {
    captured = {};
  });

  async function mountAndCapture(): Promise<ResolveRegarding> {
    await act(async () => {
      render(<ReconciliationWorkspaceWidget data={null} widgetType="reconciliation" />);
    });
    expect(captured.resolveRegarding).toBeDefined();
    return captured.resolveRegarding as ResolveRegarding;
  }

  it('un-gates a Resolved row from its typed regarding lookup, with no viewer name lookup', async () => {
    const resolveRegarding = await mountAndCapture();
    expect(resolveRegarding(resolvedRow())).toEqual({ entityType: 'sprk_matter', recordId: MATTER });
  });

  it('keeps the gate closed for a row that is not Resolved', async () => {
    const resolveRegarding = await mountAndCapture();
    expect(resolveRegarding(resolvedRow({ sprk_associationstatus: 100000001 }))).toBeNull();
  });

  it('keeps the gate closed for a Resolved row with no typed regarding lookup', async () => {
    const resolveRegarding = await mountAndCapture();
    expect(resolveRegarding(resolvedRow({ _sprk_regardingmatter_value: null }))).toBeNull();
  });
});
