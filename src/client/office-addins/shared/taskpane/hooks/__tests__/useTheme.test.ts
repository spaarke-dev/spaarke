import { renderHook, act } from '@testing-library/react';
import { useTheme } from '../useTheme';

describe('useTheme', () => {
  beforeEach(() => {
    // Clear session storage before each test
    sessionStorage.clear();

    // Reset matchMedia mock
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: jest.fn().mockImplementation((query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addListener: jest.fn(),
        removeListener: jest.fn(),
        addEventListener: jest.fn(),
        removeEventListener: jest.fn(),
        dispatchEvent: jest.fn(),
      })),
    });
  });

  it('returns default auto preference', () => {
    const { result } = renderHook(() => useTheme());

    expect(result.current.preference).toBe('auto');
    expect(result.current.resolvedType).toBe('light'); // Default to light
  });

  it('allows setting preference to dark', () => {
    const { result } = renderHook(() => useTheme());

    act(() => {
      result.current.setPreference('dark');
    });

    expect(result.current.preference).toBe('dark');
    expect(result.current.resolvedType).toBe('dark');
    expect(result.current.isDarkMode).toBe(true);
  });

  it('allows setting preference to light', () => {
    const { result } = renderHook(() => useTheme());

    act(() => {
      result.current.setPreference('light');
    });

    expect(result.current.preference).toBe('light');
    expect(result.current.resolvedType).toBe('light');
    expect(result.current.isDarkMode).toBe(false);
  });

  it('persists preference to sessionStorage', () => {
    const { result } = renderHook(() => useTheme());

    act(() => {
      result.current.setPreference('dark');
    });

    expect(sessionStorage.getItem('spaarke-theme-preference')).toBe('dark');
  });

  it('loads preference from sessionStorage', () => {
    sessionStorage.setItem('spaarke-theme-preference', 'dark');

    const { result } = renderHook(() => useTheme());

    expect(result.current.preference).toBe('dark');
    expect(result.current.isDarkMode).toBe(true);
  });

  it('toggles theme through cycle: auto -> light -> dark -> auto', () => {
    const { result } = renderHook(() => useTheme());

    // Start at auto
    expect(result.current.preference).toBe('auto');

    // Toggle to light
    act(() => {
      result.current.toggleTheme();
    });
    expect(result.current.preference).toBe('light');

    // Toggle to dark
    act(() => {
      result.current.toggleTheme();
    });
    expect(result.current.preference).toBe('dark');

    // Toggle back to auto
    act(() => {
      result.current.toggleTheme();
    });
    expect(result.current.preference).toBe('auto');
  });

  it('returns correct theme object for each mode', () => {
    const { result } = renderHook(() => useTheme());

    // Light theme
    act(() => {
      result.current.setPreference('light');
    });
    expect(result.current.theme).toBeDefined();
    expect(result.current.theme.colorNeutralBackground1).toBeDefined();

    // Dark theme
    act(() => {
      result.current.setPreference('dark');
    });
    expect(result.current.theme).toBeDefined();
    expect(result.current.theme.colorNeutralBackground1).toBeDefined();
  });

  it('detects system dark mode preference', () => {
    // Mock matchMedia to return dark mode preference
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: jest.fn().mockImplementation((query: string) => ({
        matches: query === '(prefers-color-scheme: dark)',
        media: query,
        onchange: null,
        addListener: jest.fn(),
        removeListener: jest.fn(),
        addEventListener: jest.fn(),
        removeEventListener: jest.fn(),
        dispatchEvent: jest.fn(),
      })),
    });

    const { result } = renderHook(() => useTheme());

    // With auto preference and system dark mode
    expect(result.current.preference).toBe('auto');
    expect(result.current.resolvedType).toBe('dark');
    expect(result.current.isDarkMode).toBe(true);
  });

  it('isHighContrast returns false for normal themes', () => {
    const { result } = renderHook(() => useTheme());

    act(() => {
      result.current.setPreference('light');
    });
    expect(result.current.isHighContrast).toBe(false);

    act(() => {
      result.current.setPreference('dark');
    });
    expect(result.current.isHighContrast).toBe(false);
  });
});
