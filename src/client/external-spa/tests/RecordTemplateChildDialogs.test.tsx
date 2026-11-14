/**
 * R4 task 004 (FR-03, NFR-02) — the record's child dialogs are SprkModal presets mounted INSIDE the parent record modal.
 *
 * What runs for real: `RecordTemplateModal` and its real sections (Documents, Messages), the toolbar (`RecordCommandBar`),
 * `DocumentLibrary` / `RecordMessages` with their new dialogs, the canonical `SprkModal` shell and presets, and Fluent's
 * own dialog + tabster focus management. What is substituted: the network seam (`bffApiCall`) and jsdom's missing layout
 * (see `installLayout`).
 *
 * The record under test is a SERVICE REQUEST opened by its requester: its toolbar offers Upload and New message (form
 * dialogs) and Delete request (a confirm dialog), all reachable today. Add to-do and Add event are not in a service
 * request's toolbar, so those two dialogs are proven through the lists' own `onCreateActionChange` seam (the last describe
 * block); task 006's `RecordTemplateAccess.test.tsx` drives them from a matter's and a work assignment's toolbar.
 *
 * THE FOCUS / ESC CONTRACT, and how each assertion can fail:
 *   - Tab wraps inside the child (last -> first, Shift+Tab first -> last). Control: the same probe against a non-blocking
 *     (non-trapped) SprkModal reports that focus is NOT contained.
 *   - The child is a React descendant of the section that owns it: a key pressed inside the dialog's portal reaches the
 *     section's handler. Control: a dialog rendered beside the section (the "sibling" the prototype found letting focus
 *     escape) does not.
 *   - Esc follows the modal standard (MODAL-DESIGN-SYSTEM / ADR-050, owner decision 2026-11-11): FormModal (`explicit`) and
 *     ConfirmModal (`alert`) ignore it, so with a child open Esc leaves BOTH the child and the record open and a typed
 *     value intact (focus asserted inside the child first, so the key really is pressed there). Cancel then closes the
 *     child with focus back on the invoking toolbar button, and a later Esc closes the record. This fails if the child
 *     closes on Esc, if the Esc also reaches the parent, or if focus is left on the page body (or not inside the record).
 *   - A child's accessibility is asserted: once the parent's ~250 ms aria-hidden pass has run, the open child is not inside
 *     an aria-hidden subtree.
 *   - Cancel is the first (left) footer slot and the primary action the second.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { Button, FluentProvider, webLightTheme } from '@fluentui/react-components';
import { FormModal, SprkModal } from '@spaarke/ui-components/components/SprkModal';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return {
    ...actual,
    bffApiCall: h.bffApiCall,
    bffApiBlob: vi.fn(),
    getExternalUserContext: () => h.bffApiCall('/api/v1/external/me'),
  };
});

import { ApiError, AccessLevel } from '../src/types';
import { EventsCalendar } from '../src/components/EventsCalendar';
import { SmartTodo } from '../src/components/SmartTodo';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import { RECORD_TEMPLATES } from '../src/components/record/recordTemplates';
import { renderRecordSection } from '../src/components/record/sections';
import type { RecordSectionContext } from '../src/components/record/recordTemplate.types';
import { restoreFocusTo, currentFocusTarget, type CreateActionOffer } from '../src/components/createAction';
import { installLayout, uninstallLayout } from './jsdomLayout';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const ME = guid(7);
const SR = guid(601);
const SLOW = { timeout: 60000 };

const problem = (status: number, extra: Record<string, unknown> = {}): string =>
  JSON.stringify({ status, title: 'refused', ...extra });

interface Net {
  documents: Array<Record<string, unknown>>;
  messages: Array<Record<string, unknown>>;
  /** A POST to the documents route; the answer is the 201 body. */
  onUpload?: (path: string, body: unknown) => unknown;
  /** A POST to the message thread; the answer is the 201 body. */
  onPostMessage?: (path: string, body: unknown) => unknown;
  /** Answers GET .../messages instead of `messages` (to hold or refuse the first read). */
  onReadMessages?: () => unknown;
  /** Versions of any document (GET .../versions). */
  versions?: Array<Record<string, unknown>>;
}

const doc = (n: number, name: string): Record<string, unknown> => ({
  sprk_documentid: guid(900 + n),
  sprk_name: name,
  createdon: '2026-03-01T10:00:00Z',
  hasFile: true,
});

