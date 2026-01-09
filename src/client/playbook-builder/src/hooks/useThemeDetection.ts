import { useState, useEffect } from 'react';

/**
 * Hook to detect and react to system dark mode preference.
 * Also listens for theme messages from parent iframe (PCF host).
 */
export function useThemeDetection(): boolean {
  const [isDarkMode, setIsDarkMode] = useState(() => {
    // Initial check for system preference
    if (typeof window !== 'undefined' && window.matchMedia) {
      return window.matchMedia('(prefers-color-scheme: dark)').matches;
    }
    return false;
  });

  useEffect(() => {
    // Listen for system preference changes
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

    const handleChange = (e: MediaQueryListEvent) => {
      setIsDarkMode(e.matches);
    };

    mediaQuery.addEventListener('change', handleChange);

    // Listen for theme messages from parent (PCF host)
    const handleMessage = (event: MessageEvent) => {
      if (event.data?.type === 'THEME_CHANGE') {
        setIsDarkMode(event.data.isDarkMode);
      }
    };

    window.addEventListener('message', handleMessage);

    return () => {
      mediaQuery.removeEventListener('change', handleChange);
      window.removeEventListener('message', handleMessage);
    };
  }, []);

  return isDarkMode;
}
