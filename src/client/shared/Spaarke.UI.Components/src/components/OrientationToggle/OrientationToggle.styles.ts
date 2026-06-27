/**
 * OrientationToggle — styles (Griffel makeStyles + Fluent v9 tokens)
 *
 * Per ADR-021: no hard-coded colors, no inline styles, no CSS modules.
 */
import { makeStyles } from '@fluentui/react-components';

export const useOrientationToggleStyles = makeStyles({
  /** Single icon button — matches the subtle inline-icon style used across
   *  Spaarke toolbars (see DocumentToolbar / ThemeToggle). */
  toggleButton: {
    minWidth: 'auto',
  },
});
