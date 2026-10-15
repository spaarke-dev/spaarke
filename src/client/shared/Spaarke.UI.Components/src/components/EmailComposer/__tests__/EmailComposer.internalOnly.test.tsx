/**
 * EmailComposer.internalOnly.test.tsx (task 068; owner 2026-10-14)
 *
 * The "External" | "Internal Only" tags on the email composer (owner 2026-10-14): one choice, External by
 * default, its value reaches POST /api/communications/send as an explicit boolean, a reply/forward whose
 * source is known internal-only starts on Internal Only, and a thread the host knows is Private locks it there.
 * Mocking is at the network boundary only (`authenticatedFetch`).
 */
import * as React from 'react';
import { act, fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import { SendEmailPage } from '../wrappers/SendEmailPage';
import {
  initialInternalOnly,
  mapStateToSendRequest,
  initialState,
  emailComposerReducer,
} from '../EmailComposer.reducer';
import type { IEmailComposerHandle, IEmailComposerProps } from '../EmailComposer.types';

function okFetch() {
  return jest.fn().mockResolvedValue({
    ok: true,
    status: 200,
    headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => ({ communicationId: 'comm-1' }),
  } as unknown as Response);
}

function renderComposer(overrides: Partial<IEmailComposerProps> = {}) {
  const ref = React.createRef<IEmailComposerHandle>();
  const authenticatedFetch = okFetch();
  renderWithProviders(
    <EmailComposer
      ref={ref}
      mode="compose"
      mount="dialog"
      authenticatedFetch={authenticatedFetch as unknown as IEmailComposerProps['authenticatedFetch']}
      initialTo={['recipient@example.com']}
      initialSubject="Subject"
      initialBody="Body"
      onCancel={jest.fn()}
      {...overrides}
    />
  );
  return { ref, authenticatedFetch };
}

async function sentBody(ref: React.RefObject<IEmailComposerHandle>, authenticatedFetch: jest.Mock) {
  await act(async () => {
    await ref.current!.send();
  });
  expect(authenticatedFetch).toHaveBeenCalledTimes(1);
  const [url, init] = authenticatedFetch.mock.calls[0];
  expect(String(url)).toContain('/api/communications/send');
  return JSON.parse(init.body as string) as Record<string, unknown>;
}

describe('EmailComposer - External / Internal Only tags', () => {
  it('renders exactly one External / Internal Only choice, External selected by default', () => {
    renderComposer();
    expect(screen.getAllByRole('radiogroup')).toHaveLength(1);
    expect(screen.getByRole('radio', { name: 'External' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Internal Only' })).not.toBeChecked();
  });

  it('a compose sends isInternalOnly: false when the switch is left off (an explicit false, never omitted)', async () => {
    const { ref, authenticatedFetch } = renderComposer();
    const body = await sentBody(ref, authenticatedFetch);
    expect(body).toHaveProperty('isInternalOnly', false);
  });

  it('sends isInternalOnly: true after the user switches it on', async () => {
    const { ref, authenticatedFetch } = renderComposer();
    const internal = screen.getByRole('radio', { name: 'Internal Only' });
    fireEvent.click(internal);
    expect(internal).toBeChecked();
    const body = await sentBody(ref, authenticatedFetch);
    expect(body).toHaveProperty('isInternalOnly', true);
  });

  it('a reply whose source is internal-only starts with Internal Only selected and sends true', async () => {
    const { ref, authenticatedFetch } = renderComposer({
      mode: 'reply',
      communicationId: 'src-1',
      sourceIsInternalOnly: true,
    });
    expect(screen.getByRole('radio', { name: 'Internal Only' })).toBeChecked();
    const body = await sentBody(ref, authenticatedFetch);
    expect(body).toHaveProperty('isInternalOnly', true);
  });

  it('a forward whose source is internal-only starts with Internal Only selected', () => {
    renderComposer({ mode: 'forward', communicationId: 'src-1', sourceIsInternalOnly: true });
    expect(screen.getByRole('radio', { name: 'Internal Only' })).toBeChecked();
  });

  it('a reply whose source is NOT known internal starts off', () => {
    renderComposer({ mode: 'reply', communicationId: 'src-1' });
    expect(screen.getByRole('radio', { name: 'Internal Only' })).not.toBeChecked();
  });

  // Fix round 1 (F1): the BFF honours an explicit false over its reply/forward raise, so an untouched reply or
  // forward of a source the host does not know must OMIT the field and let the server raise it.
  it('an untouched reply with a communicationId and no sourceIsInternalOnly sends NO isInternalOnly property', async () => {
    const { ref, authenticatedFetch } = renderComposer({ mode: 'reply', communicationId: 'src-1' });
    const body = await sentBody(ref, authenticatedFetch);
    expect(body).not.toHaveProperty('isInternalOnly');
    expect(body).toHaveProperty('inheritRegardingFromCommunicationId', 'src-1');
  });

  it('the same reply after the user toggles off then on sends an explicit true', async () => {
    const { ref, authenticatedFetch } = renderComposer({ mode: 'reply', communicationId: 'src-1' });
    const internal = screen.getByRole('radio', { name: 'Internal Only' });
    fireEvent.click(internal); // on
    fireEvent.click(screen.getByRole('radio', { name: 'External' })); // off
    fireEvent.click(internal); // on
    expect(internal).toBeChecked();
    const body = await sentBody(ref, authenticatedFetch);
    expect(body).toHaveProperty('isInternalOnly', true);
  });

  it('the same reply after the user toggles on then off sends an explicit false (the sender chose it)', async () => {
    const { ref, authenticatedFetch } = renderComposer({ mode: 'reply', communicationId: 'src-1' });
    const internal = screen.getByRole('radio', { name: 'Internal Only' });
    fireEvent.click(internal); // on
    fireEvent.click(screen.getByRole('radio', { name: 'External' })); // off
    expect(internal).not.toBeChecked();
    const body = await sentBody(ref, authenticatedFetch);
    expect(body).toHaveProperty('isInternalOnly', false);
  });

  it('an untouched forward of an unknown source also omits the field', async () => {
    const { ref, authenticatedFetch } = renderComposer({ mode: 'forward', communicationId: 'src-1' });
    const body = await sentBody(ref, authenticatedFetch);
    expect(body).not.toHaveProperty('isInternalOnly');
  });

  it('a reply whose source is known NOT internal sends an explicit false', async () => {
    const { ref, authenticatedFetch } = renderComposer({
      mode: 'reply',
      communicationId: 'src-1',
      sourceIsInternalOnly: false,
    });
    expect(screen.getByRole('radio', { name: 'Internal Only' })).not.toBeChecked();
    const body = await sentBody(ref, authenticatedFetch);
    expect(body).toHaveProperty('isInternalOnly', false);
  });

  it('an untouched reply on a Private thread still sends true even when the source is unknown', async () => {
    const { ref, authenticatedFetch } = renderComposer({
      mode: 'reply',
      communicationId: 'src-1',
      threadIsPrivate: true,
    });
    const body = await sentBody(ref, authenticatedFetch);
    expect(body).toHaveProperty('isInternalOnly', true);
  });

  it('a host that knows the thread is Private gets Internal Only selected and External unavailable, and send carries true', async () => {
    const { ref, authenticatedFetch } = renderComposer({ threadIsPrivate: true });
    expect(screen.getByRole('radio', { name: 'Internal Only' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'External' })).toHaveAttribute('aria-disabled', 'true');
    const body = await sentBody(ref, authenticatedFetch);
    expect(body).toHaveProperty('isInternalOnly', true);
  });

  it('a new compose never starts on by itself, even when sourceIsInternalOnly is passed', () => {
    renderComposer({ mode: 'compose', sourceIsInternalOnly: true });
    expect(screen.getByRole('radio', { name: 'Internal Only' })).not.toBeChecked();
  });

  it('the SendEmailPage wrapper (CommunicationActions PCF) forwards sourceIsInternalOnly so a reply starts on', () => {
    renderWithProviders(
      <SendEmailPage
        mode="reply"
        communicationId="src-1"
        sourceIsInternalOnly
        initialTo={['recipient@example.com']}
        initialSubject="Re: Subject"
        initialBody="Body"
        authenticatedFetch={okFetch() as unknown as IEmailComposerProps['authenticatedFetch']}
        bffBaseUrl="https://bff.example.com"
      />
    );
    expect(screen.getByRole('radio', { name: 'Internal Only' })).toBeChecked();
  });

  it('view mode (read-only) shows no External / Internal Only choice', () => {
    renderComposer({
      mode: 'view',
      communicationId: 'src-1',
      sourceRecord: {
        communicationId: 'src-1',
        from: 'a@example.com',
        to: ['b@example.com'],
        subject: 'S',
        body: 'B',
        bodyFormat: 'HTML',
      },
    });
    expect(screen.queryByRole('radiogroup')).toBeNull();
  });
});

describe('Internal Only - reducer and request mapping', () => {
  const base = { mount: 'dialog' as const, authenticatedFetch: jest.fn() };

  it('initialInternalOnly: Private wins, reply/forward of an internal source is on, everything else off', () => {
    expect(initialInternalOnly({ mode: 'compose' })).toBe(false);
    expect(initialInternalOnly({ mode: 'compose', sourceIsInternalOnly: true })).toBe(false);
    expect(initialInternalOnly({ mode: 'reply', sourceIsInternalOnly: true })).toBe(true);
    expect(initialInternalOnly({ mode: 'forward', sourceIsInternalOnly: true })).toBe(true);
    expect(initialInternalOnly({ mode: 'reply', sourceIsInternalOnly: false })).toBe(false);
    expect(initialInternalOnly({ mode: 'compose', threadIsPrivate: true })).toBe(true);
  });

  it('SET_INTERNAL_ONLY marks the switch touched and RESET clears it', () => {
    const props = { ...base, mode: 'reply', communicationId: 'src-1' } as IEmailComposerProps;
    const start = initialState(props);
    expect(start.internalOnlyTouched).toBe(false);
    const touched = emailComposerReducer(start, { type: 'SET_INTERNAL_ONLY', value: false });
    expect(touched.internalOnlyTouched).toBe(true);
    expect(mapStateToSendRequest(start)).not.toHaveProperty('isInternalOnly');
    expect(mapStateToSendRequest(touched)).toHaveProperty('isInternalOnly', false);
    const reset = emailComposerReducer(touched, { type: 'RESET', state: initialState(props) });
    expect(reset.internalOnlyTouched).toBe(false);
    expect(mapStateToSendRequest(reset)).not.toHaveProperty('isInternalOnly');
  });

  it('SET_INTERNAL_ONLY changes the state and a compose request carries an explicit boolean', () => {
    const start = initialState({ ...base, mode: 'compose' } as IEmailComposerProps);
    expect(mapStateToSendRequest(start).isInternalOnly).toBe(false);
    const on = emailComposerReducer(start, { type: 'SET_INTERNAL_ONLY', value: true });
    expect(on.isInternalOnly).toBe(true);
    expect(mapStateToSendRequest(on).isInternalOnly).toBe(true);
    // A host-known Private thread forces true even when the state says off.
    expect(mapStateToSendRequest(start, undefined, true).isInternalOnly).toBe(true);
  });
});
