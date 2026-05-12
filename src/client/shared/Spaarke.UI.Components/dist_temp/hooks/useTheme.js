import { useState, useEffect, useCallback } from "react";
import { getUserThemePreference, setUserThemePreference, resolveCodePageTheme, setupCodePageThemeListener, applyMdaTheme, } from "../utils/themeStorage";
function preferenceToMode(pref) {
    return pref === "dark" ? "dark" : "light";
}
/**
 * React hook for theme management in Code Pages and workspace SPAs.
 *
 * Delegates to shared theme utilities:
 * - getUserThemePreference() / setUserThemePreference() for localStorage
 * - resolveCodePageTheme() for Fluent UI v9 theme resolution
 * - setupCodePageThemeListener() for cross-tab and same-tab change events
 *
 * OS `prefers-color-scheme` is intentionally NOT consulted (ADR-021).
 */
export function useTheme() {
    const [theme, setTheme] = useState(resolveCodePageTheme);
    const [themeMode, setDarkLightModeState] = useState(() => preferenceToMode(getUserThemePreference()));
    const setDarkLightMode = useCallback((mode) => {
        setUserThemePreference(mode);
        // Apply to MDA shell — triggers full page reload with dark mode URL flag.
        // After reload, all surfaces re-initialize from localStorage.
        applyMdaTheme(mode);
        // If applyMdaTheme didn't reload (flag already matched), update React state
        setDarkLightModeState(mode);
        setTheme(resolveCodePageTheme());
    }, []);
    useEffect(() => {
        const cleanup = setupCodePageThemeListener((newTheme) => {
            setTheme(newTheme);
            setDarkLightModeState(preferenceToMode(getUserThemePreference()));
        });
        return cleanup;
    }, []);
    return { theme, themeMode, setDarkLightMode };
}
//# sourceMappingURL=useTheme.js.map