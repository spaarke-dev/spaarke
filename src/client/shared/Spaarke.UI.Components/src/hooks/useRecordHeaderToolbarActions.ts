/**
 * useRecordHeaderToolbarActions — LINCHPIN toolbar-actions hook for the
 * record-header composition surface.
 *
 * FR-07/08/08a/09/10/11 (record-header-and-notepad-r1): consumed by every
 * per-entity RecordHeader PCF (`MatterHeaderPcf` today; `ProjectHeaderPcf`,
 * `InvoiceHeaderPcf`, `EventHeaderPcf` next) to produce a fully-wired
 * `IHeaderToolbarProps` with three canonical icon slots — sparkle (AI summary
 * popover), checkmark (SmartTodo modal launcher), annotation (Notepad modal
 * launcher). Per-entity PCFs MUST consume this hook and MUST NOT re-implement
 * the three-slot wiring themselves (spec MUST rule, CLAUDE.md §11
 * "extend-existing-not-duplicate" rule, task-012 POML §justification).
 *
 * Behavior summary (per FR-08 REVISED):
 *  - **Sparkle** — no `Xrm.Navigation` call. Toggles an internal open state that
 *    the consumer wires to a Fluent v9 `<Popover>`; the hook returns the
 *    `PopoverSurface`-ready content (summary body OR empty state, plus the
 *    unwired refresh icon per FR-08a). Consumers own the `<Popover>` +
 *    `<PopoverTrigger>` shell so the button-anchor relationship is preserved.
 *  - **Checkmark** — `Xrm.Navigation.navigateTo` opens the SmartTodo code page
 *    (Layout 1 85%×85%). Badge = live `sprk_todo` count for this record.
 *  - **Annotation** — `Xrm.Navigation.navigateTo` opens the Notepad code page
 *    (70%×80% specialized editor modal). Badge = live `sprk_memo` count for
 *    this record (entity-specific ADR-024 lookup filter via
 *    `buildMemoFilterForParent`).
 *
 * Boundary constraints (project NFRs + repo ADRs):
 *  - Host-context surface. Uses `Xrm.Navigation.navigateTo` + `Xrm.WebApi`
 *    (via `useRelatedCount`) only. **Zero** `@spaarke/auth`, zero BFF (NFR-05,
 *    NFR-07; ADR-028 host-context boundary).
 *  - Refresh icon rendered but **UNWIRED** in R1 (FR-08a, NFR-07). Click is a
 *    no-op. Tooltip states the deferral. Any wiring change requires a follow-on
 *    BFF endpoint — out of R1 scope.
 *  - React 16/17 compatible: `React.useState` / `React.useCallback` /
 *    `React.useMemo` only. No `use()`, no `useSyncExternalStore`, no React 18
 *    exclusive APIs (spec NFR-06; ADR-022).
 *  - Fluent v9 semantic tokens only in the returned popover content — zero
 *    hex / rgb / hsl literals (ADR-021; spec NFR-03).
 *  - Notepad launch-contract URL params (`regardingEntity`, `regardingId`)
 *    are external API surface — do not rename (NFR-09).
 *
 * File is `.ts` (not `.tsx`) — element trees are built with
 * `React.createElement` so the module conforms to the `hooks/` folder
 * convention (all other hooks in this folder are `.ts`). Prefer this over
 * introducing a lone `.tsx` under `hooks/`.
 *
 * @see FR-07 / FR-08 / FR-08a / FR-09 / FR-10 / FR-11 in
 *      `projects/record-header-and-notepad-r1/spec.md`
 * @see `.claude/adr/ADR-021-fluent-ui-design-system.md`
 * @see `.claude/adr/ADR-028-spaarke-auth-architecture.md`
 * @see `docs/standards/MODAL-DECISION-CRITERIA.md`
 * @see `./toolbarLaunchDefaults`
 * @see `./useRelatedCount`
 */

import * as React from 'react';
import {
  Button,
  Text,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  ArrowClockwise20Regular,
  Note24Regular,
  Sparkle24Regular,
  Checkmark24Regular,
} from '@fluentui/react-icons';

import type { IHeaderToolbarProps, IHeaderToolbarSlot } from '../components/HeaderToolbar/types';
import { getXrm } from '../utils/xrmContext';
import { useRelatedCount } from './useRelatedCount';
import {
  LAYOUT_1_MODAL,
  NOTEPAD_MODAL,
  NOTEPAD_WEBRESOURCE_NAME,
  SMARTTODO_WEBRESOURCE_NAME,
  buildMemoFilterForParent,
} from './toolbarLaunchDefaults';

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Deferral tooltip copy for the unwired refresh icon (FR-08a; U-04 default).
 * If reviewer wants different copy, change this in one place.
 */
