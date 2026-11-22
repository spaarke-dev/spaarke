/**
 * EmailComposer.lookupFocus.test.tsx - task 199.
 *
 * After a host lookup (people picker for To/Cc/Bcc, "Link a record", "Link documents", record search) settles, keyboard
 * focus goes back to the button that started it. The platform pane returns focus to whatever opened the dialog (a grid
 * link behind the wizard on dev), so the composer takes focus back, but only when focus is outside the composer.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { IEmailComposerHandle, IEmailComposerProps, IPickedRecord } from '../EmailComposer.types';

const noopFetch = jest.fn() as unknown as IEmailComposerProps['authenticatedFetch'];

function renderComposer(overrides: Partial<IEmailComposerProps> = {}) {
  const ref = React.createRef<IEmailComposerHandle>();
  const utils = renderWithProviders(
    <EmailComposer ref={ref} mode="compose" mount="page" authenticatedFetch={noopFetch} {...overrides} />
  );
  return { ref, ...utils };
}

/** An element outside the composer, standing in for the form's grid link the platform pane hands focus back to. */
function makeOutsideLink(): HTMLButtonElement {
  const el = document.createElement('button');
  el.textContent = 'grid link behind the dialogs';
  document.body.appendChild(el);
  return el;
}

/** A host lookup that, like the platform pane, leaves focus on the outside element before it settles. */
function pane<T>(outside: HTMLElement, result: T) {
  return jest.fn().mockImplementation(async () => {
    outside.focus();
    return result;
  });
}

const picked = { entityType: 'sprk_matter', id: 'm-1', name: 'Matter One' } as unknown as IPickedRecord;
const contact = { id: 'c-1', name: 'Pat Contact', email: 'pat@example.com' };

const toButton = () => screen.getByRole('button', { name: 'Look up To recipients' });
const ccButton = () => screen.getByRole('button', { name: 'Look up Cc recipients' });

describe('EmailComposer: focus returns to the lookup trigger (task 199)', () => {
  let outside: HTMLButtonElement;
  beforeEach(() => {
    outside = makeOutsideLink();
  });
  afterEach(() => {
    outside.remove();
  });

  it('To lookup with a pick: focus ends on the To button', async () => {
    const onLookupRecipients = pane(outside, [contact]);
    renderComposer({ onLookupRecipients });

    fireEvent.click(toButton());

    await waitFor(() => expect(screen.getByText('pat@example.com')).toBeInTheDocument());
    await waitFor(() => expect(document.activeElement).toBe(toButton()));
    expect(onLookupRecipients).toHaveBeenCalledWith('to');
  });

  it('Cc lookup with a pick: focus ends on the Cc button', async () => {
    renderComposer({ onLookupRecipients: pane(outside, [contact]) });

    fireEvent.click(ccButton());

    await waitFor(() => expect(document.activeElement).toBe(ccButton()));
  });

  it('To lookup cancelled (null): focus still ends on the To button', async () => {
    renderComposer({ onLookupRecipients: pane(outside, null) });

    fireEvent.click(toButton());

    await waitFor(() => expect(document.activeElement).toBe(toButton()));
  });

  it('Link a record with a pick: focus ends on the Link a record button', async () => {
    const onAddRelationship = pane(outside, picked);
    renderComposer({ onAddRelationship });

    fireEvent.click(screen.getByRole('button', { name: /link a record/i }));

    await waitFor(() => expect(onAddRelationship).toHaveBeenCalled());
    await waitFor(() => expect(document.activeElement?.textContent).toMatch(/link (a|another) record/i));
  });

  it('Link a record cancelled (null): focus ends on the Link a record button', async () => {
    renderComposer({ onAddRelationship: pane(outside, null) });

    fireEvent.click(screen.getByRole('button', { name: /link a record/i }));

    await waitFor(() => expect(document.activeElement?.textContent).toMatch(/link a record/i));
  });

  it('Link documents (Attach menu): focus ends on the Attach button', async () => {
    const onLookupRecord = pane(outside, null);
    renderComposer({ onLookupRecord, attachmentSources: [] });

    const attach = screen.getByRole('button', { name: 'Attach' });
    fireEvent.click(attach);
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Link documents' }));

    await waitFor(() => expect(onLookupRecord).toHaveBeenCalledWith('sprk_document'));
    await waitFor(() => expect(document.activeElement).toBe(attach));
  });

  it('Record search (toolbar): focus ends on the Insert record link button', async () => {
    const onLookupRecord = pane(outside, picked);
    renderComposer({
      onLookupRecord,
      recordLookupCatalog: [
        { logicalName: 'sprk_matter', displayName: 'Matter' },
      ] as unknown as IEmailComposerProps['recordLookupCatalog'],
    });

    const trigger = screen.getByRole('button', { name: 'Insert record link' });
    fireEvent.click(trigger);
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Matter' }));

    await waitFor(() => expect(onLookupRecord).toHaveBeenCalledWith('sprk_matter'));
    await waitFor(() => expect(document.activeElement).toBe(trigger));
  });

  it('does not move focus that is already inside the composer when the lookup settles', async () => {
    const subject = () => screen.getByRole('textbox', { name: /subject/i });
    const onLookupRecipients = jest.fn().mockImplementation(async () => {
      subject().focus(); // the host or user put focus somewhere inside the composer on purpose
      return [contact];
    });
    renderComposer({ onLookupRecipients });

    fireEvent.click(toButton());

    await waitFor(() => expect(screen.getByText('pat@example.com')).toBeInTheDocument());
    await act(async () => {
      await new Promise(r => window.setTimeout(r, 20));
    });
    expect(document.activeElement).toBe(subject());
  });

  it('composer unmounted before the lookup settles: no error and no focus call', async () => {
    let settle!: (v: unknown) => void;
    const onLookupRecipients = jest.fn().mockImplementation(() => new Promise(r => (settle = r)));
    const { unmount } = renderComposer({ onLookupRecipients });
    fireEvent.click(toButton());
    await waitFor(() => expect(onLookupRecipients).toHaveBeenCalled());

    unmount();
    const focusSpy = jest.spyOn(HTMLElement.prototype, 'focus');
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    const error = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      await act(async () => {
        settle([contact]);
        await new Promise(r => window.setTimeout(r, 20));
      });
      expect(focusSpy).not.toHaveBeenCalled();
      expect(warn).not.toHaveBeenCalled();
      expect(error).not.toHaveBeenCalled();
    } finally {
      focusSpy.mockRestore();
      warn.mockRestore();
      error.mockRestore();
    }
  });
});