function installNet(net: Net): void {
  h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    const method = options?.method ?? 'GET';
    if (path === '/api/v1/external/me') return { contactId: ME, email: 'me@example.test', projects: [] };
    if (path.endsWith('/api/dataverse/fetch')) {
      const body = JSON.parse(String(options?.body)) as { entityName: string };
      if (body.entityName !== 'sprk_servicerequest') return { entities: [], moreRecords: false };
      return {
        entities: [
          {
            sprk_servicerequestid: SR,
            sprk_name: 'Request R',
            sprk_servicerequestnumber: 'SR-1',
            sprk_requestedby: { id: ME, logicalName: 'contact', name: 'Me Myself' },
            statuscode: 1,
            statecode: 0,
          },
        ],
        moreRecords: false,
      };
    }
    if (/\/versions$/.test(path)) return { versions: net.versions ?? [] };
    if (/\/documents$/.test(path)) {
      if (method === 'POST') {
        if (!net.onUpload) throw new Error(`unexpected upload ${path}`);
        return net.onUpload(path, options?.body);
      }
      return { value: net.documents, canCreate: true };
    }
    if (/\/messages$/.test(path)) {
      if (method === 'POST') {
        if (!net.onPostMessage) throw new Error(`unexpected message POST ${path}`);
        return net.onPostMessage(path, JSON.parse(String(options?.body)));
      }
      if (net.onReadMessages) return net.onReadMessages();
      // Task 006: New message is offered only when the thread says the caller may post (the requester may).
      return { value: net.messages, canCreate: true };
    }
    throw new Error(`unexpected BFF call ${method} ${path}`);
  });
}

const callsOf = (method: string, pattern: RegExp): unknown[][] =>
  h.bffApiCall.mock.calls.filter(
    c => ((c[1] as RequestInit | undefined)?.method ?? 'GET') === method && pattern.test(String(c[0]))
  );

/** Lets tabster's and the dialogs' timers (focus moves, the 250 ms aria-hidden pass) run. */
const settle = (ms = 60): Promise<void> =>
  act(async () => void (await new Promise(resolve => setTimeout(resolve, ms))));

beforeEach(() => {
  installLayout();
  h.bffApiCall.mockReset();
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  vi.restoreAllMocks();
  uninstallLayout();
});

// ---------------------------------------------------------------------------
// Harness
// ---------------------------------------------------------------------------

/** The sections' React ancestors seen by a key pressed inside a dialog portal (`section:key`). */
let reachedSection: string[] = [];
const withProbe = (id: Parameters<typeof renderRecordSection>[0], ctx: RecordSectionContext): React.ReactNode => (
  <div data-testid={`probe-${id}`} onKeyDown={e => reachedSection.push(`${id}:${e.key}`)}>
    {renderRecordSection(id, ctx)}
  </div>
);

const Harness: React.FC<{ onClose: () => void; onIndexChange?: (i: number) => void }> = ({
  onClose,
  onIndexChange,
}) => {
  const [open, setOpen] = React.useState(true);
  return (
    <FluentProvider theme={webLightTheme}>
      <RecordTemplateModal
        open={open}
        onClose={() => {
          onClose();
          setOpen(false);
        }}
        config={RECORD_TEMPLATES.sprk_servicerequest}
        recordIds={[SR, guid(602)]}
        index={0}
        onIndexChange={onIndexChange ?? (() => undefined)}
        renderSection={withProbe}
      />
    </FluentProvider>
  );
};

async function openRecord(onClose: () => void = vi.fn()): Promise<HTMLElement> {
  reachedSection = [];
  render(<Harness onClose={onClose} />);
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-template-columns');
  return dialog;
}

/** Focuses a toolbar button (as a keyboard user would), activates it, and returns the dialog it opened. */
async function openChild(
  toolbarName: string,
  dialogName: string
): Promise<{ button: HTMLElement; child: HTMLElement }> {
  const button = await screen.findByRole('button', { name: toolbarName });
  button.focus();
  fireEvent.click(button);
  // ConfirmModal is dismiss=alert, which Fluent renders as an alertdialog; the other presets are dialogs.
  const child = await screen.findByRole(dialogName.startsWith('Delete') ? 'alertdialog' : 'dialog', {
    name: dialogName,
  });
  // Past the parent modal's ~250 ms aria-hidden pass (tabster): the open child must then be reachable, not hidden with it.
  await settle(350);
  expect(child.closest('[aria-hidden="true"]')).toBeNull();
  return { button, child };
}

const FOCUSABLE =
  'button:not([disabled]), textarea:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), [tabindex="0"]';
const tabbablesOf = (dialog: HTMLElement): HTMLElement[] => Array.from(dialog.querySelectorAll<HTMLElement>(FOCUSABLE));

/**
 * Presses Tab on the last tabbable and Shift+Tab on the first and reports whether focus wrapped to the other end inside
 * the dialog (what a focus trap does). Fluent's modal trap (tabster) brackets the dialog with two focusable sentinels
 * (data-tabster-dummy): a real Tab from the last control lands on the end sentinel, which sends focus to the first
 * real control, and Shift+Tab from the first lands on the start sentinel, which sends it to the last. The probe therefore
 * starts from the outermost tabbable (the sentinel when there is one) and expects the first / last REAL control. jsdom moves
 * no focus on Tab by itself, so only the trap can produce that move: a dialog without it leaves focus where it was and the
 * probe returns false (see the control test).
 */
