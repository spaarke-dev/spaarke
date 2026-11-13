/**
 * R4 task 003 (FR-01 / FR-02) — the config-driven record template: `RECORD_TEMPLATES`, `recordLinkFor` and
 * `RecordTemplateModal`.
 *
 * What runs for real: `RecordTemplateModal`, `RecordTemplateHeader`, the moved R3 read (`useRecord`), the canonical
 * `SprkModal`, the shared `RecordCommandBar`, `record-targets`. What is substituted: the network seam (`bffApiCall`,
 * answering the module fetch the record read uses), the caller's `/me` read (`getExternalUserContext`) and the clipboard
 * (jsdom has none). Section renderers are test stand-ins (`renderSection`; the real sections and the action hosts are
 * proven in RecordTemplateSections.test.tsx and the ported R3 suites, task 005); the default stand-in renders an empty
 * labelled card per section where a test does not care.
 *
 * Layout at 1440 / 1000 / 375px is a browser check (jsdom has no layout): here the CSS contract is proven instead — the
 * columns element carries exactly the 66/34 + stack-below-1000px classes, and the content element the thin scrollbar.
 */
import * as React from 'react';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, makeStyles, webLightTheme } from '@fluentui/react-components';
import { thinScrollbarStyle } from '@spaarke/ui-components/theme/scrollbar';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn(), getExternalUserContext: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall, getExternalUserContext: h.getExternalUserContext };
});

import { ApiError } from '../src/types';
import { RECORD_DETAIL_ENTITIES } from '../src/components/detail/RecordDetailFields';
import {
  FEED_RECORD_TYPES,
  RECORD_LINK_TOKENS,
  parseRecordLink,
  recordLinkFor,
} from '../src/components/detail/record-targets';
import { RECORD_TEMPLATES, getRecordTemplate, recordActionIdsOf } from '../src/components/record/recordTemplates';
import {
  RECORD_SECTION_LABELS,
  type RecordSectionId,
  type RecordTemplateConfig,
} from '../src/components/record/recordTemplate.types';
import { RecordTemplateModal, type RecordSectionContext } from '../src/components/record/RecordTemplateModal';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const IDS = [guid(201), guid(202), guid(203)];

type Read = (entity: string, id: string) => Record<string, unknown> | null | Promise<Record<string, unknown> | null>;

/** Answers the module fetch (the one record read) with `read(entity, id)`; `null` = no row (the denied answer). */
function installRead(read: Read): void {
  h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    if (!path.endsWith('/api/v1/external/api/dataverse/fetch')) throw new Error(`unexpected BFF call ${path}`);
    const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
    const id = /operator="eq" value="([^"]+)"/.exec(body.fetchXml)?.[1] ?? '';
    const row = await read(body.entityName, id);
    return { entities: row ? [row] : [], moreRecords: false };
  });
}

const matterRow = (id: string, over: Record<string, unknown> = {}): Record<string, unknown> => ({
  sprk_matterid: id,
  sprk_mattername: `Matter name ${id.slice(-3)}`,
  sprk_matternumber: `MAT-${id.slice(-3)}`,
  statuscode: 1,
  '@formattedValues': { statuscode: 'Active' },
  ...over,
});

/** A row for any of the six entities: its id column, a number and a name. */
function rowFor(entity: string, id: string): Record<string, unknown> {
  const detail = RECORD_DETAIL_ENTITIES[entity];
  const config = RECORD_TEMPLATES[entity];
  return {
    [detail.idAttribute]: id,
    [config.nameAttribute]: `${config.label} name`,
    ...(config.titleNumberAttribute ? { [config.titleNumberAttribute]: 'NUM-1' } : {}),
  };
}

const fetchCalls = (): Array<{ entityName: string; fetchXml: string }> =>
  h.bffApiCall.mock.calls
    .filter(call => String(call[0]).endsWith('/api/dataverse/fetch'))
    .map(call => JSON.parse(String((call[1] as RequestInit).body)) as { entityName: string; fetchXml: string });

