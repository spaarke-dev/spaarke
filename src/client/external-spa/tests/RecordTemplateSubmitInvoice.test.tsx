/**
 * R4 task 007 (FR-10) — "Submit invoice" in the record template: shown on a Matter, Project or Work assignment only when
 * `/me` `firms` is a non-empty array AND the firm-records answer of at least one of those firms lists the record; it leaves
 * through the host's `onNavigate` (no router in the template) to `/invoices/submit?invoiceRecord={kind}:{id}`; never on an
 * Invoice, a Service request or a Document.
 *
 * What runs for real: `RecordTemplateModal`, the record read, `RecordCommandBar`, `SubmitInvoiceAction` (the host that
 * reads the firm-records answers), `recordAccess.submitInvoiceRightOf`, `readInvoiceFirmRecords` and `SprkModal`. What is
 * substituted: the network seam (`bffApiCall`: the record read and the firm-records route), `/me`
 * (`getExternalUserContext`), and the sections (stand-ins: nothing in them reads or offers a toolbar action).
 *
 * Every "hidden" case is a one-variable twin of the "visible" case and is asserted only AFTER the reads it depends on have
 * been made and have settled, so an absent button cannot pass merely because it is early. The landing on the submission
 * page (the wizard shows the record chosen) is proven in InvoiceSubmitPage.test.tsx.
 */
import * as React from 'react';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn(), getExternalUserContext: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall, getExternalUserContext: h.getExternalUserContext };
});

import { ApiError } from '../src/types';
import { RECORD_DETAIL_ENTITIES } from '../src/components/detail/RecordDetailFields';
import { RECORD_TEMPLATES } from '../src/components/record/recordTemplates';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import type { RecordSectionContext, RecordSectionId } from '../src/components/record/recordTemplate.types';
import { invoiceSubmitPathFor } from '../src/pages/invoiceSubmitLink';
import { installLayout, uninstallLayout } from './jsdomLayout';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SLOW = { timeout: 60000 };
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const FIRM_A = { firmId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Alpha LLP' };
const FIRM_B = { firmId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', name: 'Beta & Co' };

/** The three types that offer it, with the firm-records list and the route token each uses. */
const OFFERING = [
  { label: 'matter', entity: 'sprk_matter', id: guid(201), list: 'matters', kind: 'matter' },
  { label: 'project', entity: 'sprk_project', id: guid(101), list: 'projects', kind: 'project' },
  {
    label: 'work assignment',
    entity: 'sprk_workassignment',
    id: guid(301),
    list: 'workAssignments',
    kind: 'workassignment',
  },
] as const;
const MATTER = OFFERING[0];

type FirmAnswer =
  | { matters?: string[]; projects?: string[]; workAssignments?: string[]; partial?: boolean; truncated?: boolean }
  | 'fail'
  | 'pending';

interface Net {
  /** `/me`: a caller context, or 'fail'. */
  me: Record<string, unknown> | 'fail';
  /** The firm-records answer per firm id. */
  firmRecords: Record<string, FirmAnswer>;
}

const meOf = (extra: Record<string, unknown> = {}): Record<string, unknown> => ({
  contactId: guid(7),
  email: 'me@example.test',
  projects: [],
  ...extra,
});

/** A firm-records body listing `id` under `list` (and nothing under the others). */
const listing = (list: (typeof OFFERING)[number]['list'], id: string): FirmAnswer => ({ [list]: [id] });
const listsNothing: FirmAnswer = { matters: [], projects: [], workAssignments: [] };

function install(net: Net): void {
  h.getExternalUserContext.mockImplementation(async () => {
    if (net.me === 'fail') throw new ApiError(500, '');
    return net.me;
  });
  h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    if (path.endsWith('/api/v1/external/api/dataverse/fetch')) {
      const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
      const id = /operator="eq" value="([^"]+)"/.exec(body.fetchXml)?.[1] ?? '';
      const detail = RECORD_DETAIL_ENTITIES[body.entityName];
      const config = RECORD_TEMPLATES[body.entityName];
      return {
        entities: [
          {
            [detail.idAttribute]: id,
            [config.nameAttribute]: `${config.label} name`,
            ...(config.titleNumberAttribute ? { [config.titleNumberAttribute]: 'NUM-1' } : {}),
          },
        ],
        moreRecords: false,
      };
    }
    const firm = /\/invoice-submissions\/firm-records\?firmId=([^&]+)/.exec(path);
    if (firm) {
      const answer = net.firmRecords[decodeURIComponent(firm[1])];
      if (answer === 'pending') return new Promise(() => undefined);
      if (answer === undefined || answer === 'fail') {
        throw new ApiError(500, JSON.stringify({ status: 500, title: 'broken' }));
      }
      return { firmId: decodeURIComponent(firm[1]), partial: false, truncated: false, ...answer };
    }
    throw new Error(`unexpected BFF call ${path}`);
  });
}