async function tabWrapsInside(dialog: HTMLElement): Promise<boolean> {
  const tabbables = tabbablesOf(dialog);
  const real = tabbables.filter(t => !t.hasAttribute('data-tabster-dummy'));
  expect(real.length).toBeGreaterThan(1);
  const firstReal = real[0];
  const lastReal = real[real.length - 1];
  const outermostFirst = tabbables[0];
  const outermostLast = tabbables[tabbables.length - 1];

  outermostLast.focus();
  fireEvent.keyDown(outermostLast, { key: 'Tab' });
  await settle();
  const afterTab = document.activeElement;
  outermostFirst.focus();
  fireEvent.keyDown(outermostFirst, { key: 'Tab', shiftKey: true });
  await settle();
  const afterShiftTab = document.activeElement;

  const inside = (el: Element | null): boolean => !!el && dialog.contains(el);
  return inside(afterTab) && inside(afterShiftTab) && afterTab === firstReal && afterShiftTab === lastReal;
}

/** Cancel is in the first (left) footer slot, and the primary action in the second. */
function expectCancelLeft(dialog: HTMLElement, primaryName: string): void {
  const cancel = within(dialog).getByRole('button', { name: 'Cancel' });
  const primary = within(dialog).getByRole('button', { name: primaryName });
  const startSlot = cancel.parentElement as HTMLElement;
  const footer = startSlot.parentElement as HTMLElement;
  const slots = Array.from(footer.children);
  expect(slots).toHaveLength(2);
  expect(slots[0]).toBe(startSlot);
  expect(slots[1].contains(primary)).toBe(true);
  expect(slots[0].contains(primary)).toBe(false);
}

/** Esc pressed with focus asserted INSIDE the child first, so the key is really pressed there. */
async function pressEscInside(child: HTMLElement): Promise<void> {
  expect(child.contains(document.activeElement)).toBe(true);
  fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' });
  await settle(150);
}

/** The record dialog is still open (nothing closed it): connected, and the harness's `onClose` has not run. */
function expectRecordOpen(parent: HTMLElement, onClose: ReturnType<typeof vi.fn>): void {
  expect(parent.isConnected).toBe(true);
  // Read from the DOM: while a child is open the modal layer hides the record from the accessibility tree.
  expect(document.body.contains(parent)).toBe(true);
  expect(parent.querySelector('[id^="sprk-modal-title-"]')?.textContent).toBe('Service request SR-1');
  expect(onClose).not.toHaveBeenCalled();
}

// ---------------------------------------------------------------------------
// Form dialogs opened from the toolbar (Upload, New message)
// ---------------------------------------------------------------------------

