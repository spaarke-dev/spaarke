/**
 * R4 task 013 (owner retest 2026-11-14) — "these forms the sections should expand/collapse; and save settings after close
 * for the user".
 *
 * Every section card has a chevron button (`aria-expanded` / `aria-controls`) that collapses its body. The choice is
 * remembered per USER (the hashed `/me` contact id), record TYPE and section in `localStorage` (`recordSectionState.ts`).
 *
 * What runs for real: `RecordTemplateModal` (the record read, `/me` through `useCallerContext`, the section-state scope),
 * `RecordSectionCard` (the toggle, the header toolbar, the body), and the storage helper. What is substituted: the network
 * seam (`bffApiCall`, `getExternalUserContext`) and the section renderers (a card with one header action and one line of
 * content), so the test sees the card and not what a section draws in it.
 *
 * Each case fails on the old card (a static section: no toggle, nothing stored); the "throwing storage" and "other user"
 * cases fail if the helper does not fail open / does not key on the user.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn(), getExternalUserContext: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall, getExternalUserContext: h.getExternalUserContext };
});

import { RECORD_DETAIL_ENTITIES } from '../src/components/detail/RecordDetailFields';
import { RECORD_TEMPLATES } from '../src/components/record/recordTemplates';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import { RecordSectionCard } from '../src/components/record/sections/RecordSectionCard';
import { sectionStateUserKeyOf } from '../src/components/record/recordSectionState';
import type { RecordSectionContext, RecordSectionId } from '../src/components/record/recordTemplate.types';

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const MATTER_A = guid(201);
const MATTER_B = guid(202);
const PROJECT = guid(101);
const ALICE = guid(7);
const BOB = guid(8);
const ALICE_EMAIL = 'alice.example@secret-firm.test';
const WAIT = { timeout: 12000 };

let me: { contactId: string; email: string } = { contactId: ALICE, email: ALICE_EMAIL };
const headerAction = vi.fn();

beforeEach(() => {
  localStorage.clear();
  headerAction.mockReset();
  me = { contactId: ALICE, email: ALICE_EMAIL };
  h.getExternalUserContext.mockReset();
  h.getExternalUserContext.mockImplementation(async () => ({ ...me, projects: [] }));
  h.bffApiCall.mockReset();
  h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    if (path.endsWith('/api/v1/external/api/dataverse/fetch')) {
      const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
      const id = /operator="eq" value="([^"]+)"/.exec(body.fetchXml)?.[1] ?? '';
      const detail = RECORD_DETAIL_ENTITIES[body.entityName];
      const config = RECORD_TEMPLATES[body.entityName];
      return {
        entities: [{ [detail.idAttribute]: id, [config.nameAttribute]: `${config.label} name` }],
        moreRecords: false,
      };
    }
    throw new Error(`unexpected BFF call ${path}`);
  });
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

/** A section stand-in that draws the REAL card with one header action and one line of content. */
const renderSection = (id: RecordSectionId, _ctx: RecordSectionContext): React.ReactNode => (
  <RecordSectionCard
    id={id}
    actions={[{ key: 'act', icon: <span aria-hidden>+</span>, tooltip: `Act on ${id}`, onClick: headerAction }]}
  >
    <p data-testid={`content-${id}`}>content of {id}</p>
  </RecordSectionCard>
);

const Harness: React.FC<{ entity: string; recordId: string }> = ({ entity, recordId }) => (
  <FluentProvider theme={webLightTheme}>
    <RecordTemplateModal
      open
      onClose={() => undefined}
      config={RECORD_TEMPLATES[entity]}
      recordIds={[recordId]}
      index={0}
      onIndexChange={() => undefined}
      renderSection={renderSection}
    />
  </FluentProvider>
);

/** Opens a record and waits until its sections are on screen AND `/me` has answered (the user key is known). */
async function openRecord(entity: string, recordId: string): Promise<HTMLElement> {
  render(<Harness entity={entity} recordId={recordId} />);
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-template-columns', undefined, WAIT);
  await waitFor(() => expect(h.getExternalUserContext).toHaveBeenCalled(), WAIT);
  // Let the `/me` answer reach the cards.
  await new Promise(resolve => setTimeout(resolve, 30));
  return dialog;
}

const toggleOf = (id: RecordSectionId): HTMLElement => screen.getByTestId(`section-toggle-${id}`);
const bodyOf = (id: RecordSectionId): HTMLElement => screen.getByTestId(`section-body-${id}`);
const isCollapsed = (id: RecordSectionId): boolean =>
  toggleOf(id).getAttribute('aria-expanded') === 'false' && bodyOf(id).hidden;

describe('a section card collapses and expands', () => {
  it('is expanded by default; the toggle flips aria-expanded and hides the body it controls (keyboard operable)', async () => {
    await openRecord('sprk_matter', MATTER_A);
    const toggle = toggleOf('documents');
    expect(toggle.tagName).toBe('BUTTON');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(toggle.getAttribute('aria-controls')).toBe(bodyOf('documents').id);
    expect(bodyOf('documents').hidden).toBe(false);

    toggle.focus();
    fireEvent.click(toggle);
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(bodyOf('documents').hidden).toBe(true);
    // Only this section: the others are untouched.
    expect(toggleOf('invoices').getAttribute('aria-expanded')).toBe('true');
    expect(bodyOf('invoices').hidden).toBe(false);

    fireEvent.click(toggle);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(bodyOf('documents').hidden).toBe(false);
  });

  it('keeps the header, with its section actions, visible and usable while collapsed (the body stays mounted)', async () => {
    await openRecord('sprk_matter', MATTER_A);
    fireEvent.click(toggleOf('documents'));
    expect(isCollapsed('documents')).toBe(true);

    const card = screen.getByTestId('record-section-documents');
    // The title and the action are outside the hidden body.
    expect(within(card).getByRole('heading', { name: 'Documents' })).toBeTruthy();
    const action = within(card).getByRole('button', { name: 'Act on documents' });
    expect(bodyOf('documents').contains(action)).toBe(false);
    expect(action.closest('[hidden]')).toBeNull();
    fireEvent.click(action);
    expect(headerAction).toHaveBeenCalledTimes(1);
    // The content is still mounted (a list's dialog and the action it offers live there), only hidden.
    expect(bodyOf('documents').contains(screen.getByTestId('content-documents'))).toBe(true);
  });
});

