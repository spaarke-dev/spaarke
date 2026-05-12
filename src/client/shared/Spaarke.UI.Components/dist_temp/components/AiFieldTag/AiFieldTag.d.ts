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
export interface IAiFieldTagProps {
    /** Optional CSS class to allow caller-side spacing overrides. */
    className?: string;
}
/**
 * Small inline pill — SparkleRegular icon + "AI" text — rendered inside a
 * field label to indicate the field was pre-populated by AI pre-fill.
 *
 * Accessible: includes an aria-label on the outer span so screen readers
 * announce "AI pre-filled" rather than reading the icon as a decorative
 * glyph and the text "AI" as separate words.
 */
export declare const AiFieldTag: React.FC<IAiFieldTagProps>;
//# sourceMappingURL=AiFieldTag.d.ts.map