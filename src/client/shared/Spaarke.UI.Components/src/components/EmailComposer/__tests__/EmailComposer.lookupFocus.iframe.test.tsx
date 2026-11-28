/**
 * EmailComposer.lookupFocus.iframe.test.tsx - ontology task 238 (c).
 *
 * The composer hosted by the Document Relationship Viewer lives in an iframe inside Semantic Search's Find Similar
 * dialog. After a side-pane recipient pick (or a cancel) keyboard focus must land on the field's lookup button INSIDE the
 * frame, with the frame holding the top-level focus. Observed on dev before the fix: the frame document's
 * activeElement was <body> and the top document's was the iframe. jsdom cannot move top-level focus, so it is modelled
 * as a browser behaves: `element.focus()` only records the frame document's activeElement; the frame window's own
 * `focus()` makes the frame the focused one (`doc.hasFocus()`), and the lookup pane leaves it without.
 */
import * as React from 'react';
import { fireEvent, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { IEmailComposerProps } from '../EmailComposer.types';

const noopFetch = jest.fn() as unknown as IEmailComposerProps['authenticatedFetch'];
const contact = { email: 'pat@example.com', resolved: true, entityType: 'contact' as const };

describe('EmailComposer in an iframe: focus returns to the lookup button inside the frame (task 238 c)', () => {
  let iframe: HTMLIFrameElement;
  let frameDoc: Document;
  let frameWin: Window;
  let frameHasTopLevelFocus: boolean;

  beforeEach(() => {
    iframe = document.createElement('iframe');
    document.body.appendChild(iframe);
    frameDoc = iframe.contentDocument as Document;
    frameWin = iframe.contentWindow as Window;
    frameHasTopLevelFocus = false;
    Object.defineProperty(frameWin, 'focus', {
      value: () => {
        frameHasTopLevelFocus = true;
      },
      configurable: true,
    });
    Object.defineProperty(frameDoc, 'hasFocus', { value: () => frameHasTopLevelFocus, configurable: true });
  });
  afterEach(() => iframe.remove());

  function renderInFrame(onLookupRecipients: IEmailComposerProps['onLookupRecipients']) {
    const container = frameDoc.createElement('div');
    frameDoc.body.appendChild(container);
    return renderWithProviders(
      <EmailComposer
        mode="compose"
        mount="page"
        authenticatedFetch={noopFetch}
        onLookupRecipients={onLookupRecipients}
      />,
      { container }
    );
  }

  /** The platform pane: while open, focus is in the top window and the frame has none; it resolves with the result. */
  const pane = (result: unknown) =>
    jest.fn().mockImplementation(async () => {
      frameHasTopLevelFocus = false;
      return result;
    });

  it('after a pick: the To button in the frame is the frame active element and the frame holds the focus', async () => {
    const view = renderInFrame(pane([contact]));
    const to = view.getByRole('button', { name: 'Look up To recipients' });

    fireEvent.click(to);

    await waitFor(() => expect(view.getByText('pat@example.com')).toBeInTheDocument());
    await waitFor(
      () => expect(frameDoc.activeElement).toBe(view.getByRole('button', { name: 'Look up To recipients' })),
      {
        timeout: 2000,
      }
    );
    expect(frameDoc.hasFocus()).toBe(true);
  });

  it('after a cancel: the same', async () => {
    const view = renderInFrame(pane(null));

    fireEvent.click(view.getByRole('button', { name: 'Look up To recipients' }));

    await waitFor(
      () => expect(frameDoc.activeElement).toBe(view.getByRole('button', { name: 'Look up To recipients' })),
      {
        timeout: 2000,
      }
    );
    expect(frameDoc.hasFocus()).toBe(true);
  });
});
