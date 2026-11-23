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

/**
 * Task 198 — a BLOCKING SprkModal (no `nonBlocking`) that yields to the lookup pane must not leave its backdrop over
 * the pane: Fluent renders `.fui-DialogSurface__backdrop` full-window for `modalType='modal'`, above the pane when
 * layering ends in 'dock' (pane layer without a numeric z-index) or is still 'pending'. Seen live on dev: file
 * preview → ⋮ → Email → To, the lookup pane could not be clicked.
 */
describe('SprkModal yieldToSidePane — a blocking modal gives up its backdrop (task 198)', () => {
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

  function stubPaneAppearing(zIndex: string): void {
    const layer = document.createElement('div');
    layer.style.zIndex = zIndex;
    layer.style.position = 'fixed';
    pane = document.createElement('div');
    layer.appendChild(pane);
    let shown = false;
    (document as { elementsFromPoint?: unknown }).elementsFromPoint = jest.fn(() => (shown && pane ? [pane] : []));
    setTimeout(() => {
      document.body.appendChild(layer);
      shown = true;
    }, 120);
  }

  /** True when a modal backdrop exists and would take pointer events (so would sit on top of the lookup pane). */
  function backdropTakesPointer(): boolean {
    const backdrop = document.querySelector('.fui-DialogSurface__backdrop');
    return backdrop !== null && window.getComputedStyle(backdrop).pointerEvents !== 'none';
  }

  const blocking = (yieldToSidePane: boolean, extra?: Partial<React.ComponentProps<typeof SprkModal>>) => (
    <SprkModal open onClose={noop} title="Preview" yieldToSidePane={yieldToSidePane} {...extra}>
      <div>Body</div>
    </SprkModal>
  );

  it('control: a non-yielding blocking modal has a backdrop that takes pointer events; a nonBlocking one has none', () => {
    const blockingRender = renderWithProviders(blocking(false));
    expect(backdropTakesPointer()).toBe(true);
    blockingRender.unmount();

    renderWithProviders(blocking(false, { nonBlocking: true }));
    expect(document.querySelector('.fui-DialogSurface__backdrop')).toBeNull();
  });

  it("layering 'dock' (pane layer has no z-index): no backdrop takes pointer events", () => {
    stubPaneAppearing('auto');
    renderWithProviders(blocking(true));
    act(() => {
      jest.advanceTimersByTime(300);
    });
    expect(screen.getByRole('dialog').style.marginRight).toBe('max(440px, 34vw)'); // really ended in dock
    expect(backdropTakesPointer()).toBe(false);
  });

  it("layering 'under' (pane layer z 1000): no backdrop takes pointer events", () => {
    stubPaneAppearing('1000');
    renderWithProviders(blocking(true));
    act(() => {
      jest.advanceTimersByTime(300);
    });
    expect(pageLayerOf(screen.getByRole('dialog')).style.zIndex).toBe('999'); // really ended in under
    expect(backdropTakesPointer()).toBe(false);
  });

  it("layering 'pending' (before the pane appears): no backdrop takes pointer events", () => {
    stubPaneAppearing('1000');
    renderWithProviders(blocking(true));
    expect(screen.getByRole('dialog')).toHaveAttribute('inert'); // yielding, pane not found yet
    expect(backdropTakesPointer()).toBe(false);
  });

  it('while yielding the surface takes neither pointer input nor keyboard focus (unchanged)', () => {
    stubPaneAppearing('auto');
    renderWithProviders(blocking(true));
    act(() => {
      jest.advanceTimersByTime(300);
    });
    const dialog = screen.getByRole('dialog');
    expect(dialog.style.pointerEvents).toBe('none');
    expect(dialog).toHaveAttribute('inert');
  });

  it('an alert modal that yields also stops blocking, and blocks again afterwards', () => {
    stubPaneAppearing('auto');
    const { rerender } = renderWithProviders(blocking(false, { dismiss: 'alert' }));
    expect(backdropTakesPointer()).toBe(true);
    rerender(blocking(true, { dismiss: 'alert' }));
    act(() => {
      jest.advanceTimersByTime(300);
    });
    expect(backdropTakesPointer()).toBe(false);
    rerender(blocking(false, { dismiss: 'alert' }));
    expect(backdropTakesPointer()).toBe(true);
  });

  it('`hidden` is unchanged: a hidden blocking modal is invisible and click-through, backdrop untouched', () => {
    renderWithProviders(blocking(false, { hidden: true }));
    const dialog = screen.getByRole('dialog', { hidden: true });
    expect(dialog.style.visibility).toBe('hidden');
    expect(dialog.style.pointerEvents).toBe('none');
    expect(dialog).not.toHaveAttribute('inert');
    expect(backdropTakesPointer()).toBe(true);
  });

  it('yielding on then off: backdrop returns and takes pointer events; body and surface are NOT remounted', () => {
    let mounts = 0;
    const Probe: React.FC = () => {
      const [clicks, setClicks] = React.useState(0);
      React.useEffect(() => {
        mounts += 1;
      }, []);
      return (
        <button data-testid="probe" onClick={() => setClicks(c => c + 1)}>
          clicks:{clicks}
        </button>
      );
    };
    const withProbe = (y: boolean) => (
      <SprkModal open onClose={noop} title="Preview" yieldToSidePane={y}>
        <Probe />
      </SprkModal>
    );
    stubPaneAppearing('auto');
    const { rerender } = renderWithProviders(withProbe(false));
    const surface = screen.getByRole('dialog');
    const probe = screen.getByTestId('probe');
    act(() => {
      probe.click();
    });
    expect(probe).toHaveTextContent('clicks:1');
    expect(mounts).toBe(1);

    rerender(withProbe(true));
    act(() => {
      jest.advanceTimersByTime(300);
    });
    expect(backdropTakesPointer()).toBe(false);
    expect(screen.getByRole('dialog')).toBe(surface);
    expect(screen.getByTestId('probe')).toBe(probe);
    expect(mounts).toBe(1);

    rerender(withProbe(false));
    expect(backdropTakesPointer()).toBe(true);
    expect(screen.getByRole('dialog')).toBe(surface);
    expect(screen.getByTestId('probe')).toBe(probe);
    expect(screen.getByTestId('probe')).toHaveTextContent('clicks:1'); // child state kept
    expect(mounts).toBe(1);
    expect(surface).not.toHaveAttribute('inert');
    expect(surface.style.pointerEvents).toBe('');
  });
  it('when yielding ends, any focus() Fluent calls inside the surface happens while it is still inert (a no-op in browsers)', () => {
    stubPaneAppearing('auto');
    const withButton = (y: boolean) => (
      <SprkModal open onClose={noop} title="Preview" yieldToSidePane={y}>
        <button>Inside</button>
      </SprkModal>
    );
    const { rerender } = renderWithProviders(withButton(true));
    act(() => {
      jest.advanceTimersByTime(300);
    });
    const surface = screen.getByRole('dialog');
    const calls: boolean[] = [];
    const realFocus = HTMLElement.prototype.focus;
    const spy = jest.spyOn(HTMLElement.prototype, 'focus').mockImplementation(function (this: HTMLElement, ...args) {
      if (surface.contains(this)) calls.push(surface.hasAttribute('inert'));
      return realFocus.apply(this, args);
    });
    try {
      rerender(withButton(false));
    } finally {
      spy.mockRestore();
    }
    expect(calls.length).toBeGreaterThan(0); // Fluent did try to move focus in, so the ordering is what is tested
    expect(calls.every(inertAtCall => inertAtCall)).toBe(true);
    expect(surface).not.toHaveAttribute('inert');
  });
});
