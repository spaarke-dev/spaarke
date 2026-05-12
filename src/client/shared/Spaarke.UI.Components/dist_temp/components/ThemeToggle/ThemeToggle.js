import * as React from "react";
import { Button, Tooltip, makeStyles, } from "@fluentui/react-components";
import { WeatherSunnyRegular, WeatherMoonRegular, } from "@fluentui/react-icons";
import { useTheme } from "../../hooks/useTheme";
const useStyles = makeStyles({
    toggleButton: {
        minWidth: "auto",
    },
});
const NEXT_MODE = {
    light: "dark",
    dark: "light",
};
const MODE_LABELS = {
    light: "Light theme",
    dark: "Dark theme",
};
function ThemeIcon({ mode }) {
    switch (mode) {
        case "dark":
            return React.createElement(WeatherMoonRegular, null);
        case "light":
        default:
            return React.createElement(WeatherSunnyRegular, null);
    }
}
/**
 * Theme toggle button — sun/moon icon that cycles light ↔ dark.
 *
 * Uses the shared `useTheme` hook for state management.
 * Writes to localStorage and dispatches theme change events
 * so all surfaces (PCF controls, other Code Pages) stay in sync.
 *
 * @example
 * ```tsx
 * import { ThemeToggle } from "@spaarke/ui-components";
 * <PageHeader><ThemeToggle /></PageHeader>
 * ```
 */
export const ThemeToggle = () => {
    const { themeMode, setDarkLightMode } = useTheme();
    const styles = useStyles();
    const handleClick = React.useCallback(() => {
        setDarkLightMode(NEXT_MODE[themeMode]);
    }, [themeMode, setDarkLightMode]);
    const currentLabel = MODE_LABELS[themeMode];
    const nextMode = NEXT_MODE[themeMode];
    const nextLabel = MODE_LABELS[nextMode];
    return (React.createElement(Tooltip, { content: `${currentLabel} — click to switch to ${nextLabel}`, relationship: "label" },
        React.createElement(Button, { className: styles.toggleButton, appearance: "subtle", icon: React.createElement(ThemeIcon, { mode: themeMode }), onClick: handleClick, "aria-label": `Current theme: ${currentLabel}. Click to switch to ${nextLabel}.` })));
};
//# sourceMappingURL=ThemeToggle.js.map