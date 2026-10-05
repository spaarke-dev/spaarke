/**
 * taskPaneWidthService (task 095, item 1): +75 px over each platform's documented default via the released
 * `Office.extensionLifeCycle.taskpane.setWidth` (TaskPaneApi 1.1); best-effort, never throws.
 */
import { preferredTaskPaneWidth, requestWiderTaskPane, TASK_PANE_WIDTH_INCREASE_PX } from '../taskPaneWidthService';

interface FakeOffice {
  context: { platform: string; requirements: { isSetSupported: jest.Mock } };
  extensionLifeCycle?: { taskpane?: { setWidth?: jest.Mock } };
}

function installOffice(over: Partial<FakeOffice> & { platform?: string; supported?: boolean } = {}): jest.Mock {
  const setWidth = jest.fn();
  const fake: FakeOffice = {
    context: {
      platform: over.platform ?? 'OfficeOnline',
      requirements: { isSetSupported: jest.fn().mockReturnValue(over.supported ?? true) },
    },
    extensionLifeCycle: 'extensionLifeCycle' in over ? over.extensionLifeCycle : { taskpane: { setWidth } },
  };
  (globalThis as unknown as { Office: FakeOffice }).Office = fake;
  return setWidth;
}

describe('taskPaneWidthService (task 095)', () => {
  const original = (globalThis as unknown as { Office?: unknown }).Office;
  afterEach(() => {
    (globalThis as unknown as { Office?: unknown }).Office = original;
  });

  it('is +75 px over each documented default', () => {
    expect(TASK_PANE_WIDTH_INCREASE_PX).toBe(75);
    expect(preferredTaskPaneWidth('OfficeOnline')).toBe(405);
    expect(preferredTaskPaneWidth('PC')).toBe(395);
    expect(preferredTaskPaneWidth('Mac')).toBe(345);
    expect(preferredTaskPaneWidth('iOS')).toBeNull();
    expect(preferredTaskPaneWidth(undefined)).toBeNull();
  });

  it.each([
    ['OfficeOnline', 405],
    ['PC', 395],
    ['Mac', 345],
  ])('requests %s width %i via setWidth from the default width', (platform, width) => {
    const setWidth = installOffice({ platform });
    expect(requestWiderTaskPane(width - TASK_PANE_WIDTH_INCREASE_PX)).toBe(width);
    expect(setWidth).toHaveBeenCalledWith(width);
  });

  it('never shrinks a pane already at or beyond the target (a user who dragged it wider keeps it)', () => {
    const setWidth = installOffice({ platform: 'OfficeOnline' });
    expect(requestWiderTaskPane(405)).toBeNull();
    expect(requestWiderTaskPane(480)).toBeNull();
    expect(setWidth).not.toHaveBeenCalled();
  });

  it('defaults the current width to the pane viewport (window.innerWidth)', () => {
    const setWidth = installOffice({ platform: 'OfficeOnline' });
    const original = window.innerWidth;
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 330 });
    try {
      expect(requestWiderTaskPane()).toBe(405);
      expect(setWidth).toHaveBeenCalledWith(405);
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: original });
    }
  });

  it('does nothing when TaskPaneApi 1.1 is unsupported', () => {
    const setWidth = installOffice({ supported: false });
    expect(requestWiderTaskPane(330)).toBeNull();
    expect(setWidth).not.toHaveBeenCalled();
  });

  it('does nothing on an unknown platform, or when the API object is absent', () => {
    const setWidth = installOffice({ platform: 'iOS' });
    expect(requestWiderTaskPane(300)).toBeNull();
    expect(setWidth).not.toHaveBeenCalled();

    installOffice({ extensionLifeCycle: {} });
    expect(requestWiderTaskPane(330)).toBeNull();
  });

  it('never throws, even if setWidth does', () => {
    const setWidth = installOffice();
    setWidth.mockImplementation(() => {
      throw new Error('boom');
    });
    expect(() => requestWiderTaskPane(330)).not.toThrow();
    expect(requestWiderTaskPane(330)).toBeNull();
  });
});
