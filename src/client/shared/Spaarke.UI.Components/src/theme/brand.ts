/**
 * Spaarke Brand Theme
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import {
  BrandVariants,
  createLightTheme,
  createDarkTheme,
  Theme
} from "@fluentui/react-components";

/**
 * Spaarke brand color palette
 * Primary color: Blue (#2173d7 / brand 80)
 */
export const spaarkeBrand: BrandVariants = {
  10: "#020305",
  20: "#0b1a33",
  30: "#102a52",
  40: "#14386c",
  50: "#184787",
  60: "#1c56a2",
  70: "#1f64bc",
  80: "#2173d7",
  90: "#2683f2",
  100: "#4a98ff",
  110: "#73adff",
  120: "#99c1ff",
  130: "#b9d3ff",
  140: "#d2e2ff",
  150: "#e6eeff",
  160: "#f3f7ff"
};

/**
 * Spaarke light theme (default)
 */
export const spaarkeLight: Theme = createLightTheme(spaarkeBrand);

/**
 * Spaarke dark theme
 */
export const spaarkeDark: Theme = createDarkTheme(spaarkeBrand);
