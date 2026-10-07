/**
 * taskPaneWidthService expand/collapse (task 103, UAT round 6 item 6): requests 3x the platform default and steps
 * down when the host silently ignores an out-of-limit request; collapse returns to the round-4 width.
 */
import {
  collapseTaskPane,
  expandCandidateWidths,
  expandTaskPane,
  isTaskPaneResizeSupported,
} from '../taskPaneWidthService';

interface HostOptions {
  platform?: string;
  supported?: boolean;
  min?: number;
  max?: number;
  noApi?: boolean;
}

/** A fake host whose setWidth silently ignores values outside [min, max] — like the real one. */
function installHost(options: HostOptions = {}): jest.Mock {
  const { platform = 'OfficeOnline', supported = true, min = 330, max = 500, noApi = false } = options;
  const setWidth = jest.fn((width: number) => {
    if (width < min || width > max) return;
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: width });
    window.dispatchEvent(new Event('resize'));
  });
  (globalThis as unknown as { Office: unknown }).Office = {
    context: { platform, requirements: { isSetSupported: jest.fn().mockReturnValue(supported) } },
    extensionLifeCycle: noApi ? {} : { taskpane: { setWidth } },
  };
  return setWidth;
}

function setInnerWidth(value: number): void {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value });
}

function setScreenWidth(value: number): void {
  Object.defineProperty(window.screen, 'availWidth', { configurable: true, value });
}

function setOuterWidth(value: number): void {
  Object.defineProperty(window, 'outerWidth', { configurable: true, value });
}

