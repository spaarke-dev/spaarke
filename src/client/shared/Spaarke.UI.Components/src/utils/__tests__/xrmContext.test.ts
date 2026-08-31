/**
 * xrmContext Utility Unit Tests
 *
 * @see utils/xrmContext.ts
 */

import {
  getXrm,
  getXrmPage,
  isCustomPageContext,
  isPcfContext,
  detectThemeFromHost,
  getClientUrl,
  getCurrentUserId,
  getCurrentUserName,
  type SidePane,
  type SidePanesApi,
  type PageInput,
} from '../xrmContext';

/**
 * jsdom marks `window.top` `[LegacyUnforgeable]` (configurable: false), so it
 * cannot be reassigned via `Object.defineProperty` (unlike `window.parent`,
 * which jsdom implements as a plain replaceable property). jsdom's getter
 * (`get top() { return window._top; }`) reads a plain internal `_top` field,
 * so tests mock the frame by assigning that field directly. This is a jsdom
 * implementation detail, not public API — scoped to this test file only.
 */
function setWindowTop(value: unknown): void {
  (window as unknown as { _top: unknown })._top = value;
}

describe('xrmContext', () => {
  // Save original window properties
  const originalXrm = (window as any).Xrm;
  const originalParent = window.parent;
  const originalTop = window.top;

  beforeEach(() => {
    // Reset window.Xrm before each test
    delete (window as any).Xrm;
    // Reset window.parent / window.top to window (same-origin default —
    // matches an un-nested MDA host where window/parent/top are the same frame)
    Object.defineProperty(window, 'parent', {
      value: window,
      writable: true,
    });
    setWindowTop(window);
  });

  afterEach(() => {
    // Restore original values
    if (originalXrm) {
      (window as any).Xrm = originalXrm;
    } else {
      delete (window as any).Xrm;
    }
    Object.defineProperty(window, 'parent', {
      value: originalParent,
      writable: true,
    });
    setWindowTop(originalTop);
  });

  describe('getXrm', () => {
    it('should return window.Xrm when available', () => {
      const mockXrm = {
        WebApi: {
          retrieveMultipleRecords: jest.fn(),
          retrieveRecord: jest.fn(),
          createRecord: jest.fn(),
          updateRecord: jest.fn(),
          deleteRecord: jest.fn(),
        },
      };
      (window as any).Xrm = mockXrm;

      const result = getXrm();

      expect(result).toBe(mockXrm);
    });

    it('should return parent.Xrm when window.Xrm is not available', () => {
      const mockParentXrm = {
        WebApi: {
          retrieveMultipleRecords: jest.fn(),
          retrieveRecord: jest.fn(),
          createRecord: jest.fn(),
          updateRecord: jest.fn(),
          deleteRecord: jest.fn(),
        },
      };

      // Create mock parent that's different from window
      const mockParent = { Xrm: mockParentXrm } as any;
      Object.defineProperty(window, 'parent', {
        value: mockParent,
        writable: true,
      });

      const result = getXrm();

      expect(result).toBe(mockParentXrm);
    });

    it('should prefer window.Xrm over parent.Xrm', () => {
      const mockWindowXrm = {
        WebApi: {
          retrieveMultipleRecords: jest.fn(),
          source: 'window',
        },
      };
      const mockParentXrm = {
        WebApi: {
          retrieveMultipleRecords: jest.fn(),
          source: 'parent',
        },
      };

      (window as any).Xrm = mockWindowXrm;
      Object.defineProperty(window, 'parent', {
        value: { Xrm: mockParentXrm },
        writable: true,
      });

      const result = getXrm();

      expect((result?.WebApi as any).source).toBe('window');
    });

    it('should return undefined when Xrm is not available', () => {
      const result = getXrm();

      expect(result).toBeUndefined();
    });

    it('should return undefined when Xrm has no WebApi', () => {
      (window as any).Xrm = { Navigation: {} };

      const result = getXrm();

      expect(result).toBeUndefined();
    });

    // --- 3-frame walk (task 010): window -> parent -> top -------------------

    it('should return top.Xrm when neither window.Xrm nor parent.Xrm is available', () => {
      const mockTopXrm = {
        WebApi: {
          retrieveMultipleRecords: jest.fn(),
          source: 'top',
        },
      };

      // window.parent left at default (=== window, no Xrm); only top has Xrm —
      // simulates the side-pane host nested one level deeper than a single iframe.
      setWindowTop({ Xrm: mockTopXrm });

      const result = getXrm();

      expect((result?.WebApi as any).source).toBe('top');
    });

    it('should prefer parent.Xrm over top.Xrm when both are available', () => {
      const mockParentXrm = {
        WebApi: { retrieveMultipleRecords: jest.fn(), source: 'parent' },
      };
      const mockTopXrm = {
        WebApi: { retrieveMultipleRecords: jest.fn(), source: 'top' },
      };

      Object.defineProperty(window, 'parent', {
        value: { Xrm: mockParentXrm },
        writable: true,
      });
      setWindowTop({ Xrm: mockTopXrm });

      const result = getXrm();

      expect((result?.WebApi as any).source).toBe('parent');
    });

    it('should prefer window.Xrm over parent.Xrm and top.Xrm when all three are available', () => {
      const mockWindowXrm = {
        WebApi: { retrieveMultipleRecords: jest.fn(), source: 'window' },
      };
      const mockParentXrm = {
        WebApi: { retrieveMultipleRecords: jest.fn(), source: 'parent' },
      };
      const mockTopXrm = {
        WebApi: { retrieveMultipleRecords: jest.fn(), source: 'top' },
      };

      (window as any).Xrm = mockWindowXrm;
      Object.defineProperty(window, 'parent', {
        value: { Xrm: mockParentXrm },
        writable: true,
      });
      setWindowTop({ Xrm: mockTopXrm });

      const result = getXrm();

      expect((result?.WebApi as any).source).toBe('window');
    });

    it('should return undefined (never throw) when none of window/parent/top has Xrm', () => {
      // Simulate a nested iframe stack where window, parent, and top are all
      // distinct frames but none carries Xrm (e.g. a non-MDA embed).
      Object.defineProperty(window, 'parent', {
        value: {
          /* no Xrm */
        },
        writable: true,
      });
      setWindowTop({
        /* no Xrm */
      });

      expect(() => getXrm()).not.toThrow();
      expect(getXrm()).toBeUndefined();
    });

    it('should be safe to call repeatedly (no caching) — re-acquires fresh each call', () => {
      // Task 001 spike lesson: consumers must re-read Xrm every poll rather
      // than caching a stale reference. getXrm() itself does no memoization,
      // so back-to-back calls reflect the current frame state.
      expect(getXrm()).toBeUndefined();

      const mockXrm = { WebApi: { retrieveMultipleRecords: jest.fn(), source: 'late-injected' } };
      (window as any).Xrm = mockXrm;

      expect((getXrm()?.WebApi as any).source).toBe('late-injected');
    });
  });

  describe('getXrmPage', () => {
    // Task 021 (FR-20): the single shared accessor replacing the two former
    // private `getXrmPage()` duplicates in FieldMappingHandler.ts and
    // MatterHeaderView.tsx.

    it('should return window.Xrm.Page when it exists', () => {
      const mockAttr = { setValue: jest.fn() };
      const mockPage = { getAttribute: jest.fn(() => mockAttr) };
      (window as any).Xrm = { WebApi: { retrieveMultipleRecords: jest.fn() }, Page: mockPage };

      const result = getXrmPage();

      expect(result).toBe(mockPage);
    });

    it('should walk to window.parent.Xrm.Page when window.Xrm has no Page', () => {
      const mockPage = { getAttribute: jest.fn() };
      // window.Xrm exists but carries no Page (e.g. WebApi-only host) — must
      // still fall through to parent, not stop at window.
      (window as any).Xrm = { WebApi: { retrieveMultipleRecords: jest.fn() } };
      Object.defineProperty(window, 'parent', {
        value: { Xrm: { WebApi: { retrieveMultipleRecords: jest.fn() }, Page: mockPage } },
        writable: true,
      });

      const result = getXrmPage();

      expect(result).toBe(mockPage);
    });

    it('should prefer window.Xrm.Page over parent.Xrm.Page when both are available', () => {
      const windowPage = { getAttribute: jest.fn(), source: 'window' };
      const parentPage = { getAttribute: jest.fn(), source: 'parent' };

      (window as any).Xrm = { WebApi: { retrieveMultipleRecords: jest.fn() }, Page: windowPage };
      Object.defineProperty(window, 'parent', {
        value: { Xrm: { WebApi: { retrieveMultipleRecords: jest.fn() }, Page: parentPage } },
        writable: true,
      });

      const result = getXrmPage();

      expect(result).toBe(windowPage);
    });

    it('should return null (no throw) when no Xrm.Page exists on either frame', () => {
      // window.Xrm entirely absent; parent left at beforeEach default (===
      // window, an un-nested MDA host).
      expect(() => getXrmPage()).not.toThrow();
      expect(getXrmPage()).toBeNull();
    });

    it('should return null (no throw) when Xrm exists on both frames but neither carries Page', () => {
      (window as any).Xrm = { WebApi: { retrieveMultipleRecords: jest.fn() } };
      Object.defineProperty(window, 'parent', {
        value: { Xrm: { WebApi: { retrieveMultipleRecords: jest.fn() } } },
        writable: true,
      });

      expect(() => getXrmPage()).not.toThrow();
      expect(getXrmPage()).toBeNull();
    });

    it('should never throw even if accessing window.parent raises (cross-origin SecurityError)', () => {
      // Same construction as getXrm(): each frame access is independently
      // try/caught, so a throwing accessor on one frame must not propagate.
      Object.defineProperty(window, 'parent', {
        get() {
          throw new DOMException('Blocked a frame with origin from accessing a cross-origin frame.', 'SecurityError');
        },
        configurable: true,
      });

      expect(() => getXrmPage()).not.toThrow();
      expect(getXrmPage()).toBeNull();

      // Restore a plain replaceable descriptor so afterEach's
      // Object.defineProperty(window, 'parent', { value: ... }) doesn't itself
      // throw against the getter-only descriptor installed above.
      Object.defineProperty(window, 'parent', { value: window, writable: true, configurable: true });
    });
  });

  describe('isCustomPageContext', () => {
    it('should return true when parent is different from window', () => {
      const mockParent = { Xrm: {} } as any;
      Object.defineProperty(window, 'parent', {
        value: mockParent,
        writable: true,
      });

      expect(isCustomPageContext()).toBe(true);
    });

    it('should return false when parent equals window', () => {
      // Default - window.parent === window
      expect(isCustomPageContext()).toBe(false);
    });
  });

  describe('isPcfContext', () => {
    it('should return true when window.Xrm.WebApi exists', () => {
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
      };

      expect(isPcfContext()).toBe(true);
    });

    it('should return false when window.Xrm is not available', () => {
      expect(isPcfContext()).toBe(false);
    });

    it('should return false when Xrm exists but WebApi is missing', () => {
      (window as any).Xrm = { Navigation: {} };

      expect(isPcfContext()).toBe(false);
    });
  });

  describe('detectThemeFromHost', () => {
    it('should detect dark theme from Xrm global context', () => {
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
        Utility: {
          getGlobalContext: () => ({
            userSettings: {
              userId: 'test-user',
              userName: 'Test User',
              languageId: 1033,
              isDarkTheme: true,
            },
            getClientUrl: () => 'https://test.crm.dynamics.com',
            getCurrentAppUrl: () => 'https://test.crm.dynamics.com/main.aspx',
            getVersion: () => '9.2.0',
          }),
        },
      };

      const result = detectThemeFromHost();

      expect(result.isDarkTheme).toBe(true);
      expect(result.source).toBe('xrm');
    });

    it('should detect light theme from Xrm global context', () => {
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
        Utility: {
          getGlobalContext: () => ({
            userSettings: {
              userId: 'test-user',
              userName: 'Test User',
              languageId: 1033,
              isDarkTheme: false,
            },
            getClientUrl: () => 'https://test.crm.dynamics.com',
            getCurrentAppUrl: () => 'https://test.crm.dynamics.com/main.aspx',
            getVersion: () => '9.2.0',
          }),
        },
      };

      const result = detectThemeFromHost();

      expect(result.isDarkTheme).toBe(false);
      expect(result.source).toBe('xrm');
    });

    it('should return default light theme when Xrm is not available', () => {
      // Xrm not available — should NOT fall back to OS prefers-color-scheme (ADR-021)
      const result = detectThemeFromHost();

      expect(result.isDarkTheme).toBe(false);
      expect(result.source).toBe('default');
    });
  });

  describe('getClientUrl', () => {
    it('should return client URL from Xrm context', () => {
      const expectedUrl = 'https://test.crm.dynamics.com';
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
        Utility: {
          getGlobalContext: () => ({
            userSettings: { userId: 'test' },
            getClientUrl: () => expectedUrl,
            getCurrentAppUrl: () => expectedUrl,
            getVersion: () => '9.2.0',
          }),
        },
      };

      const result = getClientUrl();

      expect(result).toBe(expectedUrl);
    });

    it('should return undefined when Xrm is not available', () => {
      const result = getClientUrl();

      expect(result).toBeUndefined();
    });
  });

  describe('getCurrentUserId', () => {
    it('should return user ID from Xrm context', () => {
      const expectedUserId = '12345678-1234-1234-1234-123456789012';
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
        Utility: {
          getGlobalContext: () => ({
            userSettings: {
              userId: expectedUserId,
              userName: 'Test User',
              languageId: 1033,
            },
            getClientUrl: () => 'https://test.crm.dynamics.com',
            getCurrentAppUrl: () => 'https://test.crm.dynamics.com',
            getVersion: () => '9.2.0',
          }),
        },
      };

      const result = getCurrentUserId();

      expect(result).toBe(expectedUserId);
    });

    it('should return undefined when Xrm is not available', () => {
      const result = getCurrentUserId();

      expect(result).toBeUndefined();
    });
  });

  describe('getCurrentUserName', () => {
    it('should return user display name from Xrm context', () => {
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
        Utility: {
          getGlobalContext: () => ({
            userSettings: {
              userId: '12345678-1234-1234-1234-123456789012',
              userName: 'Jane Attorney',
              languageId: 1033,
            },
            getClientUrl: () => 'https://test.crm.dynamics.com',
            getCurrentAppUrl: () => 'https://test.crm.dynamics.com',
            getVersion: () => '9.2.0',
          }),
        },
      };

      const result = getCurrentUserName();

      expect(result).toBe('Jane Attorney');
    });

    it('should return undefined when Xrm is not available', () => {
      const result = getCurrentUserName();

      expect(result).toBeUndefined();
    });
  });

  // --- Task 010: widened SidePanesApi / PageInput typed surface -------------

  describe('SidePanesApi typed surface (getPane + pane.select)', () => {
    it('exposes getPane(paneId) returning a SidePane, and pane.select()', () => {
      const mockPane: SidePane = {
        paneId: 'sprk-navigator',
        title: 'Navigator',
        navigate: jest.fn().mockResolvedValue(undefined),
        close: jest.fn(),
        select: jest.fn(),
      };

      const mockSidePanes: SidePanesApi = {
        createPane: jest.fn().mockResolvedValue(mockPane),
        getSelectedPane: jest.fn().mockReturnValue(mockPane),
        getAllPanes: jest.fn().mockReturnValue([mockPane]),
        getPane: jest.fn().mockReturnValue(mockPane),
      };

      const found = mockSidePanes.getPane('sprk-navigator');

      expect(found).toBe(mockPane);

      found?.select();

      expect(mockPane.select).toHaveBeenCalledTimes(1);
    });

    it('getPane returns undefined when no pane with that id has been created', () => {
      const mockSidePanes: SidePanesApi = {
        createPane: jest.fn(),
        getSelectedPane: jest.fn(),
        getAllPanes: jest.fn().mockReturnValue([]),
        getPane: jest.fn().mockReturnValue(undefined),
      };

      expect(mockSidePanes.getPane('does-not-exist')).toBeUndefined();
    });
  });

  describe('PageInput webresource contract', () => {
    it('uses webresourceName (not webresource) for pageType: "webresource"', () => {
      const input: PageInput = {
        pageType: 'webresource',
        webresourceName: 'sprk_navigatorsidepane.html',
        data: 'entityType=account&entityId=00000000-0000-0000-0000-000000000000',
      };

      expect(input.pageType).toBe('webresource');
      expect(input.webresourceName).toBe('sprk_navigatorsidepane.html');
      // No legacy `webresource` field on the type — additive-only widening.
      expect((input as Record<string, unknown>).webresource).toBeUndefined();
    });

    it('still supports Record-shaped data for entityrecord/entitylist page inputs', () => {
      const input: PageInput = {
        pageType: 'entityrecord',
        entityName: 'account',
        entityId: '00000000-0000-0000-0000-000000000000',
        data: { someParam: 'value' },
      };

      expect(input.data).toEqual({ someParam: 'value' });
    });
  });
});