const firmRecordReads = (): string[] =>
  h.bffApiCall.mock.calls
    .map(call => String(call[0]))
    .filter(path => path.includes('/invoice-submissions/firm-records'))
    .map(path => decodeURIComponent(/firmId=([^&]+)/.exec(path)?.[1] ?? ''));

const settle = (ms = 80): Promise<void> =>
  act(async () => void (await new Promise(resolve => setTimeout(resolve, ms))));

beforeEach(() => {
  installLayout();
  h.bffApiCall.mockReset();
  h.getExternalUserContext.mockReset();
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  uninstallLayout();
});

// ---------------------------------------------------------------------------
// Harness
// ---------------------------------------------------------------------------

/** A section stand-in: an empty labelled card (nothing reads, nothing is offered to the toolbar). */
const standIn = (id: RecordSectionId): React.ReactNode => (
  <section aria-label={id} data-testid={`record-section-${id}`} />
);

/** A section stand-in that holds the view busy, as an operation in flight does. */
const BusySection: React.FC<{ ctx: RecordSectionContext }> = ({ ctx }) => {
  const { onBusyChange } = ctx;
  React.useEffect(() => {
    onBusyChange(true);
    return () => onBusyChange(false);
  }, [onBusyChange]);
  return null;
};

interface OpenOptions {
  entity?: string;
  ids?: readonly string[];
  onNavigate?: ((path: string) => void) | null;
  onClose?: () => void;
  busy?: boolean;
}

async function openRecord(opts: OpenOptions = {}): Promise<HTMLElement> {
  const entity = opts.entity ?? MATTER.entity;
  const ids = opts.ids ?? [MATTER.id];
  const onNavigate = opts.onNavigate === undefined ? () => undefined : (opts.onNavigate ?? undefined);
  const Harness: React.FC = () => {
    const [index, setIndex] = React.useState(0);
    return (
      <FluentProvider theme={webLightTheme}>
        <RecordTemplateModal
          open
          onClose={opts.onClose ?? (() => undefined)}
          config={RECORD_TEMPLATES[entity]}
          recordIds={ids}
          index={index}
          onIndexChange={setIndex}
          onNavigate={onNavigate}
          renderSection={(id, ctx) => (opts.busy && id === 'information' ? <BusySection ctx={ctx} /> : standIn(id))}
        />
      </FluentProvider>
    );
  };
  render(<Harness />);
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-template-columns');
  return dialog;
}

/** The ids of the toolbar buttons that are rendered, in order. */
const toolbarOf = (dialog: HTMLElement): string[] =>
  within(dialog)
    .queryAllByTestId(/^header-toolbar-button-/)
    .map(b => (b.getAttribute('data-testid') ?? '').replace('header-toolbar-button-', ''));

const submitButton = (dialog: HTMLElement): HTMLElement | null =>
  within(dialog).queryByRole('button', { name: /submit invoice/i });

/** The reads this opening makes have been made and answered, and the toolbar has had time to react. */
async function settledAfterReads(expectedFirmReads: number): Promise<void> {
  await waitFor(() => expect(firmRecordReads()).toHaveLength(expectedFirmReads), SLOW);
  await settle();
}

// ---------------------------------------------------------------------------
// Visible
// ---------------------------------------------------------------------------

