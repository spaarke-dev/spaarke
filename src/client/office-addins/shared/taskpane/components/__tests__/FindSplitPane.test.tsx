/**
 * Unit tests for FindSplitPane (spaarkeai-word-add-in-r1 task 102, UAT round 6 item 4).
 *
 * Covers the ARIA window-splitter contract: role/orientation/valuenow, even initial split, keyboard
 * (Up/Down/Home/End) and pointer-drag resizing, the minimum-height clamp, and per-viewer persistence
 * that tolerates a throwing localStorage. jsdom has no layout, so the container's bounding rect is
 * stubbed to a known height.
 */

import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { FindSplitPane, SPLIT_MIN_REGION_PX, SPLIT_STORAGE_KEY } from '../FindSplitPane';

// jsdom (older versions) has no PointerEvent — without it fireEvent.pointer* loses clientY.
if (typeof (globalThis as { PointerEvent?: unknown }).PointerEvent === 'undefined') {
  (globalThis as { PointerEvent?: unknown }).PointerEvent = class extends MouseEvent {
    pointerId: number;
    constructor(type: string, init: PointerEventInit = {}) {
      super(type, init);
      this.pointerId = init.pointerId ?? 1;
    }
  };
}

const HEIGHT = 510; // usable = 500 (divider is 10px)

function renderSplit() {
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <FindSplitPane top={<div>top</div>} bottom={<div>bottom</div>} />
    </FluentProvider>
  );
  const root = screen.getByTestId('find-split-pane');
  root.getBoundingClientRect = () =>
    ({ top: 100, height: HEIGHT, bottom: 100 + HEIGHT, left: 0, right: 300, width: 300, x: 0, y: 100 }) as DOMRect;
  return { ...utils, divider: screen.getByRole('separator') };
}

const valueNow = (el: HTMLElement) => Number(el.getAttribute('aria-valuenow'));

describe('FindSplitPane', () => {
  beforeEach(() => window.localStorage.clear());

  it('is an ARIA horizontal separator that starts as an even split', () => {
    const { divider } = renderSplit();
    expect(divider.getAttribute('aria-orientation')).toBe('horizontal');
    expect(divider.getAttribute('tabindex')).toBe('0');
    expect(valueNow(divider)).toBe(50);
    expect(screen.getByTestId('find-split-top').style.flexGrow).toBe('0.5');
    expect(screen.getByTestId('find-split-bottom').style.flexGrow).toBe('0.5');
  });

  it('ArrowDown grows the top region, ArrowUp shrinks it (and the bottom mirrors)', () => {
    const { divider } = renderSplit();
    fireEvent.keyDown(divider, { key: 'ArrowDown' });
    expect(valueNow(divider)).toBe(55);
    expect(Number(screen.getByTestId('find-split-top').style.flexGrow)).toBeCloseTo(0.55);
    expect(Number(screen.getByTestId('find-split-bottom').style.flexGrow)).toBeCloseTo(0.45);
    fireEvent.keyDown(divider, { key: 'ArrowUp' });
    fireEvent.keyDown(divider, { key: 'ArrowUp' });
    expect(valueNow(divider)).toBe(45);
  });

  it('keyboard never goes below the minimum region height (Home / End)', () => {
    const { divider } = renderSplit();
    const minRatio = SPLIT_MIN_REGION_PX / (HEIGHT - 10);
    fireEvent.keyDown(divider, { key: 'Home' });
    expect(Number(screen.getByTestId('find-split-top').style.flexGrow)).toBeCloseTo(minRatio);
    fireEvent.keyDown(divider, { key: 'ArrowUp' });
    expect(Number(screen.getByTestId('find-split-top').style.flexGrow)).toBeCloseTo(minRatio);
    fireEvent.keyDown(divider, { key: 'End' });
    expect(Number(screen.getByTestId('find-split-bottom').style.flexGrow)).toBeCloseTo(minRatio);
    fireEvent.keyDown(divider, { key: 'ArrowDown' });
    expect(Number(screen.getByTestId('find-split-bottom').style.flexGrow)).toBeCloseTo(minRatio);
  });

  it('dragging with the pointer moves the divider to the pointer position', () => {
    const { divider } = renderSplit();
    fireEvent.pointerDown(divider, { clientY: 355, pointerId: 1, button: 0 });
    // top=100, divider half=5, usable=500 -> clientY 355 => (355-100-5)/500 = 0.5; move to 0.7
    fireEvent.pointerMove(divider, { clientY: 100 + 5 + 350, pointerId: 1 });
    expect(valueNow(divider)).toBe(70);
    fireEvent.pointerUp(divider, { pointerId: 1 });
    // Further moves after release do nothing.
    fireEvent.pointerMove(divider, { clientY: 150, pointerId: 1 });
    expect(valueNow(divider)).toBe(70);
  });

  it('dragging past either end clamps at the minimum region height', () => {
    const { divider } = renderSplit();
    const minPct = Math.round((SPLIT_MIN_REGION_PX / (HEIGHT - 10)) * 100);
    fireEvent.pointerDown(divider, { clientY: 355, pointerId: 1, button: 0 });
    fireEvent.pointerMove(divider, { clientY: 0, pointerId: 1 });
    expect(valueNow(divider)).toBe(minPct);
    fireEvent.pointerMove(divider, { clientY: 5000, pointerId: 1 });
    expect(valueNow(divider)).toBe(100 - minPct);
  });

  it('remembers the split and restores it on the next mount', () => {
    const first = renderSplit();
    fireEvent.keyDown(first.divider, { key: 'ArrowDown' });
    expect(window.localStorage.getItem(SPLIT_STORAGE_KEY)).toBe('0.55');
    first.unmount();
    const second = renderSplit();
    expect(valueNow(second.divider)).toBe(55);
  });

  it('works when localStorage throws (private window)', () => {
    const getSpy = jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const setSpy = jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    try {
      const { divider } = renderSplit();
      expect(valueNow(divider)).toBe(50);
      fireEvent.keyDown(divider, { key: 'ArrowDown' });
      expect(valueNow(divider)).toBe(55);
    } finally {
      getSpy.mockRestore();
      setSpy.mockRestore();
    }
  });
});
