/**
 * useMcpEmailHostProps (task 011): one key per compose session, stable across re-renders, and the engine hides every
 * control the v1 host leaves undefined (harness mount of the real EmailComposer with a fake caller).
 */
import * as React from 'react';
import { cleanup, renderHook } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import { useMcpEmailHostProps } from '../useMcpEmailHostProps';
import type { McpToolCaller } from '../mcpToolCaller';

const caller: McpToolCaller = { callTool: jest.fn(async () => ({})) };
const matter = { id: 'matter-1', name: 'Smith v. Jones' };

afterEach(() => {
  cleanup();
  jest.clearAllMocks();
});

describe('useMcpEmailHostProps', () => {
  it('keeps one handler set (and idempotency key) across re-renders, even with a new inline pickDocument', () => {
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

  it('starts a new compose session (new key) when the matter changes', () => {
    const { result, rerender } = renderHook(
      ({ id }: { id: string }) => useMcpEmailHostProps({ caller, regarding: { id } }),
      {
        initialProps: { id: 'm1' },
      }
    );
    const first = result.current.idempotencyKey;
    rerender({ id: 'm2' });
    expect(result.current.idempotencyKey).not.toBe(first);
  });

  it('omits the attach picker when no pickDocument is given', () => {
    const { result } = renderHook(() => useMcpEmailHostProps({ caller, regarding: matter }));
    expect(result.current.onLookupRecord).toBeUndefined();
  });

  function Harness() {
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
      />
    );
  }

  it('renders the composer with the matter-documents attach control and none of the out-of-scope controls', () => {
    const errors = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const { container, queryByText, queryByLabelText } = renderWithProviders(<Harness />);
    expect(container.textContent).toContain('Smith v. Jones'); // preset regarding matter
    expect(container.textContent).toContain('Attach'); // matter-documents attach menu
    expect(queryByLabelText(/template/i)).toBeNull();
    expect(queryByLabelText(/draft with ai|ai draft/i)).toBeNull();
    expect(queryByText(/add files/i)).toBeNull();
    expect(errors).not.toHaveBeenCalled();
    errors.mockRestore();
  });
});
