/**
 * FindSimilarViewerDialog.frameSidePane.test.tsx - spaarke-ontology-platform-r1 task 194 (owner 185 check C2).
 *
 * Semantic Search (a PCF) shows the Document Relationship Viewer in an iframe inside this dialog. The viewer's
 * To/Cc/Bcc lookup pane opens in the PCF's page, under this dialog. With the opt-in `yieldToFrameSidePane` the dialog
 * yields (SprkModal `yieldToSidePane`) while its own iframe reports an open lookup, and returns afterwards. Messages
 * from any other window or origin, or of another shape, change nothing; a frame reload ends the yield. Without the
 * opt-in the dialog is the blocking modal it was. `postSidePaneLookupToParent` is the framed page's side.
 */
import * as React from 'react';
import { act, fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { FindSimilarViewerDialog } from '../FindSimilarViewerDialog';
import { postSidePaneLookupToParent, SIDE_PANE_LOOKUP_MESSAGE_TYPE } from '../../SprkModal/sidePaneFrameSignal';

const ORIGIN = 'https://org.crm.example';
const URL_ = `${ORIGIN}/WebResources/sprk_documentrelationshipviewer?data=documentId%3Ddoc-1`;

const frame = () => document.querySelector('iframe') as HTMLIFrameElement;
const dialog = () => screen.getByRole('dialog');

function post(data: unknown, opts: { source?: MessageEventSource | null; origin?: string } = {}) {
  const source = opts.source === undefined ? frame().contentWindow : opts.source;
  act(() => {
    window.dispatchEvent(new MessageEvent('message', { data, origin: opts.origin ?? ORIGIN, source }));
  });
}
const lookup = (open: boolean) => ({ type: SIDE_PANE_LOOKUP_MESSAGE_TYPE, open });

describe('FindSimilarViewerDialog — yields to the lookup pane its framed page opens (task 194)', () => {
  it('with the opt-in: non-blocking; yields while its iframe reports an open lookup and returns when it settles', () => {
    renderWithProviders(<FindSimilarViewerDialog open onClose={jest.fn()} url={URL_} yieldToFrameSidePane />);
    expect(dialog()).not.toHaveAttribute('aria-modal', 'true');
    expect(dialog()).not.toHaveAttribute('inert');

    post(lookup(true));
    expect(dialog()).toHaveAttribute('inert');
    expect(dialog().style.filter).toBe('brightness(0.75)');

    post(lookup(false));
    expect(dialog()).not.toHaveAttribute('inert');
    expect(dialog().style.filter).toBe('');
  });

  it('ignores messages from another window, another origin, or of another shape', () => {
    renderWithProviders(<FindSimilarViewerDialog open onClose={jest.fn()} url={URL_} yieldToFrameSidePane />);
    post(lookup(true), { source: window });
    post(lookup(true), { origin: 'https://evil.example' });
    post({ type: SIDE_PANE_LOOKUP_MESSAGE_TYPE, open: 'yes' });
    post({ type: 'something-else', open: true });
    post('sprk:side-pane-lookup');
    expect(dialog()).not.toHaveAttribute('inert');
  });

  it('a frame reload ends the yield, so a page unloaded mid-lookup cannot leave the dialog inert', () => {
    renderWithProviders(<FindSimilarViewerDialog open onClose={jest.fn()} url={URL_} yieldToFrameSidePane />);
    post(lookup(true));
    expect(dialog()).toHaveAttribute('inert');
    fireEvent.load(frame());
    expect(dialog()).not.toHaveAttribute('inert');
  });

  it('tells the host each time the frame lookup opens and settles (so it can make a preview below yield too)', () => {
    const onFrameSidePaneLookupChange = jest.fn();
    renderWithProviders(
      <FindSimilarViewerDialog
        open
        onClose={jest.fn()}
        url={URL_}
        yieldToFrameSidePane
        onFrameSidePaneLookupChange={onFrameSidePaneLookupChange}
      />
    );
    expect(onFrameSidePaneLookupChange).not.toHaveBeenCalled();
    post(lookup(true));
    post(lookup(true)); // a repeat is not a change
    expect(onFrameSidePaneLookupChange.mock.calls).toEqual([[true]]);
    fireEvent.load(frame());
    expect(onFrameSidePaneLookupChange.mock.calls).toEqual([[true], [false]]);
  });

  it('without the opt-in: the blocking modal it was, and the message changes nothing', () => {
    renderWithProviders(<FindSimilarViewerDialog open onClose={jest.fn()} url={URL_} />);
    expect(dialog()).toHaveAttribute('aria-modal', 'true');
    post(lookup(true));
    expect(dialog()).not.toHaveAttribute('inert');
  });
});

describe('postSidePaneLookupToParent — the framed page tells its parent (task 194)', () => {
  function framedWindow(postMessage: jest.Mock) {
    return { parent: { postMessage }, location: { origin: ORIGIN } } as unknown as Window;
  }

  it('posts the lookup state to the parent, addressed to its own origin', () => {
    const postMessage = jest.fn();
    postSidePaneLookupToParent(true, framedWindow(postMessage));
    postSidePaneLookupToParent(false, framedWindow(postMessage));
    expect(postMessage.mock.calls).toEqual([
      [lookup(true), ORIGIN],
      [lookup(false), ORIGIN],
    ]);
  });

  it('does nothing in a top-level window (the viewer opened in its own tab)', () => {
    const top = { location: { origin: ORIGIN } } as { parent?: unknown; location: { origin: string } };
    top.parent = top;
    expect(() => postSidePaneLookupToParent(true, top as unknown as Window)).not.toThrow();
  });

  it('never throws when the parent cannot be told', () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});
    const postMessage = jest.fn(() => {
      throw new Error('detached');
    });
    expect(() => postSidePaneLookupToParent(true, framedWindow(postMessage))).not.toThrow();
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });
});
