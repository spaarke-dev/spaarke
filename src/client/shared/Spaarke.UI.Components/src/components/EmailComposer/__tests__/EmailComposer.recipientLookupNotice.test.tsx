/**
 * EmailComposer.recipientLookupNotice.test.tsx - ontology task 238 (b).
 *
 * When the host's people picker reports picks it could not resolve (`skipped` on the result), the composer shows one
 * warning line per pick - "Could not add {name}: {reason}" - and still adds the picks that resolved. A resolvable pick
 * shows no warning. The warning clears on the next settled pick and on any edit of the recipients.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { IEmailComposerProps, IRecipient, IRecipientLookupResult } from '../EmailComposer.types';

const noopFetch = jest.fn() as unknown as IEmailComposerProps['authenticatedFetch'];

function renderComposer(overrides: Partial<IEmailComposerProps> = {}) {
  return renderWithProviders(
    <EmailComposer mode="compose" mount="page" authenticatedFetch={noopFetch} {...overrides} />
  );
}

const chip: IRecipient = { email: 'pat@example.com', resolved: true, entityType: 'contact' };

function result(recipients: IRecipient[], skipped?: Array<{ name: string; reason: string }>): IRecipientLookupResult {
  const r: IRecipientLookupResult = [...recipients];
  if (skipped) r.skipped = skipped;
  return r;
}

const toButton = () => screen.getByRole('button', { name: 'Look up To recipients' });
const notice = () => screen.queryByTestId('recipient-lookup-notice');

describe('EmailComposer: a recipient pick that cannot be resolved is reported (task 238 b)', () => {
  it('must fire: a not-found pick adds nothing and shows "Could not add {name}: {reason}"', async () => {
    const onLookupRecipients = jest
      .fn()
      .mockResolvedValue(result([], [{ name: 'Jane Smith', reason: 'the record could not be read.' }]));
    renderComposer({ onLookupRecipients });

    fireEvent.click(toButton());

    await waitFor(() => expect(notice()).not.toBeNull());
    expect(notice()).toHaveTextContent('Could not add Jane Smith: the record could not be read.');
    expect(screen.queryByText('pat@example.com')).toBeNull();
  });

  it('must fire: a no-email pick shows its own line', async () => {
    renderComposer({
      onLookupRecipients: jest
        .fn()
        .mockResolvedValue(result([], [{ name: 'Sam Nomail', reason: 'it has no email address on file.' }])),
    });

    fireEvent.click(toButton());

    await waitFor(() =>
      expect(notice()).toHaveTextContent('Could not add Sam Nomail: it has no email address on file.')
    );
  });

  it('must not fire: a resolvable pick adds a chip and shows no warning', async () => {
    renderComposer({ onLookupRecipients: jest.fn().mockResolvedValue(result([chip])) });

    fireEvent.click(toButton());

    await waitFor(() => expect(screen.getByText('pat@example.com')).toBeInTheDocument());
    expect(notice()).toBeNull();
  });

  it('a plain array result (a host that knows nothing of `skipped`) works unchanged', async () => {
    renderComposer({ onLookupRecipients: jest.fn().mockResolvedValue([chip]) });

    fireEvent.click(toButton());

    await waitFor(() => expect(screen.getByText('pat@example.com')).toBeInTheDocument());
    expect(notice()).toBeNull();
  });

  it('a mixed multi-select adds the good pick and warns for each bad one, one line each', async () => {
    renderComposer({
      onLookupRecipients: jest.fn().mockResolvedValue(
        result(
          [chip],
          [
            { name: 'Jane Smith', reason: 'the record could not be read.' },
            { name: 'Sam Nomail', reason: 'it has no email address on file.' },
          ]
        )
      ),
    });

    fireEvent.click(toButton());

    await waitFor(() => expect(screen.getByText('pat@example.com')).toBeInTheDocument());
    // the warning survives the pick's own recipients change
    await waitFor(() => expect(notice()).not.toBeNull());
    expect(notice()!.querySelectorAll('.fui-MessageBarBody > div')).toHaveLength(2);
    expect(notice()).toHaveTextContent('Could not add Jane Smith');
    expect(notice()).toHaveTextContent('Could not add Sam Nomail');
  });

  it('the next pick that resolves clears the warning', async () => {
    const onLookupRecipients = jest
      .fn()
      .mockResolvedValueOnce(result([], [{ name: 'Jane Smith', reason: 'the record could not be read.' }]))
      .mockResolvedValueOnce(result([chip]));
    renderComposer({ onLookupRecipients });

    fireEvent.click(toButton());
    await waitFor(() => expect(notice()).not.toBeNull());

    fireEvent.click(toButton());
    await waitFor(() => expect(screen.getByText('pat@example.com')).toBeInTheDocument());
    await waitFor(() => expect(notice()).toBeNull());
  });

  it('editing the recipients (typing an address) clears the warning', async () => {
    renderComposer({
      onLookupRecipients: jest
        .fn()
        .mockResolvedValue(result([], [{ name: 'Jane Smith', reason: 'the record could not be read.' }])),
    });
    fireEvent.click(toButton());
    await waitFor(() => expect(notice()).not.toBeNull());

    const to = screen.getByRole('group', { name: 'To' });
    const input = to.querySelector('input') as HTMLInputElement;
    fireEvent.change(input, { target: { value: 'lee@example.com' } });
    fireEvent.keyDown(input, { key: 'Enter' });

    await waitFor(() => expect(screen.getByText('lee@example.com')).toBeInTheDocument());
    await waitFor(() => expect(notice()).toBeNull());
  });

  it('a cancelled lookup (null) leaves the existing warning alone and adds nothing', async () => {
    const onLookupRecipients = jest
      .fn()
      .mockResolvedValueOnce(result([], [{ name: 'Jane Smith', reason: 'the record could not be read.' }]))
      .mockResolvedValueOnce(null);
    renderComposer({ onLookupRecipients });
    fireEvent.click(toButton());
    await waitFor(() => expect(notice()).not.toBeNull());

    fireEvent.click(toButton());
    await waitFor(() => expect(onLookupRecipients).toHaveBeenCalledTimes(2));

    expect(notice()).not.toBeNull();
  });
});
