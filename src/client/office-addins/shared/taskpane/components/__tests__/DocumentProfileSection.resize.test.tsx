/**
 * Task 105 (spaarkeai-word-add-in-r1, UAT round 7 item 4): the Profile's Summary viewport is resizable with the
 * shared ResizeHandle — by pointer drag and by Up/Down/Home/End, never below its minimum (or above its maximum),
 * and remembered per viewer in localStorage (a throwing localStorage must not break it).
 */
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  DocumentProfileSection,
  SUMMARY_DEFAULT_HEIGHT_PX,
  SUMMARY_KEY_STEP_PX,
  SUMMARY_MAX_HEIGHT_PX,
  SUMMARY_MIN_HEIGHT_PX,
  SUMMARY_STORAGE_KEY,
} from '../DocumentProfileSection';
import { apiClient } from '@shared/services';

jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
  };
});

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

const DOCUMENT_ID = '11111111-1111-1111-1111-111111111111';

async function renderCompleted() {
  (apiClient.get as jest.Mock).mockResolvedValue({
    data: { summary: 'The summary text.', tldr: 't', keywords: 'a', documentType: 'NDA', summaryStatus: 100000002 },
  });
  render(
    <FluentProvider theme={webLightTheme}>
      <DocumentProfileSection documentId={DOCUMENT_ID} />
    </FluentProvider>
  );
  const handle = await screen.findByTestId('profile-summary-handle');
  return { handle, summary: screen.getByTestId('profile-summary') };
}

const valueNow = (el: HTMLElement) => Number(el.getAttribute('aria-valuenow'));

describe('DocumentProfileSection — resizable Summary (task 105)', () => {
  beforeEach(() => {
    jest.restoreAllMocks();
    window.localStorage.clear();
  });

  it('exposes an ARIA horizontal separator at the default height, before any adjustment', async () => {
    const { handle, summary } = await renderCompleted();
    expect(handle.getAttribute('role')).toBe('separator');
    expect(handle.getAttribute('aria-orientation')).toBe('horizontal');
    expect(handle.getAttribute('tabindex')).toBe('0');
    expect(valueNow(handle)).toBe(SUMMARY_DEFAULT_HEIGHT_PX);
    expect(summary.style.height).toBe(''); // content-sized until the user adjusts
  });

  it('ArrowDown / ArrowUp change the height by a step and remember it', async () => {
    const { handle, summary } = await renderCompleted();
    fireEvent.keyDown(handle, { key: 'ArrowDown' });
    const grown = SUMMARY_DEFAULT_HEIGHT_PX + SUMMARY_KEY_STEP_PX;
    expect(summary.style.height).toBe(`${grown}px`);
    expect(window.localStorage.getItem(SUMMARY_STORAGE_KEY)).toBe(String(grown));
    fireEvent.keyDown(handle, { key: 'ArrowUp' });
    fireEvent.keyDown(handle, { key: 'ArrowUp' });
    expect(valueNow(handle)).toBe(SUMMARY_DEFAULT_HEIGHT_PX - SUMMARY_KEY_STEP_PX);
  });

  it('never goes below the minimum or above the maximum (Home / End / repeated keys)', async () => {
    const { handle, summary } = await renderCompleted();
    fireEvent.keyDown(handle, { key: 'Home' });
    expect(summary.style.height).toBe(`${SUMMARY_MIN_HEIGHT_PX}px`);
    fireEvent.keyDown(handle, { key: 'ArrowUp' });
    expect(summary.style.height).toBe(`${SUMMARY_MIN_HEIGHT_PX}px`);
    fireEvent.keyDown(handle, { key: 'End' });
    expect(summary.style.height).toBe(`${SUMMARY_MAX_HEIGHT_PX}px`);
    fireEvent.keyDown(handle, { key: 'ArrowDown' });
    expect(summary.style.height).toBe(`${SUMMARY_MAX_HEIGHT_PX}px`);
  });

  it('dragging with the pointer resizes by the pointer movement, and clamps at the minimum', async () => {
    const { handle, summary } = await renderCompleted();
    fireEvent.pointerDown(handle, { clientY: 400, pointerId: 1, button: 0 });
    fireEvent.pointerMove(handle, { clientY: 460, pointerId: 1 });
    expect(summary.style.height).toBe(`${SUMMARY_DEFAULT_HEIGHT_PX + 60}px`);
    fireEvent.pointerMove(handle, { clientY: -1000, pointerId: 1 });
    expect(summary.style.height).toBe(`${SUMMARY_MIN_HEIGHT_PX}px`);
    fireEvent.pointerUp(handle, { clientY: -1000, pointerId: 1 });
    fireEvent.pointerMove(handle, { clientY: 900, pointerId: 1 }); // no longer dragging
    expect(summary.style.height).toBe(`${SUMMARY_MIN_HEIGHT_PX}px`);
  });

  it('restores the remembered height on the next mount', async () => {
    window.localStorage.setItem(SUMMARY_STORAGE_KEY, '300');
    const { handle, summary } = await renderCompleted();
    expect(summary.style.height).toBe('300px');
    expect(valueNow(handle)).toBe(300);
  });

  it('works when localStorage throws (the height just is not remembered)', async () => {
    jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const { handle, summary } = await renderCompleted();
    fireEvent.keyDown(handle, { key: 'ArrowDown' });
    expect(summary.style.height).toBe(`${SUMMARY_DEFAULT_HEIGHT_PX + SUMMARY_KEY_STEP_PX}px`);
  });
});
