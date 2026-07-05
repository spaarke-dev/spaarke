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
import { Note24Regular, Checkmark24Regular } from '@fluentui/react-icons';

import type { IHeaderToolbarProps, IHeaderToolbarSlot } from '../components/HeaderToolbar/types';
import { getXrm } from '../utils/xrmContext';
import { useRelatedCount } from './useRelatedCount';
import {
  LAYOUT_1_MODAL,
  NOTEPAD_MODAL,
  NOTEPAD_WEBRESOURCE_NAME,
  SMARTTODO_WEBRESOURCE_NAME,
  buildMemoFilterForParent,
  buildTodoFilterForParent,
} from './toolbarLaunchDefaults';

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Which toolbar slots to emit. Every flag defaults to `true`. Setting a flag
 * to `false` REMOVES that slot from `toolbarProps.iconSlots` entirely.
 *
 * v1.0.10: the `sparkle` slot was RETIRED from this hook — the sparkle
 * popover is now the shared `<AiSummaryPopover>` component (see
 * `SHARED-UI-COMPONENTS-GUIDE.md` §AiSummaryPopover). Consumers render it
 * DIRECTLY alongside the toolbar (see `CardChrome.tsx` in VisualHost for the
 * canonical pattern). The `sparkle?` flag on this interface is retained as a
 * no-op for backward compat but ignored — passing `false` no longer
 * suppresses anything (there is nothing to suppress).
 */
export interface IUseRecordHeaderToolbarActionsEnabled {
  /** Retained for backward compat; ignored. See v1.0.10 note above. */
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
  /**
   * Optional title rendered on the left of the toolbar (in the same row as icons).
   * When omitted, the toolbar renders icons only. Added in v1.0.2 per user
   * feedback ("title should be on the form and inline with the icons").
   */
  title?: string;
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
/**
 * Hook result — v1.0.10 minimal surface.
 *
 * Before v1.0.10 this returned `sparklePopoverOpen`, `setSparklePopoverOpen`,
 * `sparklePopoverContent`, and `sparkleButtonRef` so the consumer could
 * assemble a hand-rolled sparkle `<Popover>`. Rounds 6–9 of live QA taught us
 * that re-implementing the AI Summary popover is a mistake — the shared
 * `<AiSummaryPopover>` component (`SHARED-UI-COMPONENTS-GUIDE.md` §180) has
 * already been battle-tested by VisualHost, SemanticSearchControl,
 * DocumentCard, RichFilePreview, InsightSummaryCard, and DocumentLibrary.
 * Consumers of this hook render `<AiSummaryPopover>` DIRECTLY next to the
 * `<HeaderToolbar>` (see MatterHeaderView for the pattern) and this hook
 * only supplies the launcher slots (checkmark → SmartTodo, annotation →
 * Notepad) it uniquely owns.
 */
export interface IUseRecordHeaderToolbarActionsResult {
  /** Drop-in for `<HeaderToolbar {...toolbarProps} />`. */
  toolbarProps: IHeaderToolbarProps;
}

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
  const { entity, recordId, enabled, title } = options;
  // `recordSummary` retained on the options type for backward compat; the
  // sparkle popover is now the shared `<AiSummaryPopover>` (v1.0.10) which
  // consumers render themselves. This hook no longer reads the summary.

  const checkmarkEnabled = enabled?.checkmark !== false;
  const annotationEnabled = enabled?.annotation !== false;

  // ── Badge counts (FR-11: mount + focus refresh; no polling) ────────────────
  // Both sprk_todo AND sprk_memo use ADR-024 dual-field pattern with entity-specific
  // lookups (verified via Dataverse MCP describe queries — see notes/sprk-memo-schema.md
  // + v1.0.2 fix note for sprk_todo). The initial assumption that sprk_todo used the
  // standard polymorphic `_regardingobjectid_value` was WRONG — sprk_todo has 11
  // entity-specific `sprk_regarding{entity}` lookups (a superset of sprk_memo's 6).
  //
  // Both helpers return `null` for unsupported entities (→ useRelatedCount idles
  // at count=0). Discovered 2026-07-03 during live QA.
  const todoFilter = buildTodoFilterForParent(entity, recordId);
  const memoFilter = buildMemoFilterForParent(entity, recordId);

