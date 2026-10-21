/**
 * TimelineComposeBox.internalOnly.test.tsx (task 068; owner 2026-10-14)
 *
 * The "Internal Only" switch on the timeline's compose box: one switch labelled exactly "Internal Only",
 * off by default, its value reaches the send payload as an explicit boolean, a reply to an internal item
 * starts it on, and a thread the host knows is Private shows it on and disabled.
 */
import * as React from 'react';
import { render, fireEvent, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { TimelineComposeBox } from '../subcomponents/TimelineComposeBox';
import type { ITimelineSendPayload } from '../subcomponents/TimelineComposeBox';

function renderBox(overrides?: Partial<React.ComponentProps<typeof TimelineComposeBox>>) {
  const onSend = jest.fn().mockResolvedValue(undefined);
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <TimelineComposeBox isSending={false} onSend={onSend} {...overrides} />
    </FluentProvider>
  );
  return { onSend, ...utils };
}

async function fillAndSend() {
  fireEvent.change(screen.getByRole('textbox', { name: 'To' }), { target: { value: 'alice@example.com' } });
  fireEvent.blur(screen.getByRole('textbox', { name: 'To' }));
  fireEvent.change(screen.getByPlaceholderText('Subject'), { target: { value: 'Status update' } });
  fireEvent.click(screen.getByRole('button', { name: 'Switch to plain text' }));
  fireEvent.change(screen.getByRole('textbox', { name: 'Message body' }), { target: { value: 'FYI.' } });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Send' })).not.toBeDisabled());
  fireEvent.click(screen.getByRole('button', { name: 'Send' }));
}

describe('TimelineComposeBox - Internal Only switch', () => {
  it('renders exactly one switch labelled "Internal Only", off by default, with the owner helper text', () => {
    renderBox();
    const switches = screen.getAllByRole('switch');
    expect(switches).toHaveLength(1);
    expect(screen.getByRole('switch', { name: 'Internal Only' })).not.toBeChecked();
    expect(screen.getByText('Only people in your organization can see this')).toBeInTheDocument();
  });

  it('sends isInternalOnly: false when the switch is left off', async () => {
    const { onSend } = renderBox();
    await fillAndSend();
    await waitFor(() => expect(onSend).toHaveBeenCalledTimes(1));
    const payload = onSend.mock.calls[0][0] as ITimelineSendPayload;
    expect(payload.isInternalOnly).toBe(false);
  });

  it('sends isInternalOnly: true when the switch is on, and off again after the toggle is undone', async () => {
    const { onSend } = renderBox();
    const toggle = screen.getByRole('switch', { name: 'Internal Only' });
    fireEvent.click(toggle);
    expect(toggle).toBeChecked();
    fireEvent.click(toggle);
    expect(toggle).not.toBeChecked();
    fireEvent.click(toggle);
    await fillAndSend();
    await waitFor(() => expect(onSend).toHaveBeenCalledTimes(1));
    expect((onSend.mock.calls[0][0] as ITimelineSendPayload).isInternalOnly).toBe(true);
  });

  it('a reply to an internal item (prefill.isInternalOnly) starts with the switch on and sends true', async () => {
    const { onSend } = renderBox({ prefill: { isInternalOnly: true } });
    expect(screen.getByRole('switch', { name: 'Internal Only' })).toBeChecked();
    await fillAndSend();
    await waitFor(() => expect(onSend).toHaveBeenCalledTimes(1));
    expect((onSend.mock.calls[0][0] as ITimelineSendPayload).isInternalOnly).toBe(true);
  });

  it('a host that knows the thread is Private gets the switch on and disabled, and send carries true', async () => {
    const { onSend } = renderBox({ threadIsPrivate: true });
    const toggle = screen.getByRole('switch', { name: 'Internal Only' });
    expect(toggle).toBeChecked();
    expect(toggle).toBeDisabled();
    await fillAndSend();
    await waitFor(() => expect(onSend).toHaveBeenCalledTimes(1));
    expect((onSend.mock.calls[0][0] as ITimelineSendPayload).isInternalOnly).toBe(true);
  });

  it('returns to the starting value after a send (a new message is off unless the host says otherwise)', async () => {
    const { onSend } = renderBox();
    const toggle = screen.getByRole('switch', { name: 'Internal Only' });
    fireEvent.click(toggle);
    await fillAndSend();
    await waitFor(() => expect(onSend).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('switch', { name: 'Internal Only' })).not.toBeChecked());
  });
});