describe('Upload: a FormModal inside the record modal', () => {
  it(
    'is offered by the toolbar and not by the list; opens with Cancel on the left; the section is its React ancestor',
    async () => {
      installNet({ documents: [doc(1, 'Mine.pdf')], messages: [] });
      const parent = await openRecord();
      await within(parent).findByText('Mine.pdf');

      // One entry point: the toolbar's Upload, not the list's own "Upload Document" button.
      expect(await screen.findByRole('button', { name: 'Upload documents' })).toBeTruthy();
      expect(screen.queryByRole('button', { name: /Upload Document/ })).toBeNull();

      const { child } = await openChild('Upload documents', 'Upload Document');
      expectCancelLeft(child, 'Upload');

      // The dialog renders in a portal, but it is a React descendant of the Documents section: a key pressed in it
      // reaches the section's handler (a sibling dialog would not).
      fireEvent.keyDown(within(child).getByRole('button', { name: 'Cancel' }), { key: 'F9' });
      expect(reachedSection).toContain('documents:F9');
    },
    SLOW
  );

  it(
    'keeps Tab and Shift+Tab inside the dialog (and the probe can tell a trapped dialog from an untrapped one)',
    async () => {
      installNet({ documents: [], messages: [] });
      await openRecord();
      const { child } = await openChild('Upload documents', 'Upload Document');
      expect(await tabWrapsInside(child)).toBe(true);
    },
    SLOW
  );

  it(
    'Esc leaves the dialog and the record open and keeps the chosen file; Cancel closes it with focus back on Upload; Esc then closes the record',
    async () => {
      installNet({ documents: [], messages: [] });
      const onClose = vi.fn();
      const parent = await openRecord(onClose);
      const { button, child } = await openChild('Upload documents', 'Upload Document');
      fireEvent.change(child.querySelector('input[type="file"]') as HTMLInputElement, {
        target: { files: [new File(['%PDF'], 'keep.pdf', { type: 'application/pdf' })] },
      });
      expect(await within(child).findByText('keep.pdf')).toBeTruthy();

      await pressEscInside(child);
      // Neither closed: the child is still open (and still accessible), the chosen file is still listed.
      expect(child.isConnected).toBe(true);
      expect(screen.getByRole('dialog', { name: 'Upload Document' })).toBe(child);
      expect(within(child).getByText('keep.pdf')).toBeTruthy();
      expectRecordOpen(parent, onClose);

      fireEvent.click(within(child).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Upload Document' })).toBeNull());
      await waitFor(() => expect(document.activeElement).toBe(button));
      expectRecordOpen(parent, onClose);

      fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' });
      await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    },
    SLOW
  );

  it(
    'the x closes it too, with focus back on Upload',
    async () => {
      installNet({ documents: [], messages: [] });
      await openRecord();
      const { button, child } = await openChild('Upload documents', 'Upload Document');
      fireEvent.click(within(child).getByRole('button', { name: 'Close' }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Upload Document' })).toBeNull());
      await waitFor(() => expect(document.activeElement).toBe(button));
    },
    SLOW
  );

  it(
    'Cancel closes it without a request; the record is unchanged and focus is back on Upload',
    async () => {
      installNet({ documents: [], messages: [] });
      const onClose = vi.fn();
      await openRecord(onClose);
      const { button, child } = await openChild('Upload documents', 'Upload Document');

      fireEvent.click(within(child).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Upload Document' })).toBeNull());
      await waitFor(() => expect(document.activeElement).toBe(button));
      expect(onClose).not.toHaveBeenCalled();
      expect(callsOf('POST', /\/documents$/)).toHaveLength(0);
    },
    SLOW
  );

  it(
    'posts the same multipart request to the record documents route, then re-reads the list',
    async () => {
      const documents = [doc(1, 'Mine.pdf')];
      installNet({
        documents,
        messages: [],
        onUpload: () => {
          documents.push(doc(2, 'new.pdf'));
          return { sprk_documentid: guid(902) };
        },
      });
      const parent = await openRecord();
      await within(parent).findByText('Mine.pdf');
      const { child } = await openChild('Upload documents', 'Upload Document');

      const input = child.querySelector('input[type="file"]') as HTMLInputElement;
      fireEvent.change(input, { target: { files: [new File(['%PDF'], 'new.pdf', { type: 'application/pdf' })] } });
      fireEvent.click(await within(child).findByRole('button', { name: 'Upload' }));

      await waitFor(() => {
        const post = callsOf('POST', /\/documents$/)[0];
        expect(post?.[0]).toBe(`/api/v1/external/servicerequests/${SR}/documents`);
        expect((post?.[1] as RequestInit).body).toBeInstanceOf(FormData);
      });
      // The list is re-read and the new row shown; the dialog is gone.
      expect(await within(parent).findByText('new.pdf')).toBeTruthy();
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Upload Document' })).toBeNull());
    },
    SLOW
  );

  it(
    'a refused upload keeps the dialog open with the existing error text, holds the record while in flight and releases after',
    async () => {
      let refuse: (err: unknown) => void = () => undefined;
      installNet({
        documents: [],
        messages: [],
        onUpload: () => new Promise((_resolve, reject) => (refuse = reject)),
      });
      const onClose = vi.fn();
      const parent = await openRecord(onClose);
      const { child } = await openChild('Upload documents', 'Upload Document');

      fireEvent.change(child.querySelector('input[type="file"]') as HTMLInputElement, {
        target: { files: [new File(['%PDF'], 'big.pdf', { type: 'application/pdf' })] },
      });
      fireEvent.click(await within(child).findByRole('button', { name: 'Upload' }));

      // In flight: the record's footer Close is disabled (the hold), Cancel is disabled, and Esc closes nothing.
      const recordClose = within(parent).getByText('Close').closest('button') as HTMLElement;
      await waitFor(() => expect((recordClose as HTMLButtonElement).disabled).toBe(true));
      expect((within(child).getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(true);
      fireEvent.keyDown(document.activeElement ?? child, { key: 'Escape' });
      await settle();
      expect(screen.getByRole('dialog', { name: 'Upload Document' })).toBeTruthy();
      expectRecordOpen(parent, onClose);

      await act(async () => refuse(new ApiError(413, '')));
      expect(await within(child).findByText('The file is too large to upload.')).toBeTruthy();
      expect(screen.getByRole('dialog', { name: 'Upload Document' })).toBeTruthy();
      await waitFor(() => expect((recordClose as HTMLButtonElement).disabled).toBe(false));
    },
    SLOW
  );
});

describe('New message: a FormModal inside the record modal', () => {
  it(
    'replaces the inline compose box; Cancel left; posts the same request, closes, re-reads the thread; focus back on New message',
    async () => {
      const messages: Array<Record<string, unknown>> = [];
      installNet({
        documents: [],
        messages,
        onPostMessage: () => {
          messages.push({
            sprk_communicationid: guid(950),
            sprk_body: 'Hello there',
            sprk_bodyformat: 100000000,
            isMine: true,
            createdon: '2026-03-02T10:00:00Z',
          });
          return {};
        },
      });
      const parent = await openRecord();
      await within(parent).findByTestId('record-messages-empty');
      // No second entry point: the thread draws no compose box of its own.
      expect(within(parent).queryByRole('textbox', { name: 'Write a message' })).toBeNull();

      const { button, child } = await openChild('New message', 'New message');
      expectCancelLeft(child, 'Send');
      const box = within(child).getByRole('textbox', { name: 'Write a message' });
      fireEvent.change(box, { target: { value: '  Hello there  ' } });
      fireEvent.click(within(child).getByRole('button', { name: 'Send' }));

      await waitFor(() => {
        const post = callsOf('POST', /\/messages$/)[0];
        expect(post?.[0]).toBe(`/api/v1/external/servicerequests/${SR}/messages`);
        expect(JSON.parse(String((post?.[1] as RequestInit).body))).toEqual({ sprk_body: 'Hello there' });
      });
      expect(await within(parent).findByText('Hello there')).toBeTruthy();
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'New message' })).toBeNull());
      await waitFor(() => expect(document.activeElement).toBe(button));
    },
    SLOW
  );

  it(
    'a refused send keeps the dialog open with the draft and the existing error text; Cancel keeps the draft',
    async () => {
      installNet({
        documents: [],
        messages: [],
        onPostMessage: () => {
          throw new ApiError(503, '');
        },
      });
      const parent = await openRecord();
      await within(parent).findByTestId('record-messages-empty');
      const { child } = await openChild('New message', 'New message');
      const box = within(child).getByRole('textbox', { name: 'Write a message' }) as HTMLTextAreaElement;
      fireEvent.change(box, { target: { value: 'Keep me' } });
      fireEvent.click(within(child).getByRole('button', { name: 'Send' }));

      expect(
        await within(child).findByText(
          'The message could not be sent just now. Your message has been kept - please try again.'
        )
      ).toBeTruthy();
      expect(screen.getByRole('dialog', { name: 'New message' })).toBeTruthy();
      expect(box.value).toBe('Keep me');

      // Cancel closes it; reopening shows the same draft.
      fireEvent.click(within(child).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'New message' })).toBeNull());
      const reopened = (await openChild('New message', 'New message')).child;
      expect((within(reopened).getByRole('textbox', { name: 'Write a message' }) as HTMLTextAreaElement).value).toBe(
        'Keep me'
      );
    },
    SLOW
  );
});