describe('Submit invoice: offered', () => {
  it.each(OFFERING)(
    '$label: visible when /me firms is non-empty and the firm lists the record; the config lists it',
    async ({ entity, id, list }) => {
      install({ me: meOf({ firms: [FIRM_A] }), firmRecords: { [FIRM_A.firmId]: listing(list, id) } });
      const dialog = await openRecord({ entity, ids: [id] });

      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);
      expect(toolbarOf(dialog)).toContain('submitInvoice');
      expect(RECORD_TEMPLATES[entity].toolbar).toContain('submitInvoice');
    },
    SLOW
  );

  it(
    'the id is matched without regard to case',
    async () => {
      install({
        me: meOf({ firms: [FIRM_A] }),
        firmRecords: { [FIRM_A.firmId]: { matters: [MATTER.id.toUpperCase()] } },
      });
      const dialog = await openRecord();
      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);
    },
    SLOW
  );

  it(
    'a caller with several firms where only the SECOND firm lists the record: visible',
    async () => {
      install({
        me: meOf({ firms: [FIRM_A, FIRM_B] }),
        firmRecords: { [FIRM_A.firmId]: listsNothing, [FIRM_B.firmId]: listing('matters', MATTER.id) },
      });
      const dialog = await openRecord();

      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);
      expect([...firmRecordReads()].sort()).toEqual([FIRM_A.firmId, FIRM_B.firmId].sort());
    },
    SLOW
  );

  it(
    'a record that is listed stays offered when the same answer is flagged partial or truncated (it IS linked)',
    async () => {
      install({
        me: meOf({ firms: [FIRM_A] }),
        firmRecords: { [FIRM_A.firmId]: { matters: [MATTER.id], partial: true, truncated: true } },
      });
      const dialog = await openRecord();
      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Hidden
// ---------------------------------------------------------------------------

describe('Submit invoice: hidden (not rendered, never disabled)', () => {
  it.each([
    ['firms absent', meOf()],
    ['firms null (eBilling off or unknown)', meOf({ firms: null })],
    ['firms empty', meOf({ firms: [] })],
    ['a /me that fails', 'fail' as const],
  ])(
    '%s: no action and no firm-records read',
    async (_name, me) => {
      // The record IS listed for a firm: only /me's answer is withheld, so nothing but the /me rule can hide it.
      install({ me, firmRecords: { [FIRM_A.firmId]: listing('matters', MATTER.id) } });
      const dialog = await openRecord();
      await waitFor(() => expect(h.getExternalUserContext).toHaveBeenCalled(), SLOW);
      await settle();

      expect(submitButton(dialog)).toBeNull();
      expect(toolbarOf(dialog)).not.toContain('submitInvoice');
      expect(firmRecordReads()).toEqual([]);
    },
    SLOW
  );

  it(
    'firms but the record is not listed for any of them',
    async () => {
      install({
        me: meOf({ firms: [FIRM_A, FIRM_B] }),
        firmRecords: {
          [FIRM_A.firmId]: { matters: [guid(299)], projects: [MATTER.id], workAssignments: [MATTER.id] },
          [FIRM_B.firmId]: listsNothing,
        },
      });
      const dialog = await openRecord();
      await settledAfterReads(2);

      // The same id under ANOTHER kind does not count, and neither does another matter.
      expect(submitButton(dialog)).toBeNull();
      expect(toolbarOf(dialog)).toEqual(['refresh', 'copyLink']);
    },
    SLOW
  );

  it(
    'a failed firm-records read is hidden',
    async () => {
      install({ me: meOf({ firms: [FIRM_A] }), firmRecords: { [FIRM_A.firmId]: 'fail' } });
      const dialog = await openRecord();
      await settledAfterReads(1);
      expect(submitButton(dialog)).toBeNull();
    },
    SLOW
  );

  it(
    'one firm fails and the other lists the record: visible (a failed firm hides only itself)',
    async () => {
      install({
        me: meOf({ firms: [FIRM_A, FIRM_B] }),
        firmRecords: { [FIRM_A.firmId]: 'fail', [FIRM_B.firmId]: listing('matters', MATTER.id) },
      });
      const dialog = await openRecord();
      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);
    },
    SLOW
  );

  it.each([
    ['truncated', { matters: [guid(299)], truncated: true }],
    ['partial', { matters: [guid(299)], partial: true }],
    ['partial and truncated', { projects: [], partial: true, truncated: true }],
  ] as const)(
    'a %s answer that does not list the record is hidden (never shown on firms alone)',
    async (_name, answer) => {
      install({ me: meOf({ firms: [FIRM_A] }), firmRecords: { [FIRM_A.firmId]: answer } });
      const dialog = await openRecord();
      await settledAfterReads(1);
      expect(submitButton(dialog)).toBeNull();
    },
    SLOW
  );

  it(
    'an answer that has not arrived is hidden (unknown / loading)',
    async () => {
      install({ me: meOf({ firms: [FIRM_A] }), firmRecords: { [FIRM_A.firmId]: 'pending' } });
      const dialog = await openRecord();
      await settledAfterReads(1);
      expect(submitButton(dialog)).toBeNull();
    },
    SLOW
  );

  it(
    'without a host onNavigate: hidden, and nothing is read for it',
    async () => {
      install({ me: meOf({ firms: [FIRM_A] }), firmRecords: { [FIRM_A.firmId]: listing('matters', MATTER.id) } });
      const dialog = await openRecord({ onNavigate: null });
      await waitFor(() => expect(h.getExternalUserContext).toHaveBeenCalled(), SLOW);
      await settle();

      expect(submitButton(dialog)).toBeNull();
      expect(firmRecordReads()).toEqual([]);
    },
    SLOW
  );

  // The twin of the visible cases above: the caller and the answers would offer it on a matter, a project or a work
  // assignment, and the record type's config simply does not list it.
  it.each([
    ['an invoice', 'sprk_invoice', guid(401)],
    ['a service request', 'sprk_servicerequest', guid(501)],
    ['a document', 'sprk_document', guid(601)],
  ])(
    'never on %s, whoever the caller is and whatever the firm lists',
    async (_name, entity, id) => {
      const everywhere = { matters: [id], projects: [id], workAssignments: [id] };
      install({ me: meOf({ firms: [FIRM_A] }), firmRecords: { [FIRM_A.firmId]: everywhere } });
      const dialog = await openRecord({ entity, ids: [id] });
      await waitFor(() => expect(h.getExternalUserContext).toHaveBeenCalled(), SLOW);
      await settle();

      expect(RECORD_TEMPLATES[entity].toolbar).not.toContain('submitInvoice');
      expect(toolbarOf(dialog)).toContain('refresh'); // the toolbar is on screen, so the absence is real
      expect(submitButton(dialog)).toBeNull();
      expect(firmRecordReads()).toEqual([]);
    },
    SLOW
  );

  it('only the matter, project and work assignment templates list it', () => {
    const listed = Object.values(RECORD_TEMPLATES)
      .filter(config => config.toolbar.includes('submitInvoice'))
      .map(config => config.entity)
      .sort();
    expect(listed).toEqual(['sprk_matter', 'sprk_project', 'sprk_workassignment']);
  });
});

// ---------------------------------------------------------------------------
// Click, reads, hold
// ---------------------------------------------------------------------------

describe('Submit invoice: the click', () => {
  it.each(OFFERING)(
    '$label: closes the record view, then navigates to the submission page with this record',
    async ({ entity, id, list, kind }) => {
      install({ me: meOf({ firms: [FIRM_A] }), firmRecords: { [FIRM_A.firmId]: listing(list, id) } });
      const order: string[] = [];
      const onNavigate = vi.fn((path: string) => void order.push(`navigate:${path}`));
      const onClose = vi.fn(() => void order.push('close'));
      const dialog = await openRecord({ entity, ids: [id], onNavigate, onClose });
      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);

      fireEvent.click(submitButton(dialog)!);

      await waitFor(() => expect(onNavigate).toHaveBeenCalledTimes(1), SLOW);
      const path = `/invoices/submit?invoiceRecord=${encodeURIComponent(`${kind}:${id}`)}`;
      expect(onNavigate).toHaveBeenCalledWith(path);
      expect(path).toBe(invoiceSubmitPathFor(kind, id));
      expect(onClose).toHaveBeenCalledTimes(1);
      expect(order).toEqual(['close', `navigate:${path}`]);
    },
    SLOW
  );

  it(
    'while an operation holds the view the click closes nothing and goes nowhere',
    async () => {
      install({ me: meOf({ firms: [FIRM_A] }), firmRecords: { [FIRM_A.firmId]: listing('matters', MATTER.id) } });
      const onNavigate = vi.fn();
      const onClose = vi.fn();
      const dialog = await openRecord({ onNavigate, onClose, busy: true });
      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);

      fireEvent.click(submitButton(dialog)!);
      await settle();

      expect(onClose).not.toHaveBeenCalled();
      expect(onNavigate).not.toHaveBeenCalled();
    },
    SLOW
  );
});

