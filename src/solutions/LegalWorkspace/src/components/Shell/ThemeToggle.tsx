import * as React from "react";
import {
  Button,
  Tooltip,
  makeStyles,
} from "@fluentui/react-components";
import {
  WeatherSunnyRegular,
  WeatherMoonRegular,
  AccessibilityRegular,
} from "@fluentui/react-icons";
import { useTheme, ThemeMode } from "../../hooks/useTheme";

const useStyles = makeStyles({
  toggleButton: {
    minWidth: "auto",
  },
});

const NEXT_MODE: Record<ThemeMode, ThemeMode> = {
  light: "dark",
  dark: "high-contrast",
  "high-contrast": "light",
};

const MODE_LABELS: Record<ThemeMode, string> = {
  light: "Light theme",
  dark: "Dark theme",
  "high-contrast": "High contrast theme",
};

function ThemeIcon({ mode }: { mode: ThemeMode }): React.ReactElement {
  switch (mode) {
    case "dark":
      return <WeatherMoonRegular />;
    case "high-contrast":
      return <AccessibilityRegular />;
    case "light":
    default:
      return <WeatherSunnyRegular />;
  }
}

export const ThemeToggle: React.FC = () => {
  const { themeMode, setThemeMode } = useTheme();
  const styles = useStyles();

  const handleClick = React.useCallback(() => {
    setThemeMode(NEXT_MODE[themeMode]);
  }, [themeMode, setThemeMode]);

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