describe('New message: Esc and the thread read', () => {
  it(
    'Esc leaves the dialog and the record open and keeps the typed draft; Cancel closes it with focus back on New message',
    async () => {
      installNet({ documents: [], messages: [] });
      const onClose = vi.fn();
      const parent = await openRecord(onClose);
      await within(parent).findByTestId('record-messages-empty');
      const { button, child } = await openChild('New message', 'New message');
      const box = within(child).getByRole('textbox', { name: 'Write a message' }) as HTMLTextAreaElement;
      fireEvent.change(box, { target: { value: 'Do not lose me' } });
      box.focus();

      await pressEscInside(child);
      expect(screen.getByRole('dialog', { name: 'New message' })).toBe(child);
      expect(box.value).toBe('Do not lose me');
      expectRecordOpen(parent, onClose);

      fireEvent.click(within(child).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'New message' })).toBeNull());
      await waitFor(() => expect(document.activeElement).toBe(button));
      fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' });
      await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    },
    SLOW
  );

  it(
    'a thread re-read that withdraws the right while New message is open closes the dialog properly: focus stays in the record, Esc closes it, and the dialog does not reappear when the thread comes back',
    async () => {
      // Task 006: New message is offered only once the thread has answered `canCreate: true`, so it can no longer be
      // opened before the first read (the race task 004's F2 test used). The same orphan risk now comes from a RE-read
      // that withdraws the right while the dialog is open: read 1 may post, read 2 (Refresh) may not, read 3 may again.
      let reads = 0;
      installNet({
        documents: [],
        messages: [],
        onReadMessages: () => {
          reads += 1;
          return { value: [], canCreate: reads !== 2 };
        },
      });
      const onClose = vi.fn();
      const parent = await openRecord(onClose);
      const { child } = await openChild('New message', 'New message');
      expect(reads).toBe(1);

      // Refresh (behind the open child, so read with `hidden`) re-reads the thread, which now says "cannot post". The read
      // resolves outside act, as a real one does (fix round 1 F: the act wrapper that used to hide the focus race is gone):
      // the dialog closes in one render and the toolbar button goes in another.
      fireEvent.click(within(parent).getByRole('button', { name: 'Refresh', hidden: true }));
      // The offer is withdrawn and the dialog goes with it, through hide(): open state reset, focus on the record dialog
      // (the toolbar button that held it is gone), not left on the page body.
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'New message' })).toBeNull());
      expect(reads).toBe(2);
      await waitFor(() => expect(screen.queryByRole('button', { name: 'New message', hidden: true })).toBeNull());
      await settle(100);
      expect(document.activeElement).toBe(parent);
      expect(child.isConnected).toBe(false);

      // No orphan open state: re-reading the thread brings the offer back, and the dialog stays closed.
      fireEvent.click(await within(parent).findByRole('button', { name: 'Refresh' }));
      expect(await within(parent).findByRole('button', { name: 'New message' })).toBeTruthy();
      await settle(350);
      expect(screen.queryByRole('dialog', { name: 'New message' })).toBeNull();
      expect(reads).toBe(3);

      // Esc, pressed where focus is now, closes the record.
      fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' });
      await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    },
    SLOW
  );

  it(
    'a thread re-read that FAILS (500) while New message is open withdraws it the same way: the dialog closes and focus is on the record dialog',
    async () => {
      // Fix round 1 E: a failed re-read forgets the posting right, so this path is now reachable from a plain Refresh.
      let reads = 0;
      installNet({
        documents: [],
        messages: [],
        onReadMessages: () => {
          reads += 1;
          if (reads === 2) throw new ApiError(500, JSON.stringify({ status: 500, title: 'broken' }));
          return { value: [], canCreate: true };
        },
      });
      const parent = await openRecord();
      const { child } = await openChild('New message', 'New message');

      fireEvent.click(within(parent).getByRole('button', { name: 'Refresh', hidden: true }));
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'New message' })).toBeNull());
      expect(reads).toBe(2);
      await within(parent).findByTestId('record-messages-refresh-failed');
      await waitFor(() => expect(screen.queryByRole('button', { name: 'New message', hidden: true })).toBeNull());
      await settle(100);
      expect(document.activeElement).toBe(parent);
      expect(child.isConnected).toBe(false);
    },
    SLOW
  );
});

