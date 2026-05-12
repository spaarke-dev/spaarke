/**
 * CardIcon — branded circle wrapper for card icons.
 *
 * Renders a Fluent icon inside a colored circle, used as the left
 * column of RecordCardShell. Supports custom colors for entity-specific
 * theming (e.g., file-type icons for documents).
 *
 * @see ADR-021 - Fluent UI v9 design system
 */
import * as React from 'react';
export interface ICardIconProps {
    /** Fluent icon element (e.g., <DocumentRegular />, <GavelRegular />). */
    children: React.ReactNode;
    /** Circle size in pixels. Default: 40. */
    size?: number;
    /** Background color. Default: brand background 2. */
    backgroundColor?: string;
    /** Icon color. Default: brand foreground 1. */
    iconColor?: string;
    /** Additional CSS class. */
    className?: string;
}
export declare const CardIcon: React.FC<ICardIconProps>;
export default CardIcon;
//# sourceMappingURL=CardIcon.d.ts.map