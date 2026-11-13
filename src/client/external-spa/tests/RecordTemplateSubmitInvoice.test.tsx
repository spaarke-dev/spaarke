/**
 * R4 task 007 (FR-10), moved by task 010 — "Submit invoice" in the record template: shown on a Matter, Project or Work
 * assignment only when `/me` `firms` is a non-empty array AND the firm-records answer of at least one of those firms lists
 * the record; never on an Invoice, a Service request or a Document. A Matter and a Project show it in the Invoices
 * section's header, a Work assignment (no Invoices section) in the record header. The click opens the shared submission
 * wizard in a child `SprkModal` over the record with that record fixed (`lockedRecord`); nothing navigates; Done, or any
 * other close once the server accepted the submission, re-reads the Invoices section.
 *
 * What runs for real: `RecordTemplateModal`, the record read, `RecordCommandBar`, `RecordSectionCard`,
 * `SubmitInvoiceAction` (the host that reads the firm-records answers and owns the dialog), `recordAccess.submitInvoiceRightOf`,
 * `readInvoiceFirmRecords`, `SprkModal` and the shared `InvoiceSubmissionWizard` (with its shell, file zone and result screen).
 * What is substituted: the network seam (`bffApiCall`: the record read and the firm-records route), `/me`
 * (`getExternalUserContext`), the wizard's four transport callbacks (`listInvoiceCoreRecords`, `listInvoiceFirms`,
 * `submitInvoice`, `getInvoiceSubmission`: the shell test InvoiceSubmitShell.test.tsx runs them over a fake `fetch`), and the
 * sections (stand-ins; the Invoices one draws the real section card with the template's header actions).
 *
 * Every "hidden" case is a one-variable twin of the "visible" case and is asserted only AFTER the reads it depends on have
 * been made and have settled, so an absent button cannot pass merely because it is early. The page path (`?invoiceRecord=`)
 * is proven in InvoiceSubmitPage.test.tsx.
 */
import * as React from 'react';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({
  bffApiCall: vi.fn(),
  getExternalUserContext: vi.fn(),
  listInvoiceCoreRecords: vi.fn(),
  listInvoiceFirms: vi.fn(),
  submitInvoice: vi.fn(),
  getInvoiceSubmission: vi.fn(),
}));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall, getExternalUserContext: h.getExternalUserContext };
});

vi.mock('../src/api/web-api-client', async () => {
  const actual = await vi.importActual<typeof import('../src/api/web-api-client')>('../src/api/web-api-client');
  return {
    ...actual,
    listInvoiceCoreRecords: h.listInvoiceCoreRecords,
    listInvoiceFirms: h.listInvoiceFirms,
    submitInvoice: h.submitInvoice,
    getInvoiceSubmission: h.getInvoiceSubmission,
  };
});