describe('focus return when the control that opened a dialog is gone', () => {
  it('goes to the dialog that control sat in; to the control itself while it is still there', async () => {
    render(
      <div>
        <div role="dialog" aria-label="Record" data-testid="record">
          <button>Open</button>
        </div>
        <button>Elsewhere</button>
      </div>
    );
    const record = screen.getByTestId('record');
    const open = within(record).getByRole('button', { name: 'Open' });

    // Still on screen: the control itself.
    open.focus();
    const target = currentFocusTarget();
    expect(target?.dialog).toBe(record);
    screen.getByRole('button', { name: 'Elsewhere' }).focus();
    restoreFocusTo(target);
    await waitFor(() => expect(document.activeElement).toBe(open));

    // Gone (withdrawn offer): the record dialog, so the next Esc still reaches it.
    open.focus();
    const detached = currentFocusTarget();
    open.remove();
    expect(document.activeElement).toBe(document.body);
    restoreFocusTo(detached);
    await waitFor(() => expect(document.activeElement).toBe(record));

    // Neither on screen: nothing to focus, nothing thrown.
    record.remove();
    expect(() => restoreFocusTo(detached)).not.toThrow();
  });

  it('goes to the dialog when the control is still there as the child closes but is removed right after (fix round 1 F)', async () => {
    render(
      <div role="dialog" aria-label="Record" data-testid="record">
        <button>Open</button>
        <button>Other</button>
      </div>
    );
    const record = screen.getByTestId('record');
    const open = within(record).getByRole('button', { name: 'Open' });
    open.focus();
    const target = currentFocusTarget();
    within(record).getByRole('button', { name: 'Other' }).focus();

    // The control gets focus back (it is still connected when the deferred move runs) ...
    restoreFocusTo(target);
    await waitFor(() => expect(document.activeElement).toBe(open));
    // ... and is then removed (the withdrawn toolbar action's render lands later): focus must not fall to the page body.
    open.remove();
    await waitFor(() => expect(document.activeElement).toBe(record));
  });

  it('goes to the dialog when the control cannot take focus any more (disabled)', async () => {
    render(
      <div role="dialog" aria-label="Record" data-testid="record">
        <button>Open</button>
      </div>
    );
    const record = screen.getByTestId('record');
    const open = within(record).getByRole('button', { name: 'Open' });
    open.focus();
    const target = currentFocusTarget();
    open.blur();
    open.setAttribute('disabled', '');

    restoreFocusTo(target);
    await waitFor(() => expect(document.activeElement).toBe(record));
  });
});

// ---------------------------------------------------------------------------
// Confirm dialog opened from the toolbar (Delete request)
// ---------------------------------------------------------------------------

