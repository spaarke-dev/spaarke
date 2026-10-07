import React, { useState } from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

/**
 * ResizeHandle — the horizontal drag/keyboard handle shared by the Find split (FindSplitPane, task 102) and the
 * Profile's Summary viewport (DocumentProfileSection, task 105, UAT round 7 item 4). Extracted so the two share
 * ONE implementation (CLAUDE.md §11).
 *
 * An ARIA window splitter: `role="separator"`, `aria-orientation="horizontal"`, focusable. Pointer dragging uses
 * pointer capture (mouse, pen, touch; keeps tracking outside the thin handle). The handle owns only the
 * INTERACTION; what a position or a key means (a ratio, a pixel height, clamping, persistence) stays with the
 * host, which receives `onDragStart` / `onDrag` (clientY) and `onKeyAction`.
 *   ArrowUp -> 'decrease', ArrowDown -> 'increase', Home -> 'min', End -> 'max'.
 */

export const RESIZE_HANDLE_HEIGHT_PX = 10;

export type ResizeKeyAction = 'decrease' | 'increase' | 'min' | 'max';

const KEY_ACTIONS: Record<string, ResizeKeyAction> = {
  ArrowUp: 'decrease',
  ArrowDown: 'increase',
  Home: 'min',
  End: 'max',
};

const useStyles = makeStyles({
  handle: {
    position: 'relative',
    flexShrink: 0,
    flexGrow: 0,
    height: `${RESIZE_HANDLE_HEIGHT_PX}px`,
    cursor: 'row-resize',
    touchAction: 'none',
    outlineStyle: 'none',
    '::after': {
      content: '""',
      position: 'absolute',
      top: '50%',
      left: '50%',
      width: '40px',
      height: '3px',
      transform: 'translate(-50%, -50%)',
      borderRadius: tokens.borderRadiusMedium,
      backgroundColor: tokens.colorNeutralStroke1,
    },
    ':hover::after': { backgroundColor: tokens.colorNeutralStroke1Hover },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: tokens.strokeWidthThick,
      outlineColor: tokens.colorStrokeFocus2,
      borderRadius: tokens.borderRadiusMedium,
    },
  },
  active: {
    '::after': { backgroundColor: tokens.colorBrandStroke1 },
  },
});

export interface ResizeHandleProps {
  label: string;
  valueNow: number;
  valueMin: number;
  valueMax: number;
  /** Called once when a drag begins, with the pointer's clientY. */
  onDragStart?: (clientY: number) => void;
  /** Called on every pointer move during a drag, with the pointer's clientY. */
  onDrag: (clientY: number) => void;
  onKeyAction: (action: ResizeKeyAction) => void;
  testId?: string;
}

export const ResizeHandle: React.FC<ResizeHandleProps> = ({
  label,
  valueNow,
  valueMin,
  valueMax,
  onDragStart,
  onDrag,
  onKeyAction,
  testId,
}) => {
  const styles = useStyles();
  const [dragging, setDragging] = useState(false);

  const onPointerDown = (event: React.PointerEvent<HTMLDivElement>) => {
    if (event.button !== undefined && event.button !== 0) return;
    event.currentTarget.setPointerCapture?.(event.pointerId);
    setDragging(true);
    onDragStart?.(event.clientY);
  };

  const onPointerMove = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!dragging) return;
    onDrag(event.clientY);
  };

  const endDrag = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!dragging) return;
    event.currentTarget.releasePointerCapture?.(event.pointerId);
    setDragging(false);
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const action = KEY_ACTIONS[event.key];
    if (!action) return;
    onKeyAction(action);
    event.preventDefault();
  };

  return (
    <div
      role="separator"
      aria-orientation="horizontal"
      aria-label={label}
      aria-valuenow={valueNow}
      aria-valuemin={valueMin}
      aria-valuemax={valueMax}
      tabIndex={0}
      className={mergeClasses(styles.handle, dragging && styles.active)}
      data-testid={testId}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onKeyDown={onKeyDown}
    />
  );
};

export default ResizeHandle;
