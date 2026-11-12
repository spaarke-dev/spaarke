/**
 * AccessGrantModal — the notes bell (unified-access-control-r2 task 196, owner UX decision 2026-11-12).
 *
 * The informational messages (the Access Permission explanation, the parent's minimum access, the secure owner) move
 * from stacked banners at the top of the body to a bell in the modal header, with a count badge and a popover that lists
 * each note in full, links included. Messages that block an action, explain a disabled action where it is, or report a
 * failure stay inline where they were: the delegation deny banner, Current Access's read error, the No Access List's
 * read error, an action's outcome notice.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import { apiErrorFor, throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import { accessNote, accessNotesBell, openAccessNotes } from '../../../__tests__/helpers/accessNotes';
import type { IAccessGrantModalProps, IFollowsParent } from '../types';

const RECORD_ID = '6f1c2d3e-4a5b-4c6d-8e7f-90a1b2c3d4e5';
const PARENT_MATTER: IFollowsParent = {
  recordType: 'matter',
  recordId: 'dddddddd-0000-0000-0000-000000000001',
  name: 'Acme v. Beta',
};

function json(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as unknown as Response;
}

const NO_ACCESS_OK = {
  recordType: 'sprk_project',
  recordId: RECORD_ID,
  secure: 'doesNotApply',
  noAccess: 'doesNotApply',
  entriesState: 'complete',
  entries: [],
};

interface ISetup {
  /** Answers `/user-shares`; default an empty list. */
  userShares?: () => Response;
  /** Answers `/no-access`; default an empty, complete list. */
  noAccess?: () => Response;
  overrides?: Partial<IAccessGrantModalProps>;
}

function makeProps(setup: ISetup = {}): IAccessGrantModalProps {
  const authenticatedFetch = throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/no-access')) return setup.noAccess ? setup.noAccess() : json(NO_ACCESS_OK);
    if (url.includes('/user-shares')) return setup.userShares ? setup.userShares() : json({ shares: [] });
    if (url.includes('/assigned-access?')) return json({ entries: [] });
    return json({});
  });
  return {
    open: true,
    onClose: jest.fn(),
    recordId: RECORD_ID,
    recordType: 'project',
    canGrantAccess: true,
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants: jest.fn(async () => [
      {
        accessRecordId: 'grant-1',
        contactId: 'aaaaaaaa-0000-0000-0000-000000000001',
        fullName: 'Carla Contact',
        accessLevel: 100000001,
      },
    ]),
    searchContacts: jest.fn(async () => []),
    pickPerson: jest.fn(async () => null),
    pickOrganization: jest.fn(async () => null),
    ...setup.overrides,
  };
}

const renderModal = (props: IAccessGrantModalProps, theme = webLightTheme) =>
  render(
    <FluentProvider theme={theme}>
      <AccessGrantModal {...props} />
    </FluentProvider>
  );

async function loaded(): Promise<void> {
  await screen.findByRole('region', { name: 'No Access List' });
  await waitFor(() => expect(screen.queryByText('Loading access data…')).not.toBeInTheDocument());
}

/** A Secure + Restricted record filed under a matter, with a secure owner: three notes. */
const THREE_NOTES: Partial<IAccessGrantModalProps> = {
  accessPermissionState: 'restricted',
  isSecureRecord: true,
  followsParents: [PARENT_MATTER],
  fetchSecureOwnerInfo: jest.fn(async () => ({ ownerName: 'Secure Owners', businessUnitName: 'Secure BU' })),
};

