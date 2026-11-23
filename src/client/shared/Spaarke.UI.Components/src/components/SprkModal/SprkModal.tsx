import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  Button,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
  useFluent,
} from '@fluentui/react-components';
import { ChevronLeft20Regular, ChevronRight20Regular } from '@fluentui/react-icons';
import { ModalWindowControls } from '../ModalWindowControls/ModalWindowControls';
import { ModalScrollArea } from './ModalScrollArea';
import { getSurfaceStyle, SIZE_SPEC, type SprkModalSize, type SprkModalLayout } from './sizes';
import { useSidePaneLayering } from './sidePaneLayering';
import { scheduleFocusReturn } from '../../utils/focusReturn';
import type { SprkModalProps } from './SprkModal.types';

export type { SprkModalDismiss, SprkModalBodyScroll, SprkModalNav, SprkModalProps } from './SprkModal.types';

/**
 * SprkModal — the canonical Spaarke modal shell (spec FR-01/03/04/05/07/08). Owns
 * the Fluent v9 `Dialog`/`DialogSurface` envelope + a standard header (optional
 * `‹ N of M ›` nav · ellipsized title · `headerActions` · `ModalWindowControls`)
 * + a native thin-scrollbar body + a standard footer (Cancel-left / actions-right).
 * Parameterized by a NAMED size + layout; a consumer supplies content + intent only.
 *
 * Composes the already-built primitives: `getSurfaceStyle` (sizing), the reconciled
 * `ModalWindowControls` (FullScreen glyph), and `ModalScrollArea` (opt-in chevrons).
 * Renders NO `FluentProvider` — it inherits the host provider, and therefore the
 * scaled theme (`--sprk-ui-scale`) the host installs (design §6.9). ADR-021 semantic
 * tokens only: zero hex, zero `'1px'` literals (`tokens.strokeWidthThin`), zero
 * inline color styles.
 *
 * Centering survives a CSS-transformed ancestor because the Fluent `Dialog` portal
 * mounts above the transform — the invariant the whole modal system rests on.
 *
 * design.md §6.1 (architecture) · §6.2 (sizes) · §6.4 (header) · §6.5 (footer)
 * · §6.7 (dismiss) · §6.9 (scale).
 */
// Monotonic id source for the title's `aria-labelledby` wiring. NOT `React.useId`
// — that is React 18+; this shell must stay React-16/17-safe (NFR-04).
let sprkModalTitleIdCounter = 0;

// Task 202 follow-up (modal swap handover). A host sometimes replaces one SprkModal with a different one in the same
// place (RuleWizard: a "loading" modal until its catalog arrives, then the wizard's own modal). The second modal opens
// while focus sits on the first one's Close button, so that button, not the control that opened the first, is what it
// captures; at close the button is long gone and focus would be lost. Each open SprkModal therefore records, against its
// surface element, the chain of elements to try at close: what had focus when it opened, then (when that element lay
// inside another open SprkModal's surface) that modal's own chain, which a swap of a swap extends again. Module-level
// and internal on purpose: no prop, no export.
interface OpenerCandidate {
  el: HTMLElement;
  /** Surfaces that must all be gone before `el` may be used: an inherited opener belongs behind a modal that may still be open. */
  goneSurfaces: HTMLElement[];
}
interface ModalFocusRecord {
  surface: HTMLElement | null;
  chain: OpenerCandidate[];
}
const modalFocusRecords = new WeakMap<HTMLElement, ModalFocusRecord>();

/** The chain a modal opening now should keep: `focused` first, then the chain of the SprkModal surface it sits in, if any. */
function openerChainOf(focused: HTMLElement | null): OpenerCandidate[] {
  if (!focused) return [];
  let parent: ModalFocusRecord | undefined;
  for (let node: HTMLElement | null = focused; node && !parent; node = node.parentElement) {
    parent = modalFocusRecords.get(node);
  }
  const inherited =
    parent && parent.surface
      ? parent.chain.map(c => ({ el: c.el, goneSurfaces: [...c.goneSurfaces, parent!.surface as HTMLElement] }))
      : [];
  return [{ el: focused, goneSurfaces: [] }, ...inherited];
}

/** The first element of the chain that may take focus now: connected, enabled, not inert, and its holding modals gone. */
function firstUsableOpener(chain: OpenerCandidate[]): HTMLElement | null {
  const usable = chain.find(
    ({ el, goneSurfaces }) =>
      el.isConnected &&
      !el.closest('[inert]') &&
      !(el as HTMLButtonElement).disabled &&
      goneSurfaces.every(surface => !surface.isConnected)
  );
  return usable ? usable.el : null;
}