  const { count: todoCount, loading: todoLoading, error: todoError } =
    useRelatedCount('sprk_todo', todoFilter);
  const { count: memoCount, loading: memoLoading, error: memoError } =
    useRelatedCount('sprk_memo', memoFilter);

  // v1.0.15 UAT diagnostic (2026-07-04) — user reports no badges visible on
  // Matter where Dataverse ground-truth shows 3 memos + 2 todos. Log the
  // filter shapes + count states so we can distinguish:
  //   • unsupported entity (filter null)              — memoFilter/todoFilter null
  //   • Xrm.WebApi unavailable                        — error set, count 0
  //   • query returned 0 (unexpected)                 — no error, count 0
  //   • counts loaded correctly but badge not rendering (CSS/component issue)
  // Diagnostic is intentionally chatty on mount; removed after root cause
  // identified.
  React.useEffect(() => {
    // eslint-disable-next-line no-console
    console.info('[RecordHeader] badge counts', {
      entity,
      recordId,
      todoFilter,
      memoFilter,
      todoCount,
      todoLoading,
      todoError: todoError?.message ?? null,
      memoCount,
      memoLoading,
      memoError: memoError?.message ?? null,
    });
  }, [entity, recordId, todoFilter, memoFilter, todoCount, todoLoading, todoError, memoCount, memoLoading, memoError]);

  // ── Handlers ───────────────────────────────────────────────────────────────

  // Xrm.Navigation.navigateTo pageInput for `pageType: "webresource"` requires
  // TWO things that v1.0.2 got wrong (fixed 2026-07-03 in v1.0.4):
  //   1. Property name is `webresourceName` (NOT `name`)
  //   2. `data` must be a URL-encoded query STRING (NOT an object)
  // Passing the wrong shape produces a silent no-op — no console error, no
  // modal, nothing. Verified by cross-referencing every working
  // `pageType: "webresource"` call in this repo (LegalWorkspace WorkspaceGrid,
  // SpaarkeAi launch-resolver, xrmNavigationServiceAdapter).
  // Notepad launch payload: `regardingEntity=<entity>&regardingId=<id>` per
  // spec NFR-09 external API contract. Data MUST be a string — see gotcha
  // notes above about `pageType: 'webresource'` payload shape.
  const buildNotepadLaunchData = React.useCallback((): string => {
    return `regardingEntity=${encodeURIComponent(entity)}&regardingId=${encodeURIComponent(recordId)}`;
  }, [entity, recordId]);

  // DEF-11 (2026-07-04): SmartTodo openTodos launch payload — reuses the R4
  // FR-34 contract shipped in `src/solutions/SmartTodo/src/hooks/useLaunchContext.ts`
  // to pre-filter the Kanban to the current record's related to-dos.
  // Explicit `action=openTodos` form; SmartTodo's `useLaunchContext` recognizes
  // this and applies the regarding filter automatically.
  // Data MUST be a string (same gotcha as Notepad payload).
  const buildSmartTodoLaunchData = React.useCallback((): string => {
    return `action=openTodos&regardingType=${encodeURIComponent(entity)}&regardingId=${encodeURIComponent(recordId)}`;
  }, [entity, recordId]);

