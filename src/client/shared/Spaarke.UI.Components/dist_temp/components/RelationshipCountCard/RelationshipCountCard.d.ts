/**
 * RelationshipCountCard - Displays a count of semantically related documents
 * with drill-through capability.
 *
 * Callback-based component with zero service dependencies.
 * Supports loading, error, zero-count, and normal states.
 *
 * @see ADR-012 - Shared component library (callback-based props)
 * @see ADR-021 - Fluent UI v9 design tokens
 */
import * as React from 'react';
export interface IRelationshipCountCardProps {
    /** Card title — no longer displayed but kept for API compatibility. */
    title?: string;
    /** Number of semantically related documents. */
    count: number;
    /** Whether the count is currently being loaded. */
    isLoading?: boolean;
    /** Error message to display. Pass null or undefined for no error. */
    error?: string | null;
    /** Called when the user clicks to open/drill-through to related documents. */
    onOpen: () => void;
    /** Called when the user clicks the refresh button. */
    onRefresh?: () => void;
    /** Timestamp of the last relationship analysis. */
    lastUpdated?: Date;
    /** Optional graph preview element rendered above the count when count > 0. */
    graphPreview?: React.ReactElement | null;
}
export declare const RelationshipCountCard: React.FC<IRelationshipCountCardProps>;
export default RelationshipCountCard;
//# sourceMappingURL=RelationshipCountCard.d.ts.map