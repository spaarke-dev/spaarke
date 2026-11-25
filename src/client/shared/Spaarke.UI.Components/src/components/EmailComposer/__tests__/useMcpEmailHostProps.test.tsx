/**
 * useMcpEmailHostProps (task 011): the idempotency key lives and dies with the compose session (not with the caller
 * object, the matter's display name or the inline picker), and the engine hides every control the v1 host leaves
 * undefined or cannot honour (harness mount of the real EmailComposer with a fake caller, plus a control mount that
 * proves each "absent" assertion can fail).
 */
import * as React from 'react';
import { cleanup, fireEvent, renderHook, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { sendCommunication } from '../../../services/communicationApi';
import { EmailComposer } from '../EmailComposer';
import { useMcpEmailHostProps } from '../useMcpEmailHostProps';
import type { McpToolCaller, McpToolCallResult } from '../mcpToolCaller';

const caller: McpToolCaller = { callTool: jest.fn(async () => ({})) };
const matter = { id: 'matter-1', name: 'Smith v. Jones' };

function fakeCaller(handler: (name: string, args: Record<string, unknown>) => McpToolCallResult) {
  const callTool = jest.fn(async (name: string, args: Record<string, unknown>) => handler(name, args));
  return { callTool } as McpToolCaller & { callTool: jest.Mock };
}

const sendOptions = {
  to: ['a@example.com'],
  subject: 'Hello',
  body: '<p>Hi</p>',
  bodyFormat: 'html' as const,
  associations: [{ entityType: 'sprk_matter', entityId: 'matter-1' }],
};

afterEach(() => {
  cleanup();
  jest.clearAllMocks();
});

describe('useMcpEmailHostProps - the idempotency key', () => {
  it('keeps one handler set (and key) across re-renders, even with a new inline pickDocument', () => {
    const { result, rerender } = renderHook(
      ({ pick }: { pick: () => Promise<null> }) =>
        useMcpEmailHostProps({ caller, regarding: { ...matter }, pickDocument: pick }),
      { initialProps: { pick: async () => null } }
    );
    const first = result.current;
    rerender({ pick: async () => null });
    expect(result.current).toBe(first);
    expect(result.current.idempotencyKey).toBe(first.idempotencyKey);
  });

  it('starts a new compose session (new key) when the matter id changes', () => {
    const { result, rerender } = renderHook(
      ({ id }: { id: string }) => useMcpEmailHostProps({ caller, regarding: { id } }),
      { initialProps: { id: 'm1' } }
    );
    const first = result.current.idempotencyKey;
    rerender({ id: 'm2' });
    expect(result.current.idempotencyKey).not.toBe(first);
  });

  it('keeps the key when the caller object changes or the matter name arrives late', () => {
    const { result, rerender } = renderHook(
      ({ c, name }: { c: McpToolCaller; name?: string }) =>
        useMcpEmailHostProps({ caller: c, regarding: { id: 'm1', name } }),
      { initialProps: { c: fakeCaller(() => ({})) as McpToolCaller, name: undefined as string | undefined } }
    );
    const first = result.current.idempotencyKey;

    rerender({ c: fakeCaller(() => ({})) as McpToolCaller, name: undefined }); // a new callTool object
    expect(result.current.idempotencyKey).toBe(first);

    rerender({ c: fakeCaller(() => ({})) as McpToolCaller, name: 'Smith v. Jones' }); // the name arrives late
    expect(result.current.idempotencyKey).toBe(first);
    expect(result.current.associations[0].entityName).toBe('Smith v. Jones'); // the label did update
  });

  it('sends through the LATEST caller while keeping the key', async () => {
    const first = fakeCaller(() => ({
      isError: true,
      structuredContent: { status: 500, reasonCode: 'x', message: 'm' },
    }));
    const second = fakeCaller(() => ({ structuredContent: { communicationId: 'c1' } }));
    const { result, rerender } = renderHook(
      ({ c }: { c: McpToolCaller }) => useMcpEmailHostProps({ caller: c, regarding: matter }),
      { initialProps: { c: first as McpToolCaller } }
    );
    await sendCommunication(sendOptions, { authenticatedFetch: result.current.authenticatedFetch }).catch(
      () => undefined
    );
    rerender({ c: second as McpToolCaller });
    await sendCommunication(sendOptions, { authenticatedFetch: result.current.authenticatedFetch });

    expect(first.callTool).toHaveBeenCalledTimes(1);
    expect(second.callTool).toHaveBeenCalledTimes(1);
    expect(second.callTool.mock.calls[0][1].idempotencyKey).toBe(first.callTool.mock.calls[0][1].idempotencyKey);
  });

  it('sends the same key again after a failure, and a NEW key after a Send that returned a communicationId', async () => {
    let calls = 0;
    const c = fakeCaller(() => {
      calls += 1;
      if (calls === 1) throw new TypeError('Failed to fetch'); // timeout: outcome unknown
      return { structuredContent: { communicationId: `comm-${calls}` } };
    });
    const { result } = renderHook(() => useMcpEmailHostProps({ caller: c, regarding: matter }));
    const fetchAdapter = (...args: Parameters<typeof result.current.authenticatedFetch>) =>
      result.current.authenticatedFetch(...args);

    await sendCommunication(sendOptions, { authenticatedFetch: fetchAdapter }).catch(() => undefined); // fails
    await sendCommunication(sendOptions, { authenticatedFetch: fetchAdapter }); // retry: succeeds
    const keyAfterSuccess = result.current.idempotencyKey;
    await sendCommunication(sendOptions, { authenticatedFetch: fetchAdapter }); // a second deliberate send

    const keys = c.callTool.mock.calls.map(call => call[1].idempotencyKey);
    expect(keys).toHaveLength(3);
    expect(keys[1]).toBe(keys[0]); // retry after a failure replays the key
    expect(keys[2]).not.toBe(keys[1]); // after a confirmed send the key is new
    expect(keys[2]).toBe(keyAfterSuccess);
  });
});

describe('useMcpEmailHostProps - the host props', () => {
  it('omits the attach picker when no pickDocument is given', () => {
    const { result } = renderHook(() => useMcpEmailHostProps({ caller, regarding: matter }));
    expect(result.current.onLookupRecord).toBeUndefined();
  });

  it('fixes the sender and hides the controls the send tool has no field for', () => {
    const { result } = renderHook(() => useMcpEmailHostProps({ caller, regarding: matter }));
    expect(result.current).toMatchObject({
      sendMode: 'sharedMailbox',
      showBcc: false,
      showInternalOnly: false,
      associationsRemovable: false,
    });
  });
});

describe('EmailComposer with the MCP host (harness)', () => {
  function Harness({ hideUnsupported = true }: { hideUnsupported?: boolean }) {
    const host = useMcpEmailHostProps({ caller, regarding: matter, pickDocument: async () => null });
    return (
      <EmailComposer
        mode="compose"
        mount="inline"
        authenticatedFetch={host.authenticatedFetch}
        attachmentSources={host.attachmentSources}
        associations={host.associations}
        recordLookupCatalog={host.recordLookupCatalog}
        onLookupRecord={host.onLookupRecord}
        {...(hideUnsupported
          ? {
              sendMode: host.sendMode,
              showBcc: host.showBcc,
              showInternalOnly: host.showInternalOnly,
              associationsRemovable: host.associationsRemovable,
            }
          : {})}
      />
    );
  }

  const openAttachMenu = async () => {
    fireEvent.click(screen.getByRole('button', { name: 'Attach' }));
    return screen.findByRole('menuitem', { name: 'Link documents' });
  };

  it('shows the v1 controls and the matter-documents attach menu, with no console errors', async () => {
    const errors = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const { container } = renderWithProviders(<Harness />);
    expect(container.textContent).toContain('Smith v. Jones'); // preset regarding matter
    expect(screen.getByRole('region', { name: 'Recipients' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Subject' })).toBeInTheDocument();

    expect(await openAttachMenu()).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Add files' })).toBeNull();
    expect(errors).not.toHaveBeenCalled();
    errors.mockRestore();
  });

  it('hides every control the v1 host leaves out or cannot honour', () => {
    renderWithProviders(<Harness />);
    expect(screen.queryByRole('button', { name: 'Apply template' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Draft with AI' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'From mailbox' })).toBeNull(); // sender is fixed: no switcher
    expect(screen.queryByRole('button', { name: 'Bcc' })).toBeNull();
    expect(screen.queryByRole('radio', { name: 'Internal Only' })).toBeNull();
    expect(screen.queryByRole('radio', { name: 'External' })).toBeNull();
    expect(screen.queryByLabelText('Remove')).toBeNull(); // the matter chip cannot be removed
    expect(screen.queryByRole('button', { name: 'Link a record' })).toBeNull();
  });

  it('control: the same mount without the host switches DOES show From, Bcc, Internal Only and the chip remove', () => {
    renderWithProviders(<Harness hideUnsupported={false} />);
    expect(screen.getByRole('button', { name: 'From mailbox' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Bcc' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Internal Only' })).toBeInTheDocument();
    expect(screen.getByLabelText('Remove')).toBeInTheDocument();
  });
});
