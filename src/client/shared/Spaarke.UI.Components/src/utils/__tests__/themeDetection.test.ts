/**
 * themeDetection Utility Unit Tests
 */

import { detectTheme, isDarkMode } from '../themeDetection';
import { webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { spaarkeLight } from '../../theme/brand';

describe('themeDetection', () => {
  describe('detectTheme', () => {
    it('should return Spaarke theme when mode is "Spaarke"', () => {
      const mockContext = {
        parameters: {},
        mode: {}
      };

      const theme = detectTheme(mockContext, 'Spaarke');

      expect(theme).toBe(spaarkeLight);
    });

    it('should return host theme when mode is "Host" and tokenTheme available', () => {
      const mockHostTheme = { ...webLightTheme };
      const mockContext = {
        fluentDesignLanguage: {
          tokenTheme: mockHostTheme
        }
      };

      const theme = detectTheme(mockContext, 'Host');

      expect(theme).toBe(mockHostTheme);
    });

    it('should return webDarkTheme when mode is "Host" with isDarkTheme=true', () => {
      const mockContext = {
        fluentDesignLanguage: {
          isDarkTheme: true
        }
      };

      const theme = detectTheme(mockContext, 'Host');

      expect(theme).toBe(webDarkTheme);
    });

    it('should return webLightTheme when mode is "Host" with isDarkTheme=false', () => {
      const mockContext = {
        fluentDesignLanguage: {
          isDarkTheme: false
        }
      };

      const theme = detectTheme(mockContext, 'Host');

      expect(theme).toBe(webLightTheme);
    });

    it('should return host theme in Auto mode when available', () => {
      const mockHostTheme = { ...webDarkTheme };
      const mockContext = {
        fluentDesignLanguage: {
          tokenTheme: mockHostTheme
        }
      };

      const theme = detectTheme(mockContext, 'Auto');

      expect(theme).toBe(mockHostTheme);
    });

    it('should fallback to Spaarke theme in Auto mode when host theme unavailable', () => {
      const mockContext = {
        parameters: {},
        mode: {}
      };

      const theme = detectTheme(mockContext, 'Auto');

      expect(theme).toBe(spaarkeLight);
    });

    it('should return Spaarke theme when mode is undefined (defaults to Auto)', () => {
      const mockContext = {
        parameters: {},
        mode: {}
      };

      const theme = detectTheme(mockContext, undefined);

      expect(theme).toBe(spaarkeLight);
    });
  });

  describe('isDarkMode', () => {
    it('should return true when isDarkTheme is true', () => {
      const mockContext = {
        fluentDesignLanguage: {
          isDarkTheme: true
        }
      };

      const result = isDarkMode(mockContext);

      expect(result).toBe(true);
    });

    it('should return false when isDarkTheme is false', () => {
      const mockContext = {
        fluentDesignLanguage: {
          isDarkTheme: false
        }
      };

      const result = isDarkMode(mockContext);

      expect(result).toBe(false);
    });

    it('should return false when fluentDesignLanguage is undefined', () => {
      const mockContext = {};

      const result = isDarkMode(mockContext);

      expect(result).toBe(false);
    });
  });
});
