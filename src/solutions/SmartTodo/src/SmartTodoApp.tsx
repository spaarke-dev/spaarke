/**
 * SmartTodoApp — Main layout component for the SmartTodo Code Page.
 *
 * Integrates the Kanban board (left panel) with a collapsible TodoDetailPanel
 * (right panel) separated by a draggable PanelSplitter. The detail panel
 * automatically opens when a to-do item is selected and closes when deselected.
 *
 * Layout:
 *   TodoProvider (shared state)
 *     ├── Left panel  — SmartToDo (Kanban board)
 *     ├── PanelSplitter (draggable, keyboard-accessible)
 *     └── Right panel — TodoDetailPanel (collapsible)
 *
 * Panel behaviour:
 *   - Default detail width: 400px (persisted via localStorage)
 *   - Panel open/close animation < 200ms (NFR-01)
 *   - Splitter supports mouse drag, keyboard (Arrow keys), and double-click reset
 *   - Panel state persisted to localStorage under 'smarttodo-panel-layout'
 *
 * @see ADR-012 - PanelSplitter and useTwoPanelLayout from @spaarke/ui-components
 * @see ADR-021 - Fluent UI v9 design system (makeStyles + tokens only)
 */

import * as React from "react";
import { makeStyles, mergeClasses, tokens } from "@fluentui/react-components";
import { PanelSplitter } from "@spaarke/ui-components/PanelSplitter";
import { useTwoPanelLayout } from "@spaarke/ui-components/hooks";
import { TodoProvider, useTodoContext } from "./context/TodoContext";
import { SmartToDo } from "./components/SmartToDo";
import { TodoDetailPanel } from "./components/TodoDetailPanel";
import { getWebApi, getUserId } from "./services/xrmProvider";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  primaryPanel: {
    overflow: "auto",
    minWidth: 0,
  },
  detailPanel: {
    overflow: "hidden",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
  },
  /** Smooth collapse/expand animation for panel toggle transitions.
   *  Applied only when NOT dragging to avoid janky animation during resize. */
  detailPanelAnimated: {
    transitionProperty: "width",
    transitionDuration: "150ms", // NFR-01: < 200ms
    transitionTimingFunction: tokens.curveEasyEase,
    "@media (prefers-reduced-motion: reduce)": {
      transitionDuration: "0ms",
    },
  },
});

// ---------------------------------------------------------------------------
// Inner layout (needs TodoContext access)
// ---------------------------------------------------------------------------

function SmartTodoLayout(): React.ReactElement {
  const styles = useStyles();
  const { selectedEventId } = useTodoContext();

  const {
    primaryWidth,
    detailWidth,
    isDetailVisible,
    showDetail,
    hideDetail,
    splitterHandlers,
    isDragging,
    containerRef,
    currentRatio,
  } = useTwoPanelLayout({
    defaultDetailWidth: 400,
    storageKey: "smarttodo-panel-layout",
  });

  // Wire selectedEventId to panel visibility:
  // open when an item is selected, close when deselected.
  // Use refs to avoid infinite loop — showDetail/hideDetail change refs
  // when isDetailVisible changes, which would re-trigger this effect.
  const showDetailRef = React.useRef(showDetail);
  const hideDetailRef = React.useRef(hideDetail);
  showDetailRef.current = showDetail;
  hideDetailRef.current = hideDetail;

  React.useEffect(() => {
    if (selectedEventId !== null) {
      showDetailRef.current();
    } else {
      hideDetailRef.current();
    }
  }, [selectedEventId]);

  return (
    <div ref={containerRef} className={styles.container}>
      {/* Left panel — Kanban Board */}
      <div className={styles.primaryPanel} style={{ width: primaryWidth }}>
        <SmartToDo webApi={getWebApi()} userId={getUserId()} />
      </div>

      {/* Splitter + Detail Panel (only when visible) */}
      {isDetailVisible && (
        <>
          <PanelSplitter
            onMouseDown={splitterHandlers.onMouseDown}
            onKeyDown={splitterHandlers.onKeyDown}
            onDoubleClick={splitterHandlers.onDoubleClick}
            isDragging={isDragging}
            currentRatio={currentRatio}
          />
          <div
            className={mergeClasses(
              styles.detailPanel,
              !isDragging && styles.detailPanelAnimated,
            )}
            style={{ width: detailWidth }}
          >
            <TodoDetailPanel />
          </div>
        </>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Exported component (wraps in TodoProvider)
// ---------------------------------------------------------------------------

export function SmartTodoApp(): React.ReactElement {
  return (
    <TodoProvider>
      <SmartTodoLayout />
    </TodoProvider>
  );
}