// Room left at the right edge for the platform's lookup side pane while
// `yieldToSidePane` is on: at least the pane's width, more on wide windows.
const SIDE_PANE_CLEARANCE = 'max(440px, 34vw)';
// Dimmed and click-through while yielding, wherever the modal sits.
const SIDE_PANE_DIM_STYLE: React.CSSProperties = {
  // `filter`, not `opacity`: newer Fluent animates the surface's opacity with a persisted Web Animation, which
  // overrides an inline opacity. Brightness, not opacity: the surface stays solid (owner test 2026-10-07 — a
  // see-through modal let the form's text show through it). Works in light and dark themes. Pointer input is
  // blocked here; keyboard focus is blocked by `inert` (see below).
  filter: 'brightness(0.75)',
  pointerEvents: 'none',
};
// The fallback when the lookup pane cannot be placed above the modal: dock left of it.
const SIDE_PANE_DOCK_STYLE: React.CSSProperties = {
  ...SIDE_PANE_DIM_STYLE,
  marginLeft: 'auto',
  marginRight: SIDE_PANE_CLEARANCE,
  maxWidth: `calc(100vw - ${SIDE_PANE_CLEARANCE} - 16px)`,
};

const useStyles = makeStyles({
  surface: {
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    borderRadius: tokens.borderRadiusXLarge,
  },
  surfaceFull: { borderRadius: 0 },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalL,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  headerLeft: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
  },
  headerRight: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  navGroup: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    flexShrink: 0,
  },
  counter: {
    fontVariantNumeric: 'tabular-nums',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    whiteSpace: 'nowrap',
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    minWidth: 0,
  },
  body: {
    flex: 1,
    minHeight: 0,
    overflowY: 'auto',
    // Modern thin scrollbar (the Fluent 2 / Win11 look) — the STANDARD. Thin,
    // rounded, inset thumb; darkens on hover; reserves gutter so content doesn't
    // shift when the bar appears.
    scrollbarGutter: 'stable',
    scrollbarWidth: 'thin',
    scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
    '::-webkit-scrollbar': { width: '10px', height: '10px' },
    '::-webkit-scrollbar-thumb': {
      backgroundColor: tokens.colorNeutralStroke1,
      borderRadius: tokens.borderRadiusCircular,
      border: '2px solid transparent',
      backgroundClip: 'content-box',
    },
    '::-webkit-scrollbar-thumb:hover': { backgroundColor: tokens.colorNeutralStroke1Hover },
    '::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
  },
  bodyPadded: { padding: tokens.spacingHorizontalL },
  footer: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalL,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  footerBetween: { justifyContent: 'space-between' },
  footerEnd: { justifyContent: 'flex-end' },
  footerSlot: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
});

