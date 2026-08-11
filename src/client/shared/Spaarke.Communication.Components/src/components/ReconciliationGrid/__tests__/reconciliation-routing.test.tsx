/**
 * reconciliation-routing.test.tsx — FR-E7 (task 057) per-team reconciliation routing.
 *
 * Routing has two halves; this suite covers the FRONTEND half (per-team FILTERED grid
 * views). It asserts:
 *  (a) `per-team.gridconfiguration.json` is a VALID DataGrid v1.0 config whose
 *      `behavior.membershipFilter` scopes the view to the caller's TEAM ownership —
 *      the FRAMEWORK mechanism (AC #2 "not a bespoke client filter");
 *  (b) the default `needs-review` config does NOT set `membershipFilter` (the
 *      everyone/default-unassigned queue) — so an unmapped/unassigned email still
 *      appears there (AC #5 "default view, no forced mis-assignment");
 *  (c) `<ReconciliationGrid />` forwards a host `membershipResolver` to the shared
 *      `<DataGrid />` so the config's `membershipFilter` can resolve at query time.
 *
 * The backend half (category→team assignment at triage time) is covered by the BFF
 * seam tests (EmailTriageSeamTests) + the CategoryRoutingGate unit tests.
 */
import * as React from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { isValidDataGridConfiguration, type DataGridConfiguration } from '@spaarke/ui-components';
import perTeamConfig from '../per-team.gridconfiguration.json';
import needsReviewConfig from '../needs-review.gridconfiguration.json';

// Capture the props the framework `<DataGrid />` receives (var is `mock`-prefixed so jest.mock may close over it).
const mockDataGridProps: { current: Record<string, unknown> | null } = { current: null };
jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    DataGrid: (props: Record<string, unknown>) => {
      mockDataGridProps.current = props;
      return <div data-testid="mock-datagrid" />;
    },
  };
});

import { ReconciliationGrid } from '../ReconciliationGrid';

describe('reconciliation routing (FR-E7 / task 057)', () => {
  afterEach(() => {
    mockDataGridProps.current = null;
  });

  it('per-team config is a valid DataGrid config scoped by membershipFilter to team ownership', () => {
    expect(isValidDataGridConfiguration(perTeamConfig)).toBe(true);
    const cfg = perTeamConfig as unknown as DataGridConfiguration;
    const mf = cfg.behavior?.membershipFilter;
    expect(mf).toBeTruthy();
    expect(mf).not.toBe(true); // an explicit filter object, not the `true` shorthand
    expect((mf as { roles?: string[] }).roles).toEqual(['owner']);
    expect((mf as { identityTypes?: string[] }).identityTypes).toEqual(['team']);
  });

  it('the default needs-review config does NOT scope by membershipFilter (default/unassigned queue)', () => {
    const cfg = needsReviewConfig as unknown as DataGridConfiguration;
    expect(cfg.behavior?.membershipFilter).toBeUndefined();
  });

  it('ReconciliationGrid forwards a host membershipResolver to the framework DataGrid', () => {
    const membershipResolver = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <ReconciliationGrid configId="per-team-config-id" membershipResolver={membershipResolver as never} />
      </FluentProvider>
    );
    expect(mockDataGridProps.current).not.toBeNull();
    expect(mockDataGridProps.current!.membershipResolver).toBe(membershipResolver);
    expect(mockDataGridProps.current!.configId).toBe('per-team-config-id');
  });
});
