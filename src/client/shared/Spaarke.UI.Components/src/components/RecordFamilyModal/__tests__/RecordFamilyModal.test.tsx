/**
 * RecordFamilyModal — component tests (field-mapping-server-write-path-r1 task 073, spec FR-32).
 *
 * The FR-31 endpoint (task 072) is not built yet, so every test drives the modal through a
 * `@spaarke/auth`-shaped fetch double (`throwingAuthenticatedFetch`: a 2xx is returned, any other
 * status is THROWN as the real `ApiError`) answering with the FR-31 response shape.
 *
 * Behaviours covered, and only these: grouped rows + the five owner columns, truncation + "Load more",
 * selection + the action handler, the disabled-with-tooltip toolbar, the fixed error state, dark mode.
 */

import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { RecordFamilyModal } from '../RecordFamilyModal';
import type { RecordFamilyModalProps } from '../RecordFamilyModal';
import type { IFamilyMember, IFamilyResponse } from '../recordFamilyService';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const MATTER = 'aaaaaaaa-0000-0000-0000-000000000001';
const PROJECT = 'bbbbbbbb-0000-0000-0000-000000000002';
const WA1 = 'cccccccc-0000-0000-0000-000000000003';
const WA2 = 'cccccccc-0000-0000-0000-000000000004';
const WA3 = 'cccccccc-0000-0000-0000-000000000005';

function member(over: Partial<IFamilyMember> & Pick<IFamilyMember, 'id' | 'table' | 'depth'>): IFamilyMember {
  return {
    name: null,
    recordNumber: null,
    recordType: null,
    status: null,
    modifiedOn: null,
    kind: 'Filing',
    edgeKind: 'FilingCore',
    parentId: null,
    ...over,
  };
}

const M_MATTER = member({
  id: MATTER,
  table: 'sprk_matter',
  depth: 0,
  name: 'Acme Acquisition',
  recordNumber: 'MAT-0001',
  recordType: 'Litigation',
  status: 'Active',
  modifiedOn: '2026-10-20T12:00:00Z',
});
// A project has no record number: the cell must be blank.
const M_PROJECT = member({
  id: PROJECT,
  table: 'sprk_project',
  depth: 1,
  parentId: MATTER,
  name: 'Diligence Project',
  recordType: 'Due Diligence',
  status: 'Open',
  modifiedOn: '2026-10-21T12:00:00Z',
});
const M_WA1 = member({
  id: WA1,
  table: 'sprk_workassignment',
  depth: 2,
  parentId: PROJECT,
  edgeKind: 'Stamp',
  name: 'Review contracts',
  recordNumber: 'WA-001',
  recordType: 'Review',
  status: 'Assigned',
});
const M_WA2 = member({
  id: WA2,
  table: 'sprk_workassignment',
  depth: 2,
  parentId: PROJECT,
  edgeKind: 'Stamp',
  name: 'Draft memo',
  recordNumber: 'WA-002',
  status: 'Draft',
});
const M_WA3 = member({
  id: WA3,
  table: 'sprk_workassignment',
  depth: 2,
  parentId: PROJECT,
  edgeKind: 'Stamp',
  name: 'File motion',
  recordNumber: 'WA-003',
  status: 'Draft',
});

function response(over: Partial<IFamilyResponse> = {}): IFamilyResponse {
  return {
    anchor: { id: PROJECT, table: 'sprk_project' },
    root: { id: MATTER, table: 'sprk_matter' },
    truncated: false,
    continuation: null,
    members: [M_MATTER, M_PROJECT, M_WA1, M_WA2],
    ...over,
  };
}

function ok(body: unknown): Response {
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
}
function problem(status: number): Response {
  return { ok: false, status, json: async () => ({ status, title: 'Not found' }) } as unknown as Response;
}

function makeProps(
  respond: (url: string) => Response | Promise<Response>,
  overrides: Partial<RecordFamilyModalProps> = {}
): RecordFamilyModalProps & { fetchMock: jest.Mock } {
  const fetchMock = throwingAuthenticatedFetch(async (url: string) => respond(url));
  return {
    open: true,
    onClose: jest.fn(),
    table: 'sprk_project',
    recordId: `{${PROJECT.toUpperCase()}}`,
    authenticatedFetch: fetchMock,
    fetchMock,
    ...overrides,
  };
}

