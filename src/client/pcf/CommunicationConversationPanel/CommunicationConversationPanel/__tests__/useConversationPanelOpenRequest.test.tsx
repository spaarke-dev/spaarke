/**
 * useConversationPanelOpenRequest — the panel's side of the grid's "+ New Message" (#2908).
 *
 * Contract:
 *  - ready: a request for this record is taken (the grid hears "opened") and opens the modal once;
 *  - auth still loading: the request is taken and opens the modal when auth is ready;
 *  - the modal can never open here (no regarding context, auth failed): the request is NOT taken, so the grid tells
 *    the user (must not claim it);
 *  - auth fails after the request was taken: onFailed reports why, the modal does not open;
 *  - a request for another record is ignored.
 */
import * as React from 'react';
import { act, render } from '@testing-library/react';
import { requestConversationPanelOpen } from '@spaarke/ui-components/dist/utils/conversationPanelLaunch';
import {
  useConversationPanelOpenRequest,
  type IUseConversationPanelOpenRequestOptions,
} from '../useConversationPanelOpenRequest';

const MATTER = { entityType: 'sprk_matter', id: '33333333-3333-4333-8333-333333333333' };
const OTHER = { entityType: 'sprk_matter', id: '44444444-4444-4444-8444-444444444444' };

/** Dispatch inside act() so the listener's state update and the effects it triggers are flushed. */
function request(host: { entityType: string; id: string }): boolean {
  let taken = false;
  act(() => {
    taken = requestConversationPanelOpen(host, window);
  });
  return taken;
}

const Harness: React.FC<IUseConversationPanelOpenRequestOptions> = props => {
  useConversationPanelOpenRequest(props);
  return null;
};

function setup(overrides: Partial<IUseConversationPanelOpenRequestOptions>) {
  const onOpen = jest.fn();
  const onFailed = jest.fn();
  const base: IUseConversationPanelOpenRequestOptions = {
    host: MATTER,
    canOpen: true,
    ready: true,
    failure: null,
    onOpen,
    onFailed,
    ...overrides,
  };
  const view = render(<Harness {...base} />);
  const rerender = (next: Partial<IUseConversationPanelOpenRequestOptions>) =>
    view.rerender(<Harness {...base} {...next} />);
  return { onOpen, onFailed, rerender, unmount: view.unmount };
}

describe('useConversationPanelOpenRequest', () => {
  it('ready: takes the request for its record and opens the modal once', () => {
    const { onOpen, onFailed, unmount } = setup({});
    expect(request(MATTER)).toBe(true);
    expect(onOpen).toHaveBeenCalledTimes(1);
    expect(onFailed).not.toHaveBeenCalled();
    unmount();
  });

  it('auth still loading: takes the request and opens the modal when auth is ready', () => {
    const { onOpen, rerender, unmount } = setup({ ready: false });
    expect(request(MATTER)).toBe(true);
    expect(onOpen).not.toHaveBeenCalled();
    rerender({ ready: true });
    expect(onOpen).toHaveBeenCalledTimes(1);
    unmount();
  });

  it('cannot open here (no regarding context or auth failed): does not take the request (must not claim it)', () => {
    const { onOpen, unmount } = setup({ canOpen: false, ready: false, failure: 'Auth failed.' });
    expect(request(MATTER)).toBe(false);
    expect(onOpen).not.toHaveBeenCalled();
    unmount();
  });

  it('auth fails after the request was taken: reports why and does not open', () => {
    const { onOpen, onFailed, rerender, unmount } = setup({ ready: false });
    expect(request(MATTER)).toBe(true);
    rerender({ canOpen: false, ready: false, failure: 'Auth failed.' });
    expect(onFailed).toHaveBeenCalledWith('Auth failed.');
    expect(onOpen).not.toHaveBeenCalled();
    unmount();
  });

  it('ignores a request for another record, and stops listening when unmounted', () => {
    const { onOpen, unmount } = setup({});
    expect(request(OTHER)).toBe(false);
    unmount();
    expect(request(MATTER)).toBe(false);
    expect(onOpen).not.toHaveBeenCalled();
  });
});
