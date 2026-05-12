/**
 * useTwoPanelLayout Hook
 *
 * Manages a two-panel layout: a primary (left) panel and a collapsible detail
 * (right) panel. Handles visibility toggling, splitter drag operations,
 * keyboard resize, minimum width enforcement, and localStorage persistence.
 *
 * Adapted from the three-panel usePanelLayout in AnalysisWorkspace, simplified
 * for the common two-panel pattern (e.g., kanban board + detail panel).
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 design system
 */
export interface UseTwoPanelLayoutOptions {
    /** Default width for the detail panel in pixels (default: 400) */
    defaultDetailWidth?: number;
    /** Minimum width for the primary panel in pixels (default: 300) */
    minPrimaryWidth?: number;
    /** Minimum width for the detail panel in pixels (default: 280) */
    minDetailWidth?: number;
    /** localStorage key for persisting state (default: 'panel-layout') */
    storageKey?: string;
}
export interface SplitterHandlers {
    onMouseDown: (e: React.MouseEvent) => void;
    onKeyDown: (e: React.KeyboardEvent) => void;
    onDoubleClick: () => void;
}
export interface UseTwoPanelLayoutResult {
    /** CSS width value for the primary (left) panel */
    primaryWidth: string;
    /** CSS width value for the detail (right) panel, '0px' when hidden */
    detailWidth: string;
    /** Whether the detail panel is currently visible */
    isDetailVisible: boolean;
    /** Toggle detail panel visibility */
    toggleDetail: () => void;
    /** Show detail panel */
    showDetail: () => void;
    /** Hide detail panel */
    hideDetail: () => void;
    /** Handlers to pass to PanelSplitter component */
    splitterHandlers: SplitterHandlers;
    /** Whether the splitter is being dragged */
    isDragging: boolean;
    /** Ref to attach to the container element */
    containerRef: React.RefObject<HTMLDivElement | null>;
    /** Current ratio (0-1) of primary panel width — for PanelSplitter ARIA */
    currentRatio: number;
    /** Reset to default layout */
    resetToDefaults: () => void;
}
export declare function useTwoPanelLayout(options?: UseTwoPanelLayoutOptions): UseTwoPanelLayoutResult;
//# sourceMappingURL=useTwoPanelLayout.d.ts.map