describe('taskPaneWidthService expand/collapse (task 103, 104)', () => {
  const originalOffice = (globalThis as unknown as { Office?: unknown }).Office;
  const originalWidth = window.innerWidth;
  const originalOuter = window.outerWidth;
  const originalScreen = window.screen.availWidth;
  beforeEach(() => {
    jest.useFakeTimers();
    setInnerWidth(330);
    setOuterWidth(0); // no window width unless a test sets one (screen fallback)
  });
  afterEach(() => {
    jest.useRealTimers();
    (globalThis as unknown as { Office?: unknown }).Office = originalOffice;
    setInnerWidth(originalWidth);
    setOuterWidth(originalOuter);
    setScreenWidth(originalScreen);
  });

  it('is supported only with TaskPaneApi 1.1, setWidth and a known platform (never by host type)', () => {
    installHost();
    expect(isTaskPaneResizeSupported()).toBe(true);
    installHost({ supported: false });
    expect(isTaskPaneResizeSupported()).toBe(false);
    installHost({ noApi: true });
    expect(isTaskPaneResizeSupported()).toBe(false);
    installHost({ platform: 'iOS' });
    expect(isTaskPaneResizeSupported()).toBe(false);
    (globalThis as unknown as { Office?: unknown }).Office = undefined;
    expect(isTaskPaneResizeSupported()).toBe(false);
  });

  // Task 108 (owner UAT round 10): 2x the default (was 3x, which covered the whole Word window), capped at 35% of the
  // screen when the window width is unknown, then one 85% step, then the web cap.
  it('no usable window width: 2x default capped at 35% of the screen, one step down, then the web cap', () => {
    expect(expandCandidateWidths('OfficeOnline', 330, 1920, 0)).toEqual([660, 561, 500]);
    expect(expandCandidateWidths('PC', 395, 1920, 0)).toEqual([640, 544]);
    expect(expandCandidateWidths('Mac', 345, 0, 0)).toEqual([540, 459]);
    expect(expandCandidateWidths('iOS', 300, 1920, 0)).toEqual([]);
  });

  it('drops candidates that are not wider than the current pane', () => {
    expect(expandCandidateWidths('OfficeOnline', 600, 1920, 0)).toEqual([660]);
  });

  it('web: a 1200 px browser window on a 2560 px screen never requests more than 600', () => {
    const candidates = expandCandidateWidths('OfficeOnline', 330, 2560, 1200);
    expect(candidates).toEqual([600, 510, 500]);
    expect(Math.max(...candidates)).toBe(600);
  });

  it('window cap is the smaller of 2x default and 50% of the window', () => {
    expect(expandCandidateWidths('OfficeOnline', 330, 2560, 3000)[0]).toBe(660);
    expect(expandCandidateWidths('PC', 395, 2560, 1000)[0]).toBe(500);
  });

  it('a small screen caps the desktop expansion below 2x', () => {
    expect(expandCandidateWidths('PC', 395, 1366, 400)).toEqual([478, 406]);
  });

  it('desktop: outerWidth about the pane size is not a window width, so the screen fallback applies', () => {
    expect(expandCandidateWidths('PC', 395, 1920, 400)).toEqual([640, 544]);
  });

  it('window width wins over the screen when both are available', () => {
    expect(expandCandidateWidths('PC', 395, 2560, 1000)).toEqual([500, 425]);
  });

  it('expandTaskPane reads window.outerWidth and requests nothing above half of it', async () => {
    const setWidth = installHost({ platform: 'OfficeOnline', min: 330, max: 5000 });
    setScreenWidth(2560);
    setOuterWidth(1200);
    const pending = expandTaskPane();
    await jest.advanceTimersByTimeAsync(5000);
    await expect(pending).resolves.toBe(600);
    expect(setWidth.mock.calls.map(c => c[0])).toEqual([600]);
  });

  it('web: steps down past ignored requests until 500 takes', async () => {
    const setWidth = installHost({ platform: 'OfficeOnline', min: 330, max: 500 });
    setScreenWidth(1920);
    const pending = expandTaskPane();
    await jest.advanceTimersByTimeAsync(5000);
    await expect(pending).resolves.toBe(500);
    expect(setWidth.mock.calls.map(c => c[0])).toEqual([660, 561, 500]);
    expect(window.innerWidth).toBe(500);
  });

  it('Windows: stops at the first width the host accepts (2x here)', async () => {
    const setWidth = installHost({ platform: 'PC', min: 86, max: 1200 });
    setScreenWidth(1920);
    setInnerWidth(395);
    const pending = expandTaskPane();
    await jest.advanceTimersByTimeAsync(5000);
    await expect(pending).resolves.toBe(640);
    expect(setWidth).toHaveBeenCalledTimes(1);
  });

  it('returns null and leaves the width when every request is ignored', async () => {
    installHost({ platform: 'OfficeOnline', min: 600, max: 620 });
    setScreenWidth(1920);
    const pending = expandTaskPane();
    await jest.advanceTimersByTimeAsync(5000);
    await expect(pending).resolves.toBeNull();
    expect(window.innerWidth).toBe(330);
  });

  it('returns null without calling setWidth when unsupported, and never throws', async () => {
    const unsupported = installHost({ supported: false });
    await expect(expandTaskPane()).resolves.toBeNull();
    expect(unsupported).not.toHaveBeenCalled();

    const throwing = installHost();
    throwing.mockImplementation(() => {
      throw new Error('boom');
    });
    await expect(expandTaskPane()).resolves.toBeNull();
  });

  it('collapse returns to the round-4 width per platform', () => {
    for (const [platform, width] of [
      ['OfficeOnline', 405],
      ['PC', 395],
      ['Mac', 345],
    ] as const) {
      const setWidth = installHost({ platform });
      expect(collapseTaskPane()).toBe(width);
      expect(setWidth).toHaveBeenCalledWith(width);
    }
  });

  it('collapse is a no-op when unsupported and never throws', () => {
    const setWidth = installHost({ supported: false });
    expect(collapseTaskPane()).toBeNull();
    expect(setWidth).not.toHaveBeenCalled();
    const throwing = installHost();
    throwing.mockImplementation(() => {
      throw new Error('boom');
    });
    expect(collapseTaskPane()).toBeNull();
  });
});
