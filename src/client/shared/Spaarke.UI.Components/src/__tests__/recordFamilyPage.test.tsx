/**
 * RecordFamily code page - wiring test (field-mapping-server-write-path-r1 task 075 fix round 2).
 *
 * Renders the page's view (`src/solutions/RecordFamily/src/FamilyPage.tsx`, the component `main.tsx` mounts) with a
 * bootstrap double and asserts the two things the page exists to do:
 *   1. the request is gated by `readFamilyParams` - a malformed launch shows the error bar and never fetches;
 *   2. the modal is rendered `embedded` (no Fluent dialog inside the platform dialog), and its Close / Escape reach
 *      the bootstrap's `closeDialog`.
 * Removing `embedded`, or bypassing `readFamilyParams`, fails these.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { webLightTheme } from '@fluentui/react-components';
import { FamilyPage } from '../../../../../solutions/RecordFamily/src/FamilyPage';
import type { IFamilyPageBootstrap } from '../../../../../solutions/RecordFamily/src/FamilyPage';
import { throwingAuthenticatedFetch } from './helpers/authenticatedFetchDouble';

const PROJECT = 'bbbbbbbb-0000-0000-0000-000000000002';

const familyBody = {
  anchor: { id: PROJECT, table: 'sprk_project' },
  root: { id: PROJECT, table: 'sprk_project' },
  truncated: false,
  continuation: null,
  members: [
    {
      id: PROJECT,
      table: 'sprk_project',
      depth: 0,
      name: 'Diligence Project',
      recordNumber: null,
      recordType: null,
      status: 'Open',
      modifiedOn: null,
      kind: 'Filing',
      edgeKind: 'FilingCore',
      parentId: null,
    },
  ],
};

function makeBootstrap(
  params: Record<string, string | undefined>,
  over: Partial<IFamilyPageBootstrap> = {}
): IFamilyPageBootstrap & { fetchMock: jest.Mock } {
  const fetchMock = throwingAuthenticatedFetch(
    async () => ({ ok: true, status: 200, json: async () => familyBody }) as unknown as Response
  );
  return {
    theme: webLightTheme,
    params,
    isAuthReady: true,
    authenticatedFetch: fetchMock,
    closeDialog: jest.fn(),
    fetchMock,
    ...over,
  } as IFamilyPageBootstrap & { fetchMock: jest.Mock };
}

describe('RecordFamily page (FamilyPage)', () => {
  it('renders the Family embedded: a named region, no dialog, and Close / Escape close the platform dialog', async () => {
    const b = makeBootstrap({ entityName: 'sprk_project', id: `{${PROJECT.toUpperCase()}}` });
    render(<FamilyPage b={b} />);

    await screen.findByText('Diligence Project');
    // embedded: no Fluent Dialog inside the platform dialog (ADR-050), a landmark instead.
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Family' })).toBeInTheDocument();
    expect(b.fetchMock).toHaveBeenCalledTimes(1);
    expect(String(b.fetchMock.mock.calls[0][0])).toContain(PROJECT);

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(b.closeDialog).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(screen.getByRole('region', { name: 'Family' }), { key: 'Escape' });
    expect(b.closeDialog).toHaveBeenCalledTimes(2);
  });

  it.each([
    ['a missing id', { entityName: 'sprk_project' }],
    ['a missing table', { id: PROJECT }],
    ['a table name that is not a logical name', { entityName: 'sprk_project/../x', id: PROJECT }],
    ['an id that is not a GUID', { entityName: 'sprk_project', id: 'not-a-guid' }],
    ['no params at all', {}],
  ])('shows the error bar and never fetches for %s', (_label, params) => {
    const b = makeBootstrap(params);
    render(<FamilyPage b={b} />);

    expect(screen.getByText('The family could not be opened. Close this window and try again.')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Family' })).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(b.fetchMock).not.toHaveBeenCalled();
  });

  it('waits for auth before anything renders or fetches', () => {
    const b = makeBootstrap({ entityName: 'sprk_project', id: PROJECT }, { isAuthReady: false });
    render(<FamilyPage b={b} />);

    expect(screen.getByText('Initializing...')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Family' })).not.toBeInTheDocument();
    expect(b.fetchMock).not.toHaveBeenCalled();
  });
});