describe('Delete request: a ConfirmModal inside the record modal', () => {
  it(
    'opens with Cancel on the left and keeps Tab inside; Esc leaves it and the record open; Cancel closes it (focus back on Delete request); Esc then closes the record',
    async () => {
      installNet({ documents: [], messages: [] });
      const onClose = vi.fn();
      const parent = await openRecord(onClose);
      const { button, child } = await openChild('Delete request', 'Delete service request');

      expectCancelLeft(child, 'Delete');
      expect(await tabWrapsInside(child)).toBe(true);

      // Put focus on a real control inside (tabWrapsInside leaves it on a sentinel), then press Esc there.
      within(child).getByRole('button', { name: 'Cancel' }).focus();
      await pressEscInside(child);
      expect(screen.getByRole('alertdialog', { name: 'Delete service request' })).toBe(child);
      expectRecordOpen(parent, onClose);
      expect(callsOf('DELETE', /./)).toHaveLength(0);

      fireEvent.click(within(child).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByRole('alertdialog', { name: 'Delete service request' })).toBeNull());
      await waitFor(() => expect(document.activeElement).toBe(button));
      expectRecordOpen(parent, onClose);
      expect(callsOf('DELETE', /./)).toHaveLength(0);

      fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' });
      await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Version history: a read-only list, so the base SprkModal (light dismiss)
// ---------------------------------------------------------------------------

describe('Version history: a SprkModal inside the record modal', () => {
  it(
    'opens from a document row, lists the versions, and Esc closes it first',
    async () => {
      installNet({
        documents: [doc(1, 'Mine.pdf')],
        messages: [],
        versions: [{ versionId: 'v1', versionLabel: '1.0', createdAt: '2026-03-01T10:00:00Z', createdByName: 'Pat' }],
      });
      const onClose = vi.fn();
      const parent = await openRecord(onClose);
      await within(parent).findByText('Mine.pdf');
      const { child } = await openChild('View version history for Mine.pdf', 'Version History — Mine.pdf');

      expect(await within(child).findByText('Version 1.0')).toBeTruthy();
      fireEvent.keyDown(document.activeElement ?? child, { key: 'Escape' });
      await waitFor(() => expect(screen.queryByRole('dialog', { name: /Version History/ })).toBeNull());
      expect(onClose).not.toHaveBeenCalled();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// The probes can fail (controls)
// ---------------------------------------------------------------------------

describe('the focus and ancestry probes can fail', () => {
  it(
    'a non-trapped (non-blocking) dialog does not contain Tab: the probe reports false',
    async () => {
      render(
        <FluentProvider theme={webLightTheme}>
          <SprkModal
            open
            onClose={() => undefined}
            title="Untrapped"
            nonBlocking
            footer={
              <>
                <Button>One</Button>
                <Button>Two</Button>
              </>
            }
          >
            <input aria-label="field" />
          </SprkModal>
        </FluentProvider>
      );
      const dialog = await screen.findByRole('dialog', { name: 'Untrapped' });
      await settle();
      expect(await tabWrapsInside(dialog)).toBe(false);
    },
    SLOW
  );

  it(
    'a dialog rendered beside the section (a sibling, not a descendant) does not reach the section: the ancestry probe is silent',
    async () => {
      const heard: string[] = [];
      render(
        <FluentProvider theme={webLightTheme}>
          <div onKeyDown={e => heard.push(e.key)}>section</div>
          <FormModal open onClose={() => undefined} onSubmit={() => undefined} title="Sibling">
            <input aria-label="field" />
          </FormModal>
        </FluentProvider>
      );
      const dialog = await screen.findByRole('dialog', { name: 'Sibling' });
      fireEvent.keyDown(within(dialog).getByRole('button', { name: 'Cancel' }), { key: 'F9' });
      expect(heard).toEqual([]);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Add event and Add to-do: the lists' dialogs (reached through the lists' own seam)
// ---------------------------------------------------------------------------

describe('Add event / Add to-do: the lists offer the toolbar action and render a FormModal', () => {
  const PROJECT = guid(101);

  async function offered(
    ui: (report: (a: CreateActionOffer | null) => void) => React.ReactElement
  ): Promise<{ offer: () => CreateActionOffer | null }> {
    let last: CreateActionOffer | null = null;
    render(<FluentProvider theme={webLightTheme}>{ui(a => (last = a))}</FluentProvider>);
    return { offer: () => last };
  }

  it(
    'EventsCalendar: no own button; the action is offered under the same rule; the dialog has Cancel left and posts the same payload',
    async () => {
      const created = {
        sprk_eventid: guid(801),
        sprk_name: 'Court date',
        sprk_duedate: '2026-04-01',
      };
      h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (options?.method === 'POST') return created;
        if (/\/events$/.test(path)) return { value: [] };
        throw new Error(`unexpected BFF call ${path}`);
      });
      const { offer } = await offered(report => (
        <EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.Collaborate} onCreateActionChange={report} />
      ));
      await waitFor(() => expect(offer()?.visible).toBe(true));
      expect(screen.queryByRole('button', { name: /Create Event/ })).toBeNull();

      act(() => offer()!.onClick());
      const dialog = await screen.findByRole('dialog', { name: 'Create Event' });
      expectCancelLeft(dialog, 'Create Event');
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), { target: { value: 'Court date' } });
      fireEvent.change(within(dialog).getByLabelText('Due Date'), { target: { value: '2026-04-01' } });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Create Event' }));

      await waitFor(() => {
        const post = callsOf('POST', /\/events$/)[0];
        expect(post?.[0]).toMatch(/\/events$/);
        expect(JSON.parse(String((post?.[1] as RequestInit).body))).toEqual({
          sprk_name: 'Court date',
          sprk_duedate: '2026-04-01',
          'sprk_RegardingProject@odata.bind': `/sprk_projects(${PROJECT})`,
        });
      });
      expect(await screen.findByText('Court date')).toBeTruthy();
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Create Event' })).toBeNull());
    },
    SLOW
  );

  it(
    'EventsCalendar: a failed create keeps the dialog open with the existing error text',
    async () => {
      h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (options?.method === 'POST') throw new ApiError(500, problem(500));
        if (/\/events$/.test(path)) return { value: [] };
        throw new Error(`unexpected BFF call ${path}`);
      });
      const { offer } = await offered(report => (
        <EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.Collaborate} onCreateActionChange={report} />
      ));
      await waitFor(() => expect(offer()?.visible).toBe(true));
      act(() => offer()!.onClick());
      const dialog = await screen.findByRole('dialog', { name: 'Create Event' });
      fireEvent.change(within(dialog).getByRole('textbox', { name: /Title/ }), { target: { value: 'Court date' } });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Create Event' }));
      expect(await within(dialog).findByText(/^Failed to create event:/)).toBeTruthy();
      expect(screen.getByRole('dialog', { name: 'Create Event' })).toBeTruthy();
    },
    SLOW
  );

  it(
    'EventsCalendar: a View-only caller is offered nothing (hidden, not disabled)',
    async () => {
      h.bffApiCall.mockImplementation(async () => ({ value: [] }));
      const { offer } = await offered(report => (
        <EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} onCreateActionChange={report} />
      ));
      await screen.findByText('No events yet');
      expect(offer()?.visible).toBe(false);
    },
    SLOW
  );

  it(
    'SmartTodo: no own button; the action is offered; the dialog has Cancel left and posts the same payload',
    async () => {
      h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (options?.method === 'POST') return { sprk_todoid: guid(701), sprk_name: 'Call client', statuscode: 1 };
        if (/\/todos$/.test(path)) return { value: [] };
        throw new Error(`unexpected BFF call ${path}`);
      });
      const { offer } = await offered(report => (
        <SmartTodo projectId={PROJECT} accessLevel={AccessLevel.Collaborate} onCreateActionChange={report} />
      ));
      await waitFor(() => expect(offer()?.visible).toBe(true));
      expect(screen.queryByRole('button', { name: /Create new task/ })).toBeNull();

      act(() => offer()!.onClick());
      const dialog = await screen.findByRole('dialog', { name: 'Create Task' });
      expectCancelLeft(dialog, 'Create Task');
      fireEvent.change(within(dialog).getByPlaceholderText('Enter task title...'), {
        target: { value: 'Call client' },
      });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Create Task' }));

      await waitFor(() => {
        const post = callsOf('POST', /\/todos$/)[0];
        expect(JSON.parse(String((post?.[1] as RequestInit).body))).toMatchObject({
          sprk_name: 'Call client',
          sprk_priorityscore: 50,
        });
      });
      expect(await screen.findByText('Call client')).toBeTruthy();
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Create Task' })).toBeNull());
    },
    SLOW
  );

  it(
    'SmartTodo: a failed create keeps the dialog open with the existing error text',
    async () => {
      h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (options?.method === 'POST') throw new Error('Server said no');
        if (/\/todos$/.test(path)) return { value: [] };
        throw new Error(`unexpected BFF call ${path}`);
      });
      const { offer } = await offered(report => (
        <SmartTodo projectId={PROJECT} accessLevel={AccessLevel.Collaborate} onCreateActionChange={report} />
      ));
      await waitFor(() => expect(offer()?.visible).toBe(true));
      act(() => offer()!.onClick());
      const dialog = await screen.findByRole('dialog', { name: 'Create Task' });
      fireEvent.change(within(dialog).getByPlaceholderText('Enter task title...'), {
        target: { value: 'Call client' },
      });
      fireEvent.click(within(dialog).getByRole('button', { name: 'Create Task' }));
      expect(await within(dialog).findByText('Server said no')).toBeTruthy();
      expect(screen.getByRole('dialog', { name: 'Create Task' })).toBeTruthy();
    },
    SLOW
  );
});
