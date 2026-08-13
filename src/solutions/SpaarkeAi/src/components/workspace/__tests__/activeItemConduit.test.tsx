/**
 * activeItemConduit.test.tsx — spaarkeai-assistant-enhancements-r3 task 001.
 *
 * Unit-tests the widget-agnostic active-item conduit directly (no WorkspacePane harness needed — the
 * module is dependency-free). Asserts the four binding invariants:
 *   1. Publishing a handle exposes exactly ONE; publishing a second REPLACES it (never two).
 *   2. Publishing `null` (deselect / tab-switch to a non-item surface) CLEARS the handle.
 *   3. A grid MULTI-select path maps to `setActiveItem(null)` → handle cleared.
 *   4. The handle carries id/type/label ONLY — no content/bytes/body (ADR-015), asserted at compile
 *      time (@ts-expect-error) and at runtime (Object.keys).
 * Plus: off-provider null-safety (publisher inert, reader null) so isolated mounts are safe.
 *
 * Test category per ADR-038: Component/Contract Test (KEEP — the cross-pane conduit contract that
 * WorkspacePane publishes into and ConversationPane consumes).
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import {
  ActiveItemConduitProvider,
  useActiveItem,
  useActiveItemConduit,
  usePublishActiveItem,
  type ActiveItemHandle,
  type ActiveItemConduitValue,
} from '../activeItemConduit';

// Captured from within the tree so tests can drive the feed + inspect the conduit imperatively.
let publish: (handle: ActiveItemHandle | null) => void = () => {};
let conduit: ActiveItemConduitValue | null = null;

function Harness(): React.JSX.Element {
  publish = usePublishActiveItem();
  conduit = useActiveItemConduit();
  const item = useActiveItem();
  return (
    <div data-testid="active">{item ? `${item.type}|${item.id}|${item.label}` : 'NONE'}</div>
  );
}

function renderWithProvider(): { text: () => string } {
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <ActiveItemConduitProvider>
        <Harness />
      </ActiveItemConduitProvider>
    </FluentProvider>,
  );
  return { text: () => utils.getByTestId('active').textContent ?? '' };
}

beforeEach(() => {
  publish = () => {};
  conduit = null;
});

describe('activeItemConduit', () => {
  it('publishes exactly one handle and a second publish REPLACES it (never two)', () => {
    const { text } = renderWithProvider();
    expect(text()).toBe('NONE');

    act(() => publish({ id: 'sess-1', type: 'compose', label: 'NDA.docx' }));
    expect(text()).toBe('compose|sess-1|NDA.docx');
    expect(conduit?.getActiveItem()).toEqual({ id: 'sess-1', type: 'compose', label: 'NDA.docx' });

    // Second publish replaces — the single active item is the newest one, not both.
    act(() => publish({ id: 'sess-2', type: 'compose', label: 'MSA.docx' }));
    expect(text()).toBe('compose|sess-2|MSA.docx');
    expect(conduit?.getActiveItem()).toEqual({ id: 'sess-2', type: 'compose', label: 'MSA.docx' });
  });

  it('publishing null (deselect / tab-switch to a non-item surface) clears the handle', () => {
    const { text } = renderWithProvider();
    act(() => publish({ id: 'sess-1', type: 'compose', label: 'NDA.docx' }));
    expect(text()).toBe('compose|sess-1|NDA.docx');

    act(() => publish(null));
    expect(text()).toBe('NONE');
    expect(conduit?.getActiveItem()).toBeNull();
  });

  it('a grid MULTI-select maps to setActiveItem(null) → conduit cleared (cards would suppress)', () => {
    const { text } = renderWithProvider();

    // A single selection yields one handle...
    const selectionToHandle = (rows: Array<{ id: string; label: string }>): ActiveItemHandle | null =>
      rows.length === 1 ? { id: rows[0].id, type: 'document', label: rows[0].label } : null;

    act(() => publish(selectionToHandle([{ id: 'row-1', label: 'Deed.pdf' }])));
    expect(text()).toBe('document|row-1|Deed.pdf');

    // ...but a MULTI-select (2+ rows) maps to null → the conduit clears (no single active item).
    act(() =>
      publish(
        selectionToHandle([
          { id: 'row-1', label: 'Deed.pdf' },
          { id: 'row-2', label: 'Lease.pdf' },
        ]),
      ),
    );
    expect(text()).toBe('NONE');
    expect(conduit?.getActiveItem()).toBeNull();
  });

  it('the handle carries id/type/label ONLY — no content/bytes/body (ADR-015)', () => {
    // Compile-time: a content/bytes/body field must NOT be assignable to ActiveItemHandle.
    // @ts-expect-error — the conduit handle is content-free by contract (ADR-015).
    const forbidden: ActiveItemHandle = { id: 'x', type: 'compose', label: 'y', content: 'BYTES' };
    void forbidden;

    renderWithProvider();
    // Runtime: even if a caller passes an extra field, the conduit retains ONLY id/type/label.
    // Cast smuggles an extra `content` field past the type; the conduit must DROP it at runtime.
    const smuggled = { id: 'sess-9', type: 'compose', label: 'Contract.docx', content: 'NEVER' } as unknown as ActiveItemHandle;
    act(() => publish(smuggled));
    const stored = conduit?.getActiveItem();
    expect(stored).not.toBeNull();
    expect(Object.keys(stored as object).sort()).toEqual(['id', 'label', 'type']);
    expect((stored as unknown as { content?: unknown }).content).toBeUndefined();
  });

  it('is inert + null OFF-provider (isolated / standalone mount is safe)', () => {
    const utils = render(
      <FluentProvider theme={webLightTheme}>
        <Harness />
      </FluentProvider>,
    );
    expect(utils.getByTestId('active').textContent).toBe('NONE');
    expect(conduit).toBeNull();
    // Publishing off-provider is a no-op (does not throw).
    act(() => publish({ id: 'sess-1', type: 'compose', label: 'NDA.docx' }));
    expect(utils.getByTestId('active').textContent).toBe('NONE');
  });
});