interface HarnessProps {
  config?: RecordTemplateConfig;
  ids?: readonly string[];
  start?: number;
  onClose?: () => void;
  onRecordDeleted?: () => void;
  onSettled?: () => void;
  renderSection?: (id: RecordSectionId, ctx: RecordSectionContext) => React.ReactNode;
}

/** The default section stand-in: an empty labelled card per section (no reads of its own). */
const standInSection = (id: RecordSectionId): React.ReactNode => (
  <section aria-label={RECORD_SECTION_LABELS[id]} data-testid={`record-section-${id}`} />
);

/** The modal with its browse index held in state, as an opener holds it. */
const Harness: React.FC<HarnessProps> = ({ config = RECORD_TEMPLATES.sprk_matter, ids = IDS, start = 0, ...rest }) => {
  const [index, setIndex] = React.useState(start);
  return (
    <FluentProvider theme={webLightTheme}>
      <RecordTemplateModal
        open
        onClose={rest.onClose ?? (() => undefined)}
        config={config}
        recordIds={ids}
        index={index}
        onIndexChange={setIndex}
        onRecordDeleted={rest.onRecordDeleted}
        onSettled={rest.onSettled}
        renderSection={rest.renderSection ?? standInSection}
      />
    </FluentProvider>
  );
};

/** The modal's header title (SprkModal's title element). */
const titleOf = (dialog: HTMLElement): string => dialog.querySelector('[id^="sprk-modal-title-"]')?.textContent ?? '';

async function openReady(props: HarnessProps = {}): Promise<HTMLElement> {
  render(<Harness {...props} />);
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-template-columns');
  return dialog;
}

/** The atomic classes a style object compiles to (Griffel class names are a hash of property + value + media). */
function atomicClassesOf(style: Parameters<typeof makeStyles>[0][string]): string[] {
  const useProbe = makeStyles({ probe: style });
  let classes = '';
  const Probe: React.FC = () => {
    classes = useProbe().probe;
    return null;
  };
  const { unmount } = render(<Probe />);
  unmount();
  // The `___…` class names the makeStyles call itself, not a declaration.
  return classes.split(/\s+/).filter(c => c && !c.startsWith('___'));
}

beforeEach(() => {
  h.bffApiCall.mockReset();
  h.getExternalUserContext.mockReset();
  h.getExternalUserContext.mockResolvedValue({ contactId: guid(7), email: 'me@example.test', projects: [] });
  installRead((entity, id) => (entity === 'sprk_matter' ? matterRow(id) : rowFor(entity, id)));
});

