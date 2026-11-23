/**
 * AccessGrantModal "Add person" / "Add organization" focus return - task 200.
 *
 * After the host's native lookup pane settles (pick or cancel), keyboard focus goes back to the "+" button that opened
 * it: the platform pane leaves focus on <body> or on the link behind the modal, and both "+" buttons are disabled while
 * a pane is open. Focus the host or user put on another control inside the modal on purpose is left alone.
 */
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { IAccessGrantModalProps, IContactSearchResult, IOrganizationPick } from '../types';
import { personFromContact } from './personPickDouble';

const ADA: IContactSearchResult = { contactId: 'contact-ada', fullName: 'Ada (contact)', email: 'ada@spaarke.example' };
const ACME: IOrganizationPick = { id: 'org-acme', name: 'Acme Corp' };

function makeProps(overrides: Partial<IAccessGrantModalProps>): IAccessGrantModalProps {
  const authenticatedFetch = throwingAuthenticatedFetch(async () => ({
    ok: true,
    status: 200,
    json: async () => ({ shares: [] }),
  })) as unknown as IAccessGrantModalProps['authenticatedFetch'];
  return {
    open: true,
    onClose: jest.fn(),
    recordId: 'matter-1',
    recordType: 'matter',
    authenticatedFetch,
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants: jest.fn(async () => []),
    searchContacts: jest.fn(async () => []),
    canGrantAccess: true,
    ...overrides,
  };
}

const renderModal = (props: IAccessGrantModalProps) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <AccessGrantModal {...props} />
    </FluentProvider>
  );

const addPersonButton = (): HTMLElement => screen.getByRole('button', { name: 'Add person' });
const addOrganizationButton = (): HTMLElement => screen.getByRole('button', { name: 'Add organization' });

describe('AccessGrantModal: focus returns to the "+" button that opened the lookup (task 200)', () => {
  let outside: HTMLButtonElement;
  beforeEach(() => {
    outside = document.createElement('button');
    document.body.appendChild(outside);
  });
  afterEach(() => outside.remove());

  it('Add person, pick: focus ends on the Add person button', async () => {
    const pickPerson = jest.fn(async () => {
      outside.focus();
      return personFromContact(ADA);
    });
    renderModal(makeProps({ pickPerson, pickOrganization: jest.fn(async () => null) }));
    await screen.findByRole('button', { name: 'Add person' });

    fireEvent.click(addPersonButton());

    await waitFor(() => expect(pickPerson).toHaveBeenCalled());
    await screen.findByText(ADA.fullName);
    await waitFor(() => expect(document.activeElement).toBe(addPersonButton()));
  });

  it('Add person, cancel: focus ends on the Add person button', async () => {
    const pickPerson = jest.fn(async () => {
      outside.focus();
      return null;
    });
    renderModal(makeProps({ pickPerson, pickOrganization: jest.fn(async () => null) }));
    await screen.findByRole('button', { name: 'Add person' });

    fireEvent.click(addPersonButton());

    await waitFor(() => expect(pickPerson).toHaveBeenCalled());
    await waitFor(() => expect(document.activeElement).toBe(addPersonButton()));
  });

  it('Add organization, cancel: focus ends on the Add organization button', async () => {
    const pickOrganization = jest.fn(async () => {
      outside.focus();
      return null;
    });
    renderModal(makeProps({ pickPerson: jest.fn(async () => null), pickOrganization }));
    await screen.findByRole('button', { name: 'Add organization' });

    fireEvent.click(addOrganizationButton());

    await waitFor(() => expect(pickOrganization).toHaveBeenCalled());
    await waitFor(() => expect(document.activeElement).toBe(addOrganizationButton()));
  });

  it('Add organization, pick: focus ends on the Add organization button', async () => {
    const pickOrganization = jest.fn(async () => {
      outside.focus();
      return ACME;
    });
    renderModal(makeProps({ pickPerson: jest.fn(async () => null), pickOrganization }));
    await screen.findByRole('button', { name: 'Add organization' });

    fireEvent.click(addOrganizationButton());

    await waitFor(() => expect(pickOrganization).toHaveBeenCalled());
    await waitFor(() => expect(document.activeElement).toBe(addOrganizationButton()));
  });

  it('does not move focus that is on another control inside the modal when the lookup settles', async () => {
    const pickPerson = jest.fn(async () => {
      addOrganizationButton().focus(); // put there on purpose, inside the same dialog
      return null;
    });
    renderModal(makeProps({ pickPerson, pickOrganization: jest.fn(async () => null) }));
    await screen.findByRole('button', { name: 'Add person' });

    fireEvent.click(addPersonButton());

    await waitFor(() => expect(pickPerson).toHaveBeenCalled());
    await act(async () => {
      await new Promise(r => window.setTimeout(r, 30));
    });
    expect(document.activeElement).toBe(addOrganizationButton());
  });
});
