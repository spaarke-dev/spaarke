import * as React from "react";
import {
  Button,
  Tooltip,
  makeStyles,
} from "@fluentui/react-components";
import {
  WeatherSunnyRegular,
  WeatherMoonRegular,
} from "@fluentui/react-icons";
import { useTheme, DarkLightMode } from "../../hooks/useTheme";

const useStyles = makeStyles({
  toggleButton: {
    minWidth: "auto",
  },
});

const NEXT_MODE: Record<DarkLightMode, DarkLightMode> = {
  light: "dark",
  dark: "light",
};

const MODE_LABELS: Record<DarkLightMode, string> = {
  light: "Light theme",
  dark: "Dark theme",
};

function ThemeIcon({ mode }: { mode: DarkLightMode }): React.ReactElement {
  switch (mode) {
    case "dark":
      return <WeatherMoonRegular />;
    case "light":
    default:
      return <WeatherSunnyRegular />;
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
export const ThemeToggle: React.FC = () => {
  const { themeMode, setDarkLightMode } = useTheme();
  const styles = useStyles();

  const handleClick = React.useCallback(() => {
    setDarkLightMode(NEXT_MODE[themeMode]);
  }, [themeMode, setDarkLightMode]);

  const currentLabel = MODE_LABELS[themeMode];
  const nextMode = NEXT_MODE[themeMode];
  const nextLabel = MODE_LABELS[nextMode];

  return (
    <Tooltip
      content={`${currentLabel} — click to switch to ${nextLabel}`}
      relationship="label"
    >
      <Button
        className={styles.toggleButton}
        appearance="subtle"
        icon={<ThemeIcon mode={themeMode} />}
        onClick={handleClick}
        aria-label={`Current theme: ${currentLabel}. Click to switch to ${nextLabel}.`}
      />
    </Tooltip>
  );
};