const REFRESH_DEFERRAL_TOOLTIP =
  'Refresh available in a follow-on release';

/**
 * Empty-state copy for the sparkle popover when `recordSummary` is null / empty
 * (FR-08 REVISED; U-03 default).
 */
const SPARKLE_EMPTY_STATE = 'No summary yet';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Which toolbar slots to emit. Every flag defaults to `true`. Setting a flag
 * to `false` REMOVES that slot from `toolbarProps.iconSlots` entirely — it is
 * not merely hidden. Per-entity PCFs use this to opt out of surfaces that
 * do not apply to their entity (e.g., a lightweight lookup entity that has
 * no summary field can disable sparkle).
 */
export interface IUseRecordHeaderToolbarActionsEnabled {
  /** Emit the sparkle (AI summary popover) slot. Default `true`. */
  sparkle?: boolean;
  /** Emit the checkmark (SmartTodo launcher) slot. Default `true`. */
  checkmark?: boolean;
  /** Emit the annotation (Notepad launcher) slot. Default `true`. */
  annotation?: boolean;
}

/**
 * Hook options.
 *
 * `recordSummary` is the value of the parent record's `sprk_recordsummary`
 * field, already fetched by the consumer via `useRecordFieldValues`. Passing
 * it in avoids a second `Xrm.WebApi.retrieveRecord` call inside this hook
 * (per task-001 correction — see `notes/design-alignment-corrections.md`).
 * `undefined` / `null` / `""` all render the empty-state message.
 */
export interface IUseRecordHeaderToolbarActionsOptions {
  /** Parent entity logical name (e.g., `"sprk_matter"`). */
  entity: string;
  /** Parent record GUID (no braces). */
  recordId: string;
  /** Pre-fetched `sprk_recordsummary` field value. Optional. */
  recordSummary?: string | null;
  /** Which slots to emit. Every flag defaults to `true`. */
  enabled?: IUseRecordHeaderToolbarActionsEnabled;
}

/**
 * Hook result.
 *
 * `toolbarProps` is drop-in for `<HeaderToolbar {...toolbarProps} />` (via
 * `RecordHeaderShell`'s `toolbar` prop).
 *
 * `sparklePopoverOpen` / `setSparklePopoverOpen` are controlled Fluent v9
 * `<Popover>` state. The consumer wires them like:
 * ```tsx
 * <Popover open={sparklePopoverOpen} onOpenChange={(_, d) => setSparklePopoverOpen(d.open)}>
 *   <PopoverTrigger disableButtonEnhancement>{sparkleTriggerButton}</PopoverTrigger>
 *   <PopoverSurface>{sparklePopoverContent}</PopoverSurface>
 * </Popover>
 * ```
 * Rationale for split (vs baking Popover into the slot handler): the anchor
 * button MUST live inside `<PopoverTrigger>`. `HeaderToolbar` renders its own
 * button per slot — so the sparkle slot's `onClick` toggles state, and the
 * consumer renders the Popover shell as a sibling. This preserves anchor
 * positioning and keeps the shared `HeaderToolbar` contract unchanged.
 */