import { InvoiceSubmissionError } from '@spaarke/ui-components/components/InvoiceSubmission';
import { ApiError } from '../src/types';
import { RECORD_DETAIL_ENTITIES } from '../src/components/detail/RecordDetailFields';
import { RECORD_TEMPLATES, recordActionIdsOf } from '../src/components/record/recordTemplates';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import { RecordSectionCard } from '../src/components/record/sections/RecordSectionCard';
import type { RecordSectionContext, RecordSectionId } from '../src/components/record/recordTemplate.types';
import { installLayout, uninstallLayout } from './jsdomLayout';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SLOW = { timeout: 60000 };
// Waits inside a test are shorter than the test, so a stuck step fails on its own line.
const WAIT = { timeout: 12000 };
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const FIRM_A = { firmId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Alpha LLP' };
const FIRM_B = { firmId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', name: 'Beta & Co' };

/** The three types that offer it, with the firm-records list and the route token each uses. */
const OFFERING = [
  { label: 'matter', entity: 'sprk_matter', id: guid(201), list: 'matters', kind: 'matter', where: 'section' },
  { label: 'project', entity: 'sprk_project', id: guid(101), list: 'projects', kind: 'project', where: 'section' },
  {
    label: 'work assignment',
    entity: 'sprk_workassignment',
    id: guid(301),
    list: 'workAssignments',
    kind: 'workassignment',
    // No Invoices section on a work assignment (sprk_invoice has no lookup to it): the record header keeps the action.
    where: 'toolbar',
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

/**
 * The Invoices stand-in: the REAL section card with the template's header actions (where Submit invoice sits), and the
 * section's refresh signal on screen so a test can see the section being asked to re-read.
 */
const InvoicesStandIn: React.FC<{ ctx: RecordSectionContext }> = ({ ctx }) => (
  <RecordSectionCard id="invoices" actions={ctx.headerActions}>
    <output data-testid="invoices-refresh-signal">{ctx.refreshSignal}</output>
  </RecordSectionCard>
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
  onClose?: () => void;
  busy?: boolean;
}

async function openRecord(opts: OpenOptions = {}): Promise<HTMLElement> {
  const entity = opts.entity ?? MATTER.entity;
  const ids = opts.ids ?? [MATTER.id];
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
          renderSection={(id, ctx) =>
            opts.busy && id === 'information' ? (
              <BusySection ctx={ctx} />
            ) : id === 'invoices' ? (
              <InvoicesStandIn ctx={ctx} />
            ) : (
              standIn(id)
            )
          }
        />
      </FluentProvider>
    );
  };
  render(<Harness />);
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-template-columns');
  return dialog;
}

/** The ids of the `HeaderToolbar` buttons rendered inside `el`, in order. */
const actionIdsIn = (el: Element | null): string[] =>
  Array.from(el?.querySelectorAll('[data-testid^="header-toolbar-button-"]') ?? []).map(b =>
    (b.getAttribute('data-testid') ?? '').replace('header-toolbar-button-', '')
  );

/** The ids of the record header's buttons (`RecordCommandBar`), in order. */
const toolbarOf = (dialog: HTMLElement): string[] => actionIdsIn(within(dialog).queryByTestId('record-command-bar'));

/** The ids of the Invoices section's header buttons (empty when the record shows no Invoices section). */
const invoicesHeaderOf = (dialog: HTMLElement): string[] =>
  actionIdsIn(within(dialog).queryByTestId('record-section-invoices'));

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
    '$label: visible when /me firms is non-empty and the firm lists the record, in the one place its config lists it ($where)',
    async ({ entity, id, list, where }) => {
      install({ me: meOf({ firms: [FIRM_A] }), firmRecords: { [FIRM_A.firmId]: listing(list, id) } });
      const dialog = await openRecord({ entity, ids: [id] });

      await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);
      // Exactly one button, in exactly one place: the Invoices header (a matter, a project) or the record header (a work
      // assignment, which has no Invoices section). The config lists it once.
      expect(within(dialog).getAllByRole('button', { name: /submit invoice/i })).toHaveLength(1);
      expect(invoicesHeaderOf(dialog)).toEqual(where === 'section' ? ['submitInvoice'] : []);
      expect(toolbarOf(dialog)).toEqual(
        where === 'toolbar' ? ['refresh', 'submitInvoice', 'copyLink'] : ['refresh', 'copyLink']
      );
      const config = RECORD_TEMPLATES[entity];
      expect(recordActionIdsOf(config).filter(a => a === 'submitInvoice')).toHaveLength(1);
      expect(config.toolbar.includes('submitInvoice')).toBe(where === 'toolbar');
      expect(config.sectionActions?.invoices ?? []).toEqual(where === 'section' ? ['submitInvoice'] : []);
      if (where === 'section')
        expect(submitButton(dialog)!.closest('[data-testid="record-section-invoices"]')).not.toBeNull();
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
      expect(invoicesHeaderOf(dialog)).toEqual([]);
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

      expect(recordActionIdsOf(RECORD_TEMPLATES[entity])).not.toContain('submitInvoice');
      expect(toolbarOf(dialog)).toContain('refresh'); // the toolbar is on screen, so the absence is real
      expect(submitButton(dialog)).toBeNull();
      expect(firmRecordReads()).toEqual([]);
    },
    SLOW
  );

  it('only the matter, project and work assignment templates list it', () => {
    const listed = Object.values(RECORD_TEMPLATES)
      .filter(config => recordActionIdsOf(config).includes('submitInvoice'))
      .map(config => config.entity)
      .sort();
    expect(listed).toEqual(['sprk_matter', 'sprk_project', 'sprk_workassignment']);
  });
});

