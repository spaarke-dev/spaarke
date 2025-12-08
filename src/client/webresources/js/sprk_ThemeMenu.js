/**
 * Theme Menu Command Bar Handler
 *
 * MINIMAL web resource per ADR-006.
 * Only handles ribbon invocation; actual logic in shared library.
 *
 * @see ADR-006 - Prefer PCF over web resources
 * @see projects/mda-darkmode-theme/spec.md Section 3.3
 */

var Spaarke = window.Spaarke || {};
Spaarke.Theme = Spaarke.Theme || {};

(function() {
    'use strict';

    var STORAGE_KEY = 'spaarke-theme';
    var VALID_THEMES = ['auto', 'light', 'dark'];

    var DARK_MODE_FLAG = 'flags=themeOption%3Ddarkmode';

    // Logo web resource names (without publisher prefix path)
    var LOGO_LIGHT = 'sprk_spaarkefulllogoSVG';    // Default colored logo for light mode
    var LOGO_DARK = 'sprk_spaarkelogoallwhite';    // White logo for dark mode

    /**
     * Set theme - called by ribbon menu items
     * Uses Power Platform URL flag to toggle MDA dark mode
     * @param {string} theme - Theme to set ('auto', 'light', or 'dark')
     */
    Spaarke.Theme.setTheme = function(theme) {
        if (VALID_THEMES.indexOf(theme) === -1) {
            console.warn('[Spaarke.Theme] Invalid theme:', theme);
            return;
        }

        localStorage.setItem(STORAGE_KEY, theme);
        console.log('[Spaarke.Theme] Theme set to:', theme);

        // Dispatch custom event for same-tab listeners (PCF controls)
        window.dispatchEvent(new CustomEvent('spaarke-theme-change', {
            detail: { theme: theme }
        }));

        // Apply MDA dark mode via URL flag
        // Use top.location to navigate main window (web resources run in iframe context)
        var topWindow = window.top || window;
        var currentUrl = topWindow.location.href;
        var hasDarkFlag = currentUrl.indexOf(DARK_MODE_FLAG) !== -1;

        console.log('[Spaarke.Theme] Current URL:', currentUrl);
        console.log('[Spaarke.Theme] Has dark flag:', hasDarkFlag);

        if (theme === 'dark' && !hasDarkFlag) {
            // Add dark mode flag and reload
            var separator = currentUrl.indexOf('?') !== -1 ? '&' : '?';
            var newUrl = currentUrl + separator + DARK_MODE_FLAG;
            console.log('[Spaarke.Theme] Navigating to:', newUrl);
            topWindow.location.href = newUrl;
        } else if ((theme === 'light' || theme === 'auto') && hasDarkFlag) {
            // Remove dark mode flag and reload
            var newUrl = currentUrl
                .replace('&' + DARK_MODE_FLAG, '')
                .replace('?' + DARK_MODE_FLAG + '&', '?')
                .replace('?' + DARK_MODE_FLAG, '');
            console.log('[Spaarke.Theme] Navigating to:', newUrl);
            topWindow.location.href = newUrl;
        }
    };

    /**
     * Check if theme is currently selected
     * Note: Not required for Button elements per Fluent V9 pattern,
     * but included for potential future use.
     * @param {string} theme - Theme to check
     * @returns {boolean} True if this theme is selected
     */
    Spaarke.Theme.isSelected = function(theme) {
        var stored = localStorage.getItem(STORAGE_KEY) || 'auto';
        return stored === theme;
    };

    /**
     * Enable rule - always enabled
     * Called by ribbon enable rules
     * SIDE EFFECT: Enforces theme on every ribbon load (all pages including entity views)
     * @returns {boolean} Always returns true
     */
    Spaarke.Theme.isEnabled = function() {
        // Enforce theme whenever ribbon loads - runs on every page including entity views
        Spaarke.Theme.init();
        return true;
    };

    // Listen for system preference changes (for auto mode)
    if (window.matchMedia) {
        window.matchMedia('(prefers-color-scheme: dark)')
            .addEventListener('change', function(e) {
                var stored = localStorage.getItem(STORAGE_KEY) || 'auto';
                if (stored === 'auto') {
                    console.log('[Spaarke.Theme] System preference changed, dark mode:', e.matches);
                    window.dispatchEvent(new CustomEvent('spaarke-theme-change', {
                        detail: { theme: 'auto', effectiveDark: e.matches }
                    }));
                }
            });
    }

    /**
     * Auto-initialize: Check localStorage and redirect if needed
     * This runs when the script loads - call Spaarke.Theme.init() from form onLoad
     */
    Spaarke.Theme.init = function() {
        var topWindow = window.top || window;
        var currentUrl = topWindow.location.href;
        var storedTheme = localStorage.getItem(STORAGE_KEY) || 'auto';
        var hasDarkFlag = currentUrl.indexOf(DARK_MODE_FLAG) !== -1;

        console.log('[Spaarke.Theme] Init - stored theme:', storedTheme, 'hasDarkFlag:', hasDarkFlag);

        // If user wants dark but URL doesn't have flag, add it
        if (storedTheme === 'dark' && !hasDarkFlag) {
            var separator = currentUrl.indexOf('?') !== -1 ? '&' : '?';
            var newUrl = currentUrl + separator + DARK_MODE_FLAG;
            console.log('[Spaarke.Theme] Auto-redirecting to dark mode:', newUrl);
            topWindow.location.href = newUrl;
            return;
        }

        // If user wants light/auto but URL has dark flag, remove it
        if ((storedTheme === 'light' || storedTheme === 'auto') && hasDarkFlag) {
            var newUrl = currentUrl
                .replace('&' + DARK_MODE_FLAG, '')
                .replace('?' + DARK_MODE_FLAG + '&', '?')
                .replace('?' + DARK_MODE_FLAG, '');
            console.log('[Spaarke.Theme] Auto-redirecting to light mode:', newUrl);
            topWindow.location.href = newUrl;
            return;
        }

        // No redirect needed - update the logo for current theme
        Spaarke.Theme.initLogo();
    };

    /**
     * Update the app header logo based on current theme
     * Swaps between colored logo (light mode) and white logo (dark mode)
     */
    Spaarke.Theme.updateLogo = function() {
        var topWindow = window.top || window;
        var currentUrl = topWindow.location.href;
        var isDarkMode = currentUrl.indexOf(DARK_MODE_FLAG) !== -1;

        // Find the app header logo image
        // MDA header logo is typically in the navbar area
        // Use specific selectors first, then generic ones
        var specificSelectors = [
            'img[src*="spaarkelogo"]',
            'img[src*="sprk_spaarke"]'
        ];
        var genericSelectors = [
            '[data-id="navbar-container"] img',
            '.pa-s img[alt*="logo" i]',
            '.navBarArea img'
        ];

        var logoImg = null;
        var foundViaSpecific = false;
        var doc = topWindow.document;

        // Try specific selectors first
        for (var i = 0; i < specificSelectors.length; i++) {
            logoImg = doc.querySelector(specificSelectors[i]);
            if (logoImg) {
                foundViaSpecific = true;
                break;
            }
        }

        // Fall back to generic selectors
        if (!logoImg) {
            for (var i = 0; i < genericSelectors.length; i++) {
                logoImg = doc.querySelector(genericSelectors[i]);
                if (logoImg) break;
            }
        }

        if (!logoImg) {
            console.log('[Spaarke.Theme] Logo image not found, will retry...');
            return false;
        }

        var currentSrc = logoImg.src || '';
        var targetLogo = isDarkMode ? LOGO_DARK : LOGO_LIGHT;

        // Check if logo already correct
        if (currentSrc.indexOf(targetLogo) !== -1) {
            console.log('[Spaarke.Theme] Logo already set to:', targetLogo);
            return true;
        }

        // Build the new logo URL
        var newSrc;
        if (isDarkMode && currentSrc.indexOf(LOGO_LIGHT) !== -1) {
            // Switching from light to dark - simple replace
            newSrc = currentSrc.replace(LOGO_LIGHT, LOGO_DARK);
        } else if (!isDarkMode && currentSrc.indexOf(LOGO_DARK) !== -1) {
            // Switching from dark to light - simple replace
            newSrc = currentSrc.replace(LOGO_DARK, LOGO_LIGHT);
        } else if (foundViaSpecific) {
            // Found a Spaarke logo but pattern didn't match - extract and rebuild
            // Handle case where logo might have query params or different format
            // Match both: sprk_spaarkelogoallwhite and sprk_spaarkefulllogoSVG
            var logoMatch = currentSrc.match(/sprk_spaarke[a-zA-Z]*/i);
            if (logoMatch) {
                newSrc = currentSrc.replace(logoMatch[0], targetLogo);
            } else {
                // Shouldn't happen but fallback to current src modification
                newSrc = currentSrc.replace(/\/[^\/]+$/, '/' + targetLogo);
            }
        } else {
            // Found via generic selector - construct full web resource URL
            // Extract org URL from current page URL
            var orgUrl = topWindow.location.origin;
            newSrc = orgUrl + '/WebResources/' + targetLogo;
            console.log('[Spaarke.Theme] Constructing web resource URL:', newSrc);
        }

        console.log('[Spaarke.Theme] Switching logo from', currentSrc, 'to', newSrc);
        logoImg.src = newSrc;
        return true;
    };

    /**
     * Initialize logo with retries (DOM may not be ready immediately)
     */
    Spaarke.Theme.initLogo = function() {
        var attempts = 0;
        var maxAttempts = 10;

        var tryUpdateLogo = function() {
            attempts++;
            if (Spaarke.Theme.updateLogo()) {
                console.log('[Spaarke.Theme] Logo updated successfully');
            } else if (attempts < maxAttempts) {
                setTimeout(tryUpdateLogo, 500);
            } else {
                console.log('[Spaarke.Theme] Logo update failed after', maxAttempts, 'attempts');
            }
        };

        // Start trying after a short delay to let DOM render
        setTimeout(tryUpdateLogo, 300);
    };

    console.log('[Spaarke.Theme] Theme menu handler loaded');

})();
