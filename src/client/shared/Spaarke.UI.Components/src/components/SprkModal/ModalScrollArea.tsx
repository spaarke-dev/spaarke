import * as React from 'react';
import { Button, makeStyles, tokens } from '@fluentui/react-components';
import { ChevronUp20Regular, ChevronDown20Regular } from '@fluentui/react-icons';

/**
 * ModalScrollArea — the opt-in "arrow scroll" body variant (owner UAT 2026-07-31):
 * hide the traditional right scrollbar; surface up/down chevron affordances that
 * page the content on click.
 *
 * The chevrons are CO-LOCATED as a stacked pair at the bottom-centre (up above
 * down) so "scroll back up" is always one control away. Each greys out at its
 * boundary.
 *
 * IMPORTANT (accessibility): native scrolling is NOT disabled — wheel, trackpad,
 * keyboard (PageUp/Down, arrows, Space), and touch all still work. The chevrons
 * are an ADDITIONAL affordance, not a replacement (removing native scroll would
 * be a WCAG regression; see design §6.9). This is why `SprkModal`'s DEFAULT body
 * scroll is `native` (a plain thin scrollbar), and `arrows` is opt-in only.
 *
 * Layout: the scroll pane is `position:absolute; inset:0` inside a relative
 * `flex:1` root, so it always has a definite height. Overflow detection observes
 * BOTH the pane and the content, plus a double-rAF after mount, so the chevrons
 * appear reliably once content overflows.
 */
export interface ModalScrollAreaProps {
  padded?: boolean;
  children?: React.ReactNode;
}

const useStyles = makeStyles({
  root: { position: 'relative', flex: 1, minHeight: 0 },
  scroll: {
    position: 'absolute',
    inset: 0,
    overflowY: 'auto',
    scrollbarWidth: 'none',
    '::-webkit-scrollbar': { width: 0, height: 0 },
  },
  padded: { padding: tokens.spacingHorizontalL },
  stack: {
    position: 'absolute',
    left: '50%',
    transform: 'translateX(-50%)',
    bottom: tokens.spacingVerticalM,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    zIndex: 1,
  },
  chev: {
    minWidth: '36px',
    width: '36px',
    height: '36px',
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1, // high-contrast icon (override subtle's fg2)
    boxShadow: tokens.shadow28,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStrokeAccessible}`,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      color: tokens.colorNeutralForeground1,
    },
    ':hover:active': {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
      color: tokens.colorNeutralForeground1,
    },
  },
});

export const ModalScrollArea: React.FC<ModalScrollAreaProps> = ({ padded = true, children }) => {
  const styles = useStyles();
  const paneRef = React.useRef<HTMLDivElement>(null);
  const contentRef = React.useRef<HTMLDivElement>(null);
  const [canUp, setCanUp] = React.useState(false);
  const [canDown, setCanDown] = React.useState(false);

  const update = React.useCallback(() => {
    const el = paneRef.current;
    if (!el) return;
    setCanUp(el.scrollTop > 4);
    setCanDown(el.scrollTop + el.clientHeight < el.scrollHeight - 4);
  }, []);

  React.useEffect(() => {
    update();
    const r1 = requestAnimationFrame(() => requestAnimationFrame(update));
    let ro: ResizeObserver | undefined;
    if (typeof ResizeObserver !== 'undefined') {
      ro = new ResizeObserver(() => update());
      if (paneRef.current) ro.observe(paneRef.current);
      if (contentRef.current) ro.observe(contentRef.current);
    }
    return () => {
      cancelAnimationFrame(r1);
      ro?.disconnect();
    };
  }, [update, children]);

  const page = (dir: 1 | -1) => {
    const el = paneRef.current;
    if (!el) return;
    el.scrollBy({ top: dir * el.clientHeight * 0.85, behavior: 'smooth' });
  };

  const scrollable = canUp || canDown;

  return (
    <div className={styles.root}>
      <div ref={paneRef} className={styles.scroll} onScroll={update} tabIndex={0}>
        <div ref={contentRef} className={padded ? styles.padded : undefined}>
          {children}
        </div>
      </div>
      {scrollable && (
        <div className={styles.stack}>
          <Button
            className={styles.chev}
            appearance="subtle"
            icon={<ChevronUp20Regular />}
            aria-label="Scroll up"
            disabled={!canUp}
            onClick={() => page(-1)}
          />
          <Button
            className={styles.chev}
            appearance="subtle"
            icon={<ChevronDown20Regular />}
            aria-label="Scroll down"
            disabled={!canDown}
            onClick={() => page(1)}
          />
        </div>
      )}
    </div>
  );
};

export default ModalScrollArea;
