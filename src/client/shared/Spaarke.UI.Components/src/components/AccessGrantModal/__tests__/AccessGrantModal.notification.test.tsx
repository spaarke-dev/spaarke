/**
 * AccessGrantModal — the internal grant notification's notice (unified-access-control-r2 task 181, owner round 89
 * item 3).
 *
 * The SERVER now tells an internal user, in-app with a link to the record, when `/grant` (a contact that represents an
 * internal user) or `/share-user` gave them access. The modal sends nothing; it only reports what the server says:
 *  - success → "Granted access to N item(s)." — the old dev notice ("Internal notify (deep-link) is not yet
 *    available … (escalated; see project notes)") is gone;
 *  - `notificationFailed: true` (in the 200 body, or on `/share-user`'s related-records-pending 500) → the notice adds
 *    "Some people could not be notified; share the record link with them." and Save stays open once so it is read.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal, NOTIFICATION_FAILED_SENTENCE } from '../AccessGrantModal';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { IAccessGrantModalProps, IAccessGrantCandidate, IUserPick } from '../types';

const renderWithTheme = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const INTERNAL: IAccessGrantCandidate = {
  contactId: 'contact-int-1',
  fullName: 'Ralph Internal',
  email: 'ralph@spaarke.com',
  role: 'Assigned Paralegal 1',
};

const USER_PICK: IUserPick = { id: 'systemuser-1', name: 'Uma Userton' };

const CHILDREN_INCOMPLETE = 'sdap.access.user_share.children_incomplete';

function jsonResponse(body: unknown, ok = true, status = ok ? 200 : 500): Response {
  // A BFF ProblemDetails always carries `status`.
  return { ok, status, json: async () => (ok ? body : { status, ...(body as Record<string, unknown>) }) } as unknown as Response;
}

/** Every read answers empty; `/grant` and `/share-user` answer what the test says. */
function fetchWith(grant: Response | null, share: Response | null): jest.Mock {
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url.includes('/share-user') && !url.includes('/unshare-user') && share) return share;
    if (url.includes('/grant') && !url.includes('/invite-and-grant') && grant) return grant;
    return jsonResponse({});
  });
}

function makeProps(authenticatedFetch: jest.Mock): IAccessGrantModalProps {
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'project-1',
    recordType: 'project',
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => [INTERNAL]),
    fetchExistingGrants: jest.fn(async () => []),
    searchContacts: jest.fn(async () => []),
    isInternalContact: jest.fn(async () => true),
    pickUser: jest.fn(async (): Promise<IUserPick | null> => USER_PICK),
    canGrantAccess: true,
  };
}

function levelComboFor(name: string): HTMLElement {
  let el: HTMLElement | null = screen.getByText(name);
  while (el && !el.querySelector('[role="combobox"]')) el = el.parentElement;
  const combo = el?.querySelector('[role="combobox"]') as HTMLElement | null;
  if (!combo) throw new Error(`No access-level dropdown found for row "${name}"`);
  return combo;
}

async function pickLevelFor(name: string, optionLabel: string): Promise<void> {
  fireEvent.click(levelComboFor(name));
  fireEvent.click(await screen.findByRole('option', { name: optionLabel }));
}

async function grantTheInternalContact(): Promise<void> {
  await screen.findByText('Ralph Internal');
  fireEvent.click(screen.getByRole('checkbox', { name: 'Select Ralph Internal' }));
  await pickLevelFor('Ralph Internal', 'View Only');
}

async function stageTheUser(): Promise<void> {
  await screen.findByText('Ralph Internal');
  fireEvent.click(await screen.findByRole('button', { name: 'Add user' }));
  await screen.findByText('Uma Userton');
  await pickLevelFor('Uma Userton', 'Collaborate');
}

function addButton(): HTMLElement {
  return screen.getByRole('button', { name: /^Add \(\d+\)$/ });
}

describe('AccessGrantModal — internal grant notification (task 181)', () => {
  it('a /grant that reports notificationFailed adds the could-not-notify sentence', async () => {
    renderWithTheme(
      <AccessGrantModal
        {...makeProps(fetchWith(jsonResponse({ accessRecordId: 'g-1', narrowed: false, notificationFailed: true }), null))}
      />
    );

    await grantTheInternalContact();
    fireEvent.click(addButton());

    expect(await screen.findByText(`Granted access to 1 item(s). ${NOTIFICATION_FAILED_SENTENCE}`)).toBeInTheDocument();
    expect(screen.queryByText(/escalated; see project notes/)).not.toBeInTheDocument();
  });

  it('a /share-user that reports notificationFailed adds the same sentence', async () => {
    const share = jsonResponse({
      systemUserId: USER_PICK.id,
      accessLevel: 100000001,
      accessRightsMask: 22,
      outcome: 'created',
      narrowed: false,
      notificationFailed: true,
    });
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith(null, share))} />);

    await stageTheUser();
    fireEvent.click(addButton());

    expect(await screen.findByText(`Granted access to 1 item(s). ${NOTIFICATION_FAILED_SENTENCE}`)).toBeInTheDocument();
  });

  // Beyond the POML's named cases, justified: the related-records-pending answer is a 500 ProblemDetails, read through
  // a different path (the error body), so the flag would be lost there without its own test.
  it('a /share-user related-records-pending answer carrying notificationFailed keeps both sentences', async () => {
    const detail = 'The record was shared, but 1 of its 6 related records could not be updated yet.';
    const share = jsonResponse(
      { title: 'Related records not all updated', detail, reasonCode: CHILDREN_INCOMPLETE, notificationFailed: true },
      false,
      500
    );
    renderWithTheme(<AccessGrantModal {...makeProps(fetchWith(null, share))} />);

    await stageTheUser();
    fireEvent.click(addButton());

    expect(
      await screen.findByText(`Granted access to 1 item(s). ${detail} ${NOTIFICATION_FAILED_SENTENCE}`)
    ).toBeInTheDocument();
  });

  // Beyond the named cases, justified: Save closes the modal on success, so without staying open the sentence the
  // POML asks for would never be seen.
  it('Save with a grant whose person could not be notified stays open once to show it, then closes', async () => {
    const props = makeProps(
      fetchWith(jsonResponse({ accessRecordId: 'g-1', narrowed: false, notificationFailed: true }), null)
    );
    renderWithTheme(<AccessGrantModal {...props} />);

    await grantTheInternalContact();
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText(new RegExp(NOTIFICATION_FAILED_SENTENCE.replace(/[.;]/g, '.')))).toBeInTheDocument();
    expect(props.onClose).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(props.onClose).toHaveBeenCalledTimes(1));
  });
});
