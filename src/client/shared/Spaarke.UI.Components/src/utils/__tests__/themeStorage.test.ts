/**
 * themeStorage Utility Unit Tests
 *
 * Covers all PCF and Code Page theme functions from the unified module.
 *
 * @see ADR-021 - No OS prefers-color-scheme fallback
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
  detectDarkModeFromUrl,
  detectDarkModeFromNavbar,
  resolveCodePageTheme,
  setupCodePageThemeListener,
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

describe('themeStorage', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    localStorageMock.clear();
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

  describe('detectDarkModeFromUrl', () => {
    let locationSpy: jest.SpyInstance;

    beforeEach(() => {
      locationSpy = jest.spyOn(window, 'location', 'get');
    });

    afterEach(() => {
      locationSpy.mockRestore();
    });

    it('should return true for flags=themeOption=dark', () => {
      locationSpy.mockReturnValue({ search: '?flags=themeOption%3Ddark' });
      expect(detectDarkModeFromUrl()).toBe(true);
    });

    it('should return false for flags=themeOption=light', () => {
      locationSpy.mockReturnValue({ search: '?flags=themeOption%3Dlight' });
      expect(detectDarkModeFromUrl()).toBe(false);
    });

    it('should return true for direct theme=dark param', () => {
      locationSpy.mockReturnValue({ search: '?theme=dark' });
      expect(detectDarkModeFromUrl()).toBe(true);
    });

    it('should return false for direct theme=light param', () => {
      locationSpy.mockReturnValue({ search: '?theme=light' });
      expect(detectDarkModeFromUrl()).toBe(false);
    });

    it('should return null when no theme param', () => {
      locationSpy.mockReturnValue({ search: '?other=value' });
      expect(detectDarkModeFromUrl()).toBeNull();
    });

    it('should return null for empty search', () => {
      locationSpy.mockReturnValue({ search: '' });
      expect(detectDarkModeFromUrl()).toBeNull();
    });
  });

  describe('detectDarkModeFromNavbar', () => {
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

    it('should return true for dark navbar color', () => {
      const mockNavbar = document.createElement('div');
      querySelectorSpy.mockReturnValue(mockNavbar);
      getComputedStyleSpy.mockReturnValue({ backgroundColor: 'rgb(10, 10, 10)' });

      expect(detectDarkModeFromNavbar()).toBe(true);
    });

    it('should return false for light navbar color', () => {
      const mockNavbar = document.createElement('div');
      querySelectorSpy.mockReturnValue(mockNavbar);
      getComputedStyleSpy.mockReturnValue({ backgroundColor: 'rgb(240, 240, 240)' });

      expect(detectDarkModeFromNavbar()).toBe(false);
    });

    it('should use luminance fallback for custom navbar colors', () => {
      const mockNavbar = document.createElement('div');
      querySelectorSpy.mockReturnValue(mockNavbar);
      // Low luminance (dark): 0.299*30 + 0.587*30 + 0.114*30 = 30 < 128
      getComputedStyleSpy.mockReturnValue({ backgroundColor: 'rgb(30, 30, 30)' });

      expect(detectDarkModeFromNavbar()).toBe(true);
    });

    it('should detect light from high luminance custom color', () => {
      const mockNavbar = document.createElement('div');
      querySelectorSpy.mockReturnValue(mockNavbar);
      // High luminance (light): 0.299*200 + 0.587*200 + 0.114*200 = 200 > 128
      getComputedStyleSpy.mockReturnValue({ backgroundColor: 'rgb(200, 200, 200)' });

      expect(detectDarkModeFromNavbar()).toBe(false);
    });

    it('should return null when navbar not found', () => {
      querySelectorSpy.mockReturnValue(null);

      expect(detectDarkModeFromNavbar()).toBeNull();
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

    it('should default to light (false) when no preference and no context', () => {
      localStorageMock.getItem.mockReturnValue('auto');
      // No context, no navbar → should default to false (light)
      jest.spyOn(document, 'querySelector').mockReturnValue(null);

      const result = getEffectiveDarkMode();
      expect(result).toBe(false);

      (document.querySelector as jest.Mock).mockRestore();
    });

    it('should NOT consult OS prefers-color-scheme (ADR-021)', () => {
      localStorageMock.getItem.mockReturnValue('auto');
      // Even with no context and no navbar, should NOT check OS preference
      jest.spyOn(document, 'querySelector').mockReturnValue(null);

      const result = getEffectiveDarkMode();
      // Must return false (light default), NOT follow OS dark preference
      expect(result).toBe(false);

      (document.querySelector as jest.Mock).mockRestore();
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
        getComputedStyleSpy.mockReturnValue({
          backgroundColor: 'rgb(10, 10, 10)',
        });

        const result = getEffectiveDarkMode();
        expect(result).toBe(true);
      });

      it('should detect light mode from navbar background color', () => {
        localStorageMock.getItem.mockReturnValue('auto');
        const mockNavbar = document.createElement('div');
        querySelectorSpy.mockReturnValue(mockNavbar);
        getComputedStyleSpy.mockReturnValue({
          backgroundColor: 'rgb(240, 240, 240)',
        });

        const result = getEffectiveDarkMode();
        expect(result).toBe(false);
      });

      it('should use luminance for custom navbar colors', () => {
        localStorageMock.getItem.mockReturnValue('auto');
        const mockNavbar = document.createElement('div');
        querySelectorSpy.mockReturnValue(mockNavbar);
        // Low luminance → dark
        getComputedStyleSpy.mockReturnValue({
          backgroundColor: 'rgb(30, 30, 30)',
        });

        const result = getEffectiveDarkMode();
        expect(result).toBe(true);
      });

      it('should default to light when navbar not found', () => {
        localStorageMock.getItem.mockReturnValue('auto');
        querySelectorSpy.mockReturnValue(null);

        const result = getEffectiveDarkMode();
        expect(result).toBe(false); // Default light, NOT OS preference
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

  describe('resolveCodePageTheme', () => {
    let locationSpy: jest.SpyInstance;
    let querySelectorSpy: jest.SpyInstance;
    let getComputedStyleSpy: jest.SpyInstance;

    beforeEach(() => {
      locationSpy = jest.spyOn(window, 'location', 'get');
      querySelectorSpy = jest.spyOn(document, 'querySelector');
      getComputedStyleSpy = jest.spyOn(window, 'getComputedStyle');
      // Default: no URL params, no navbar
      locationSpy.mockReturnValue({ search: '' });
      querySelectorSpy.mockReturnValue(null);
    });

    afterEach(() => {
      locationSpy.mockRestore();
      querySelectorSpy.mockRestore();
      getComputedStyleSpy.mockRestore();
    });

    it('should return webDarkTheme when localStorage is "dark"', () => {
      localStorageMock.getItem.mockReturnValue('dark');
      expect(resolveCodePageTheme()).toBe(webDarkTheme);
    });

    it('should return webLightTheme when localStorage is "light"', () => {
      localStorageMock.getItem.mockReturnValue('light');
      expect(resolveCodePageTheme()).toBe(webLightTheme);
    });

    it('should use URL flags when preference is "auto"', () => {
      localStorageMock.getItem.mockReturnValue('auto');
      locationSpy.mockReturnValue({ search: '?flags=themeOption%3Ddark' });

      expect(resolveCodePageTheme()).toBe(webDarkTheme);
    });

    it('should use navbar detection when no preference and no URL', () => {
      localStorageMock.getItem.mockReturnValue('auto');
      const mockNavbar = document.createElement('div');
      querySelectorSpy.mockReturnValue(mockNavbar);
      getComputedStyleSpy.mockReturnValue({ backgroundColor: 'rgb(10, 10, 10)' });

      expect(resolveCodePageTheme()).toBe(webDarkTheme);
    });

    it('should default to light when no signals', () => {
      localStorageMock.getItem.mockReturnValue('auto');

      expect(resolveCodePageTheme()).toBe(webLightTheme);
    });

    it('should NOT consult OS prefers-color-scheme (ADR-021)', () => {
      localStorageMock.getItem.mockReturnValue('auto');
      // No signals → must return light, not OS dark
      expect(resolveCodePageTheme()).toBe(webLightTheme);
    });

    it('should prioritize localStorage over URL flags', () => {
      localStorageMock.getItem.mockReturnValue('light');
      locationSpy.mockReturnValue({ search: '?flags=themeOption%3Ddark' });

      expect(resolveCodePageTheme()).toBe(webLightTheme);
    });
  });

  describe('setupThemeListener', () => {
    let addEventListenerSpy: jest.SpyInstance;
    let removeEventListenerSpy: jest.SpyInstance;

    beforeEach(() => {
      addEventListenerSpy = jest.spyOn(window, 'addEventListener');
      removeEventListenerSpy = jest.spyOn(window, 'removeEventListener');
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

    it('should NOT add system preference listener (ADR-021)', () => {
      const onChange = jest.fn();
      setupThemeListener(onChange);

      // Only storage and theme-change events — no matchMedia listener
      const eventNames = addEventListenerSpy.mock.calls.map((call: any[]) => call[0]);
      expect(eventNames).not.toContain('change');
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
    });

    it('should call onChange when storage event fires for theme key', () => {
      const onChange = jest.fn();
      localStorageMock.getItem.mockReturnValue('dark');
      setupThemeListener(onChange);

      const storageHandler = addEventListenerSpy.mock.calls.find((call: any[]) => call[0] === 'storage')?.[1];

      storageHandler({ key: THEME_STORAGE_KEY } as StorageEvent);

      expect(onChange).toHaveBeenCalledWith(true); // dark mode
    });

    it('should not call onChange for unrelated storage events', () => {
      const onChange = jest.fn();
      setupThemeListener(onChange);

      const storageHandler = addEventListenerSpy.mock.calls.find((call: any[]) => call[0] === 'storage')?.[1];

      storageHandler({ key: 'other-key' } as StorageEvent);

      expect(onChange).not.toHaveBeenCalled();
    });

    it('should call onChange when theme event fires', () => {
      const onChange = jest.fn();
      localStorageMock.getItem.mockReturnValue('light');
      setupThemeListener(onChange);

      const themeHandler = addEventListenerSpy.mock.calls.find((call: any[]) => call[0] === THEME_CHANGE_EVENT)?.[1];

      themeHandler();

      expect(onChange).toHaveBeenCalledWith(false); // light mode
    });
  });

  describe('setupCodePageThemeListener', () => {
    let addEventListenerSpy: jest.SpyInstance;
    let removeEventListenerSpy: jest.SpyInstance;
    let locationSpy: jest.SpyInstance;

    beforeEach(() => {
      addEventListenerSpy = jest.spyOn(window, 'addEventListener');
      removeEventListenerSpy = jest.spyOn(window, 'removeEventListener');
      locationSpy = jest.spyOn(window, 'location', 'get');
      locationSpy.mockReturnValue({ search: '' });
      jest.spyOn(document, 'querySelector').mockReturnValue(null);
    });

    afterEach(() => {
      addEventListenerSpy.mockRestore();
      removeEventListenerSpy.mockRestore();
      locationSpy.mockRestore();
      (document.querySelector as jest.Mock).mockRestore();
    });

    it('should add storage and theme change listeners', () => {
      const onChange = jest.fn();
      setupCodePageThemeListener(onChange);

      expect(addEventListenerSpy).toHaveBeenCalledWith('storage', expect.any(Function));
      expect(addEventListenerSpy).toHaveBeenCalledWith(THEME_CHANGE_EVENT, expect.any(Function));
    });

    it('should return cleanup function that removes listeners', () => {
      const onChange = jest.fn();
      const cleanup = setupCodePageThemeListener(onChange);

      cleanup();

      expect(removeEventListenerSpy).toHaveBeenCalledWith('storage', expect.any(Function));
      expect(removeEventListenerSpy).toHaveBeenCalledWith(THEME_CHANGE_EVENT, expect.any(Function));
    });

    it('should call onChange with resolved Theme on storage event', () => {
      const onChange = jest.fn();
      localStorageMock.getItem.mockReturnValue('dark');
      setupCodePageThemeListener(onChange);

      const storageHandler = addEventListenerSpy.mock.calls.find((call: any[]) => call[0] === 'storage')?.[1];

      storageHandler({ key: THEME_STORAGE_KEY } as StorageEvent);

      expect(onChange).toHaveBeenCalledWith(webDarkTheme);
    });

    it('should call onChange with resolved Theme on theme event', () => {
      const onChange = jest.fn();
      localStorageMock.getItem.mockReturnValue('light');
      setupCodePageThemeListener(onChange);

      const themeHandler = addEventListenerSpy.mock.calls.find((call: any[]) => call[0] === THEME_CHANGE_EVENT)?.[1];

      themeHandler();

      expect(onChange).toHaveBeenCalledWith(webLightTheme);
    });

    it('should NOT add system preference listener (ADR-021)', () => {
      const onChange = jest.fn();
      setupCodePageThemeListener(onChange);

      const eventNames = addEventListenerSpy.mock.calls.map((call: any[]) => call[0]);
      expect(eventNames).toEqual(['storage', THEME_CHANGE_EVENT]);
    });
  });
});
