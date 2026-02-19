/**
 * GradePill — compact badge displaying a single letter grade (A–F).
 *
 * Colors are driven entirely by Fluent UI v9 semantic palette tokens so the
 * pill automatically adapts to light, dark, and high-contrast themes.
 *
 * Grade → token mapping:
 *   A → colorPaletteGreenForeground1    (green)
 *   B → colorPaletteTealForeground2     (teal)
 *   C → colorPaletteMarigoldForeground1 (amber)
 *   D → colorPalettePumpkinForeground2  (orange)
 *   F → colorPaletteCranberryForeground2 (red)
 */

import * as React from 'react';
import { makeStyles, shorthands, tokens, Text, mergeClasses } from '@fluentui/react-components';
import { GradeLevel } from '../../types/enums';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  pill: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: '20px',
    height: '20px',
    paddingLeft: tokens.spacingHorizontalXXS,
    paddingRight: tokens.spacingHorizontalXXS,
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderWidth('1px'),
    ...shorthands.borderStyle('solid'),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    fontWeight: tokens.fontWeightSemibold,
  },
  // Per-grade foreground color — background intentionally neutral so the grade
  // letter carries the semantic colour rather than the entire pill.
  gradeA: {
    color: tokens.colorPaletteGreenForeground1,
    ...shorthands.borderColor(tokens.colorPaletteGreenForeground1),
  },
  gradeB: {
    color: tokens.colorPaletteTealForeground2,
    ...shorthands.borderColor(tokens.colorPaletteTealForeground2),
  },
  gradeC: {
    color: tokens.colorPaletteMarigoldForeground1,
    ...shorthands.borderColor(tokens.colorPaletteMarigoldForeground1),
  },
  gradeD: {
    color: tokens.colorPalettePumpkinForeground2,
    ...shorthands.borderColor(tokens.colorPalettePumpkinForeground2),
  },
  gradeF: {
    color: tokens.colorPaletteCranberryForeground2,
    ...shorthands.borderColor(tokens.colorPaletteCranberryForeground2),
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function getGradeClass(
  grade: GradeLevel,
  styles: ReturnType<typeof useStyles>
): string {
  switch (grade) {
    case 'A': return styles.gradeA;
    case 'B': return styles.gradeB;
    case 'C': return styles.gradeC;
    case 'D': return styles.gradeD;
    case 'F': return styles.gradeF;
  }
}

/** Human-readable label for screen readers */
function getAriaLabel(grade: GradeLevel, dimensionLabel?: string): string {
  const gradeNames: Record<GradeLevel, string> = {
    A: 'Excellent',
    B: 'Good',
    C: 'Fair',
    D: 'Poor',
    F: 'Failing',
  };
  const gradeText = `${grade} — ${gradeNames[grade]}`;
  return dimensionLabel ? `${dimensionLabel}: ${gradeText}` : gradeText;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IGradePillProps {
  /** The letter grade to display */
  grade: GradeLevel;
  /**
   * Optional dimension label used in the aria-label attribute for
   * screen-reader context (e.g. "Budget Controls", "Guidelines Compliance").
   */
  dimensionLabel?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * GradePill renders a small pill badge with a letter grade (A–F).
 * Color is derived from the grade using Fluent semantic palette tokens.
 * Fully accessible: includes an aria-label describing the grade meaning.
 *
 * React.memo: GradePill is rendered 3× per MatterItem. With 500+ matters,
 * this prevents up to 1500 unnecessary re-renders per parent state update
 * (NFR-07 — query response < 2s for 500 matters).
 */
export const GradePill: React.FC<IGradePillProps> = React.memo(({ grade, dimensionLabel }) => {
  const styles = useStyles();
  const gradeClass = getGradeClass(grade, styles);

  return (
    <span
      className={mergeClasses(styles.pill, gradeClass)}
      role="img"
      aria-label={getAriaLabel(grade, dimensionLabel)}
      title={getAriaLabel(grade, dimensionLabel)}
    >
      <Text size={100} weight="semibold">
        {grade}
      </Text>
    </span>
  );
});

GradePill.displayName = 'GradePill';
