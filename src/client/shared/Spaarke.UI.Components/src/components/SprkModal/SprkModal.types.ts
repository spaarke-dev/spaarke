import type { ReactNode } from 'react';
import type { SprkModalSize, SprkModalLayout } from './sizes';

/** How the modal may be dismissed (design §6.7). */
export type SprkModalDismiss = 'light' | 'explicit' | 'alert';

/** Body scroll affordance: native thin scrollbar (default) or opt-in chevron pager. */
export type SprkModalBodyScroll = 'native' | 'arrows';

/** Browse ("N of M") navigation contract for the header nav group. */
export interface SprkModalNav {
  /** 0-based index of the current record. */
  index: number;
  /** Total record count. */
  total: number;
  /** Navigate to the previous/next record. */
  onNavigate: (dir: 'prev' | 'next') => void;
}

/**
 * Public contract for the `SprkModal` shell. A consumer supplies content + intent
 * (title, size, footer slots, children); the shell supplies ALL chrome (envelope,
 * header, window controls, body, footer).
 */
export interface SprkModalProps {
  /** Whether the modal is open. */
  open: boolean;
  /** Close callback — the × always; backdrop/ESC only when `dismiss==='light'`. */
  onClose: () => void;
  /** Header title (ellipsized; announced). */
  title: string;
  /** Named size (default `md`). */
  size?: SprkModalSize;
  /** Layout override (default: the size's natural layout). */
  layout?: SprkModalLayout;
  /** Dismiss semantics (default `light`). */
  dismiss?: SprkModalDismiss;
  /**
   * When `true`, the shell renders as a Fluent `modalType="non-modal"` surface —
   * NO backdrop scrim and NO focus trap — so a page-level overlay opened on top
   * of it (e.g. the native Dataverse `Xrm.Utility.lookupObjects` advanced-lookup
   * pane) stays fully interactive instead of being covered/click-swallowed by the
   * modal backdrop. This is the proven pattern the CommunicationActions composer
   * uses so its record/recipient lookups work from inside a dialog. Default
   * `false` (standard modal with backdrop). Ignored when `dismiss==='alert'`
   * (an alert is intentionally blocking). Trade-off: a non-blocking modal does
   * not dim the background — use only when a host-level lookup pane must overlay it.
   */
  nonBlocking?: boolean;
  /** The `--sprk-ui-scale` factor for sizing (default 1). */
  uiScale?: number;
  /** Whether the maximize/restore control is shown (default true). */
  maximizable?: boolean;
  /** Optional browse navigation ("N of M") in the header-left. */
  nav?: SprkModalNav;
  /** Optional header-right actions (rendered before the window controls). */
  headerActions?: ReactNode;
  /** Left-aligned footer slot — the STANDARD home for Cancel (owner UAT 2026-07-31). */
  footerStart?: ReactNode;
  /** Right-aligned footer slot — the navigation/primary actions. */
  footer?: ReactNode;
  /** Whether the body has standard padding (default true). */
  padded?: boolean;
  /** Body scroll mode (default `native`). */
  bodyScroll?: SprkModalBodyScroll;
  /** Modal content. */
  children?: ReactNode;
}
