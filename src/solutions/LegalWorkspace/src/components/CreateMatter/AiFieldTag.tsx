/**
 * AiFieldTag.tsx
 * Sparkle "AI" tag displayed next to a form field label when that field was
 * pre-populated by the BFF AI pre-fill call.
 *
 * Usage:
 *   <Label>Matter Name <AiFieldTag /></Label>
 *
 * Renders a pill containing:
 *   SparkleRegular icon + "AI" text
 *
 * Appearance adapts automatically to light, dark, and high-contrast themes
 * via Fluent v9 semantic tokens — zero hardcoded colors.
 */

import * as React from 'react';
import { Text, makeStyles, tokens, mergeClasses } from '@fluentui/react-components';
import { SparkleRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Inline pill with a subtle background tinted toward the brand accent
   * colour using semantic tokens.  Falls back gracefully in high-contrast
   * mode because Fluent token resolution handles the mapping automatically.
   */
  tag: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    paddingTop: '1px',
    paddingBottom: '1px',
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground2,
    // Keep it vertically centred with adjacent label text
    verticalAlign: 'middle',
    // Slightly bump up from baseline so it sits next to label text cleanly
    marginLeft: tokens.spacingHorizontalXS,
    // Prevent wrapping of icon + label inside the pill
    whiteSpace: 'nowrap',
    lineHeight: 1,
    // Ensure crisp rendering of the small icon
    flexShrink: 0,
  },

  icon: {
    // 12px feels right alongside caption-sized text without overpowering the label
    fontSize: '12px',
    display: 'flex',
    alignItems: 'center',
  },

  label: {
    // caption / very small text so the tag doesn't dominate the field label
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: '1',
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAiFieldTagProps {
  /** Optional CSS class to allow caller-side spacing overrides. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Small inline pill — SparkleRegular icon + "AI" text — rendered inside a
 * field label to indicate the field was pre-populated by AI pre-fill.
 *
 * Accessible: includes an aria-label on the outer span so screen readers
 * announce "AI pre-filled" rather than reading the icon as a decorative
 * glyph and the text "AI" as separate words.
 */
export const AiFieldTag: React.FC<IAiFieldTagProps> = ({ className }) => {
  const styles = useStyles();

  return (
    <span
      className={mergeClasses(styles.tag, className)}
      role="img"
      aria-label="AI pre-filled"
      title="This field was pre-filled by AI analysis"
    >
      <span className={styles.icon} aria-hidden="true">
        <SparkleRegular />
      </span>
      <Text className={styles.label} aria-hidden="true">
        AI
      </Text>
    </span>
  );
};
