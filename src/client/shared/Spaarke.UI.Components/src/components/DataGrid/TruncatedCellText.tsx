/**
 * TruncatedCellText — opt-in ellipsis + tooltip for a DataGrid text cell (`truncateWithTooltip`).
 *
 * The text is cut with an ellipsis when it does not fit the column. On hover, or when the enclosing gridcell receives
 * keyboard focus (the grid uses `focusMode="composite"`, so focus lands on the cell, not on this span), a Fluent
 * `Tooltip` shows the full text, but ONLY when the content actually overflows; a cell that fits shows no tooltip.
 * The full text is always in the DOM, so the tooltip is `relationship="inaccessible"` (screen readers already read
 * the whole value).
 *
 * `children` (optional) replaces the rendered content (the grid passes its primary-name link); `text` is the full
 * value shown in the tooltip.
 *
 * **ADR**: ADR-021 (Fluent v9 Tooltip, tokens), ADR-022 (useState / useRef / useCallback / useEffect only).
 */

import * as React from 'react';
import { Tooltip, makeStyles } from '@fluentui/react-components';

const useStyles = makeStyles({
  truncate: {
    display: 'block',
    minWidth: 0,
    maxWidth: '100%',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
});

/** Whether `el` (or its single child element, e.g. a link) is cut off horizontally. */
export function isHorizontallyOverflowing(el: HTMLElement | null): boolean {
  if (!el) return false;
  if (el.scrollWidth > el.clientWidth) return true;
  const child = el.firstElementChild as HTMLElement | null;
  return Boolean(child && child.scrollWidth > child.clientWidth);
}

export interface TruncatedCellTextProps {
  /** Full text of the cell (tooltip content). */
  text: string;
  /** Rendered content; defaults to `text`. */
  children?: React.ReactNode;
}

export const TruncatedCellText: React.FC<TruncatedCellTextProps> = ({ text, children }) => {
  const styles = useStyles();
  const ref = React.useRef<HTMLSpanElement>(null);
  // Two independent reasons to show: the pointer / trigger (Tooltip's own hover handling) and keyboard focus on the
  // enclosing gridcell.
  const [pointerVisible, setPointerVisible] = React.useState<boolean>(false);
  const [cellFocused, setCellFocused] = React.useState<boolean>(false);

  const onVisibleChange = React.useCallback((_ev: unknown, data: { visible: boolean }) => {
    setPointerVisible(data.visible ? isHorizontallyOverflowing(ref.current) : false);
  }, []);

  // `focusMode="composite"` puts keyboard focus on the gridcell, never on the inner span, so the Tooltip's own
  // focus trigger cannot fire. Listen on the cell instead (focusin also covers focus on a child such as the link).
  React.useEffect(() => {
    const cell = ref.current?.closest('[role="gridcell"]');
    if (!cell) return undefined;
    const onFocusIn = () => setCellFocused(isHorizontallyOverflowing(ref.current));
    const onFocusOut = (ev: Event) => {
      // Focus moving between the cell and its own child is still "in the cell".
      const next = (ev as FocusEvent).relatedTarget as Node | null;
      if (next && cell.contains(next)) return;
      setCellFocused(false);
    };
    cell.addEventListener('focusin', onFocusIn);
    cell.addEventListener('focusout', onFocusOut);
    return () => {
      cell.removeEventListener('focusin', onFocusIn);
      cell.removeEventListener('focusout', onFocusOut);
    };
  }, [text]);

  const content = children ?? text;
  if (!text) return <>{content}</>;

  return (
    <Tooltip
      content={text}
      relationship="inaccessible"
      visible={pointerVisible || cellFocused}
      onVisibleChange={onVisibleChange}
    >
      <span ref={ref} className={styles.truncate} data-truncate-cell="">
        {content}
      </span>
    </Tooltip>
  );
};