// ---------------------------------------------------------------------------
// Click, reads, hold
// ---------------------------------------------------------------------------

const SUBMISSION_ID = '33333333-3333-4333-8333-333333333333';
const OTHER_MATTER = { kind: 'matter', recordId: guid(299), name: 'Another matter', number: 'NUM-2' } as const;

/** The wizard's four transport callbacks: this firm lists this matter (and another), a submission is accepted at once. */
function installWizard(over: { records?: unknown; firms?: unknown } = {}): void {
  h.listInvoiceCoreRecords.mockReset();
  h.listInvoiceFirms.mockReset();
  h.submitInvoice.mockReset();
  h.getInvoiceSubmission.mockReset();
  h.listInvoiceCoreRecords.mockResolvedValue(
    over.records ?? {
      records: [{ kind: 'matter', recordId: MATTER.id, name: 'Matter name', number: 'NUM-1' }, OTHER_MATTER],
    }
  );
  h.listInvoiceFirms.mockResolvedValue(over.firms ?? [FIRM_A]);
  h.submitInvoice.mockResolvedValue({ submissionId: SUBMISSION_ID, trackingNumber: 'TRK-1' });
  h.getInvoiceSubmission.mockResolvedValue({
    submissionId: SUBMISSION_ID,
    status: 'Accepted',
    trackingNumber: 'TRK-1',
  });
}

const pdf = (): File => new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' });
const fileInputOf = (child: HTMLElement): HTMLInputElement | null => child.querySelector('input[type="file"]');
const signalOf = (dialog: HTMLElement): number =>
  Number(within(dialog).getByTestId('invoices-refresh-signal').textContent);

interface Opened {
  dialog: HTMLElement;
  button: HTMLElement;
  child: HTMLElement;
  onClose: ReturnType<typeof vi.fn>;
}

/** Open the matter, click Submit invoice in the Invoices header, and wait for the child dialog. */
async function openWizard(): Promise<Opened> {
  install({ me: meOf({ firms: [FIRM_A] }), firmRecords: { [FIRM_A.firmId]: listing('matters', MATTER.id) } });
  const onClose = vi.fn();
  const dialog = await openRecord({ onClose });
  await waitFor(() => expect(submitButton(dialog)).not.toBeNull(), SLOW);
  // tabster's aria-hidden pass over the record settles first (project gotcha, D-8 #2704).
  await settle(350);
  const button = submitButton(dialog)!;
  button.focus(); // a real click focuses the button; fireEvent.click does not
  fireEvent.click(button);
  const child = await screen.findByRole('dialog', { name: 'Submit invoice' }, { timeout: 20000 });
  return { dialog, button, child, onClose };
}