describe('Submit invoice: reads', () => {
  it(
    'one firm-records read per firm for the opening: not per render, and browsing to the next record reads nothing more',
    async () => {
      const second = guid(202);
      install({
        me: meOf({ firms: [FIRM_A, FIRM_B] }),
        firmRecords: {
          [FIRM_A.firmId]: { matters: [MATTER.id, second] },
          [FIRM_B.firmId]: listsNothing,
        },
      });
      const dialog = await openRecord({ ids: [MATTER.id, second] });
      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);
      await settledAfterReads(2);

      // Page to the next record of the same opening: the action is offered for it from the same answers.
      fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
      await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Previous record' })).not.toBeNull(), SLOW);
      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);
      await settle();

      expect([...firmRecordReads()].sort()).toEqual([FIRM_A.firmId, FIRM_B.firmId].sort());
    },
    SLOW
  );

  it(
    'a record on the browse set that no firm lists has no action, while its neighbour keeps it',
    async () => {
      const unlisted = guid(203);
      install({
        me: meOf({ firms: [FIRM_A] }),
        firmRecords: { [FIRM_A.firmId]: { matters: [MATTER.id] } },
      });
      const dialog = await openRecord({ ids: [MATTER.id, unlisted] });
      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);

      fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
      await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Previous record' })).not.toBeNull(), SLOW);
      await within(dialog).findByTestId('record-template-columns');
      await settle();

      expect(submitButton(dialog)).toBeNull();
      expect(firmRecordReads()).toEqual([FIRM_A.firmId]);
    },
    SLOW
  );
});

