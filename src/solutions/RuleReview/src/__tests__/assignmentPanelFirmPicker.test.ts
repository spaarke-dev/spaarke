/**
 * The Firm picker of the assignment panel (D-219 Q5, ontology task 187): a firm is a `sprk_organization`, chosen in the Dataverse side
 * pane when the host passes a navigation service. The shared `DataverseLookupField` is replaced by a probe here so the test reads
 * exactly what the panel hands it; the field's own behaviour (pane, chip, cancel, failed pane) is covered in `@spaarke/ui-components`.
 */

import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { summary } from './testKit';
import type { INavigationService } from '@spaarke/ui-components';
import type { FirmLookup } from '../services/firmLookup';

let lastProps: Record<string, unknown> | undefined;

jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  DataverseLookupField: (props: Record<string, unknown>) => {
    lastProps = props;
    return React.createElement('div', { 'data-probe': 'firm-field' });
  },
}));

import { AssignmentPanel } from '../components/AssignmentPanel';
import { createFirmLookup } from '../services/firmLookup';

const navigationService = { openLookup: jest.fn() } as unknown as INavigationService;

function lookup(nav?: unknown): FirmLookup {
  return {
    navigationService: nav as FirmLookup['navigationService'],
    search: jest.fn(async () => []),
    names: jest.fn(async () => ({})),
  };
}

function render(firmLookup: FirmLookup, scope: 'Firm' | 'ClientDefault' = 'Firm'): string {
  return renderToStaticMarkup(
    React.createElement(AssignmentPanel, {
      ruleSet: summary({ scope, firmId: scope === 'Firm' ? 'f-1' : null }),
      firmName: scope === 'Firm' ? 'Acme LLP' : null,
      firmLookup,
      canWrite: true,
      busy: false,
      onAssign: jest.fn(),
      onVisibility: jest.fn(),
    })
  );
}

describe('AssignmentPanel Firm picker (D-219 Q5)', () => {
  beforeEach(() => {
    lastProps = undefined;
  });

  it('opens the side pane for sprk_organization over the host navigation service, keeping the stored firm as the value', () => {
    const firmLookup = lookup(navigationService);
    const html = render(firmLookup);

    expect(html).toContain('data-probe="firm-field"');
    expect(lastProps?.entityType).toBe('sprk_organization');
    expect(lastProps?.navigationService).toBe(navigationService);
    expect(lastProps?.label).toBe('Firm');
    expect(lastProps?.required).toBe(true);
    expect(lastProps?.value).toEqual({ id: 'f-1', name: 'Acme LLP' });
  });

  it('keeps the inline search as the fallback, so a host with no navigation service renders as before', async () => {
    const firmLookup = lookup(undefined);
    render(firmLookup);

    expect(lastProps?.navigationService).toBeUndefined();
    expect(lastProps?.minSearchLength).toBe(2);
    await (lastProps?.onSearch as (q: string) => Promise<unknown>)('ac');
    expect(firmLookup.search).toHaveBeenCalledWith('ac');
  });

  it('offers no firm picker for the client default (must-not-fire)', () => {
    const html = render(lookup(navigationService), 'ClientDefault');

    expect(html).not.toContain('data-probe="firm-field"');
    expect(lastProps).toBeUndefined();
  });

  it('createFirmLookup carries the host navigation service and defaults to none', () => {
    const data = {} as never;
    expect(createFirmLookup(data, navigationService).navigationService).toBe(navigationService);
    expect(createFirmLookup(data).navigationService).toBeUndefined();
  });
});