afterEach(() => {
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// FR-01 — the config map
// ---------------------------------------------------------------------------

describe('RECORD_TEMPLATES', () => {
  it('has exactly the six types, with the left, right, toolbar and section-action lists (FR-01; task 009)', () => {
    // Task 009: a create action sits in the header of its own section; the record header keeps Refresh, Copy link and
    // the type's own actions. Submit invoice stays in the header until task 010 moves it to Invoices.
    const root = ['refresh', 'submitInvoice', 'copyLink'];
    const rootSections = { documents: ['upload'], messages: ['newMessage'], tasksEvents: ['addEvent'] };
    const shape = Object.fromEntries(
      Object.entries(RECORD_TEMPLATES).map(([entity, c]) => [
        entity,
        {
          label: c.label,
          left: [...c.left],
          right: [...c.right],
          toolbar: [...c.toolbar],
          sectionActions: Object.fromEntries(
            Object.entries(c.sectionActions ?? {}).map(([s, ids]) => [s, [...(ids ?? [])]])
          ),
        },
      ])
    );
    expect(shape).toEqual({
      sprk_project: {
        label: 'Project',
        left: ['information', 'documents', 'invoices'],
        right: ['messages', 'tasksEvents'],
        toolbar: root,
        sectionActions: rootSections,
      },
      sprk_matter: {
        label: 'Matter',
        left: ['information', 'documents', 'invoices'],
        right: ['messages', 'tasksEvents'],
        toolbar: root,
        sectionActions: rootSections,
      },
      sprk_workassignment: {
        label: 'Work assignment',
        left: ['information', 'documents'],
        right: ['messages', 'tasksEvents'],
        toolbar: root,
        sectionActions: rootSections,
      },
      sprk_invoice: {
        label: 'Invoice',
        left: ['information', 'files'],
        right: ['messages'],
        toolbar: ['refresh', 'copyLink'],
        sectionActions: { messages: ['newMessage'] },
      },
      sprk_servicerequest: {
        label: 'Service request',
        left: ['information', 'documents'],
        right: ['messages'],
        toolbar: ['refresh', 'copyLink', 'delete'],
        sectionActions: { documents: ['upload'], messages: ['newMessage'] },
      },
      sprk_document: {
        label: 'Document',
        left: ['information', 'preview'],
        right: [],
        toolbar: ['refresh', 'download', 'copyLink'],
        sectionActions: {},
      },
    });
    // No create action is left in any record header, and no section action names a section the record does not show.
    for (const c of Object.values(RECORD_TEMPLATES)) {
      for (const id of ['upload', 'newMessage', 'addEvent']) expect(c.toolbar).not.toContain(id);
      for (const section of Object.keys(c.sectionActions ?? {})) expect([...c.left, ...c.right]).toContain(section);
    }
    // Tabs are off by default for every type (owner 2026-11-08).
    for (const c of Object.values(RECORD_TEMPLATES)) expect(c.tabs).toBeUndefined();
  });

  it('lists each action once per record: never in the header and a section, nor in two sections (task 009 review)', () => {
    // One host per action id: an id listed twice would mount two hosts and show two buttons for one action.
    const repeatedIn = (ids: readonly string[]): string[] => ids.filter((id, i) => ids.indexOf(id) !== i);
    // Control: the check sees a repeat when there is one.
    expect(
      repeatedIn(recordActionIdsOf({ ...RECORD_TEMPLATES.sprk_matter, toolbar: ['refresh', 'upload', 'copyLink'] }))
    ).toEqual(['upload']);
    for (const [entity, c] of Object.entries(RECORD_TEMPLATES)) {
      expect({ entity, repeated: repeatedIn(recordActionIdsOf(c)) }).toEqual({ entity, repeated: [] });
    }
  });

  it('gives every deep-link token and every notification record type a template', () => {
    const linked = new Set([...Object.values(RECORD_LINK_TOKENS), ...Object.values(FEED_RECORD_TYPES)]);
    for (const entity of linked) expect(getRecordTemplate(entity)?.entity).toBe(entity);
    expect(getRecordTemplate('sprk_gridconfiguration')).toBeUndefined();
    expect(getRecordTemplate('constructor')).toBeUndefined();
  });

  it('names only columns the record read already fetches (no new column, no new read)', () => {
    for (const c of Object.values(RECORD_TEMPLATES)) {
      const read = new Set(RECORD_DETAIL_ENTITIES[c.entity].fields.map(f => f.attribute));
      const named = [c.nameAttribute, ...(c.titleNumberAttribute ? [c.titleNumberAttribute] : [])];
      for (const attribute of [...named, ...c.headerFacts.map(f => f.attribute)]) {
        expect(read.has(attribute), `${c.entity}.${attribute}`).toBe(true);
      }
      expect(c.headerFacts.length).toBeLessThanOrEqual(3);
    }
  });

  it('the modal source has no per-entity branch: it renders only from the config', () => {
    const source = readFileSync(resolve(__dirname, '../src/components/record/RecordTemplateModal.tsx'), 'utf8');
    expect(source).not.toMatch(/sprk_[a-z]/);
  });
});

// ---------------------------------------------------------------------------
// Copy link's builder — the inverse of parseRecordLink
// ---------------------------------------------------------------------------

describe('recordLinkFor', () => {
  const base = { origin: 'https://portal.example.test', pathname: '/' };

  it.each(Object.entries(RECORD_LINK_TOKENS))('round-trips the %s token through parseRecordLink', (token, entity) => {
    const link = recordLinkFor(entity, IDS[0].toUpperCase(), base);
    expect(link).toBe(`https://portal.example.test/?record=${token}:${IDS[0]}`);
    expect(parseRecordLink(new URL(link!).search)).toEqual({ entityName: entity, recordId: IDS[0] });
  });

  it('keeps the path but drops every other query parameter and the hash', () => {
    expect(recordLinkFor('sprk_matter', IDS[0], { origin: 'https://p.test', pathname: '/projects/x' })).toBe(
      `https://p.test/projects/x?record=matter:${IDS[0]}`
    );
  });

  it('makes no link for an entity without a token or an id that is not a GUID', () => {
    expect(recordLinkFor('sprk_gridconfiguration', IDS[0], base)).toBeNull();
    expect(recordLinkFor('sprk_matter', '"/><x/>', base)).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// FR-02 — title, chrome, layout
// ---------------------------------------------------------------------------

describe('title and header', () => {
  it('is "{label} {number}" for a ready record, with the header facts and no "Your access" yet', async () => {
    const dialog = await openReady();
    expect(titleOf(dialog)).toBe('Matter MAT-201');
    const header = within(dialog).getByTestId('record-template-header');
    expect(within(header).getByRole('heading', { name: 'Matter name 201' })).toBeTruthy();
    expect(within(header).getByText('Status')).toBeTruthy();
    expect(within(header).getByText('Active')).toBeTruthy();
    expect(within(dialog).queryByTestId('record-template-access')).toBeNull();
  });

  it('falls back to "{label} {name}", then to "{label}"', async () => {
    installRead((_e, id) =>
      id === IDS[0]
        ? matterRow(id, { sprk_matternumber: null })
        : matterRow(id, {
            sprk_matternumber: '',
            sprk_mattername: '  ',
          })
    );
    const dialog = await openReady();
    expect(titleOf(dialog)).toBe('Matter Matter name 201');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
    await waitFor(() => expect(within(dialog).getByText('2 of 3')).toBeTruthy());
    await waitFor(() => expect(within(dialog).getByTestId('record-template-header')).toBeTruthy());
    expect(titleOf(dialog)).toBe('Matter');
  });

  it('a document (no number column) is titled by its name and laid out in one column', async () => {
    const dialog = await openReady({ config: RECORD_TEMPLATES.sprk_document, ids: [IDS[0]] });
    expect(titleOf(dialog)).toBe('Document Document name');
    const columns = within(dialog).getByTestId('record-template-columns');
    expect(columns.getAttribute('data-columns')).toBe('1');
    expect(within(dialog).queryByTestId('record-template-right')).toBeNull();
    expect(
      within(columns)
        .getAllByRole('region')
        .map(s => s.getAttribute('aria-label'))
    ).toEqual(['Information', 'Preview']);
  });
});

describe('fixed chrome and scrolling content', () => {
  it('keeps the toolbar and header outside the scrolling content, which carries the thin scrollbar', async () => {
    const dialog = await openReady();
    const content = within(dialog).getByTestId('record-template-content');
    const toolbar = within(dialog).getByRole('toolbar', { name: 'Matter actions' });
    const header = within(dialog).getByTestId('record-template-header');
    expect(content.contains(toolbar)).toBe(false);
    expect(content.contains(header)).toBe(false);
    // The sections are inside the scrolling element.
    expect(content.contains(within(dialog).getByTestId('record-template-columns'))).toBe(true);
    // Chrome comes first in the template root, then the content.
    const root = within(dialog).getByTestId('record-template-body');
    expect(root.lastElementChild).toBe(content);

    const thin = atomicClassesOf({ ...thinScrollbarStyle });
    expect(thin.length).toBeGreaterThan(0);
    for (const c of thin) expect(content.classList.contains(c)).toBe(true);
    for (const c of atomicClassesOf({ overflowY: 'auto' })) expect(content.classList.contains(c)).toBe(true);
  });

  it('lays the overview out in 66% / 34% columns that stack below 1000px, left column first', async () => {
    const dialog = await openReady();
    const columns = within(dialog).getByTestId('record-template-columns');
    expect(columns.getAttribute('data-columns')).toBe('2');
    const layout = atomicClassesOf({
      gridTemplateColumns: 'minmax(0, 66fr) minmax(0, 34fr)',
      '@media (max-width: 999.98px)': { gridTemplateColumns: 'minmax(0, 1fr)' },
    });
    for (const c of layout) expect(columns.classList.contains(c)).toBe(true);

    const [left, right] = Array.from(columns.children);
    expect(left.getAttribute('data-testid')).toBe('record-template-left');
    expect(right.getAttribute('data-testid')).toBe('record-template-right');
    const labels = (el: Element): Array<string | null> =>
      within(el as HTMLElement)
        .getAllByRole('region')
        .map(s => s.getAttribute('aria-label'));
    expect(labels(left)).toEqual(['Information', 'Documents', 'Invoices']);
    expect(labels(right)).toEqual(['Messages', 'Tasks & events']);
  });
});

describe('tabs', () => {
  it('renders no TabList when the config has no tabs', async () => {
    const dialog = await openReady();
    expect(within(dialog).queryByRole('tablist')).toBeNull();
  });

  it('with two tabs, renders the TabList outside the content and switches what the content shows', async () => {
    const config: RecordTemplateConfig = {
      ...RECORD_TEMPLATES.sprk_matter,
      tabs: [
        { id: 'overview', label: 'Overview' },
        { id: 'messages', label: 'Messages' },
      ],
    };
    const seen: Array<{ id: RecordSectionId; full: boolean }> = [];
    const dialog = await openReady({
      config,
      renderSection: (id, ctx) => {
        seen.push({ id, full: ctx.full });
        return <section aria-label={id} data-testid={`stand-in-${id}`} />;
      },
    });
    const tablist = within(dialog).getByRole('tablist', { name: 'Matter sections' });
    const content = within(dialog).getByTestId('record-template-content');
    expect(content.contains(tablist)).toBe(false);
    expect(within(content).getByTestId('stand-in-documents')).toBeTruthy();

    seen.length = 0;
    fireEvent.click(within(tablist).getByRole('tab', { name: 'Messages' }));
    await waitFor(() => expect(within(content).queryByTestId('record-template-columns')).toBeNull());
    expect(within(content).getByTestId('stand-in-messages')).toBeTruthy();
    expect(within(content).queryByTestId('stand-in-documents')).toBeNull();
    expect(seen).toContainEqual({ id: 'messages', full: true });

    fireEvent.click(within(tablist).getByRole('tab', { name: 'Overview' }));
    await within(content).findByTestId('record-template-columns');
  });
});

// ---------------------------------------------------------------------------
// FR-02 — states (R3's text and test ids)
// ---------------------------------------------------------------------------

describe('states', () => {
  it('loading: the spinner, and no toolbar, header or section', async () => {
    installRead(() => new Promise(() => undefined));
    render(<Harness />);
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Loading record...')).toBeTruthy();
    expect(titleOf(dialog)).toBe('Matter');
    expect(within(dialog).queryByTestId('record-template-chrome')).toBeNull();
    expect(within(dialog).queryByRole('region')).toBeNull();
  });

  it('denied: R3 text, and no field, section, toolbar, header or record value in the DOM', async () => {
    installRead(() => null);
    render(<Harness />);
    const dialog = await screen.findByRole('dialog');
    const denied = await within(dialog).findByTestId('record-detail-denied');
    expect(denied.textContent).toContain("You don't have access to this record");
    expect(within(dialog).queryByTestId('record-template-chrome')).toBeNull();
    expect(within(dialog).queryByTestId('record-template-columns')).toBeNull();
    expect(within(dialog).queryByRole('region')).toBeNull();
    expect(within(dialog).queryByRole('toolbar', { name: 'Matter actions' })).toBeNull();
    expect(titleOf(dialog)).toBe('Matter');
  });

  it('a 403 is denied too, not an error', async () => {
    installRead(() => {
      throw new ApiError(403, '{"errorCode":"DV_RECORD_NOT_ACCESSIBLE"}');
    });
    render(<Harness />);
    await screen.findByTestId('record-detail-denied');
    expect(screen.queryByTestId('record-detail-error')).toBeNull();
  });

  it('not found: R3 text on a 404', async () => {
    installRead(() => {
      throw new ApiError(404, 'gone');
    });
    render(<Harness />);
    const notFound = await screen.findByTestId('record-detail-notfound');
    expect(notFound.textContent).toContain('This record is no longer available');
  });

  it('error: a failed read offers Try again, which reads again', async () => {
    let fail = true;
    installRead((_e, id) => {
      if (fail) throw new ApiError(500, 'boom');
      return matterRow(id);
    });
    render(<Harness />);
    const dialog = await screen.findByRole('dialog');
    const error = await within(dialog).findByTestId('record-detail-error');
    expect(error.textContent).toContain('This record could not be loaded just now.');
    expect(within(dialog).queryByTestId('record-template-chrome')).toBeNull();

    fail = false;
    fireEvent.click(within(error).getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(titleOf(dialog)).toBe('Matter MAT-201'));
  });

  it('error: a malformed id is never read and offers no Try again', async () => {
    render(<Harness ids={['"/><injected/>']} />);
    const error = await screen.findByTestId('record-detail-error');
    expect(within(error).queryByRole('button', { name: 'Try again' })).toBeNull();
    expect(h.bffApiCall).not.toHaveBeenCalled();
  });

  it('deleted: a section that deleted the record shows the deleted state and tells the opener', async () => {
    const onRecordDeleted = vi.fn();
    const dialog = await openReady({
      onRecordDeleted,
      renderSection: (id, ctx) =>
        id === 'information' ? (
          <button type="button" onClick={ctx.onRecordDeleted}>
            stand-in delete
          </button>
        ) : null,
    });
    const readsBefore = fetchCalls().length;
    fireEvent.click(within(dialog).getByRole('button', { name: 'stand-in delete' }));
    const deleted = await within(dialog).findByTestId('record-detail-deleted');
    expect(deleted.textContent).toContain('This matter was deleted');
    expect(onRecordDeleted).toHaveBeenCalledTimes(1);
    expect(within(dialog).queryByTestId('record-template-chrome')).toBeNull();
    // A deleted record is never read again.
    expect(fetchCalls()).toHaveLength(readsBefore);
  });

  it('tells the opener once the record settles (loaded, denied or not found)', async () => {
    const onSettled = vi.fn();
    await openReady({ onSettled });
    await waitFor(() => expect(onSettled).toHaveBeenCalled());
  });
});

// ---------------------------------------------------------------------------
// FR-04 / FR-05 — the toolbar
// ---------------------------------------------------------------------------

describe('toolbar', () => {
  it.each(Object.keys(RECORD_TEMPLATES))(
    '%s: only Refresh and Copy link are rendered; every other action is hidden, not disabled',
    async entity => {
      const config = RECORD_TEMPLATES[entity];
      const dialog = await openReady({ config, ids: [IDS[0]] });
      const toolbar = within(dialog).getByRole('toolbar', { name: `${config.label} actions` });
      const buttons = within(toolbar).getAllByRole('button');
      expect(buttons.map(b => b.getAttribute('aria-label'))).toEqual(['Refresh', 'Copy link']);
      expect(buttons.every(b => !(b as HTMLButtonElement).disabled)).toBe(true);
      expect(within(toolbar).queryByRole('button', { name: 'More actions' })).toBeNull();
    }
  );

  it('Refresh re-reads the record and keeps it on screen until the new answer arrives', async () => {
    let answer: ((row: Record<string, unknown>) => void) | null = null;
    let reads = 0;
    installRead((_e, id) => {
      reads += 1;
      if (reads === 1) return matterRow(id);
      return new Promise(resolveRow => {
        answer = resolveRow;
      });
    });
    const dialog = await openReady();
    expect(titleOf(dialog)).toBe('Matter MAT-201');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(fetchCalls()).toHaveLength(2));
    // Same read, same record.
    expect(fetchCalls()[1]).toEqual(fetchCalls()[0]);
    // Still on screen while the re-read is in flight.
    expect(within(dialog).getByRole('progressbar', { name: 'Refreshing record' })).toBeTruthy();
    expect(titleOf(dialog)).toBe('Matter MAT-201');
    expect(within(dialog).getByTestId('record-template-columns')).toBeTruthy();

    await act(async () => {
      answer?.(matterRow(IDS[0], { sprk_matternumber: 'MAT-NEW' }));
    });
    await waitFor(() => expect(titleOf(dialog)).toBe('Matter MAT-NEW'));
    expect(within(dialog).queryByRole('progressbar')).toBeNull();
  });

  it('Refresh tells the sections through refreshSignal', async () => {
    const signals: number[] = [];
    const dialog = await openReady({
      renderSection: (id, ctx) => {
        if (id === 'information') signals.push(ctx.refreshSignal);
        return null;
      },
    });
    const before = signals[signals.length - 1];
    fireEvent.click(within(dialog).getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(signals[signals.length - 1]).toBe(before + 1));
  });

  describe('Copy link', () => {
    afterEach(() => {
      Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
    });

    it('writes {origin}{path}?record={token}:{id} for the shown record', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined);
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
      const dialog = await openReady({ start: 1 });
      await waitFor(() => expect(titleOf(dialog)).toBe('Matter MAT-202'));

      fireEvent.click(within(dialog).getByRole('button', { name: 'Copy link' }));
      await within(dialog).findByText('Link copied.');
      const expected = `${window.location.origin}${window.location.pathname}?record=matter:${IDS[1]}`;
      expect(writeText).toHaveBeenCalledWith(expected);
      expect(parseRecordLink(new URL(expected).search)).toEqual({ entityName: 'sprk_matter', recordId: IDS[1] });
    });

    it('a clipboard failure shows an inline message with the link, not an exception', async () => {
      const writeText = vi.fn().mockRejectedValue(new Error('NotAllowedError'));
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
      const dialog = await openReady();

      fireEvent.click(within(dialog).getByRole('button', { name: 'Copy link' }));
      const notice = await within(dialog).findByTestId('record-template-notice');
      expect(notice.textContent).toContain("The link couldn't be copied.");
      expect(notice.textContent).toContain(`?record=matter:${IDS[0]}`);
      // The notice sits in the fixed chrome, and Dismiss clears it.
      expect(within(dialog).getByTestId('record-template-content').contains(notice)).toBe(false);
      fireEvent.click(within(notice).getByRole('button', { name: 'Dismiss' }));
      await waitFor(() => expect(within(dialog).queryByTestId('record-template-notice')).toBeNull());
    });

    it('no clipboard at all (an insecure context) is the same inline message', async () => {
      Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
      const dialog = await openReady();
      fireEvent.click(within(dialog).getByRole('button', { name: 'Copy link' }));
      expect((await within(dialog).findByTestId('record-template-notice')).textContent).toContain(
        "The link couldn't be copied."
      );
    });
  });
});

// ---------------------------------------------------------------------------
// FR-02 — navigation held while a section has an operation in flight
// ---------------------------------------------------------------------------

describe('holding navigation', () => {
  /** A stand-in Messages section with a "send" the test starts and finishes. */
  const busySection = (id: RecordSectionId, ctx: RecordSectionContext): React.ReactNode =>
    id === 'messages' ? (
      <section aria-label="Messages">
        <button type="button" onClick={() => ctx.onBusyChange(true)}>
          start send
        </button>
        <button type="button" onClick={() => ctx.onBusyChange(false)}>
          finish send
        </button>
      </section>
    ) : null;

  it('while busy, onBeforeNavigate blocks browsing and Close (footer, x, Escape) does not close', async () => {
    const onClose = vi.fn();
    const dialog = await openReady({ onClose, renderSection: busySection });
    fireEvent.click(within(dialog).getByRole('button', { name: 'start send' }));

    const footerClose = within(dialog)
      .getAllByRole('button', { name: 'Close' })
      .find(b => b.textContent === 'Close') as HTMLButtonElement;
    await waitFor(() => expect(footerClose.disabled).toBe(true));

    fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
    // The guard answers on a microtask; give it (and a would-be read) the chance to run.
    await new Promise(r => setTimeout(r, 20));
    expect(within(dialog).getByText('1 of 3')).toBeTruthy();
    expect(fetchCalls()).toHaveLength(1);

    for (const close of within(dialog).getAllByRole('button', { name: 'Close' })) fireEvent.click(close);
    fireEvent.keyDown(dialog, { key: 'Escape', code: 'Escape' });
    await new Promise(r => setTimeout(r, 20));
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeNull();
  });

  it('once not busy, browsing and Close work again', async () => {
    const onClose = vi.fn();
    const dialog = await openReady({ onClose, renderSection: busySection });
    fireEvent.click(within(dialog).getByRole('button', { name: 'start send' }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'finish send' }));

    fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
    await waitFor(() => expect(titleOf(dialog)).toBe('Matter MAT-202'));
    expect(within(dialog).getByText('2 of 3')).toBeTruthy();

    const footerClose = within(dialog)
      .getAllByRole('button', { name: 'Close' })
      .find(b => b.textContent === 'Close') as HTMLButtonElement;
    expect(footerClose.disabled).toBe(false);
    fireEvent.click(footerClose);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  // Task 003 review R3 (K1), decided in task 005: Refresh respects the hold. A re-read must never replace the sections
  // (unmounting the busy one and losing its result) while an operation is in flight.
  describe('Refresh and the hold (task 005)', () => {
    const toolbarNames = (dialog: HTMLElement): Array<string | null> =>
      within(within(dialog).getByRole('toolbar', { name: 'Matter actions' }))
        .getAllByRole('button')
        .map(b => b.getAttribute('aria-label'));

    it('Refresh is hidden while a section is busy, and offered again once it is not', async () => {
      const dialog = await openReady({ renderSection: busySection });
      expect(toolbarNames(dialog)).toEqual(['Refresh', 'Copy link']);

      fireEvent.click(within(dialog).getByRole('button', { name: 'start send' }));
      await waitFor(() => expect(toolbarNames(dialog)).toEqual(['Copy link']));

      fireEvent.click(within(dialog).getByRole('button', { name: 'finish send' }));
      await waitFor(() => expect(toolbarNames(dialog)).toEqual(['Refresh', 'Copy link']));
    });

    /** Opens matter 1 with its re-read held until `fail` rejects it. */
    async function refreshInFlight(): Promise<{ dialog: HTMLElement; fail: () => Promise<void> }> {
      let reject: ((err: unknown) => void) | null = null;
      let reads = 0;
      installRead((_e, id) => {
        reads += 1;
        if (reads === 1) return matterRow(id);
        return new Promise((_resolve, rejectRead) => {
          reject = rejectRead;
        });
      });
      const dialog = await openReady({ renderSection: busySection });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Refresh' }));
      await waitFor(() => expect(fetchCalls()).toHaveLength(2));
      return {
        dialog,
        fail: async () => {
          await act(async () => {
            reject?.(new ApiError(500, 'boom'));
          });
        },
      };
    }

    it('a refresh in flight when a section turns busy keeps that section on screen if the re-read fails, until the hold releases', async () => {
      const { dialog, fail } = await refreshInFlight();
      // The record (and its sections) stay on screen while the re-read is in flight; the section starts its send.
      fireEvent.click(within(dialog).getByRole('button', { name: 'start send' }));
      await fail();
      await new Promise(r => setTimeout(r, 20));

      // The failed answer does not replace the busy section: it is still mounted, with the record's chrome.
      expect(within(dialog).queryByTestId('record-detail-error')).toBeNull();
      expect(within(dialog).getByRole('button', { name: 'finish send' })).toBeTruthy();
      expect(titleOf(dialog)).toBe('Matter MAT-201');

      // Once the operation settles, the answer is shown.
      fireEvent.click(within(dialog).getByRole('button', { name: 'finish send' }));
      const error = await within(dialog).findByTestId('record-detail-error');
      expect(error.textContent).toContain('This record could not be loaded just now.');
    });

    it('with nothing busy, a failed refresh shows the error state at once (must-not-fire pair)', async () => {
      const { dialog, fail } = await refreshInFlight();
      await fail();
      await within(dialog).findByTestId('record-detail-error');
      expect(within(dialog).queryByRole('button', { name: 'start send' })).toBeNull();
    });
  });
});