/** File, Next, the firm invoice number, Submit: until the wizard shows its result. */
async function submitThroughWizard(child: HTMLElement): Promise<void> {
  await waitFor(() => expect(fileInputOf(child)).not.toBeNull(), SLOW);
  fireEvent.change(fileInputOf(child)!, { target: { files: [pdf()] } });
  const next = await within(child).findByRole('button', { name: /^next$/i });
  await waitFor(() => expect((next as HTMLButtonElement).disabled).toBe(false), SLOW);
  fireEvent.click(next);
  fireEvent.change(await within(child).findByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
  fireEvent.click(within(child).getByRole('button', { name: /^submit invoice$/i }));
  await within(child).findByText('Invoice received', undefined, { timeout: 20000 });
}

describe('Submit invoice: the click opens a modal over the record', () => {
  it(
    'a child dialog over the record with this record fixed: no record step, no picker, one close button, nothing navigates and the record stays open',
    async () => {
      installWizard();
      const { dialog, child, onClose } = await openWizard();

      await waitFor(() => expect(fileInputOf(child)).not.toBeNull(), SLOW);
      expect(within(child).getByText('Step 1 of 2')).toBeTruthy();
      expect(within(child).queryByText('Choose the record this invoice is for')).toBeNull();
      expect(within(child).queryByRole('radiogroup', { name: 'Records' })).toBeNull();
      expect(within(child).queryByRole('radio')).toBeNull();
      // One dialog owner: the SprkModal's own x, not a second one from the embedded wizard's title bar.
      expect(within(child).getAllByRole('button', { name: /^close$/i })).toHaveLength(1);
      expect(within(child).getByRole('button', { name: /^cancel$/i })).toBeTruthy();
      // The record is behind it, still open and untouched; nothing navigated or closed.
      expect(document.body.contains(dialog)).toBe(true);
      expect(onClose).not.toHaveBeenCalled();
      // The wizard asked for the records of the caller's firm (a single-firm caller), to check this one is among them.
      expect(h.listInvoiceCoreRecords).toHaveBeenCalledWith({ firmId: FIRM_A.firmId });
    },
    SLOW
  );

  it(
    'submits for the record that was open, nothing else',
    async () => {
      installWizard();
      const { child } = await openWizard();
      await submitThroughWizard(child);

      expect(h.submitInvoice).toHaveBeenCalledTimes(1);
      const input = h.submitInvoice.mock.calls[0][0] as { record: Record<string, unknown>; firmInvoiceNumber: string };
      expect(input.record).toMatchObject({ kind: 'matter', recordId: MATTER.id, name: 'Matter name', number: 'NUM-1' });
      expect(input.firmInvoiceNumber).toBe('INV-9');
    },
    SLOW
  );

  it(
    'a record the firm does not offer: the fixed message, no picker, and submit is never called',
    async () => {
      installWizard({ records: { records: [OTHER_MATTER] } });
      const { child } = await openWizard();

      expect(await within(child).findByTestId('locked-record-missing', undefined, { timeout: 20000 })).toBeTruthy();
      expect(within(child).queryByRole('radio')).toBeNull();
      expect(fileInputOf(child)).toBeNull();
      expect(h.submitInvoice).not.toHaveBeenCalled();
    },
    SLOW
  );
});

describe('Submit invoice: closing the modal', () => {
  it(
    'Done closes it, refreshes the Invoices section once, and puts focus back on the section button; the record stays open',
    async () => {
      installWizard();
      const { dialog, button, child, onClose } = await openWizard();
      const before = signalOf(dialog);

      await submitThroughWizard(child);
      // The refresh waits for the close: nothing re-reads while the result is on screen.
      expect(signalOf(dialog)).toBe(before);
      fireEvent.click(within(child).getByRole('button', { name: /^done$/i }));

      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Submit invoice' })).toBeNull(), SLOW);
      await waitFor(() => expect(signalOf(dialog)).toBe(before + 1), SLOW);
      await waitFor(() => expect(document.activeElement).toBe(button), WAIT);
      expect(onClose).not.toHaveBeenCalled();
    },
    SLOW
  );

  it(
    'the x after a submission refreshes too (any close once the server accepted it)',
    async () => {
      installWizard();
      const { dialog, child } = await openWizard();
      const before = signalOf(dialog);

      await submitThroughWizard(child);
      fireEvent.click(within(child).getByRole('button', { name: /^close$/i }));

      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Submit invoice' })).toBeNull(), SLOW);
      await waitFor(() => expect(signalOf(dialog)).toBe(before + 1), SLOW);
    },
    SLOW
  );

  it(
    'Cancel and the x without a submission close it without a refresh, and focus returns to the section button',
    async () => {
      installWizard();
      const { dialog, button, child } = await openWizard();
      const before = signalOf(dialog);
      await waitFor(() => expect(fileInputOf(child)).not.toBeNull(), SLOW);

      fireEvent.click(within(child).getByRole('button', { name: /^cancel$/i }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Submit invoice' })).toBeNull(), SLOW);
      await waitFor(() => expect(document.activeElement).toBe(button), WAIT);

      // Reopen: a clean wizard, and the x closes it the same way.
      button.focus();
      fireEvent.click(button);
      const again = await screen.findByRole('dialog', { name: 'Submit invoice' });
      await waitFor(() => expect(fileInputOf(again)).not.toBeNull(), SLOW);
      fireEvent.click(within(again).getByRole('button', { name: /^close$/i }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Submit invoice' })).toBeNull(), SLOW);

      await settle();
      expect(signalOf(dialog)).toBe(before);
      expect(h.submitInvoice).not.toHaveBeenCalled();
    },
    SLOW
  );

  it(
    'Esc inside it closes neither the dialog nor the record (the child-dialog standard); Cancel then closes it',
    async () => {
      installWizard();
      const { dialog, child, onClose } = await openWizard();
      await waitFor(() => expect(fileInputOf(child)).not.toBeNull(), SLOW);

      expect(child.contains(document.activeElement)).toBe(true);
      fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' });
      await settle(150);

      expect(screen.getByRole('dialog', { name: 'Submit invoice' })).toBeTruthy();
      expect(document.body.contains(dialog)).toBe(true);
      expect(onClose).not.toHaveBeenCalled();

      fireEvent.click(within(child).getByRole('button', { name: /^cancel$/i }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Submit invoice' })).toBeNull(), SLOW);
      expect(onClose).not.toHaveBeenCalled();
    },
    SLOW
  );

  /** File, Next, the firm invoice number, Submit: stops while the submit request is in flight (nothing settled yet). */
  async function startSubmit(child: HTMLElement): Promise<void> {
    await waitFor(() => expect(fileInputOf(child)).not.toBeNull(), SLOW);
    fireEvent.change(fileInputOf(child)!, { target: { files: [pdf()] } });
    const next = await within(child).findByRole('button', { name: /^next$/i });
    await waitFor(() => expect((next as HTMLButtonElement).disabled).toBe(false), SLOW);
    fireEvent.click(next);
    fireEvent.change(await within(child).findByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(within(child).getByRole('button', { name: /^submit invoice$/i }));
    await waitFor(() => expect(h.submitInvoice).toHaveBeenCalledTimes(1), SLOW);
  }

  it(
    'the x while the submit request is in flight does not close it; a refusal that arrives after is shown in the wizard (review 1)',
    async () => {
      installWizard();
      let refuse!: (error: unknown) => void;
      h.submitInvoice.mockReturnValue(new Promise((_resolve, reject) => (refuse = reject)));
      const { dialog, child, onClose } = await openWizard();
      const before = signalOf(dialog);
      await startSubmit(child);

      fireEvent.click(within(child).getByRole('button', { name: /^close$/i }));
      await settle(150);
      expect(screen.getByRole('dialog', { name: 'Submit invoice' })).toBeTruthy();

      await act(async () =>
        refuse(
          new InvoiceSubmissionError({
            statusCode: 400,
            errorCode: 'ebilling.submission.rejected',
            detail: 'No such firm link.',
          })
        )
      );
      expect(await within(child).findByText(/No such firm link\./)).toBeTruthy();
      // Still open and still on the details step: the user read the refusal; nothing was accepted, so nothing refreshes.
      expect(screen.getByRole('dialog', { name: 'Submit invoice' })).toBeTruthy();
      expect(signalOf(dialog)).toBe(before);
      expect(onClose).not.toHaveBeenCalled();

      // Once settled, the x closes it again.
      fireEvent.click(within(child).getByRole('button', { name: /^close$/i }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Submit invoice' })).toBeNull(), SLOW);
      expect(signalOf(dialog)).toBe(before);
    },
    SLOW
  );

  it(
    'the x while in flight, then the server accepts it: the result screen is shown, and Done closes and refreshes once',
    async () => {
      installWizard();
      let accept!: (accepted: { submissionId: string; trackingNumber: string }) => void;
      h.submitInvoice.mockReturnValue(new Promise(resolve => (accept = resolve)));
      const { dialog, child } = await openWizard();
      const before = signalOf(dialog);
      await startSubmit(child);

      fireEvent.click(within(child).getByRole('button', { name: /^close$/i }));
      await settle(150);
      expect(screen.getByRole('dialog', { name: 'Submit invoice' })).toBeTruthy();

      await act(async () => accept({ submissionId: SUBMISSION_ID, trackingNumber: 'TRK-1' }));
      await within(child).findByText('Invoice received', undefined, { timeout: 20000 });
      expect(signalOf(dialog)).toBe(before);
      fireEvent.click(within(child).getByRole('button', { name: /^done$/i }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Submit invoice' })).toBeNull(), SLOW);
      await waitFor(() => expect(signalOf(dialog)).toBe(before + 1), SLOW);
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

  // Task 010: the action no longer leaves the record. It hosts the wizard (embedded, one dialog owner, the record fixed)
  // and builds no address, so it needs neither the link module nor the page, and never calls a navigate.
  it('SubmitInvoiceAction hosts the embedded wizard with the record locked, and does not navigate', () => {
    const source = readFileSync(
      resolve(__dirname, '..', 'src/components/record/actions/SubmitInvoiceAction.tsx'),
      'utf8'
    );
    const imports = [...source.matchAll(/from\s+'([^']+)'/g)].map(m => m[1]);
    expect(imports).toContain('@spaarke/ui-components/components/InvoiceSubmission');
    expect(imports).toContain('@spaarke/ui-components/components/SprkModal');
    for (const spec of imports) {
      expect(spec, spec).not.toMatch(/pages\//);
      expect(spec, spec).not.toMatch(/react-router/);
    }
    const code = source.replace(/\/\*[\s\S]*?\*\//g, '');
    expect(code).toMatch(/<InvoiceSubmissionWizard[\s\S]*?\bembedded\b[\s\S]*?lockedRecord=\{lockedRecord\}/);
    expect(code).not.toMatch(/\bonNavigate\b|\bnavigate\(|window\.location/);
  });

  it('the link module imports no router and nothing at runtime (a type-only import of the kind union)', () => {
    const source = readFileSync(resolve(__dirname, '..', 'src/pages/invoiceSubmitLink.ts'), 'utf8');
    // Every import is `import type`: nothing is loaded when the module is.
    const importLines = source.split('\n').filter(line => /^import\b/.test(line));
    expect(importLines.length).toBeGreaterThan(0);
    for (const line of importLines) expect(line).toMatch(/^import type\b/);
  });

  // The opener is mounted once by the shell (no router bridge: nothing in the record view navigates since task 010).
  it('App.tsx mounts the shared RecordOpenerProvider directly', () => {
    const source = readFileSync(resolve(__dirname, '..', 'src/App.tsx'), 'utf8');
    expect(source).toMatch(/import\s*\{\s*RecordOpenerProvider\s*\}\s*from\s*'\.\/components\/detail\/RecordOpener'/);
    expect(source).toMatch(/<RecordOpenerProvider>/);
    expect(source).not.toMatch(/RouterRecordOpener/);
  });
});
