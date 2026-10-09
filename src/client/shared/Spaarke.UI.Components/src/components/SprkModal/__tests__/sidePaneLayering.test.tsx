/**
 * SprkModal `yieldToSidePane` layering (owner test 2026-10-07, #1371): the host's lookup pane opens ON TOP of the
 * modal when its page layer can be placed above the modal's; otherwise the modal docks left of it.
 */
import * as React from 'react';
import { act, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { SprkModal } from '../SprkModal';

const noop = () => {};

function pageLayerOf(el: Element): HTMLElement {
  let node = el as HTMLElement;
  while (node.parentElement && node.parentElement !== document.body) node = node.parentElement;
  return node;
}

describe('SprkModal yieldToSidePane — layering under the lookup pane', () => {
  const original = (document as { elementsFromPoint?: unknown }).elementsFromPoint;
  let pane: HTMLElement | null = null;

  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
    pane?.parentElement?.remove();
    pane = null;
    if (original) (document as { elementsFromPoint?: unknown }).elementsFromPoint = original;
    else delete (document as { elementsFromPoint?: unknown }).elementsFromPoint;
  });

  /** A platform-like pane: its own body-level layer with the given z-index, appearing after the modal yields. */
  function stubPaneAppearing(zIndex: string): void {
    const layer = document.createElement('div');
    layer.style.zIndex = zIndex;
    layer.style.position = 'fixed';
    pane = document.createElement('div');
    layer.appendChild(pane);
    let shown = false;
    (document as { elementsFromPoint?: unknown }).elementsFromPoint = jest.fn(() => (shown && pane ? [pane] : []));
    // The pane renders a moment after the lookup is called.
    setTimeout(() => {
      document.body.appendChild(layer);
      shown = true;
    }, 120);
  }

  function renderYielding(yieldToSidePane: boolean) {
    return renderWithProviders(
      <SprkModal open onClose={noop} title="Manage Access" nonBlocking yieldToSidePane={yieldToSidePane}>
        <div>Body</div>
      </SprkModal>
    );
  }

  it('lowers the modal layer to just below the pane, so the pane opens on top; restores it afterwards', () => {
    const { rerender } = renderYielding(false);
    const ownLayer = pageLayerOf(screen.getByRole('dialog'));
    const before = ownLayer.style.zIndex;

    stubPaneAppearing('1000');
    rerender(
      <SprkModal open onClose={noop} title="Manage Access" nonBlocking yieldToSidePane>
        <div>Body</div>
      </SprkModal>
    );
    act(() => {
      jest.advanceTimersByTime(300);
    });

    const dialog = screen.getByRole('dialog');
    expect(ownLayer.style.zIndex).toBe('999');
    expect(dialog.style.marginRight).toBe(''); // not docked: the pane covers it
    expect(dialog.style.filter).toBe('brightness(0.75)');
    expect(dialog).toHaveAttribute('inert');

    rerender(
      <SprkModal open onClose={noop} title="Manage Access" nonBlocking>
        <div>Body</div>
      </SprkModal>
    );
    expect(ownLayer.style.zIndex).toBe(before);
    expect(screen.getByRole('dialog').style.filter).toBe('');
  });

  it('docks left when the pane layer has no usable z-index (cannot be placed above the modal)', () => {
    stubPaneAppearing('auto');
    renderYielding(true);
    act(() => {
      jest.advanceTimersByTime(300);
    });
    expect(screen.getByRole('dialog').style.marginRight).toBe('max(440px, 34vw)');
  });

  it('docks left when no pane appears within ~1.5 s', () => {
    (document as { elementsFromPoint?: unknown }).elementsFromPoint = jest.fn(() => []);
    renderYielding(true);
    act(() => {
      jest.advanceTimersByTime(1000);
    });
    expect(screen.getByRole('dialog').style.marginRight).toBe('');
    act(() => {
      jest.advanceTimersByTime(1000);
    });
    expect(screen.getByRole('dialog').style.marginRight).toBe('max(440px, 34vw)');
  });
});
