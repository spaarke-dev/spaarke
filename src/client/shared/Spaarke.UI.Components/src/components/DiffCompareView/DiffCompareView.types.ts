/**
 * DiffCompareView Types
 *
 * TypeScript interfaces for the DiffCompareView component and HTML diff utilities.
 * Used to display side-by-side and inline diff views with
 * Accept, Reject, and Edit actions for AI-proposed revisions.
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent UI v9)
 */

// ─────────────────────────────────────────────────────────────────────────────
// Display Mode
// ─────────────────────────────────────────────────────────────────────────────

/** Display mode for the diff comparison */
export type DiffCompareViewMode = "side-by-side" | "inline";

// ─────────────────────────────────────────────────────────────────────────────
// Diff Segment
// ─────────────────────────────────────────────────────────────────────────────

/** Represents a single segment of a computed diff */
export interface IDiffSegment {
    /** The type of change this segment represents */
    type: "added" | "removed" | "unchanged";
    /** The text content of this segment */
    value: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// HTML Diff Options
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Configuration options for HTML diff computation.
 *
 * @example
 * ```ts
 * const options: DiffOptions = {
 *     whitespace: "relaxed",
 *     granularity: "word",
 * };
 * ```
 */
export interface DiffOptions {
    /**
     * Whitespace handling mode.
     * - "strict": Preserves all whitespace differences as changes.
     * - "relaxed": Normalizes whitespace (collapses runs of spaces, trims lines)
     *   before comparison so that insignificant whitespace changes are ignored.
     *
     * @default "relaxed"
     */
    whitespace: "strict" | "relaxed";

    /**
     * Diff granularity level.
     * - "word": Compares at word boundaries (recommended for prose).
     * - "character": Compares character-by-character (useful for short strings, codes).
     *
     * @default "word"
     */
    granularity: "word" | "character";
}

// ─────────────────────────────────────────────────────────────────────────────
// HTML Diff Result
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Result of an HTML diff computation.
 *
 * Contains annotated HTML for both original and proposed sides with
 * diff highlights applied as styled `<span>` elements, plus the
 * plain-text diff segments and summary statistics.
 */
export interface DiffResult {
    /**
     * HTML string for the original side with removed content highlighted.
     * Uses `<span class="diff-removed">` wrappers around removed words.
     */
    originalAnnotatedHtml: string;

    /**
     * HTML string for the proposed side with added content highlighted.
     * Uses `<span class="diff-added">` wrappers around added words.
     */
    proposedAnnotatedHtml: string;

    /** Flat list of diff segments (plain text, not HTML) */
    segments: IDiffSegment[];

    /** Summary statistics of the diff */
    stats: DiffStats;
}

/** Word-count statistics for a diff result */
export interface DiffStats {
    /** Number of words added in the proposed version */
    additions: number;
    /** Number of words removed from the original version */
    deletions: number;
    /** Number of words unchanged between versions */
    unchanged: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Block Change Detection
// ─────────────────────────────────────────────────────────────────────────────

/** Type of block-level change detected between two HTML documents */
export type BlockChangeType = "added" | "removed" | "modified" | "unchanged";

/**
 * Represents a block-level change (paragraph, heading, list item, etc.)
 * between the original and proposed HTML documents.
 */
export interface BlockChange {
    /** The type of change */
    type: BlockChangeType;
    /** The HTML tag name of the block (e.g. "p", "h1", "li") */
    tag: string;
    /** Text content of the block in the original version (empty for "added") */
    originalText: string;
    /** Text content of the block in the proposed version (empty for "removed") */
    proposedText: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component Props
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for the DiffCompareView component.
 *
 * @example
 * ```tsx
 * // Plain text diff
 * <DiffCompareView
 *     originalText="The quick brown fox"
 *     proposedText="The fast brown fox jumps"
 *     mode="side-by-side"
 *     onAccept={(text) => saveRevision(text)}
 *     onReject={() => discardRevision()}
 *     title="AI-Proposed Revision"
 * />
 *
 * // HTML diff (from RichTextEditor output)
 * <DiffCompareView
 *     originalText="<p>The quick brown fox</p>"
 *     proposedText="<p>The fast brown fox jumps</p>"
 *     htmlMode={true}
 *     mode="side-by-side"
 *     onAccept={(text) => saveRevision(text)}
 *     onReject={() => discardRevision()}
 * />
 * ```
 */
export interface IDiffCompareViewProps {
    /** The original (current) text content (plain text or HTML when htmlMode is true) */
    originalText: string;

    /** The proposed (revised) text content (plain text or HTML when htmlMode is true) */
    proposedText: string;

    /**
     * When true, treats originalText and proposedText as HTML strings.
     * Extracts text for diffing and renders annotated HTML with preserved structure.
     *
     * @default false
     */
    htmlMode?: boolean;

    /**
     * Options for the HTML diff algorithm. Only used when htmlMode is true.
     * Defaults to { whitespace: "relaxed", granularity: "word" }.
     */
    diffOptions?: Partial<DiffOptions>;

    /** Display mode: side-by-side columns or inline interleaved */
    mode?: DiffCompareViewMode;

    /**
     * Called when the user accepts the proposed text.
     * Receives the final text (which may be edited if the user used Edit mode).
     */
    onAccept: (acceptedText: string) => void;

    /** Called when the user rejects the proposed changes */
    onReject: () => void;

    /**
     * Called when the user wants to manually edit the proposed text.
     * If not provided, the Edit button will not be shown.
     */
    onEdit?: (editedText: string) => void;

    /** Optional title displayed above the diff view */
    title?: string;

    /** When true, hides Accept/Reject/Edit action buttons */
    readOnly?: boolean;

    /** Accessible label for the diff container (defaults to "Diff comparison view") */
    ariaLabel?: string;
}
