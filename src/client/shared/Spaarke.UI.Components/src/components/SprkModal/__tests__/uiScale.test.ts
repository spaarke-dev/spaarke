/**
 * uiScale.test.ts — the app-shell `uiScale` derivation (spec FR-06 / design
 * §6.9 / P0.5). Verifies the auto ≥2560 breakpoint, the Default/Large/
 * Extra-large multiplier mapping, the setting × breakpoint precedence
 * (`max()`), the Display-size persistence round-trip (reusing the existing
 * theme-storage localStorage + THEME_CHANGE_EVENT mechanism — no second
 * storage mechanism), and the `subscribeToViewportBreakpoint` matchMedia
 * subscription (incl. its defensive no-throw fallback).
 */
import {
  UI_SCALE_BREAKPOINT_PX,
  UI_SCALE_BREAKPOINT_MULTIPLIER,
  DISPLAY_SIZE_MULTIPLIERS,
  isLargeViewport,
  getEffectiveUiScale,
  subscribeToViewportBreakpoint,
  getDisplaySizePreference,
  setDisplaySizePreference,
} from '../uiScale';
import { DISPLAY_SIZE_STORAGE_KEY, THEME_CHANGE_EVENT } from '../../../utils/themeStorage';

// Mirrors utils/__tests__/themeStorage.test.ts's localStorage mock so the
// persistence round-trip is verified against the SAME double-shape.
const localStorageMock = (() => {
  let store: Record<string, string> = {};
  return {
    getItem: jest.fn((key: string) => store[key] || null),
    setItem: jest.fn((key: string, value: string) => {
      store[key] = value;
    }),
    removeItem: jest.fn((key: string) => {
      delete store[key];
    }),
    clear: jest.fn(() => {
      store = {};
    }),
  };
})();

Object.defineProperty(window, 'localStorage', { value: localStorageMock });

function setInnerWidth(width: number): void {
  Object.defineProperty(window, 'innerWidth', { writable: true, configurable: true, value: width });
}

describe('uiScale constants (FR-06 / design §6.9)', () => {
  it('names the auto breakpoint at 2560 CSS px → 1.15', () => {
    expect(UI_SCALE_BREAKPOINT_PX).toBe(2560);
    expect(UI_SCALE_BREAKPOINT_MULTIPLIER).toBe(1.15);
  });

  it('maps Default/Large/Extra-large to 1 / 1.25 / 1.5', () => {
    expect(DISPLAY_SIZE_MULTIPLIERS.default).toBe(1);
    expect(DISPLAY_SIZE_MULTIPLIERS.large).toBe(1.25);
    expect(DISPLAY_SIZE_MULTIPLIERS['extra-large']).toBe(1.5);
  });
});

describe('isLargeViewport (auto ≥2560 breakpoint)', () => {
  const original = window.innerWidth;
  afterEach(() => setInnerWidth(original));

  it('is false just under the breakpoint (2559px)', () => {
    setInnerWidth(2559);
    expect(isLargeViewport()).toBe(false);
  });

  it('is true exactly at the breakpoint (2560px)', () => {
    setInnerWidth(2560);
    expect(isLargeViewport()).toBe(true);
  });

  it('is true above the breakpoint (3000px)', () => {
    setInnerWidth(3000);
    expect(isLargeViewport()).toBe(true);
  });

  it('is false at a typical laptop width (1440px)', () => {
    setInnerWidth(1440);
    expect(isLargeViewport()).toBe(false);
  });
});

describe('getEffectiveUiScale — setting × breakpoint precedence (max)', () => {
  const original = window.innerWidth;
  afterEach(() => setInnerWidth(original));

  it('Default + viewport <2560 → 1.0 (no bump)', () => {
    setInnerWidth(1440);
    expect(getEffectiveUiScale('default')).toBe(1);
  });

  it('Default + viewport ≥2560 → auto breakpoint bumps to 1.15', () => {
    setInnerWidth(2560);
    expect(getEffectiveUiScale('default')).toBe(1.15);
  });

  it('Large + viewport <2560 → 1.25 (the setting)', () => {
    setInnerWidth(1440);
    expect(getEffectiveUiScale('large')).toBe(1.25);
  });

  it('Large + viewport ≥2560 → 1.25 (the setting wins — already > the 1.15 bump)', () => {
    setInnerWidth(2560);
    expect(getEffectiveUiScale('large')).toBe(1.25);
  });

  it('Extra-large + viewport <2560 → 1.5 (the setting)', () => {
    setInnerWidth(1440);
    expect(getEffectiveUiScale('extra-large')).toBe(1.5);
  });

  it('Extra-large + viewport ≥2560 → 1.5 (the setting wins)', () => {
    setInnerWidth(2560);
    expect(getEffectiveUiScale('extra-large')).toBe(1.5);
  });

  it('defaults the displaySize argument to the persisted preference when omitted', () => {
    localStorageMock.clear();
    setInnerWidth(1440);
    setDisplaySizePreference('large');
    expect(getEffectiveUiScale()).toBe(1.25);
  });
});

