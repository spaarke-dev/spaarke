/**
 * usePanelResize Hook
 *
 * Manages draggable panel splitter state for the 2-panel AnalysisWorkspace layout.
 * Tracks mouse drag and keyboard resize, enforces minimum widths, and persists
 * the last split ratio to sessionStorage.
 *
 * @see ADR-021 - Fluent UI v9 design system
 */

import { useCallback, useEffect, useRef, useState } from "react";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Minimum width for the left (editor) panel in pixels */
const MIN_LEFT_WIDTH_PX = 300;

/** Minimum width for the right (source viewer) panel in pixels */
const MIN_RIGHT_WIDTH_PX = 200;

/** Width of the splitter grip area in pixels */
const SPLITTER_WIDTH_PX = 4;

/** Default split ratio — left panel as fraction of available width */
const DEFAULT_SPLIT_RATIO = 0.6;

/** Keyboard resize step in pixels */
const KEYBOARD_STEP_PX = 20;

/** sessionStorage key for persisted split ratio */
const STORAGE_KEY = "aw-panel-split-ratio";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UsePanelResizeOptions {
    /** Override the default split ratio (0-1) */
    defaultRatio?: number;
    /** Minimum left panel width in pixels */
    minLeftWidth?: number;
    /** Minimum right panel width in pixels */
    minRightWidth?: number;
    /** Whether the right panel is currently collapsed */
    isRightCollapsed?: boolean;
}