const renderModal = (props: RecordFamilyModalProps, theme = webLightTheme) =>
  render(
    <FluentProvider theme={theme}>
      <RecordFamilyModal {...props} />
    </FluentProvider>
  );

/** The cells of the table row whose name cell reads `name` (checkbox cell first). */
function cellsOfRow(name: string): string[] {
  const row = screen.getByText(name).closest('tr') as HTMLElement;
  return within(row)
    .getAllByRole('cell')
    .map(c => c.textContent ?? '');
}

describe('RecordFamilyModal (FR-32)', () => {
  describe('rows and columns', () => {
    it('loads the family from the FR-31 route with the canonical id and renders the five owner columns', async () => {
      const props = makeProps(() => ok(response()));
      renderModal(props);

      await screen.findByText('Diligence Project');

      expect(props.fetchMock).toHaveBeenCalledTimes(1);
      expect(props.fetchMock).toHaveBeenCalledWith(`/api/v1/records/sprk_project/${PROJECT}/family`, {
        method: 'GET',
      });
      for (const header of ['Name', 'Record number', 'Record type', 'Status', 'Modified on']) {
        expect(screen.getByRole('columnheader', { name: header })).toBeInTheDocument();
      }
    });

    it('groups members by level and table, and renders a null record number as a blank cell', async () => {
      renderModal(makeProps(() => ok(response())));
      await screen.findByText('Diligence Project');

      expect(screen.getByText(/Level 0\b.*Matter/)).toBeInTheDocument();
      expect(screen.getByText(/Level 1\b.*Project/)).toBeInTheDocument();
      // Two work assignments share ONE group.
      expect(screen.getAllByText(/Level 2\b.*Work Assignment/)).toHaveLength(1);

      // cells: [select, name, record number, record type, status, modified on]
      const project = cellsOfRow('Diligence Project');
      expect(project[1]).toBe('Diligence ProjectThis record'); // the opened record carries a badge
      expect(project[2]).toBe(''); // no record number -> blank, not "null" / "—" / "N/A"
      expect(project[3]).toBe('Due Diligence');
      expect(project[4]).toBe('Open');
      expect(project[5]).toContain('2026');

      const matter = cellsOfRow('Acme Acquisition');
      expect(matter[2]).toBe('MAT-0001');

      // A member with a null record type and null modifiedOn is blank in both cells.
      const memo = cellsOfRow('Draft memo');
      expect(memo[3]).toBe('');
      expect(memo[5]).toBe('');
    });

    it('does not fetch while closed', () => {
      const props = makeProps(() => ok(response()), { open: false });
      renderModal(props);
      expect(props.fetchMock).not.toHaveBeenCalled();
    });
  });

  describe('truncation and load more', () => {
    it('shows the notice and Load more, fetches with the continuation and appends without duplicates', async () => {
      const first = response({
        truncated: true,
        continuation: 'tok/1+=',
        members: [M_MATTER, M_PROJECT, M_WA1, M_WA2],
      });
      // The second page repeats Draft memo (same table + id) and adds File motion.
      const second = response({ truncated: false, continuation: null, members: [M_WA2, M_WA3] });
      const props = makeProps(url => ok(url.includes('continuation=') ? second : first));
      renderModal(props);

      await screen.findByText('Diligence Project');
      expect(screen.getByText(/Showing part of this family/i)).toBeInTheDocument();
      const loadMore = screen.getByRole('button', { name: /load more/i });

      fireEvent.click(loadMore);

      await screen.findByText('File motion');
      expect(props.fetchMock).toHaveBeenCalledTimes(2);
      expect(props.fetchMock).toHaveBeenLastCalledWith(
        `/api/v1/records/sprk_project/${PROJECT}/family?continuation=${encodeURIComponent('tok/1+=')}`,
        { method: 'GET' }
      );
      expect(screen.getAllByText('Draft memo')).toHaveLength(1);
      expect(screen.getAllByText('File motion')).toHaveLength(1);
      expect(screen.queryByText(/Showing part of this family/i)).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
    });

    it('shows neither the notice nor Load more when the family is complete', async () => {
      renderModal(makeProps(() => ok(response())));
      await screen.findByText('Diligence Project');
      expect(screen.queryByText(/Showing part of this family/i)).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
    });

    it('keeps the loaded rows and says so when Load more fails', async () => {
      const first = response({ truncated: true, continuation: 'tok' });
      const props = makeProps(url => (url.includes('continuation=') ? problem(400) : ok(first)));
      renderModal(props);
      await screen.findByText('Diligence Project');

      fireEvent.click(screen.getByRole('button', { name: /load more/i }));

      expect(await screen.findByText(/More records could not be loaded/i)).toBeInTheDocument();
      expect(screen.getByText('Diligence Project')).toBeInTheDocument();
    });
  });

  describe('selection and actions', () => {
    it('disables both actions with no selection, and nothing runs on open', async () => {
      const onAction = jest.fn();
      renderModal(makeProps(() => ok(response()), { onAction }));
      await screen.findByText('Diligence Project');

      const push = screen.getByRole('button', { name: 'Push field updates' });
      const assign = screen.getByRole('button', { name: 'Update Assigned To' });
      expect(push).toHaveAttribute('aria-disabled', 'true');
      expect(assign).toHaveAttribute('aria-disabled', 'true');

      fireEvent.click(push);
      fireEvent.click(assign);
      expect(onAction).not.toHaveBeenCalled();
    });

    it('passes exactly the selected (table, id) pairs to the handler for the clicked action', async () => {
      const onAction = jest.fn();
      renderModal(makeProps(() => ok(response()), { onAction }));
      await screen.findByText('Diligence Project');

      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Review contracts' }));
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Acme Acquisition' }));
      // Select then unselect: must NOT be passed.
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Draft memo' }));
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Draft memo' }));

      const push = screen.getByRole('button', { name: 'Push field updates' });
      expect(push).not.toHaveAttribute('aria-disabled', 'true');
      fireEvent.click(push);

      expect(onAction).toHaveBeenCalledTimes(1);
      const [action, rows] = onAction.mock.calls[0];
      expect(action).toBe('push');
      expect(rows).toHaveLength(2);
      expect(rows).toEqual(
        expect.arrayContaining([
          { table: 'sprk_workassignment', id: WA1 },
          { table: 'sprk_matter', id: MATTER },
        ])
      );

      fireEvent.click(screen.getByRole('button', { name: 'Update Assigned To' }));
      expect(onAction).toHaveBeenCalledTimes(2);
      expect(onAction.mock.calls[1][0]).toBe('assignedTo');
    });

    it('keeps the selection when Load more appends rows', async () => {
      const onAction = jest.fn();
      const first = response({ truncated: true, continuation: 'tok' });
      const second = response({ members: [M_WA3] });
      renderModal(makeProps(url => ok(url.includes('continuation=') ? second : first), { onAction }));
      await screen.findByText('Diligence Project');

      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Review contracts' }));
      fireEvent.click(screen.getByRole('button', { name: /load more/i }));
      fireEvent.click(await screen.findByRole('checkbox', { name: 'Select File motion' }));
      fireEvent.click(screen.getByRole('button', { name: 'Push field updates' }));

      expect(onAction.mock.calls[0][1]).toEqual(
        expect.arrayContaining([
          { table: 'sprk_workassignment', id: WA1 },
          { table: 'sprk_workassignment', id: WA3 },
        ])
      );
      expect(onAction.mock.calls[0][1]).toHaveLength(2);
    });

    it('renders the actions disabled with the tooltip when no handler is supplied, even with rows selected', async () => {
      renderModal(makeProps(() => ok(response())));
      await screen.findByText('Diligence Project');

      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Review contracts' }));

      const push = screen.getByRole('button', { name: 'Push field updates' });
      const assign = screen.getByRole('button', { name: 'Update Assigned To' });
      expect(push).toHaveAttribute('aria-disabled', 'true');
      expect(assign).toHaveAttribute('aria-disabled', 'true');

      fireEvent.focus(push);
      expect((await screen.findAllByText('Available after the push update')).length).toBeGreaterThan(0);
    });
  });

  describe('error state', () => {
    it.each([
      ['a 404', () => problem(404)],
      ['a network failure', () => Promise.reject(new TypeError('Failed to fetch')) as unknown as Response],
    ])('shows the fixed error state for %s, with no rows and no hidden-record count', async (_label, respond) => {
      renderModal(makeProps(respond));

      expect(await screen.findByText(/The family could not be loaded/i)).toBeInTheDocument();
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
      const dialogText = screen.getByRole('dialog').textContent ?? '';
      expect(dialogText).not.toMatch(/hidden|not shown|more records|unreadable|\d+ (record|member)s?/i);
    });

    it('shows the same text for a 404 and a network failure (nothing distinguishes them)', async () => {
      const a = renderModal(makeProps(() => problem(404)));
      const textA = (await screen.findByText(/The family could not be loaded/i)).textContent;
      a.unmount();
      renderModal(makeProps(() => Promise.reject(new TypeError('Failed to fetch')) as unknown as Response));
      const textB = (await screen.findByText(/The family could not be loaded/i)).textContent;
      expect(textA).toBe(textB);
    });

    const { table: _dropped, ...memberWithoutTable } = M_WA1;
    it.each<[string, unknown]>([
      ['a member without a table', response({ members: [M_MATTER, M_PROJECT, memberWithoutTable as IFamilyMember] })],
      [
        'a member whose depth is not a number',
        response({ members: [M_PROJECT, { ...M_WA1, depth: '2' as unknown as number }] }),
      ],
      ['a member with a negative depth', response({ members: [M_PROJECT, { ...M_WA1, depth: -1 }] })],
      ['a member with a fractional depth', response({ members: [M_PROJECT, { ...M_WA1, depth: 1.5 }] })],
      ['a response that does not contain its anchor', response({ members: [M_MATTER, M_WA1] })],
    ])('shows the error state, never a partial family, for %s', async (_label, body) => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
      renderModal(makeProps(() => ok(body)));

      expect(await screen.findByText(/The family could not be loaded/i)).toBeInTheDocument();
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
      expect(screen.queryByText('Review contracts')).not.toBeInTheDocument();
      warnSpy.mockRestore();
    });

    it('logs the cause with console.warn and does not render it', async () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
      renderModal(makeProps(() => Promise.reject(new TypeError('Failed to fetch')) as unknown as Response));

      await screen.findByText(/The family could not be loaded/i);
      expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('RecordFamilyModal'), expect.any(TypeError));
      expect(screen.getByRole('dialog').textContent).not.toMatch(/Failed to fetch/);
      warnSpy.mockRestore();
    });
  });

  describe('table labels', () => {
    it('names child tables readably, and title-cases an unknown table without its sprk_ prefix', async () => {
      const members = [
        M_PROJECT,
        member({ id: 'dddddddd-0000-0000-0000-000000000006', table: 'sprk_kpiassessment', depth: 2, name: 'Q3' }),
        member({ id: 'dddddddd-0000-0000-0000-000000000007', table: 'sprk_widget_part', depth: 2, name: 'Part' }),
      ];
      renderModal(makeProps(() => ok(response({ members }))));
      await screen.findByText('Q3');
      expect(screen.getByText(/Level 2\b.*KPI Assessment/)).toBeInTheDocument();
      expect(screen.getByText(/Level 2\b.*Widget Part/)).toBeInTheDocument();
    });
  });

  describe('adr-021-dark-mode', () => {
    it('renders the loaded, truncated list under webDarkTheme with no console errors', async () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      renderModal(
        makeProps(() => ok(response({ truncated: true, continuation: 'tok' }))),
        webDarkTheme
      );

      await screen.findByText('Diligence Project');
      expect(screen.getByRole('button', { name: /load more/i })).toBeInTheDocument();
      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });

    it('uses no hard-coded colours in its sources (Fluent semantic tokens only)', () => {
      const dir = path.join(__dirname, '..');
      const sources = fs.readdirSync(dir).filter(f => /\.(ts|tsx)$/.test(f));
      expect(sources.length).toBeGreaterThan(0);
      for (const file of sources) {
        const text = fs.readFileSync(path.join(dir, file), 'utf8');
        const code = text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
        expect(code).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
        expect(code).not.toMatch(/\b(rgb|rgba|hsl|hsla)\(/);
      }
    });
  });
});