describe('the collapsed state is remembered per user, record type and section', () => {
  it('survives closing and reopening, and applies to another record of the same type', async () => {
    await openRecord('sprk_matter', MATTER_A);
    fireEvent.click(toggleOf('documents'));
    expect(isCollapsed('documents')).toBe(true);

    // Close (unmount), then open ANOTHER matter: Documents is collapsed, the other sections are not.
    cleanup();
    await openRecord('sprk_matter', MATTER_B);
    expect(isCollapsed('documents')).toBe(true);
    expect(toggleOf('invoices').getAttribute('aria-expanded')).toBe('true');
    expect(toggleOf('information').getAttribute('aria-expanded')).toBe('true');

    // Expanding it again is remembered too.
    fireEvent.click(toggleOf('documents'));
    cleanup();
    await openRecord('sprk_matter', MATTER_A);
    expect(toggleOf('documents').getAttribute('aria-expanded')).toBe('true');
  });

  it('is per record TYPE: a collapsed matter section does not collapse the same section of a project', async () => {
    await openRecord('sprk_matter', MATTER_A);
    fireEvent.click(toggleOf('documents'));
    cleanup();
    await openRecord('sprk_project', PROJECT);
    expect(toggleOf('documents').getAttribute('aria-expanded')).toBe('true');
  });

  it('is per USER: another signed-in user on the same browser sees the section expanded', async () => {
    await openRecord('sprk_matter', MATTER_A);
    fireEvent.click(toggleOf('documents'));
    expect(isCollapsed('documents')).toBe(true);
    cleanup();

    me = { contactId: BOB, email: 'bob@other.test' };
    await openRecord('sprk_matter', MATTER_B);
    expect(toggleOf('documents').getAttribute('aria-expanded')).toBe('true');
    expect(bodyOf('documents').hidden).toBe(false);

    // And the first user still sees theirs collapsed.
    cleanup();
    me = { contactId: ALICE, email: ALICE_EMAIL };
    await openRecord('sprk_matter', MATTER_B);
    expect(isCollapsed('documents')).toBe(true);
  });

  it('stores no email, UPN, contact id or name: the key holds a hash of the /me id and the value is a constant', async () => {
    await openRecord('sprk_matter', MATTER_A);
    fireEvent.click(toggleOf('documents'));
    fireEvent.click(toggleOf('invoices'));
    expect(localStorage.length).toBe(2);
    for (let i = 0; i < localStorage.length; i++) {
      const key = localStorage.key(i) as string;
      const value = localStorage.getItem(key) as string;
      const stored = `${key} ${value}`.toLowerCase();
      expect(stored).not.toContain(ALICE.toLowerCase());
      expect(stored).not.toContain('alice');
      expect(stored).not.toContain('secret-firm');
      expect(stored).not.toContain('@');
      expect(stored).toContain('sprk_matter');
      expect(value).toBe('1');
    }
    // The hash is stable for the same user and differs for another.
    expect(sectionStateUserKeyOf({ contactId: ALICE, systemUserId: null })).toBe(
      sectionStateUserKeyOf({ contactId: ALICE, systemUserId: null })
    );
    expect(sectionStateUserKeyOf({ contactId: ALICE, systemUserId: null })).not.toBe(
      sectionStateUserKeyOf({ contactId: BOB, systemUserId: null })
    );
    expect(sectionStateUserKeyOf(null)).toBeNull();
  });
});

describe('a store that is empty or throws never breaks the record', () => {
  it('with localStorage throwing on every read and write: everything is expanded, nothing throws, and the toggle still works in memory', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage blocked');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('storage blocked');
    });

    await openRecord('sprk_matter', MATTER_A);
    for (const id of ['information', 'documents', 'invoices', 'messages', 'tasksEvents'] as const) {
      expect(toggleOf(id).getAttribute('aria-expanded')).toBe('true');
      expect(bodyOf(id).hidden).toBe(false);
    }
    // No error surfaced: the record is on screen and no alert is shown.
    expect(screen.queryByRole('alert')).toBeNull();

    expect(() => fireEvent.click(toggleOf('documents'))).not.toThrow();
    expect(isCollapsed('documents')).toBe(true);
    expect(() => fireEvent.click(toggleOf('documents'))).not.toThrow();
    expect(toggleOf('documents').getAttribute('aria-expanded')).toBe('true');
  });

  it('with an empty store: everything is expanded', async () => {
    await openRecord('sprk_matter', MATTER_A);
    for (const id of ['information', 'documents', 'invoices'] as const) {
      expect(toggleOf(id).getAttribute('aria-expanded')).toBe('true');
    }
  });

  it('a card drawn outside a record template collapses in memory and writes nothing', () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <RecordSectionCard id="documents">
          <p>content</p>
        </RecordSectionCard>
      </FluentProvider>
    );
    fireEvent.click(toggleOf('documents'));
    expect(isCollapsed('documents')).toBe(true);
    expect(localStorage.length).toBe(0);
  });
});