export const SprkModal: React.FC<SprkModalProps> = ({
  open,
  onClose,
  title,
  size = 'md',
  layout,
  dismiss = 'light',
  nonBlocking = false,
  hidden = false,
  yieldToSidePane = false,
  uiScale = 1,
  maximizable = true,
  nav,
  headerActions,
  footerStart,
  footer,
  padded = true,
  bodyScroll = 'native',
  children,
}) => {
  const styles = useStyles();
  const [maximized, setMaximized] = React.useState(false);
  // Stable per-instance title id so the surface announces its accessible name
  // (aria-labelledby) — we render a custom header, not Fluent's DialogTitle,
  // so the auto-wiring DialogTitle provides must be supplied here explicitly.
  const [titleId] = React.useState(() => `sprk-modal-title-${++sprkModalTitleIdCounter}`);
  const surfaceRef = React.useRef<HTMLDivElement | null>(null);
  // The last surface element this modal rendered. `surfaceRef` is null by the time the close effect runs (the surface
  // unmounts in the same commit), and "focus is inside the closing surface" needs the element. A callback ref keeps both.
  const lastSurfaceRef = React.useRef<HTMLDivElement | null>(null);
  // This modal's entry in `modalFocusRecords`: what a modal opened from inside this one inherits (see openerChainOf).
  const recordRef = React.useRef<ModalFocusRecord>({ surface: null, chain: [] });
  const setSurfaceRef = React.useCallback((node: HTMLDivElement | null) => {
    surfaceRef.current = node;
    if (node) {
      lastSurfaceRef.current = node;
      recordRef.current.surface = node;
      modalFocusRecords.set(node, recordRef.current);
    }
  }, []);

  // Task 202: a SprkModal that goes open -> closed returns keyboard focus to the element that had it when the modal
  // opened. Fluent's Dialog only restores to a `DialogTrigger`; hosts open a SprkModal from state, so nothing did.
  const { targetDocument } = useFluent();
  const openerChainRef = React.useRef<OpenerCandidate[]>([]);
  const wasOpenRef = React.useRef(false);
  const cancelRestoreRef = React.useRef<(() => void) | null>(null);

  // CAPTURE, in render. The element that has focus on the render that flips `open` to true is the one the click (or key
  // press) that opened us left it on. Reading it here, not in an effect, is what puts it before anything in this commit
  // moves focus: the content mounts in the same commit as this component (the portal does not defer it), so a child's
  // layout effect or an `autoFocus` input has already focused itself by the time a layout effect of ours runs, and
  // Fluent's `useFocusFirstElement` (a passive effect) comes after both. The read has no side effect; it is committed to
  // `openerRef` by the layout effect below. Pinned by focusRestore.test.tsx ("captures the opener before ...").
  const openerCandidateRef = React.useRef<OpenerCandidate[]>([]);
  if (open && !wasOpenRef.current) {
    const doc = targetDocument ?? document;
    const focused = doc.activeElement as HTMLElement | null;
    openerCandidateRef.current = openerChainOf(
      focused && focused !== doc.body && focused !== doc.documentElement ? focused : null
    );
  }
  // RESTORE. Only on a real open -> closed transition (`open` false, or this component unmounting while open):
  // `hidden` and `yieldToSidePane` keep `open` true and never reach it (task 198).
  const restoreFocusToOpener = React.useCallback(() => {
    const chain = openerChainRef.current;
    const surface = lastSurfaceRef.current;
    openerChainRef.current = [];
    recordRef.current.chain = [];
    wasOpenRef.current = false;
    if (chain.length === 0) return;
    const doc = chain[0].el.ownerDocument;
    cancelRestoreRef.current?.();
    cancelRestoreRef.current = scheduleFocusReturn({
      doc,
      // Evaluated at each attempt (the opener may re-render); the opener itself, else the one the modal it replaced
      // had (see openerChainOf). Never one that is disabled, detached or inert.
      target: () => firstUsableOpener(chain),
      // Focus is "lost" only on body/nothing, on a node inside this closing surface, or on a removed node. Anywhere else
      // (another dialog the host focused on purpose, the parent wizard) is kept, so a host that moved focus wins.
      isKept: active =>
        !!active &&
        active !== doc.body &&
        active !== doc.documentElement &&
        active.isConnected &&
        !(surface && surface.contains(active)),
    });
  }, []);

  React.useLayoutEffect(() => {
    if (open) {
      cancelRestoreRef.current?.(); // reopened before a pending restore ran
      cancelRestoreRef.current = null;
      if (!wasOpenRef.current) {
        wasOpenRef.current = true;
        openerChainRef.current = openerCandidateRef.current;
        recordRef.current.chain = openerCandidateRef.current;
      }
    } else if (wasOpenRef.current) {
      restoreFocusToOpener();
    }
  }, [open, restoreFocusToOpener]);
  React.useLayoutEffect(
    () => () => {
      if (wasOpenRef.current) restoreFocusToOpener();
    },
    [restoreFocusToOpener]
  );

  // While yielding to a side pane the surface is `inert`: Tab and Enter cannot reach Save / Cancel / × behind the
  // lookup (pointer-events alone blocks only the mouse). Set as a DOM attribute — React 16/17 do not know `inert`.
  const surfaceInert = yieldToSidePane && !hidden;
  // ORDER MATTERS (task 198): keep this a `useEffect` in this (parent) component. Fluent's Dialog re-runs its
  // focus-first-element effect when `modalType` changes; as a child effect it runs BEFORE this one, so when yielding
  // ends the surface is still `inert` and that focus() is a no-op. Removing `inert` first (e.g. a useLayoutEffect)
  // would pull focus into this modal, under the host's wizard. Pinned by sidePaneLayering.test.tsx.
  React.useEffect(() => {
    const el = surfaceRef.current;
    if (!el) return;
    if (surfaceInert) el.setAttribute('inert', '');
    else el.removeAttribute('inert');
  }, [surfaceInert, open]);
  // Owner test 2026-10-07: the lookup pane opens ON TOP of the modal when it can be layered above it; else dock left.
  const layering = useSidePaneLayering(surfaceInert && open, surfaceRef);

  React.useEffect(() => {
    if (!open) setMaximized(false);
  }, [open]);

  const effectiveSize: SprkModalSize = maximized ? 'full' : size;
  const surfaceStyle: React.CSSProperties = getSurfaceStyle(effectiveSize, uiScale);
  const effectiveLayout: SprkModalLayout = layout ?? SIZE_SPEC[size].layout;
  // `alert` is intentionally blocking; otherwise `nonBlocking` maps to Fluent's
  // `non-modal` (no backdrop, no focus trap) so a page-level lookup pane opened
  // over the modal stays interactive (see `nonBlocking` prop doc).
  // While yielding to a side pane EVERY modal is `non-modal`, alert included (task 198): the `modal` Dialog's
  // full-window `.fui-DialogSurface__backdrop` is not covered by the surface's `pointer-events: none`, and (z-index
  // of the PCF portal) it sits above the platform's lookup pane when the pane could not be layered over the modal
  // ('dock' / 'pending'), eating every click in it. Only `modalType` changes: the Dialog, its surface and the body
  // stay mounted (the backdrop is a sibling of the surface in the same portal), and the blocking behaviour returns
  // when the flag clears.
  const modalType = surfaceInert ? 'non-modal' : dismiss === 'alert' ? 'alert' : nonBlocking ? 'non-modal' : 'modal';
  const hasFooter = Boolean(footer || footerStart);

  // Browse guard (`nav.onBeforeNavigate`): without one, navigate synchronously; with one, navigate only
  // when it allows it. A throwing / rejecting guard blocks the move (fail closed) and is reported with a
  // console.warn, so a broken guard shows up as a diagnosable failure rather than a dead ‹ › button.
  const handleNavigate = (dir: 'prev' | 'next') => {
    if (!nav) return;
    const guard = nav.onBeforeNavigate;
    if (!guard) {
      nav.onNavigate(dir);
      return;
    }
    const reportGuardFailure = (err: unknown) =>
      console.warn(`[SprkModal] nav.onBeforeNavigate failed for '${dir}'; navigation blocked.`, err);
    let verdict: boolean | Promise<boolean>;
    try {
      verdict = guard(dir);
    } catch (err) {
      reportGuardFailure(err);
      return;
    }
    void Promise.resolve(verdict).then(allowed => {
      if (allowed) nav.onNavigate(dir);
    }, reportGuardFailure);
  };

  return (
    <Dialog
      open={open}
      modalType={modalType}
      onOpenChange={(_, data) => {
        // Only `light` dismiss closes on backdrop/ESC; `explicit`/`alert` require
        // the × or a footer action.
        if (!data.open && dismiss === 'light') onClose();
      }}
    >
      <DialogSurface
        ref={setSurfaceRef}
        className={mergeClasses(styles.surface, effectiveSize === 'full' && styles.surfaceFull)}
        // `hidden` keeps the surface mounted (state preserved) but out of the way
        // of a page-level native lookup pane; `yieldToSidePane` keeps it visible
        // and dimmed, under the pane or docked left of it — see the two prop docs.
        style={
          hidden
            ? { ...surfaceStyle, visibility: 'hidden', pointerEvents: 'none' }
            : yieldToSidePane
              ? { ...surfaceStyle, ...(layering === 'dock' ? SIDE_PANE_DOCK_STYLE : SIDE_PANE_DIM_STYLE) }
              : surfaceStyle
        }
        aria-labelledby={titleId}
      >
        <div className={styles.header}>
          <div className={styles.headerLeft}>
            {nav && (
              <div className={styles.navGroup} role="group" aria-label="Record navigation">
                <Tooltip content="Previous" relationship="label">
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<ChevronLeft20Regular />}
                    disabled={nav.index <= 0}
                    onClick={() => handleNavigate('prev')}
                    aria-label="Previous record"
                  />
                </Tooltip>
                <span className={styles.counter} aria-live="polite">
                  {nav.total > 0 ? `${nav.index + 1} of ${nav.total}` : '0 of 0'}
                </span>
                <Tooltip content="Next" relationship="label">
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<ChevronRight20Regular />}
                    disabled={nav.index >= nav.total - 1}
                    onClick={() => handleNavigate('next')}
                    aria-label="Next record"
                  />
                </Tooltip>
              </div>
            )}
            <span className={styles.title} title={title} id={titleId}>
              {title}
            </span>
          </div>
          <div className={styles.headerRight}>
            {headerActions}
            <ModalWindowControls
              isMaximized={maximized}
              onToggleMaximize={maximizable ? () => setMaximized(m => !m) : undefined}
              onClose={onClose}
            />
          </div>
        </div>

        {bodyScroll === 'arrows' ? (
          <ModalScrollArea padded={padded}>{children}</ModalScrollArea>
        ) : (
          <div className={mergeClasses(styles.body, padded && styles.bodyPadded)} data-layout={effectiveLayout}>
            {children}
          </div>
        )}

        {hasFooter && (
          <div className={mergeClasses(styles.footer, footerStart ? styles.footerBetween : styles.footerEnd)}>
            {footerStart && <div className={styles.footerSlot}>{footerStart}</div>}
            {footer && <div className={styles.footerSlot}>{footer}</div>}
          </div>
        )}
      </DialogSurface>
    </Dialog>
  );
};

export default SprkModal;
