/**
 * themeStorage Utility Unit Tests
 *
 * @see projects/mda-darkmode-theme/spec.md Section 3.4
 */

import {
  THEME_STORAGE_KEY,
  THEME_CHANGE_EVENT,
  ThemePreference,
  getUserThemePreference,
  setUserThemePreference,
  getEffectiveDarkMode,
  resolveThemeWithUserPreference,
  setupThemeListener,
} from '../themeStorage';
import { webLightTheme, webDarkTheme } from '@fluentui/react-components';

// Mock localStorage
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

// Mock matchMedia
const mockMatchMedia = jest.fn();
Object.defineProperty(window, 'matchMedia', { value: mockMatchMedia });

describe('themeStorage', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    localStorageMock.clear();
    mockMatchMedia.mockReturnValue({
      matches: false,
      addEventListener: jest.fn(),
      removeEventListener: jest.fn(),
    });
  });

  describe('constants', () => {
    it('should export correct storage key', () => {
      expect(THEME_STORAGE_KEY).toBe('spaarke-theme');
    });

    it('should export correct event name', () => {
      expect(THEME_CHANGE_EVENT).toBe('spaarke-theme-change');
    });
  });

  describe('getUserThemePreference', () => {
    it('should return "auto" when localStorage is empty', () => {
      const result = getUserThemePreference();
      expect(result).toBe('auto');
    });

    it('should return "light" when stored', () => {
      localStorageMock.getItem.mockReturnValueOnce('light');
      const result = getUserThemePreference();
      expect(result).toBe('light');
    });

    it('should return "dark" when stored', () => {
      localStorageMock.getItem.mockReturnValueOnce('dark');
      const result = getUserThemePreference();
      expect(result).toBe('dark');
    });

    it('should return "auto" when stored', () => {
      localStorageMock.getItem.mockReturnValueOnce('auto');
      const result = getUserThemePreference();
      expect(result).toBe('auto');
    });

    it('should return "auto" for invalid stored value', () => {
      localStorageMock.getItem.mockReturnValueOnce('invalid');
      const result = getUserThemePreference();
      expect(result).toBe('auto');
    });
  });

  describe('setUserThemePreference', () => {
    let dispatchEventSpy: jest.SpyInstance;

    beforeEach(() => {
      dispatchEventSpy = jest.spyOn(window, 'dispatchEvent');
    });

    afterEach(() => {
      dispatchEventSpy.mockRestore();
    });

    it('should store preference in localStorage', () => {
      setUserThemePreference('dark');
      expect(localStorageMock.setItem).toHaveBeenCalledWith(THEME_STORAGE_KEY, 'dark');
    });

    it('should dispatch custom event with theme', () => {
      setUserThemePreference('light');

      expect(dispatchEventSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          type: THEME_CHANGE_EVENT,
          detail: { theme: 'light' },
        })
      );
    });

    it('should handle all valid themes', () => {
      const themes: ThemePreference[] = ['auto', 'light', 'dark'];

      themes.forEach(theme => {
        setUserThemePreference(theme);
        expect(localStorageMock.setItem).toHaveBeenCalledWith(THEME_STORAGE_KEY, theme);
      });
    });
  });

  describe('getEffectiveDarkMode', () => {
    it('should return true when preference is "dark"', () => {
      localStorageMock.getItem.mockReturnValue('dark');
      const result = getEffectiveDarkMode();
      expect(result).toBe(true);
    });

    it('should return false when preference is "light"', () => {
      localStorageMock.getItem.mockReturnValue('light');
      const result = getEffectiveDarkMode();
      expect(result).toBe(false);
    });

    it('should use context isDarkTheme in auto mode', () => {
      localStorageMock.getItem.mockReturnValue('auto');
      const mockContext = {
        fluentDesignLanguage: {
          isDarkTheme: true,
        },
      };

      const result = getEffectiveDarkMode(mockContext);
      expect(result).toBe(true);
    });

    it('should use context isDarkTheme=false in auto mode', () => {
      localStorageMock.getItem.mockReturnValue('auto');
      const mockContext = {
        fluentDesignLanguage: {
          isDarkTheme: false,
        },
      };

      const result = getEffectiveDarkMode(mockContext);
      expect(result).toBe(false);
    });

    it('should fall back to system preference when context unavailable', () => {
      localStorageMock.getItem.mockReturnValue('auto');
      mockMatchMedia.mockReturnValue({
        matches: true,
        addEventListener: jest.fn(),
        removeEventListener: jest.fn(),
      });

      const result = getEffectiveDarkMode();
      expect(result).toBe(true);
    });

    it('should fall back to light when system prefers light', () => {
      localStorageMock.getItem.mockReturnValue('auto');
      mockMatchMedia.mockReturnValue({
        matches: false,
        addEventListener: jest.fn(),
        removeEventListener: jest.fn(),
      });

      const result = getEffectiveDarkMode();
      expect(result).toBe(false);
    });

    it('should prioritize localStorage over context', () => {
      localStorageMock.getItem.mockReturnValue('light');
      const mockContext = {
        fluentDesignLanguage: {
          isDarkTheme: true,
        },
      };

      const result = getEffectiveDarkMode(mockContext);
      expect(result).toBe(false); // localStorage 'light' wins
    });

    describe('DOM navbar fallback', () => {
      let querySelectorSpy: jest.SpyInstance;
      let getComputedStyleSpy: jest.SpyInstance;

      beforeEach(() => {
        querySelectorSpy = jest.spyOn(document, 'querySelector');
        getComputedStyleSpy = jest.spyOn(window, 'getComputedStyle');
      });

      afterEach(() => {
        querySelectorSpy.mockRestore();
        getComputedStyleSpy.mockRestore();
      });

      it('should detect dark mode from navbar background color', () => {
        localStorageMock.getItem.mockReturnValue('auto');
        const mockNavbar = document.createElement('div');
        querySelectorSpy.mockReturnValue(mockNavbar);
        getComputedStyleSpy.mockReturnValue({ backgroundColor: 'rgb(10, 10, 10)' });

        const result = getEffectiveDarkMode();
        expect(result).toBe(true);
      });

      it('should detect light mode from navbar background color', () => {
        localStorageMock.getItem.mockReturnValue('auto');
        const mockNavbar = document.createElement('div');
        querySelectorSpy.mockReturnValue(mockNavbar);
        getComputedStyleSpy.mockReturnValue({ backgroundColor: 'rgb(240, 240, 240)' });

        const result = getEffectiveDarkMode();
        expect(result).toBe(false);
      });

      it('should fall back to system when navbar has different color', () => {
        localStorageMock.getItem.mockReturnValue('auto');
        const mockNavbar = document.createElement('div');
        querySelectorSpy.mockReturnValue(mockNavbar);
        getComputedStyleSpy.mockReturnValue({ backgroundColor: 'rgb(100, 100, 100)' });
        mockMatchMedia.mockReturnValue({
          matches: true,
          addEventListener: jest.fn(),
          removeEventListener: jest.fn(),
        });

        const result = getEffectiveDarkMode();
        expect(result).toBe(true); // Falls back to system preference
      });

      it('should fall back to system when navbar not found', () => {
        localStorageMock.getItem.mockReturnValue('auto');
        querySelectorSpy.mockReturnValue(null);
        mockMatchMedia.mockReturnValue({
          matches: false,
          addEventListener: jest.fn(),
          removeEventListener: jest.fn(),
        });

        const result = getEffectiveDarkMode();
        expect(result).toBe(false);
      });
    });
  });

  describe('resolveThemeWithUserPreference', () => {
    it('should return webDarkTheme when effective mode is dark', () => {
      localStorageMock.getItem.mockReturnValue('dark');
      const result = resolveThemeWithUserPreference();
      expect(result).toBe(webDarkTheme);
    });

    it('should return webLightTheme when effective mode is light', () => {
      localStorageMock.getItem.mockReturnValue('light');
      const result = resolveThemeWithUserPreference();
      expect(result).toBe(webLightTheme);
    });

    it('should use context in auto mode', () => {
      localStorageMock.getItem.mockReturnValue('auto');
      const mockContext = {
        fluentDesignLanguage: {
          isDarkTheme: true,
        },
      };

      const result = resolveThemeWithUserPreference(mockContext);
      expect(result).toBe(webDarkTheme);
    });
  });

  describe('setupThemeListener', () => {
    let addEventListenerSpy: jest.SpyInstance;
    let removeEventListenerSpy: jest.SpyInstance;
    let mockMediaQueryAddListener: jest.Mock;
    let mockMediaQueryRemoveListener: jest.Mock;

    beforeEach(() => {
      addEventListenerSpy = jest.spyOn(window, 'addEventListener');
      removeEventListenerSpy = jest.spyOn(window, 'removeEventListener');
      mockMediaQueryAddListener = jest.fn();
      mockMediaQueryRemoveListener = jest.fn();
      mockMatchMedia.mockReturnValue({
        matches: false,
        addEventListener: mockMediaQueryAddListener,
        removeEventListener: mockMediaQueryRemoveListener,
      });
    });

    afterEach(() => {
      addEventListenerSpy.mockRestore();
      removeEventListenerSpy.mockRestore();
    });

    it('should add storage event listener', () => {
      const onChange = jest.fn();
      setupThemeListener(onChange);

      expect(addEventListenerSpy).toHaveBeenCalledWith('storage', expect.any(Function));
    });

    it('should add theme change event listener', () => {
      const onChange = jest.fn();
      setupThemeListener(onChange);

      expect(addEventListenerSpy).toHaveBeenCalledWith(THEME_CHANGE_EVENT, expect.any(Function));
    });

    it('should add system preference change listener', () => {
      const onChange = jest.fn();
      setupThemeListener(onChange);

      expect(mockMediaQueryAddListener).toHaveBeenCalledWith('change', expect.any(Function));
    });

    it('should return cleanup function', () => {
      const onChange = jest.fn();
      const cleanup = setupThemeListener(onChange);

      expect(typeof cleanup).toBe('function');
    });

    it('should remove listeners on cleanup', () => {
      const onChange = jest.fn();
      const cleanup = setupThemeListener(onChange);

      cleanup();

      expect(removeEventListenerSpy).toHaveBeenCalledWith('storage', expect.any(Function));
      expect(removeEventListenerSpy).toHaveBeenCalledWith(THEME_CHANGE_EVENT, expect.any(Function));
      expect(mockMediaQueryRemoveListener).toHaveBeenCalledWith('change', expect.any(Function));
    });

    it('should call onChange when storage event fires for theme key', () => {
      const onChange = jest.fn();
      localStorageMock.getItem.mockReturnValue('dark');
      setupThemeListener(onChange);

      // Get the storage handler that was registered
      const storageHandler = addEventListenerSpy.mock.calls.find(
        call => call[0] === 'storage'
      )?.[1];

      // Simulate storage event
      storageHandler({ key: THEME_STORAGE_KEY } as StorageEvent);

      expect(onChange).toHaveBeenCalledWith(true); // dark mode
    });

    it('should not call onChange for unrelated storage events', () => {
      const onChange = jest.fn();
      setupThemeListener(onChange);

      const storageHandler = addEventListenerSpy.mock.calls.find(
        call => call[0] === 'storage'
      )?.[1];

      storageHandler({ key: 'other-key' } as StorageEvent);

      expect(onChange).not.toHaveBeenCalled();
    });

    it('should call onChange when theme event fires', () => {
      const onChange = jest.fn();
      localStorageMock.getItem.mockReturnValue('light');
      setupThemeListener(onChange);

      const themeHandler = addEventListenerSpy.mock.calls.find(
        call => call[0] === THEME_CHANGE_EVENT
      )?.[1];

      themeHandler();

      expect(onChange).toHaveBeenCalledWith(false); // light mode
    });

    it('should call onChange when system preference changes in auto mode', () => {
      const onChange = jest.fn();
      localStorageMock.getItem.mockReturnValue('auto');
      setupThemeListener(onChange);

      const systemHandler = mockMediaQueryAddListener.mock.calls[0]?.[1];

      systemHandler({ matches: true } as MediaQueryListEvent);

      expect(onChange).toHaveBeenCalledWith(true);
    });

    it('should not call onChange when system changes but not in auto mode', () => {
      const onChange = jest.fn();
      localStorageMock.getItem.mockReturnValue('light');
      setupThemeListener(onChange);

      const systemHandler = mockMediaQueryAddListener.mock.calls[0]?.[1];

      systemHandler({ matches: true } as MediaQueryListEvent);

      expect(onChange).not.toHaveBeenCalled();
    });
  });
});