describe('AccessGrantModal — the notes bell (task 196)', () => {
  it('counts the informational notes on the bell, beside the window controls, and not in the body', async () => {
    renderModal(makeProps({ overrides: { ...THREE_NOTES, onOpenParent: jest.fn() } }));
    await loaded();

    const bell = accessNotesBell();
    expect(bell).toHaveAccessibleName('Access notes (3)');
    expect(screen.getByTestId('access-notes-count')).toHaveTextContent('3');
    // In the header, before maximize and close (the same group as the window controls).
    const maximize = screen.getByRole('button', { name: 'Maximize dialog' });
    expect(bell.compareDocumentPosition(maximize) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const headerRight = maximize.closest('div')!.parentElement!;
    expect(headerRight.contains(bell)).toBe(true);
    // Not stacked in the body any more.
    expect(screen.queryByText('Secure – Restricted')).not.toBeInTheDocument();
    expect(screen.queryByText('Minimum access from the parent')).not.toBeInTheDocument();
    expect(screen.queryByText(/Secure Owners/)).not.toBeInTheDocument();
  });

  it('lists every note in full in the popover', async () => {
    renderModal(makeProps({ overrides: { ...THREE_NOTES, onOpenParent: jest.fn() } }));
    await loaded();

    const popover = await openAccessNotes();
    expect(accessNote(popover, 'Secure – Restricted')).toHaveTextContent(
      'This record is secure and Restricted. Only internal users can be given access to this record.'
    );
    expect(accessNote(popover, 'Minimum access from the parent')).toHaveTextContent(
      'Minimum access comes from the parent matter Acme v. Beta: this record can be made stricter but not looser.'
    );
    const owner = accessNote(popover, 'Secure record');
    expect(owner).toHaveTextContent('Owner: Secure Owners · Business Unit: Secure BU');
  });

  it("the parent's link in the popover still opens the parent", async () => {
    const onOpenParent = jest.fn();
    renderModal(makeProps({ overrides: { ...THREE_NOTES, onOpenParent } }));
    await loaded();

    const note = accessNote(await openAccessNotes(), 'Minimum access from the parent')!;
    fireEvent.click(within(note).getByRole('button', { name: 'Acme v. Beta' }));
    expect(onOpenParent).toHaveBeenCalledWith(PARENT_MATTER);
  });

  it('a Standard record with no parent and no secure owner counts none, and the popover says so', async () => {
    renderModal(makeProps());
    await loaded();

    expect(accessNotesBell()).toHaveAccessibleName('Access notes (0)');
    expect(screen.queryByTestId('access-notes-count')).not.toBeInTheDocument();
    expect(await openAccessNotes()).toHaveTextContent("There are no notes about this record's access.");
  });

  it('is keyboard reachable: a focusable button, not removed from the tab order', async () => {
    renderModal(makeProps({ overrides: THREE_NOTES }));
    await loaded();

    const bell = accessNotesBell();
    expect(bell.tagName).toBe('BUTTON');
    expect(bell).not.toHaveAttribute('tabindex', '-1');
    bell.focus();
    expect(bell).toHaveFocus();
  });

  it('is not shown when the caller may not manage access (no notes were shown then either)', async () => {
    renderModal(makeProps({ overrides: { ...THREE_NOTES, canGrantAccess: false } }));
    expect(
      await screen.findByText('You do not have permission to grant or revoke access for this record.')
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Access notes/ })).not.toBeInTheDocument();
  });

  describe('messages that report a failure or block actions stay inline (not counted on the bell)', () => {
    it('the delegation deny banner', async () => {
      renderModal(
        makeProps({
          overrides: THREE_NOTES,
          userShares: () => {
            throw apiErrorFor(403, {
              title: 'Forbidden',
              status: 403,
              detail: 'Write required.',
              reasonCode: 'sdap.access.deny.delegation_write_required',
            });
          },
        })
      );
      await loaded();

      // In the body without opening the bell; the bell still counts the three informational notes only.
      expect(screen.getByText('Write and Share required')).toBeInTheDocument();
      expect(accessNotesBell()).toHaveAccessibleName('Access notes (3)');
      const popover = await openAccessNotes();
      expect(within(popover).queryByText('Write and Share required')).not.toBeInTheDocument();
    });

    it("Current Access's read error", async () => {
      renderModal(
        makeProps({
          overrides: {
            ...THREE_NOTES,
            fetchExistingGrants: jest.fn(async () => {
              throw apiErrorFor(500, { title: 'Error', status: 500, detail: 'The access list could not be read.' });
            }),
          },
        })
      );
      await loaded();

      expect(screen.getByTestId('current-access-error')).toHaveTextContent('Current Access unavailable');
      expect(accessNotesBell()).toHaveAccessibleName('Access notes (3)');
    });

    it("the No Access List's read error", async () => {
      renderModal(makeProps({ overrides: THREE_NOTES, noAccess: () => json({}, 500) }));
      await loaded();

      expect(screen.getByText('No Access List unavailable')).toBeInTheDocument();
      expect(accessNotesBell()).toHaveAccessibleName('Access notes (3)');
    });
  });

  it('renders under webDarkTheme with no console errors', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    renderModal(makeProps({ overrides: { ...THREE_NOTES, onOpenParent: jest.fn() } }), webDarkTheme);
    await loaded();
    await openAccessNotes();

    expect(errorSpy).not.toHaveBeenCalled();
    expect(warnSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
    warnSpy.mockRestore();
  });
});