  const handleCheckmarkClick = React.useCallback((): void => {
    // v1.0.5 diagnostic instrumentation. `[RecordHeader]` log lines pinpoint
    // which stage broke: click reached → navigate call → resolve / reject.
    // eslint-disable-next-line no-console
    console.info('[RecordHeader] checkmark click', { webresourceName: SMARTTODO_WEBRESOURCE_NAME, entity, recordId });
    const xrm = getXrm();
    if (!xrm?.Navigation?.navigateTo) {
      // eslint-disable-next-line no-console
      console.warn('[RecordHeader] Xrm.Navigation.navigateTo unavailable');
      return;
    }
    // v1.0.6 CRITICAL: call `xrm.Navigation.navigateTo(...)` DIRECTLY. Every
    // v1.0.2..v1.0.5 aliased it as `const navigate = xrm.Navigation.navigateTo`
    // and then called `navigate(...)`, which strips the `this` binding and
    // makes the underlying Xrm method a silent no-op — no error, no modal.
    // Every working call site in this repo (SpaarkeAi launch-resolver,
    // LegalWorkspace WorkspaceGrid) calls the method directly on
    // `Xrm.Navigation`. Do not "extract" this into a local alias.
    (xrm.Navigation.navigateTo as unknown as XrmNavigateToTwoArg)
      .call(
        xrm.Navigation,
        {
          pageType: 'webresource',
          webresourceName: SMARTTODO_WEBRESOURCE_NAME,
          data: buildSmartTodoLaunchData(),
        },
        LAYOUT_1_MODAL as unknown as Record<string, unknown>
      )
      .then(() => {
        // eslint-disable-next-line no-console
        console.info('[RecordHeader] SmartTodo navigateTo resolved');
      })
      .catch(err => {
        // eslint-disable-next-line no-console
        console.error('[RecordHeader] SmartTodo navigateTo failed', err);
      });
  }, [buildSmartTodoLaunchData, entity, recordId]);

  const handleAnnotationClick = React.useCallback((): void => {
    // eslint-disable-next-line no-console
    console.info('[RecordHeader] annotation click', { webresourceName: NOTEPAD_WEBRESOURCE_NAME, entity, recordId });
    const xrm = getXrm();
    if (!xrm?.Navigation?.navigateTo) {
      // eslint-disable-next-line no-console
      console.warn('[RecordHeader] Xrm.Navigation.navigateTo unavailable');
      return;
    }
    (xrm.Navigation.navigateTo as unknown as XrmNavigateToTwoArg)
      .call(
        xrm.Navigation,
        {
          pageType: 'webresource',
          webresourceName: NOTEPAD_WEBRESOURCE_NAME,
          data: buildNotepadLaunchData(),
        },
        NOTEPAD_MODAL as unknown as Record<string, unknown>
      )
      .then(() => {
        // eslint-disable-next-line no-console
        console.info('[RecordHeader] Notepad navigateTo resolved');
      })
      .catch(err => {
        // eslint-disable-next-line no-console
        console.error('[RecordHeader] Notepad navigateTo failed', err);
      });
  }, [buildNotepadLaunchData, entity, recordId]);

  // ── Slot definitions ───────────────────────────────────────────────────────
  //
  // Each slot is built conditionally so a disabled slot is OMITTED (not merely
  // hidden — spec FR-07 acceptance criterion). Icons are the fixed set per
  // FR-08 / FR-09 / FR-10.

  const iconSlots = React.useMemo<IHeaderToolbarSlot[]>(() => {
    const slots: IHeaderToolbarSlot[] = [];

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
  }, [checkmarkEnabled, annotationEnabled, handleCheckmarkClick, handleAnnotationClick, todoCount, memoCount]);

  const toolbarProps = React.useMemo<IHeaderToolbarProps>(() => ({ iconSlots, title }), [iconSlots, title]);

  return {
    toolbarProps,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Test-only exports (symbol reuse across the hook test suite).
// v1.0.10: sparkle popover constants + component were removed from this hook
// (consumers now render the shared `<AiSummaryPopover>` directly). Empty
// object kept for import stability in existing tests that reference
// `__testables` without accessing any specific field.
// ─────────────────────────────────────────────────────────────────────────────

export const __testables = {};