describe('Display-size persistence (reuses the existing theme-storage pattern)', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    localStorageMock.clear();
  });

  it('getDisplaySizePreference defaults to "default" when unset', () => {
    expect(getDisplaySizePreference()).toBe('default');
  });

  it('getDisplaySizePreference defaults to "default" for an invalid stored value', () => {
    localStorageMock.getItem.mockReturnValueOnce('invalid-value');
    expect(getDisplaySizePreference()).toBe('default');
  });

  it('round-trips large / extra-large through localStorage', () => {
    setDisplaySizePreference('large');
    expect(localStorageMock.setItem).toHaveBeenCalledWith(DISPLAY_SIZE_STORAGE_KEY, 'large');
    expect(getDisplaySizePreference()).toBe('large');

    setDisplaySizePreference('extra-large');
    expect(getDisplaySizePreference()).toBe('extra-large');
  });

  it('dispatches the SAME THEME_CHANGE_EVENT the theme system uses (no second event/listener)', () => {
    const dispatchEventSpy = jest.spyOn(window, 'dispatchEvent');
    setDisplaySizePreference('large');

    expect(dispatchEventSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        type: THEME_CHANGE_EVENT,
        detail: { displaySize: 'large' },
      })
    );
    dispatchEventSpy.mockRestore();
  });
});

describe('subscribeToViewportBreakpoint', () => {
  let addEventListenerSpy: jest.SpyInstance;
  let removeEventListenerSpy: jest.SpyInstance;

  beforeEach(() => {
    addEventListenerSpy = jest.fn();
    removeEventListenerSpy = jest.fn();
  });

  it('queries matchMedia with the exact breakpoint and registers a change listener', () => {
    const matchMediaSpy = jest.spyOn(window, 'matchMedia').mockReturnValue({
      matches: false,
      media: '',
      onchange: null,
      addEventListener: addEventListenerSpy,
      removeEventListener: removeEventListenerSpy,
      addListener: jest.fn(),
      removeListener: jest.fn(),
      dispatchEvent: jest.fn(),
    } as unknown as MediaQueryList);

    const onChange = jest.fn();
    const cleanup = subscribeToViewportBreakpoint(onChange);

    expect(matchMediaSpy).toHaveBeenCalledWith(`(min-width: ${UI_SCALE_BREAKPOINT_PX}px)`);
    expect(addEventListenerSpy).toHaveBeenCalledWith('change', expect.any(Function));

    // Firing the registered handler invokes onChange.
    const handler = addEventListenerSpy.mock.calls[0][1];
    handler();
    expect(onChange).toHaveBeenCalledTimes(1);

    cleanup();
    expect(removeEventListenerSpy).toHaveBeenCalledWith('change', expect.any(Function));

    matchMediaSpy.mockRestore();
  });

  it('falls back to legacy addListener/removeListener when addEventListener is unavailable', () => {
    const addListenerSpy = jest.fn();
    const removeListenerSpy = jest.fn();
    const matchMediaSpy = jest.spyOn(window, 'matchMedia').mockReturnValue({
      matches: false,
      media: '',
      onchange: null,
      addListener: addListenerSpy,
      removeListener: removeListenerSpy,
      dispatchEvent: jest.fn(),
    } as unknown as MediaQueryList);

    const cleanup = subscribeToViewportBreakpoint(jest.fn());
    expect(addListenerSpy).toHaveBeenCalledWith(expect.any(Function));

    cleanup();
    expect(removeListenerSpy).toHaveBeenCalledWith(expect.any(Function));

    matchMediaSpy.mockRestore();
  });

  it('returns a no-op cleanup (does not throw) when matchMedia throws', () => {
    const matchMediaSpy = jest.spyOn(window, 'matchMedia').mockImplementation(() => {
      throw new Error('matchMedia not implemented');
    });

    let cleanup: (() => void) | undefined;
    expect(() => {
      cleanup = subscribeToViewportBreakpoint(jest.fn());
    }).not.toThrow();
    expect(typeof cleanup).toBe('function');
    expect(() => cleanup?.()).not.toThrow();

    matchMediaSpy.mockRestore();
  });

  it('returns a no-op cleanup when matchMedia is unavailable', () => {
    const original = window.matchMedia;
    // @ts-expect-error — simulate an environment without matchMedia
    delete window.matchMedia;

    let cleanup: (() => void) | undefined;
    expect(() => {
      cleanup = subscribeToViewportBreakpoint(jest.fn());
    }).not.toThrow();
    expect(typeof cleanup).toBe('function');

    window.matchMedia = original;
  });
});
