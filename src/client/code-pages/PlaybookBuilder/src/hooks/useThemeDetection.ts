/**
 * useThemeDetection -- 4-level theme resolution for Code Pages
 *
 * Priority:
 *   1. URL parameter (?theme=light|dark|highcontrast)
 *   2. Xrm frame-walk (Dataverse host theme)
 *   3. OS preference (prefers-color-scheme / forced-colors)
 *   4. Default: webLightTheme
 *
 * Copied from AnalysisWorkspace/src/hooks/useThemeDetection.ts
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 */

import { useState, useEffect, useCallback } from "react";
import {
    type Theme,
    webLightTheme,
    webDarkTheme,
    teamsHighContrastTheme,
} from "@fluentui/react-components";

const THEME_CHANGE_EVENT = "spaarke-theme-change";

export type ThemeName = "light" | "dark" | "highcontrast";

export interface UseThemeDetectionResult {
    theme: Theme;
    themeName: ThemeName;
    isDark: boolean;
}

function getThemeFromUrlParam(params?: URLSearchParams): ThemeName | null {
    if (!params) return null;
    const value = params.get("theme")?.toLowerCase();
    if (value === "dark" || value === "light" || value === "highcontrast") return value;
    return null;
}

/* eslint-disable @typescript-eslint/no-explicit-any */

function getThemeFromXrmFrameWalk(): ThemeName | null {
    const frames: Window[] = [window];
    try { if (window.parent && window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!); } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm;
            if (!xrm) continue;
            const ctx = xrm.Utility?.getGlobalContext?.();
            if (ctx?.getCurrentTheme) {
                const themeInfo = ctx.getCurrentTheme();
                if (themeInfo?.backgroundcolor) {
                    const dark = isColorDark(themeInfo.backgroundcolor);
                    if (dark !== null) return dark ? "dark" : "light";
                }
            }
        } catch { /* cross-origin */ }
    }

    for (const frame of frames) {
        try {
            const bgColor = (frame as any).getComputedStyle((frame as any).document.body).backgroundColor;
            if (bgColor && bgColor !== "rgba(0, 0, 0, 0)" && bgColor !== "transparent") {
                const dark = isColorDark(bgColor);
                if (dark !== null) return dark ? "dark" : "light";
            }
        } catch { /* cross-origin */ }
    }

    return null;
}

function isColorDark(color: string): boolean | null {
    if (!color) return null;
    let r: number, g: number, b: number;
    const rgbMatch = color.match(/\d+/g)?.map(Number);
    if (rgbMatch && rgbMatch.length >= 3) {
        [r, g, b] = rgbMatch;
    } else if (color.startsWith("#")) {
        const hex = color.replace("#", "");
        if (hex.length === 3) {
            r = parseInt(hex[0] + hex[0], 16);
            g = parseInt(hex[1] + hex[1], 16);
            b = parseInt(hex[2] + hex[2], 16);
        } else if (hex.length >= 6) {
            r = parseInt(hex.substring(0, 2), 16);
            g = parseInt(hex.substring(2, 4), 16);
            b = parseInt(hex.substring(4, 6), 16);
        } else {
            return null;
        }
    } else {
        return null;
    }
    const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
    return luminance < 0.5;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

function getSystemThemePreference(): ThemeName | null {
    try {
        if (window.matchMedia("(forced-colors: active)").matches) return "highcontrast";
        if (window.matchMedia("(prefers-color-scheme: dark)").matches) return "dark";
        if (window.matchMedia("(prefers-color-scheme: light)").matches) return "light";
    } catch { /* matchMedia not available */ }
    return null;
}

function themeNameToFluentTheme(name: ThemeName): Theme {
    switch (name) {
        case "dark": return webDarkTheme;
        case "highcontrast": return teamsHighContrastTheme;
        default: return webLightTheme;
    }
}

function resolveThemeName(params?: URLSearchParams): ThemeName {
    try {
        const urlTheme = getThemeFromUrlParam(params);
        if (urlTheme) return urlTheme;
        const xrmTheme = getThemeFromXrmFrameWalk();
        if (xrmTheme) return xrmTheme;
        const systemTheme = getSystemThemePreference();
        if (systemTheme) return systemTheme;
        return "light";
    } catch {
        return "light";
    }
}

export function useThemeDetection(params?: URLSearchParams): UseThemeDetectionResult {
    const [themeName, setThemeName] = useState<ThemeName>(() => resolveThemeName(params));

    const reResolve = useCallback(() => {
        setThemeName(resolveThemeName(params));
    }, [params]);

    useEffect(() => {
        const darkQuery = window.matchMedia("(prefers-color-scheme: dark)");
        darkQuery.addEventListener("change", reResolve);
        const hcQuery = window.matchMedia("(forced-colors: active)");
        hcQuery.addEventListener("change", reResolve);
        window.addEventListener(THEME_CHANGE_EVENT, reResolve);
        return () => {
            darkQuery.removeEventListener("change", reResolve);
            hcQuery.removeEventListener("change", reResolve);
            window.removeEventListener(THEME_CHANGE_EVENT, reResolve);
        };
    }, [reResolve]);

    return {
        theme: themeNameToFluentTheme(themeName),
        themeName,
        isDark: themeName === "dark",
    };
}
