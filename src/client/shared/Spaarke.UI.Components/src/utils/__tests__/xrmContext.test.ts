/**
 * xrmContext Utility Unit Tests
 *
 * @see utils/xrmContext.ts
 */

import {
  getXrm,
  isCustomPageContext,
  isPcfContext,
  detectThemeFromHost,
  getClientUrl,
  getCurrentUserId,
} from "../xrmContext";

describe("xrmContext", () => {
  // Save original window properties
  const originalXrm = (window as any).Xrm;
  const originalParent = window.parent;

  beforeEach(() => {
    // Reset window.Xrm before each test
    delete (window as any).Xrm;
    // Reset window.parent to window (same-origin default)
    Object.defineProperty(window, "parent", {
      value: window,
      writable: true,
    });
  });

  afterEach(() => {
    // Restore original values
    if (originalXrm) {
      (window as any).Xrm = originalXrm;
    } else {
      delete (window as any).Xrm;
    }
    Object.defineProperty(window, "parent", {
      value: originalParent,
      writable: true,
    });
  });

  describe("getXrm", () => {
    it("should return window.Xrm when available", () => {
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

    it("should return parent.Xrm when window.Xrm is not available", () => {
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
      Object.defineProperty(window, "parent", {
        value: mockParent,
        writable: true,
      });

      const result = getXrm();

      expect(result).toBe(mockParentXrm);
    });

    it("should prefer window.Xrm over parent.Xrm", () => {
      const mockWindowXrm = {
        WebApi: {
          retrieveMultipleRecords: jest.fn(),
          source: "window",
        },
      };
      const mockParentXrm = {
        WebApi: {
          retrieveMultipleRecords: jest.fn(),
          source: "parent",
        },
      };

      (window as any).Xrm = mockWindowXrm;
      Object.defineProperty(window, "parent", {
        value: { Xrm: mockParentXrm },
        writable: true,
      });

      const result = getXrm();

      expect((result?.WebApi as any).source).toBe("window");
    });

    it("should return undefined when Xrm is not available", () => {
      const result = getXrm();

      expect(result).toBeUndefined();
    });

    it("should return undefined when Xrm has no WebApi", () => {
      (window as any).Xrm = { Navigation: {} };

      const result = getXrm();

      expect(result).toBeUndefined();
    });
  });

  describe("isCustomPageContext", () => {
    it("should return true when parent is different from window", () => {
      const mockParent = { Xrm: {} } as any;
      Object.defineProperty(window, "parent", {
        value: mockParent,
        writable: true,
      });

      expect(isCustomPageContext()).toBe(true);
    });

    it("should return false when parent equals window", () => {
      // Default - window.parent === window
      expect(isCustomPageContext()).toBe(false);
    });
  });

  describe("isPcfContext", () => {
    it("should return true when window.Xrm.WebApi exists", () => {
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
      };

      expect(isPcfContext()).toBe(true);
    });

    it("should return false when window.Xrm is not available", () => {
      expect(isPcfContext()).toBe(false);
    });

    it("should return false when Xrm exists but WebApi is missing", () => {
      (window as any).Xrm = { Navigation: {} };

      expect(isPcfContext()).toBe(false);
    });
  });

  describe("detectThemeFromHost", () => {
    it("should detect dark theme from Xrm global context", () => {
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
        Utility: {
          getGlobalContext: () => ({
            userSettings: {
              userId: "test-user",
              userName: "Test User",
              languageId: 1033,
              isDarkTheme: true,
            },
            getClientUrl: () => "https://test.crm.dynamics.com",
            getCurrentAppUrl: () => "https://test.crm.dynamics.com/main.aspx",
            getVersion: () => "9.2.0",
          }),
        },
      };

      const result = detectThemeFromHost();

      expect(result.isDarkTheme).toBe(true);
      expect(result.source).toBe("xrm");
    });

    it("should detect light theme from Xrm global context", () => {
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
        Utility: {
          getGlobalContext: () => ({
            userSettings: {
              userId: "test-user",
              userName: "Test User",
              languageId: 1033,
              isDarkTheme: false,
            },
            getClientUrl: () => "https://test.crm.dynamics.com",
            getCurrentAppUrl: () => "https://test.crm.dynamics.com/main.aspx",
            getVersion: () => "9.2.0",
          }),
        },
      };

      const result = detectThemeFromHost();

      expect(result.isDarkTheme).toBe(false);
      expect(result.source).toBe("xrm");
    });

    it("should fall back to media query when Xrm is not available", () => {
      // Mock matchMedia
      const mockMatchMedia = jest.fn().mockReturnValue({
        matches: true,
        media: "(prefers-color-scheme: dark)",
      });
      Object.defineProperty(window, "matchMedia", {
        value: mockMatchMedia,
        writable: true,
      });

      const result = detectThemeFromHost();

      expect(result.source).toBe("media-query");
      expect(mockMatchMedia).toHaveBeenCalledWith("(prefers-color-scheme: dark)");
    });

    it("should return default light theme when no detection method works", () => {
      // Ensure matchMedia is not available
      Object.defineProperty(window, "matchMedia", {
        value: undefined,
        writable: true,
      });

      const result = detectThemeFromHost();

      expect(result.isDarkTheme).toBe(false);
      expect(result.source).toBe("default");
    });
  });

  describe("getClientUrl", () => {
    it("should return client URL from Xrm context", () => {
      const expectedUrl = "https://test.crm.dynamics.com";
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
        Utility: {
          getGlobalContext: () => ({
            userSettings: { userId: "test" },
            getClientUrl: () => expectedUrl,
            getCurrentAppUrl: () => expectedUrl,
            getVersion: () => "9.2.0",
          }),
        },
      };

      const result = getClientUrl();

      expect(result).toBe(expectedUrl);
    });

    it("should return undefined when Xrm is not available", () => {
      const result = getClientUrl();

      expect(result).toBeUndefined();
    });
  });

  describe("getCurrentUserId", () => {
    it("should return user ID from Xrm context", () => {
      const expectedUserId = "12345678-1234-1234-1234-123456789012";
      (window as any).Xrm = {
        WebApi: { retrieveMultipleRecords: jest.fn() },
        Utility: {
          getGlobalContext: () => ({
            userSettings: {
              userId: expectedUserId,
              userName: "Test User",
              languageId: 1033,
            },
            getClientUrl: () => "https://test.crm.dynamics.com",
            getCurrentAppUrl: () => "https://test.crm.dynamics.com",
            getVersion: () => "9.2.0",
          }),
        },
      };

      const result = getCurrentUserId();

      expect(result).toBe(expectedUserId);
    });

    it("should return undefined when Xrm is not available", () => {
      const result = getCurrentUserId();

      expect(result).toBeUndefined();
    });
  });
});
