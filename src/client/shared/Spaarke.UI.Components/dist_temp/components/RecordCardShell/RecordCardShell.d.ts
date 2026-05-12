/**
 * RecordCardShell — shared card shell for all entity record cards.
 *
 * Provides consistent layout, sizing, hover/focus states, and accessibility
 * across all card types (Documents, Matters, Projects, Todos, Events, etc.).
 * Entity-specific cards are thin wrappers that pass content + tools.
 *
 * Layout:
 *   ┌─ accent border ──────────────────────────────────────────────┐
 *   │ [icon]  Row 1: title + primary fields     [tools] [menu]   │
 *   │         Row 2: secondary content                             │
 *   └─────────────────────────────────────────────────────────────┘
 *
 * The `tools` slot renders inline action buttons (preview, pin, summary,
 * etc.) — different per entity type. The `overflowMenu` slot renders the
 * ⋮ overflow menu. Both are optional.
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 design system
 */
import * as React from 'react';
export interface IRecordCardShellProps {
    /** Left icon element — typically a Fluent icon in a colored circle. */
    icon: React.ReactNode;
    /** Row 1 content — title, badges, primary fields. */
    primaryContent: React.ReactNode;
    /** Row 2 content — status badges, description, metadata. Optional. */
    secondaryContent?: React.ReactNode;
    /**
     * Inline action buttons rendered in the right column (e.g., preview,
     * pin, AI summary). Different per entity type. Rendered before
     * the overflow menu.
     */
    tools?: React.ReactNode;
    /** Overflow menu (⋮) — rendered after tools. Optional. */
    overflowMenu?: React.ReactNode;
    /** Left accent border color. Default: brand. Set to "none" to hide. */
    accentColor?: string;
    /** Click handler (single click). Makes the card interactive. */
    onClick?: (e: React.MouseEvent | React.KeyboardEvent) => void;
    /** Double-click handler (e.g., open in new tab). */
    onDoubleClick?: (e: React.MouseEvent) => void;
    /** Accessible label for the card. */
    ariaLabel?: string;
    /** Shows a subtle loading overlay (e.g., while navigating). */
    isLoading?: boolean;
    /** Additional CSS class applied to the root element. */
    className?: string;
    /** Test ID for automated testing. */
    'data-testid'?: string;
}
export declare const RecordCardShell: React.FC<IRecordCardShellProps>;
export default RecordCardShell;
//# sourceMappingURL=RecordCardShell.d.ts.map