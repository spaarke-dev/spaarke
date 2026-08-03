import * as React from 'react';
import { Dialog, DialogSurface, Button, Tooltip, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { ChevronLeft20Regular, ChevronRight20Regular } from '@fluentui/react-icons';
import { ModalWindowControls } from '../ModalWindowControls/ModalWindowControls';
import { ModalScrollArea } from './ModalScrollArea';
import { getSurfaceStyle, SIZE_SPEC, type SprkModalSize, type SprkModalLayout } from './sizes';
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

  React.useEffect(() => {
    if (!open) setMaximized(false);
  }, [open]);

  const effectiveSize: SprkModalSize = maximized ? 'full' : size;
  const surfaceStyle = getSurfaceStyle(effectiveSize, uiScale);
  const effectiveLayout: SprkModalLayout = layout ?? SIZE_SPEC[size].layout;
  const modalType = dismiss === 'alert' ? 'alert' : 'modal';
  const hasFooter = Boolean(footer || footerStart);

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
        className={mergeClasses(styles.surface, effectiveSize === 'full' && styles.surfaceFull)}
        style={surfaceStyle}
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
                    onClick={() => nav.onNavigate('prev')}
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
                    onClick={() => nav.onNavigate('next')}
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
