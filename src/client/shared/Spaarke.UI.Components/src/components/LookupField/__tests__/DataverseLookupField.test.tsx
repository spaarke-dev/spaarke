/**
 * DataverseLookupField — contract tests for the side-pane lookup wrapper.
 *
 * Pins the behaviour ~30 wizard fields rely on: a pick maps to a clean { id, name },
 * an empty result or a failed pane is a cancel (value unchanged, button usable again,
 * no unhandled rejection), one pane at a time, `filters` pass through unchanged, and
 * the inline LookupField is used only when there is no navigationService.
 */

import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { DataverseLookupField } from '../DataverseLookupField';
import type { ILookupItem } from '../../../types/LookupTypes';
import type { INavigationService, LookupResult } from '../../../types/serviceInterfaces';

const LABEL = 'Matter Type';
const ENTITY = 'sprk_mattertype_ref';
const GUID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';

const selectButton = (): HTMLElement => screen.getByRole('button', { name: `Select ${LABEL}` });

function makeNav(openLookup: jest.Mock): INavigationService {
  return { openLookup } as unknown as INavigationService;
}

function renderField(
  openLookup: jest.Mock,
  overrides: Partial<React.ComponentProps<typeof DataverseLookupField>> = {}
) {
  const onChange = jest.fn();
  const utils = renderWithProviders(
    <DataverseLookupField
      label={LABEL}
      entityType={ENTITY}
      value={null}
      onChange={onChange}
      navigationService={makeNav(openLookup)}
      {...overrides}
    />
  );
  return { onChange, ...utils };
}

describe('DataverseLookupField', () => {
  let warnSpy: jest.SpyInstance;
  let unhandled: unknown[];
  const onUnhandled = (reason: unknown): void => {
    unhandled.push(reason);
  };

  beforeEach(() => {
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    unhandled = [];
    process.on('unhandledRejection', onUnhandled);
  });

  afterEach(() => {
    process.off('unhandledRejection', onUnhandled);
    warnSpy.mockRestore();
  });

  it('passes the picked record to onChange as { id (clean GUID), name }', async () => {
    const result: LookupResult = { id: `{${GUID.toUpperCase()}}`, name: 'Litigation', entityType: ENTITY };
    const openLookup = jest.fn().mockResolvedValue([result]);
    const { onChange } = renderField(openLookup);

    fireEvent.click(selectButton());

    await waitFor(() => expect(onChange).toHaveBeenCalledTimes(1));
    expect(onChange).toHaveBeenCalledWith({ id: GUID, name: 'Litigation' });
  });

  it('leaves the value alone when the pane returns no record', async () => {
    const openLookup = jest.fn().mockResolvedValue([]);
    const { onChange } = renderField(openLookup);

    fireEvent.click(selectButton());
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(selectButton()).toBeEnabled());

    expect(onChange).not.toHaveBeenCalled();
  });

  it('treats a rejected pane as a cancel: no onChange, button usable again, no unhandled rejection', async () => {
    const openLookup = jest.fn().mockRejectedValue(new Error('Xrm is not available'));
    const { onChange } = renderField(openLookup);

    fireEvent.click(selectButton());
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(selectButton()).toBeEnabled());
    // Let any stray rejection surface before asserting.
    await act(async () => {
      await new Promise(resolve => setTimeout(resolve, 20));
    });

    expect(onChange).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining(ENTITY));
    expect(unhandled).toEqual([]);
  });

  it('opens one pane only when Select is clicked again while a pane is open', async () => {
    let resolvePane: (r: LookupResult[]) => void = () => undefined;
    const openLookup = jest.fn().mockReturnValue(
      new Promise<LookupResult[]>(resolve => {
        resolvePane = resolve;
      })
    );
    renderField(openLookup);

    const button = selectButton();
    fireEvent.click(button);
    fireEvent.click(button);
    fireEvent.click(button);

    expect(openLookup).toHaveBeenCalledTimes(1);
    await act(async () => {
      resolvePane([]);
    });
  });

  it('passes filters unchanged with the entityType, and no filters key when unset', async () => {
    const filters = [
      {
        entityLogicalName: 'contact',
        filterXml: '<filter><condition attribute="statecode" operator="eq" value="0"/></filter>',
      },
    ];

    const withFilters = jest.fn().mockResolvedValue([]);
    const first = renderField(withFilters, { filters });
    fireEvent.click(selectButton());
    await waitFor(() => expect(withFilters).toHaveBeenCalledTimes(1));
    expect(withFilters).toHaveBeenCalledWith({ entityType: ENTITY, filters });
    expect(withFilters.mock.calls[0][0].filters).toBe(filters);
    await waitFor(() => expect(selectButton()).toBeEnabled());
    first.unmount();

    const withoutFilters = jest.fn().mockResolvedValue([]);
    renderField(withoutFilters);
    fireEvent.click(selectButton());
    await waitFor(() => expect(withoutFilters).toHaveBeenCalledTimes(1));
    expect(withoutFilters.mock.calls[0][0]).toEqual({ entityType: ENTITY });
    expect('filters' in withoutFilters.mock.calls[0][0]).toBe(false);
  });

  it('renders the inline LookupField with the onSearch callback when there is no navigationService', async () => {
    const onSearch = jest.fn(async () => [{ id: GUID, name: 'Litigation' }] as ILookupItem[]);
    renderWithProviders(
      <DataverseLookupField label={LABEL} entityType={ENTITY} value={null} onChange={jest.fn()} onSearch={onSearch} />
    );

    // The inline LookupField exposes the browse button; the side-pane Select button is absent.
    expect(screen.queryByRole('button', { name: `Select ${LABEL}` })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: `Browse ${LABEL}` }));
    await waitFor(() => expect(onSearch).toHaveBeenCalled());
  });

  it('renders hint text in the side-pane mode and in the read-only branch', () => {
    const openLookup = jest.fn();
    const pane = renderField(openLookup, { hint: 'Only active contacts' });
    expect(screen.getByText('Only active contacts')).toBeInTheDocument();
    pane.unmount();

    renderWithProviders(
      <DataverseLookupField label={LABEL} entityType={ENTITY} value={null} onChange={jest.fn()} hint="Read-only hint" />
    );
    expect(screen.getByText('Read-only hint')).toBeInTheDocument();
  });
});