export interface UsePanelResizeResult {
    /** Current left panel width as a CSS value (e.g. "60%" or "524px") */
    leftPanelWidth: string;
    /** Current right panel width as a CSS value */
    rightPanelWidth: string;
    /** Whether the splitter is actively being dragged */
    isDragging: boolean;
    /** Ref to attach to the container element for measuring available width */
    containerRef: React.RefObject<HTMLDivElement | null>;
    /** Mouse down handler to attach to the splitter element */
    onSplitterMouseDown: (e: React.MouseEvent) => void;
    /** Key down handler to attach to the splitter element for keyboard resize */
    onSplitterKeyDown: (e: React.KeyboardEvent) => void;
    /** Reset to the default 60/40 split ratio */
    resetToDefault: () => void;
    /** Current ratio (0-1) representing left panel proportion */
    currentRatio: number;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function loadPersistedRatio(fallback: number): number {
    try {
        const stored = sessionStorage.getItem(STORAGE_KEY);
        if (stored !== null) {
            const parsed = parseFloat(stored);
            if (!isNaN(parsed) && parsed > 0 && parsed < 1) {
                return parsed;
            }
        }
    } catch {
        // sessionStorage may be unavailable in some contexts
    }
    return fallback;
}

function persistRatio(ratio: number): void {
    try {
        sessionStorage.setItem(STORAGE_KEY, ratio.toFixed(4));
    } catch {
        // Silently ignore storage errors
    }
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function usePanelResize(options: UsePanelResizeOptions = {}): UsePanelResizeResult {
    const {
        defaultRatio = DEFAULT_SPLIT_RATIO,
        minLeftWidth = MIN_LEFT_WIDTH_PX,
        minRightWidth = MIN_RIGHT_WIDTH_PX,
        isRightCollapsed = false,
    } = options;

    const containerRef = useRef<HTMLDivElement | null>(null);
    const isDraggingRef = useRef(false);
    const dragStartXRef = useRef(0);
    const dragStartRatioRef = useRef(0);

    const [ratio, setRatio] = useState(() => loadPersistedRatio(defaultRatio));
    const [isDragging, setIsDragging] = useState(false);

    // Clamp ratio so both panels respect their minimum widths
    const clampRatio = useCallback(
        (newRatio: number): number => {
            const container = containerRef.current;
            if (!container) return newRatio;

            const availableWidth = container.getBoundingClientRect().width - SPLITTER_WIDTH_PX;
            if (availableWidth <= 0) return newRatio;

            const minRatioLeft = minLeftWidth / availableWidth;
            const maxRatioLeft = 1 - minRightWidth / availableWidth;

            return Math.min(Math.max(newRatio, minRatioLeft), maxRatioLeft);
        },
        [minLeftWidth, minRightWidth],
    );

    // Update ratio and persist
    const updateRatio = useCallback(
        (newRatio: number) => {
            const clamped = clampRatio(newRatio);
            setRatio(clamped);
            persistRatio(clamped);
        },
        [clampRatio],
    );

    // ------------------------------------------------------------------
    // Mouse drag handlers
    // ------------------------------------------------------------------

    const handleMouseMove = useCallback(
        (e: MouseEvent) => {
            if (!isDraggingRef.current || !containerRef.current) return;

            const containerRect = containerRef.current.getBoundingClientRect();
            const availableWidth = containerRect.width - SPLITTER_WIDTH_PX;
            if (availableWidth <= 0) return;

            const deltaX = e.clientX - dragStartXRef.current;
            const deltaRatio = deltaX / availableWidth;
            const newRatio = dragStartRatioRef.current + deltaRatio;

            updateRatio(newRatio);
        },
        [updateRatio],
    );

    const handleMouseUp = useCallback(() => {
        isDraggingRef.current = false;
        setIsDragging(false);
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
    }, []);

    // Attach/detach document-level listeners when dragging starts/stops
    useEffect(() => {
        if (isDragging) {
            document.addEventListener("mousemove", handleMouseMove);
            document.addEventListener("mouseup", handleMouseUp);
        }
        return () => {
            document.removeEventListener("mousemove", handleMouseMove);
            document.removeEventListener("mouseup", handleMouseUp);
        };
    }, [isDragging, handleMouseMove, handleMouseUp]);

    const onSplitterMouseDown = useCallback(
        (e: React.MouseEvent) => {
            e.preventDefault();
            isDraggingRef.current = true;
            dragStartXRef.current = e.clientX;
            dragStartRatioRef.current = ratio;
            setIsDragging(true);
            document.body.style.cursor = "col-resize";
            document.body.style.userSelect = "none";
        },
        [ratio],
    );

    // ------------------------------------------------------------------
    // Keyboard resize
    // ------------------------------------------------------------------

    const onSplitterKeyDown = useCallback(
        (e: React.KeyboardEvent) => {
            const container = containerRef.current;
            if (!container) return;

            const availableWidth = container.getBoundingClientRect().width - SPLITTER_WIDTH_PX;
            if (availableWidth <= 0) return;

            let delta = 0;
            if (e.key === "ArrowLeft") {
                delta = -KEYBOARD_STEP_PX;
            } else if (e.key === "ArrowRight") {
                delta = KEYBOARD_STEP_PX;
            } else if (e.key === "Home") {
                // Jump to minimum left
                delta = -(availableWidth);
            } else if (e.key === "End") {
                // Jump to maximum left
                delta = availableWidth;
            } else {
                return; // Not a key we handle
            }

            e.preventDefault();
            const deltaRatio = delta / availableWidth;
            updateRatio(ratio + deltaRatio);
        },
        [ratio, updateRatio],
    );

    // ------------------------------------------------------------------
    // Reset to default
    // ------------------------------------------------------------------

    const resetToDefault = useCallback(() => {
        updateRatio(defaultRatio);
    }, [defaultRatio, updateRatio]);

    // ------------------------------------------------------------------
    // Computed CSS values
    // ------------------------------------------------------------------

    let leftPanelWidth: string;
    let rightPanelWidth: string;

    if (isRightCollapsed) {
        leftPanelWidth = "100%";
        rightPanelWidth = "0px";
    } else {
        // Use fr-style calc to avoid sub-pixel rounding issues
        const leftPct = (ratio * 100).toFixed(2);
        const rightPct = ((1 - ratio) * 100).toFixed(2);
        leftPanelWidth = `calc(${leftPct}% - ${SPLITTER_WIDTH_PX / 2}px)`;
        rightPanelWidth = `calc(${rightPct}% - ${SPLITTER_WIDTH_PX / 2}px)`;
    }

    return {
        leftPanelWidth,
        rightPanelWidth,
        isDragging,
        containerRef,
        onSplitterMouseDown,
        onSplitterKeyDown,
        resetToDefault,
        currentRatio: ratio,
    };
}
