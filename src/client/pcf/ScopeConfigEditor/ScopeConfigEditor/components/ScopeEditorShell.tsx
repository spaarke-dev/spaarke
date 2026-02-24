/**
 * ScopeEditorShell
 *
 * FluentProvider wrapper with dark mode detection from PCF context.
 * Provides a consistent container layout for all scope editor variants.
 *
 * ADR-021: FluentProvider wraps all UI; design tokens for colors.
 * ADR-022: React 16 APIs. No createRoot.
 */

import * as React from "react";
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme,
    Theme,
    makeStyles,
    tokens,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IScopeEditorShellProps {
    /** Whether dark mode is active (detected from PCF context or URL params) */
    isDark?: boolean;
    children: React.ReactNode;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    shell: {
        display: "flex",
        flexDirection: "column",
        width: "100%",
        minHeight: "120px",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase300,
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helper: detect dark mode from URL
// ─────────────────────────────────────────────────────────────────────────────

function detectDarkMode(): boolean {
    try {
        const href = window.location.href;
        if (
            href.includes("themeOption%3Ddarkmode") ||
            href.includes("themeOption=darkmode")
        ) {
            return true;
        }
        try {
            const parentHref = window.parent?.location?.href;
            if (
                parentHref?.includes("themeOption%3Ddarkmode") ||
                parentHref?.includes("themeOption=darkmode")
            ) {
                return true;
            }
        } catch {
            // Cross-origin blocked
        }
    } catch {
        // Error accessing location
    }

    // Light mode unless Dataverse explicitly sets dark mode via URL param.
    // Do NOT respect OS prefers-color-scheme — control is embedded in Dataverse form.
    return false;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

const ShellContent: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const styles = useStyles();
    return <div className={styles.shell}>{children}</div>;
};

export const ScopeEditorShell: React.FC<IScopeEditorShellProps> = ({
    isDark,
    children,
}) => {
    const [theme, setTheme] = React.useState<Theme>(() => {
        const dark = isDark !== undefined ? isDark : detectDarkMode();
        return dark ? webDarkTheme : webLightTheme;
    });

    // Update theme when prop changes
    React.useEffect(() => {
        if (isDark !== undefined) {
            setTheme(isDark ? webDarkTheme : webLightTheme);
        }
    }, [isDark]);

    // Listen for system theme changes when isDark is uncontrolled
    React.useEffect(() => {
        if (isDark !== undefined) return;

        if (typeof window === "undefined" || !window.matchMedia) return;

        const mq = window.matchMedia("(prefers-color-scheme: dark)");
        const handler = (e: MediaQueryListEvent) => {
            setTheme(e.matches ? webDarkTheme : webLightTheme);
        };
        mq.addEventListener("change", handler);
        return () => mq.removeEventListener("change", handler);
    }, [isDark]);

    return (
        <FluentProvider theme={theme}>
            <ShellContent>{children}</ShellContent>
        </FluentProvider>
    );
};
