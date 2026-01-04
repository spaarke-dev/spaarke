import * as React from "react";
import type { Preview } from "@storybook/react";
import { withThemeByClassName } from "@storybook/addon-themes";
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
  teamsHighContrastTheme,
} from "@fluentui/react-components";

// Theme mapping for Fluent UI
const themes = {
  light: webLightTheme,
  dark: webDarkTheme,
  "high-contrast": teamsHighContrastTheme,
};

// Current theme state
let currentTheme = themes.light;

// FluentProvider decorator that wraps all stories
const FluentDecorator = (Story: React.ComponentType) => {
  return (
    <FluentProvider theme={currentTheme}>
      <div style={{ padding: "1rem", minHeight: "200px" }}>
        <Story />
      </div>
    </FluentProvider>
  );
};

// Theme switcher decorator
const ThemeSwitcherDecorator = (Story: React.ComponentType, context: { globals: { theme?: string } }) => {
  const selectedTheme = context.globals.theme || "light";
  currentTheme = themes[selectedTheme as keyof typeof themes] || themes.light;
  return <Story />;
};

const preview: Preview = {
  parameters: {
    controls: {
      matchers: {
        color: /(background|color)$/i,
        date: /Date$/i,
      },
    },
    backgrounds: {
      disable: true, // Handled by Fluent themes
    },
  },
  globalTypes: {
    theme: {
      name: "Theme",
      description: "Fluent UI theme for components",
      defaultValue: "light",
      toolbar: {
        icon: "paintbrush",
        items: [
          { value: "light", title: "Light", icon: "sun" },
          { value: "dark", title: "Dark", icon: "moon" },
          { value: "high-contrast", title: "High Contrast", icon: "contrast" },
        ],
        dynamicTitle: true,
      },
    },
  },
  decorators: [ThemeSwitcherDecorator, FluentDecorator],
};

export default preview;
