/**
 * DiffCompareView Component
 *
 * Renders side-by-side and inline diff views with Accept, Reject, and Edit
 * actions. Designed for showing AI-proposed revisions against existing document
 * content.
 *
 * Features:
 * - Side-by-side and inline diff display modes
 * - Accept, Reject, Edit action buttons
 * - Inline text editing with Save/Cancel
 * - Keyboard shortcuts: Ctrl+Enter (Accept), Escape (Reject)
 * - Fluent UI v9 styling with design tokens
 * - Dark mode and high-contrast support
 * - ARIA labels and keyboard navigation
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent UI v9)
 */
import * as React from 'react';
import type { IDiffCompareViewProps } from './DiffCompareView.types';
/**
 * DiffCompareView renders a diff comparison between original and proposed text.
 *
 * Supports side-by-side and inline display modes with Accept, Reject, and Edit
 * actions. Designed for reviewing AI-proposed revisions.
 *
 * @param props - Component configuration (see IDiffCompareViewProps)
 *
 * @example
 * ```tsx
 * <DiffCompareView
 *     originalText="The quick brown fox"
 *     proposedText="The fast brown fox jumps"
 *     mode="side-by-side"
 *     onAccept={(text) => saveRevision(text)}
 *     onReject={() => discardRevision()}
 * />
 * ```
 */
export declare const DiffCompareView: React.FC<IDiffCompareViewProps>;
export default DiffCompareView;
//# sourceMappingURL=DiffCompareView.d.ts.map