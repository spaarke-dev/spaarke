/**
 * AccessGrantModal — `initialSection` (unified-access-control-r2 task 153): the TrackingFieldTrio access-status
 * indicator opens Manage Access at the No Access List when a No Access restriction applies.
 *
 * Covers: with `initialSection: 'noAccess'` the No Access List is scrolled into view and focused once THIS open's load
 * has finished (never on the previous open's list, never twice per open); without it nothing is scrolled; a caller
 * without Write (`notShown`, the section hidden) is not scrolled and nothing throws.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessGrantModal } from '../AccessGrantModal';
import type { IAccessGrantModalProps, IContactSearchResult } from '../types';

const RECORD_ID = '6f1c2d3e-4a5b-4c6d-8e7f-90a1b2c3d4e5';

function jsonResponse(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as unknown as Response;
}

const listBody = {
  recordType: 'sprk_matter',
  recordId: RECORD_ID,
  secure: 'applies',
  noAccess: 'doesNotApply',
  entriesState: 'complete',
  entries: [],
};
const notShownBody = { ...listBody, entriesState: 'notShown', entries: null };

function makeProps(
  noAccess: () => Promise<Response>,
  overrides?: Partial<IAccessGrantModalProps>
): IAccessGrantModalProps {
  const authenticatedFetch = jest.fn(async (url: string) => {
    if (url.includes('/no-access')) return noAccess();
    if (url.includes('/user-shares')) return jsonResponse({ shares: [] });
    if (url.includes('/assigned-access')) return jsonResponse({ entries: [] });
    return jsonResponse({});
  });
  return {
    open: true,
    onClose: jest.fn(),
    recordId: RECORD_ID,
    recordType: 'matter',
    canGrantAccess: true,
    authenticatedFetch: authenticatedFetch as unknown as IAccessGrantModalProps['authenticatedFetch'],
    fetchCandidates: jest.fn(async () => []),
    fetchExistingGrants: jest.fn(async () => []),
    fetchStandingContacts: jest.fn(async () => []),
    searchContacts: jest.fn(async (): Promise<IContactSearchResult[]> => []),
    isInternalContact: jest.fn(async () => false),
    ...overrides,
  };
}

const ui = (props: IAccessGrantModalProps) => (
  <FluentProvider theme={webLightTheme}>
    <AccessGrantModal {...props} />
  </FluentProvider>
);

describe('AccessGrantModal — initialSection (task 153)', () => {
  let scrolled: Element[];
  const original = Element.prototype.scrollIntoView;

  beforeEach(() => {
    scrolled = [];
    Element.prototype.scrollIntoView = function (this: Element) {
      scrolled.push(this);
    } as typeof Element.prototype.scrollIntoView;
  });

  afterEach(() => {
    Element.prototype.scrollIntoView = original;
  });

  it("scrolls to and focuses the No Access List once this open's load has finished", async () => {
    render(ui(makeProps(async () => jsonResponse(listBody), { initialSection: 'noAccess' })));
    const section = await screen.findByRole('region', { name: 'No Access List' });
    await waitFor(() => expect(scrolled).toEqual([section]));
    expect(document.activeElement).toBe(section);
  });

  it('scrolls nothing without initialSection (the person icon opens at the top)', async () => {
    render(ui(makeProps(async () => jsonResponse(listBody))));
    await screen.findByRole('region', { name: 'No Access List' });
    await new Promise(r => setTimeout(r, 20));
    expect(scrolled).toEqual([]);
  });

  it('does nothing (and does not throw) when the section is hidden for a caller without Write', async () => {
    render(ui(makeProps(async () => jsonResponse(notShownBody), { initialSection: 'noAccess' })));
    await screen.findByText(/Current Access/i);
    await new Promise(r => setTimeout(r, 20));
    expect(screen.queryByRole('region', { name: 'No Access List' })).not.toBeInTheDocument();
    expect(scrolled).toEqual([]);
  });

  it("acts once per open, and on a reopen waits for that open's own load, never the previous list", async () => {
    let release: ((r: Response) => void) | null = null;
    let call = 0;
    const noAccess = () => {
      call += 1;
      if (call === 1) return Promise.resolve(jsonResponse(listBody));
      return new Promise<Response>(resolve => {
        release = resolve;
      });
    };
    const props = makeProps(noAccess, { initialSection: 'noAccess' });
    const view = render(ui(props));
    await screen.findByRole('region', { name: 'No Access List' });
    await waitFor(() => expect(scrolled).toHaveLength(1));

    // Further renders of the same open do not scroll again.
    view.rerender(ui({ ...props }));
    await new Promise(r => setTimeout(r, 20));
    expect(scrolled).toHaveLength(1);

    // Close, then reopen at No Access: the second load is held, so nothing may scroll yet.
    view.rerender(ui({ ...props, open: false }));
    view.rerender(ui({ ...props, open: true }));
    await waitFor(() => expect(release).not.toBeNull());
    await new Promise(r => setTimeout(r, 20));
    expect(scrolled).toHaveLength(1);

    (release as unknown as (r: Response) => void)(jsonResponse(listBody));
    await waitFor(() => expect(scrolled).toHaveLength(2));
  });
});