export interface IUseRecordHeaderToolbarActionsResult {
  /** Drop-in for `<HeaderToolbar {...toolbarProps} />`. */
  toolbarProps: IHeaderToolbarProps;
  /** Controlled `open` state for the consumer's sparkle popover. */
  sparklePopoverOpen: boolean;
  /** Setter for `sparklePopoverOpen`. Idempotent. */
  setSparklePopoverOpen: (open: boolean) => void;
  /**
   * Ready-to-render popover body (summary text OR empty state + unwired
   * refresh icon). Consumer places this inside its `<PopoverSurface>`.
   * `null` when the sparkle slot is disabled (nothing to render).
   */
  sparklePopoverContent: React.ReactNode | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles for the popover body (semantic tokens only per ADR-021 / NFR-03)
// ─────────────────────────────────────────────────────────────────────────────

const useSparkleContentStyles = makeStyles({
  surface: {
    minWidth: '320px',
    maxWidth: '480px',
    maxHeight: '480px',
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingVerticalXS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  headerLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground1,
  },
  bodyText: {
    whiteSpace: 'pre-wrap',
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
  },
  emptyText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
    fontStyle: 'italic',
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Popover body component (memoized; kept internal — not exported from index.ts)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Rendered inside the consumer's `<PopoverSurface>`. Renders the summary body
 * OR the empty-state message. Header shows the unwired refresh icon whose
 * click is a NO-OP in R1 (FR-08a). Refresh icon tooltip states the deferral.
 */
const SparklePopoverBody: React.FC<{ recordSummary: string | null | undefined }> = React.memo(
  ({ recordSummary }) => {
    const styles = useSparkleContentStyles();

    // No-op click handler is deliberate. Do NOT wire this to anything — the
    // refresh call requires a new BFF endpoint that is explicitly deferred to
    // a follow-on project (FR-08a + NFR-07). Any wiring here would violate the
    // R1 NFR-07 "zero new BFF endpoints" rule.
    const handleRefreshNoOp = React.useCallback((): void => {
      /* intentional no-op per FR-08a */
    }, []);

    const hasSummary = typeof recordSummary === 'string' && recordSummary.length > 0;

    const refreshButton = React.createElement(Button, {
      appearance: 'subtle',
      size: 'small',
      icon: React.createElement(ArrowClockwise20Regular),
      'aria-label': REFRESH_DEFERRAL_TOOLTIP,
      onClick: handleRefreshNoOp,
    });

    return React.createElement(
      'div',
      { className: styles.surface, 'data-testid': 'sparkle-popover-body' },
      React.createElement(
        'div',
        { className: styles.headerRow },
        React.createElement(Text, { className: styles.headerLabel }, 'AI Summary'),
        React.createElement(
          Tooltip,
          { content: REFRESH_DEFERRAL_TOOLTIP, relationship: 'label' },
          refreshButton
        )
      ),
      hasSummary
        ? React.createElement(
            'div',
            { 'data-testid': 'sparkle-popover-summary' },
            React.createElement(Text, { className: styles.bodyText }, recordSummary as string)
          )
        : React.createElement(
            'div',
            { 'data-testid': 'sparkle-popover-empty' },
            React.createElement(Text, { className: styles.emptyText }, SPARKLE_EMPTY_STATE)
          )
    );
  }
);
SparklePopoverBody.displayName = 'SparklePopoverBody';

// ─────────────────────────────────────────────────────────────────────────────
// Xrm.Navigation.navigateTo two-arg signature.
//
// The shared-lib `XrmNavigation.navigateTo` type declares a single-arg
// overload; the runtime SDK also accepts an optional second `navigationOptions`
// argument that carries modal target/size. We cast the function reference to
// this two-arg signature at the call site. Widening the shared-lib type is out
// of scope for this task (would ripple into every Xrm consumer) — runtime
// behavior is unchanged. The Xrm SDK `pageInput.data` for `pageType:
// "webresource"` is an OBJECT (key → string map); `navigateTo` serializes it
// as `key=value&...` internally. Tests verify the object shape (not a
// pre-encoded URL) at the call site.
// ─────────────────────────────────────────────────────────────────────────────

type XrmNavigateToTwoArg = (
  pageInput: Record<string, unknown>,
  navigationOptions?: Record<string, unknown>
) => Promise<void>;

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Compose a fully-wired `IHeaderToolbarProps` for a record-header surface.
 *
 * Signature (post-task-001 REVISED — see
 * `notes/design-alignment-corrections.md`): accepts `recordSummary` inline
 * so no separate `Xrm.WebApi.retrieveRecord` runs for the sparkle popover;
 * memo count uses the entity-specific ADR-024 lookup via
 * `buildMemoFilterForParent` (`null` for unsupported parents → memo badge 0).
 *
 * @example Per-entity PCF consumer (MatterHeaderPcf):
 * ```tsx
 * const { values } = useRecordFieldValues('sprk_matter', matterId, [
 *   'sprk_matternumber', 'sprk_mattername', 'sprk_matterdescription',
 *   'sprk_mattertype', 'sprk_practicearea', 'sprk_recordsummary',
 * ]);
 * const { toolbarProps, sparklePopoverOpen, setSparklePopoverOpen, sparklePopoverContent } =
 *   useRecordHeaderToolbarActions({
 *     entity: 'sprk_matter',
 *     recordId: matterId,
 *     recordSummary: values['sprk_recordsummary'] as string | null,
 *   });
 * // Consumer renders <HeaderToolbar {...toolbarProps} /> plus a <Popover>
 * // controlled by sparklePopoverOpen / setSparklePopoverOpen.
 * ```
 */
export function useRecordHeaderToolbarActions(
  options: IUseRecordHeaderToolbarActionsOptions
): IUseRecordHeaderToolbarActionsResult {
  const { entity, recordId, recordSummary, enabled } = options;

  // Enabled defaults — every flag `true`. A flag set to explicit `false`
  // OMITS its slot from `iconSlots` (spec FR-07 acceptance criterion).
  const sparkleEnabled = enabled?.sparkle !== false;
  const checkmarkEnabled = enabled?.checkmark !== false;
  const annotationEnabled = enabled?.annotation !== false;

  // ── Badge counts (FR-11: mount + focus refresh; no polling) ────────────────
  // Todo count uses the standard polymorphic regarding lookup.
  // Memo count uses the ADR-024 entity-specific lookup; `buildMemoFilterForParent`
  // returns `null` for unsupported entities (→ useRelatedCount idles at count=0).
  const todoFilter = `_regardingobjectid_value eq ${recordId}`;
  const memoFilter = buildMemoFilterForParent(entity, recordId);

  const { count: todoCount } = useRelatedCount('sprk_todo', todoFilter);
  const { count: memoCount } = useRelatedCount('sprk_memo', memoFilter);

  // ── Sparkle popover controlled state ───────────────────────────────────────
  const [sparklePopoverOpen, setSparklePopoverOpen] = React.useState<boolean>(false);

  // ── Handlers ───────────────────────────────────────────────────────────────

  const handleSparkleClick = React.useCallback((): void => {
    // Pure UI toggle — no Xrm.Navigation call (FR-08 REVISED). The consumer
    // wires the Popover to `sparklePopoverOpen` / `setSparklePopoverOpen`.
    setSparklePopoverOpen(prev => !prev);
  }, []);

  const handleCheckmarkClick = React.useCallback((): void => {
    const xrm = getXrm();
    // If Xrm is unavailable (unit tests / non-MDA hosts), silently no-op.
    // The count hook surfaces the "Xrm not available" error path separately;
    // there is no useful UX to render at click-time.
    if (!xrm?.Navigation?.navigateTo) return;
    const navigate = xrm.Navigation.navigateTo as unknown as XrmNavigateToTwoArg;
    void navigate(
      {
        pageType: 'webresource',
        name: SMARTTODO_WEBRESOURCE_NAME,
        data: { regardingEntity: entity, regardingId: recordId },
      },
      LAYOUT_1_MODAL as unknown as Record<string, unknown>
    );
  }, [entity, recordId]);

  const handleAnnotationClick = React.useCallback((): void => {
    const xrm = getXrm();
    if (!xrm?.Navigation?.navigateTo) return;
    const navigate = xrm.Navigation.navigateTo as unknown as XrmNavigateToTwoArg;
    void navigate(
      {
        pageType: 'webresource',
        name: NOTEPAD_WEBRESOURCE_NAME,
        data: { regardingEntity: entity, regardingId: recordId },
      },
      NOTEPAD_MODAL as unknown as Record<string, unknown>
    );
  }, [entity, recordId]);

  // ── Slot definitions ───────────────────────────────────────────────────────
  //
  // Each slot is built conditionally so a disabled slot is OMITTED (not merely
  // hidden — spec FR-07 acceptance criterion). Icons are the fixed set per
  // FR-08 / FR-09 / FR-10.

  const iconSlots = React.useMemo<IHeaderToolbarSlot[]>(() => {
    const slots: IHeaderToolbarSlot[] = [];

    if (sparkleEnabled) {
      slots.push({
        key: 'sparkle',
        icon: React.createElement(Sparkle24Regular),
        onClick: handleSparkleClick,
        tooltip: 'AI Summary',
        // FR-08: sparkle has NO badge.
      });
    }

    if (checkmarkEnabled) {
      slots.push({
        key: 'checkmark',
        icon: React.createElement(Checkmark24Regular),
        onClick: handleCheckmarkClick,
        tooltip: 'Related to-dos',
        badge: todoCount,
      });
    }

    if (annotationEnabled) {
      slots.push({
        key: 'annotation',
        icon: React.createElement(Note24Regular),
        onClick: handleAnnotationClick,
        tooltip: 'Notepad',
        badge: memoCount,
      });
    }

    return slots;
  }, [
    sparkleEnabled,
    checkmarkEnabled,
    annotationEnabled,
    handleSparkleClick,
    handleCheckmarkClick,
    handleAnnotationClick,
    todoCount,
    memoCount,
  ]);

  const toolbarProps = React.useMemo<IHeaderToolbarProps>(
    () => ({ iconSlots }),
    [iconSlots]
  );

  // ── Popover content ────────────────────────────────────────────────────────
  // Rendered inside the consumer's <PopoverSurface>. `null` when sparkle is
  // disabled — there is no popover to render.
  const sparklePopoverContent = React.useMemo<React.ReactNode | null>(() => {
    if (!sparkleEnabled) return null;
    return React.createElement(SparklePopoverBody, { recordSummary: recordSummary ?? null });
  }, [sparkleEnabled, recordSummary]);

  return {
    toolbarProps,
    sparklePopoverOpen,
    setSparklePopoverOpen,
    sparklePopoverContent,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Test-only exports (symbol reuse across the hook test suite).
// Not part of the runtime consumer surface — do NOT re-export from index.ts.
// ─────────────────────────────────────────────────────────────────────────────

export const __testables = {
  REFRESH_DEFERRAL_TOOLTIP,
  SPARKLE_EMPTY_STATE,
  SparklePopoverBody,
};