describe('Submit invoice: the template imports no router', () => {
  it.each([
    'src/components/record/RecordTemplateModal.tsx',
    'src/components/record/recordAccess.ts',
    'src/components/record/recordTemplates.ts',
    'src/components/record/actions/SubmitInvoiceAction.tsx',
    'src/components/detail/RecordOpener.tsx',
    'src/widgets/GridWidgetBody.tsx',
  ])('%s', file => {
    const source = readFileSync(resolve(__dirname, '..', file), 'utf8');
    expect(source).not.toMatch(/react-router/);
  });

  // The action builds the address from the router-free link module; importing the page would pull in the router and the
  // shared wizard (R4 task 007 fix round 1, F3).
  it('SubmitInvoiceAction does not import the page module (or the wizard)', () => {
    const source = readFileSync(
      resolve(__dirname, '..', 'src/components/record/actions/SubmitInvoiceAction.tsx'),
      'utf8'
    );
    const imports = [...source.matchAll(/from\s+'([^']+)'/g)].map(m => m[1]);
    expect(imports).toContain('../../../pages/invoiceSubmitLink');
    for (const spec of imports) {
      expect(spec, spec).not.toMatch(/pages\/InvoiceSubmitPage/);
      expect(spec, spec).not.toMatch(/ui-components\/components\/InvoiceSubmission/);
    }
  });

  it('the link module imports no router and nothing at runtime (a type-only import of the kind union)', () => {
    const source = readFileSync(resolve(__dirname, '..', 'src/pages/invoiceSubmitLink.ts'), 'utf8');
    // Every import is `import type`: nothing is loaded when the module is.
    const importLines = source.split('\n').filter(line => /^import\b/.test(line));
    expect(importLines.length).toBeGreaterThan(0);
    for (const line of importLines) expect(line).toMatch(/^import type\b/);
  });

  // The shell is the one place the opener gets the router's navigate; without it the action is hidden on every record and
  // only a browser check would notice (R4 task 007 re-check F3). No test renders App, so pin its source: it mounts the
  // shared `RouterRecordOpener` (the bridge the shell test runs) and no bare `RecordOpenerProvider` without `onNavigate`.
  it('App.tsx mounts the shared RouterRecordOpener, not a bare RecordOpenerProvider', () => {
    const source = readFileSync(resolve(__dirname, '..', 'src/App.tsx'), 'utf8');
    expect(source).toMatch(
      /import\s*\{\s*RouterRecordOpener\s*\}\s*from\s*'\.\/components\/detail\/RouterRecordOpener'/
    );
    expect(source).toMatch(/<RouterRecordOpener>/);
    expect(source).not.toMatch(/<RecordOpenerProvider\b/);
    const bridge = readFileSync(resolve(__dirname, '..', 'src/components/detail/RouterRecordOpener.tsx'), 'utf8');
    expect(bridge).toMatch(/<RecordOpenerProvider onNavigate=\{navigate\}>/);
  });
});
