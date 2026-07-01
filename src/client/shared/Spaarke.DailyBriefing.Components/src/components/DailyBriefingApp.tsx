/**
 * DailyBriefingApp — top-level composer for the Daily Briefing surface.
 *
 * R7 Wave 12 widget cutover (2026-06-30):
 *   Refactored to drive the entire widget from a single `POST /api/ai/daily-briefing/render`
 *   call. The legacy chain `useBriefingNotifications` → `appnotification` table →
 *   `useBriefingNarration` (gated by appnotification load + non-empty channels)
 *   is REMOVED from the widget data path. The previous "all caught up" early-exit
 *   that relied on `totalUnreadCount === 0` from appnotification is REMOVED —
 *   `/render` is the sole source of truth.
 *
 *   What remains from the pre-cutover composition:
 *     - `useBriefingPreferences` — still queries `sprk_userpreference` for
 *       channel filter prefs (NOT appnotification).
 *     - `useInlineTodoCreate` — still writes first-class `sprk_todo` records
 *       (ADR-024 + smart-todo-decoupling-r3 FR-29).
 *     - `handleOpenRecord` — Xrm.Navigation.navigateTo for record modal
 *       (per FR-18 / FR-19).
 *
 *   Dropped (no appnotification surface to act on):
 *     - `useBriefingActions` (markChecked / markRemoved / extendTtl)
 *     - Optimistic-update overlay state
 *     - handleCheck / handleRemove / handleKeep callbacks
 *     - FR-16 raw-notification fallback in ActivityNotesSection (no `channels`)
 *     - The per-bullet sub-list (no `items` source to expand into sub-rows)
 *
 * Hoisted into `@spaarke/daily-briefing-components/components` per ADR-012
 * (R2 task 011). Original-location top-level entry at
 * `src/solutions/DailyBriefing/src/App.tsx` is a re-export shim.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Spinner,
  Toaster,
  useToastController,
  useId,
  Toast,
  ToastTitle,
  ToastBody,
  ToastFooter,
  Link,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
} from '@fluentui/react-components';
import { DigestHeader } from './DigestHeader';
import { EmptyState } from './EmptyState';
import { TldrSection } from './TldrSection';
import { ActivityNotesSection } from './ActivityNotesSection';
import { CaughtUpFooter } from './CaughtUpFooter';
import { PreferencesDropdown } from './PreferencesDropdown';
import { useBriefingRender, useInlineTodoCreate, useBriefingPreferences } from '../hooks';
import { TOASTER_ID } from '../utils/toastUtils';
import type { IWebApi, NotificationCategory, NotificationItem } from '../types/notifications';
import type { ChannelNarrationResult, NarrativeBulletResult } from '../services/briefingService';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    boxSizing: 'border-box',
  },
  spinnerContainer: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    padding: tokens.spacingHorizontalL,
    boxSizing: 'border-box',
    justifyContent: 'center',
    alignItems: 'center',
  },
  scrollContent: {
    padding: tokens.spacingHorizontalL,
    overflowY: 'auto',
    flex: 1,
  },
  activitySection: {
    marginTop: tokens.spacingVerticalXXL,
  },
  errorBar: {
    marginBottom: tokens.spacingVerticalL,
  },
});

export interface DailyBriefingAppProps {
  params: Record<string, string>;
  /**
   * R7 task 095 / FR-18 — host-supplied callback for the "Browse Playbooks"
   * overflow menu item on the DigestHeader. The standalone DailyBriefing
   * Code Page and the SpaarkeAi briefing widget each wire this to their own
   * `Xrm.Navigation.navigateTo({pageType:'webresource',
   * webresourceName:'sprk_playbooklibrary', data:''}, {target:2, ...})`
   * thunk (shared lib stays Xrm-free per ADR-012). The launch reaches the
   * existing Library Code Page wrapper which preserves Path A.5 routing
   * (`IConsumerRoutingService → IInvokePlaybookAi`) per ADR-013.
   *
   * Optional — when omitted, the overflow menu is not rendered (back-compat
   * for non-Dataverse hosts).
   */
  onBrowsePlaybooks?: () => void;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Synthesize a `NotificationItem`-shaped record from a /render narrative
 * bullet so the existing `useInlineTodoCreate` hook (which accepts a
 * `NotificationItem`) can write a sprk_todo without further re-plumbing.
 *
 * R7 Wave 12: bullet `itemIds` in the /render path are source-record GUIDs
 * (sprk_event, sprk_document, sprk_matter, sprk_project, sprk_todo) — NOT
 * appnotification IDs. We key the synthetic item by `itemIds[0]` for state
 * tracking (`isCreated` / `isPending` maps) and supply the bullet's primary
 * entity as the regarding target so the sprk_todo `regarding` lookup
 * resolves via the existing ADR-024 catalog.
 */
function bulletToNotificationItem(bullet: NarrativeBulletResult, generatedAtUtc?: string): NotificationItem {
  const narrative = bullet.narrative ?? '';
  // Trim narrative to fit sprk_todo.subject (200 char default limit).
  const title = narrative.length > 197 ? `${narrative.slice(0, 197)}...` : narrative;
  return {
    id: bullet.itemIds?.[0] ?? bullet.primaryEntityId ?? '',
    title: title || (bullet.primaryEntityName ?? 'Daily briefing item'),
    body: '',
    category: 'system' as NotificationCategory,
    priority: 'normal',
    actionUrl: '',
    regardingName: bullet.primaryEntityName ?? '',
    regardingEntityType: bullet.primaryEntityType ?? '',
    regardingId: bullet.primaryEntityId ?? '',
    isRead: false,
    isAiGenerated: true,
    createdOn: generatedAtUtc ?? new Date().toISOString(),
    dueDate: null,
  };
}

/**
 * DailyBriefingApp — top-level composer for the Daily Briefing surface.
 *
 * Integrates /render-driven data, AI narration, inline to-do creation,
 * and preferences via a narrative digest layout.
 */
export const DailyBriefingApp: React.FC<DailyBriefingAppProps> = ({ params: _params, onBrowsePlaybooks }) => {
  const styles = useStyles();

  // Resolve Xrm via frame-walking with polling for welcome screen timing.
  // Xrm may not be available immediately when loaded as MDA welcome screen.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const [xrm, setXrm] = React.useState<any>(() => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const w = window as any;
      return w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm ?? null;
    } catch {
      return null;
    }
  });

  // Poll for Xrm if not available on mount (welcome screen / left nav timing)
  React.useEffect(() => {
    if (xrm?.WebApi) return; // Already available
    let cancelled = false;
    const interval = setInterval(() => {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const w = window as any;
        const found = w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm ?? null;
        if (found?.WebApi && !cancelled) {
          setXrm(found);
          clearInterval(interval);
        }
      } catch {
        /* cross-origin */
      }
    }, 500);
    // Stop polling after 30s
    const timeout = setTimeout(() => {
      clearInterval(interval);
    }, 30000);
    return () => {
      cancelled = true;
      clearInterval(interval);
      clearTimeout(timeout);
    };
  }, [xrm]);

  const webApi = React.useMemo<IWebApi | null>(() => xrm?.WebApi ?? null, [xrm]);

  // Resolve current user ID
  const userId = React.useMemo<string>(() => {
    try {
      return xrm?.Utility?.getGlobalContext()?.userSettings?.userId?.replace(/[{}]/g, '') ?? '';
    } catch {
      return '';
    }
  }, [xrm]);

  // ---------------------------------------------------------------------------
  // Data source — single /render call (R7 Wave 12 cutover).
  //
  // No appnotification dependency. /render queries Dataverse server-side via
  // DailyBriefingCollector across 6 entity types (sprk_event, sprk_document,
  // sprk_matter, sprk_project, sprk_todo) and narrates the result.
  // ---------------------------------------------------------------------------
  const {
    status: renderStatus,
    data: renderData,
    unavailableReason,
    error: renderError,
    refetch: refreshBriefing,
  } = useBriefingRender();

  // Preferences (sprk_userpreference, independent of appnotification) — used
  // for the client-side channel-disabled filter applied to /render's
  // channelNarratives output.
  const { preferences, updatePreferences } = useBriefingPreferences(webApi, userId);

  // Inline To Do creation from narrative bullets — writes first-class sprk_todo
  // records per ADR-024 + smart-todo-decoupling-r3 FR-29.
  //
  // R7 W12 feedback item 7 (2026-07-01): userId is passed so the hook can
  // look up the user's sprk_primarycontact and bind it to sprk_assignedto on
  // every created todo.
  // R7 W12 feedback item 8 (2026-07-01): getCreatedId returns the new sprk_todo
  // GUID so the success toast can wire an "Open To Do" action.
  const {
    createTodo,
    isCreated,
    isPending,
    getError: getTodoError,
    getCreatedId,
  } = useInlineTodoCreate(webApi, userId);

  // Toaster setup for success/error notifications
  const toasterId = useId(TOASTER_ID);
  const { dispatchToast } = useToastController(toasterId);

  // ---------------------------------------------------------------------------
  // Derived state — pure functions of renderData + preferences.
  // ---------------------------------------------------------------------------

  // Apply user's channel-disabled filter at the consumer (per FR-06 Option A
  // pattern preserved post-cutover).
  const filteredNarratives = React.useMemo<ChannelNarrationResult[]>(() => {
    if (!renderData) return [];
    const disabled = new Set<string>(preferences.disabledChannels);
    return renderData.channelNarratives.filter(cn => !disabled.has(cn.category));
  }, [renderData, preferences.disabledChannels]);

  // Total visible bullets across all non-disabled channels — drives the header
  // count badge (replaces legacy totalUnreadCount).
  const totalVisibleBullets = React.useMemo(
    () => filteredNarratives.reduce((sum, cn) => sum + cn.bullets.length, 0),
    [filteredNarratives]
  );

  // Build a fast lookup from bullet itemId → bullet for `handleAddToTodo`.
  // Every itemId in the bullet's itemIds array maps to the same bullet — so a
  // click on any sub-id resolves the source bullet.
  const bulletIndex = React.useMemo<Map<string, NarrativeBulletResult>>(() => {
    const map = new Map<string, NarrativeBulletResult>();
    for (const channel of filteredNarratives) {
      for (const bullet of channel.bullets) {
        const ids = bullet.itemIds ?? [];
        if (ids.length === 0 && bullet.primaryEntityId) {
          map.set(bullet.primaryEntityId, bullet);
        }
        for (const id of ids) {
          map.set(id, bullet);
        }
      }
    }
    return map;
  }, [filteredNarratives]);

  const generatedAtIso = React.useMemo<string | null>(() => {
    if (!renderData?.generatedAtUtc) return null;
    const value = renderData.generatedAtUtc;
    if (typeof value === 'string') return value;
    try {
      return new Date(value).toISOString();
    } catch {
      return null;
    }
  }, [renderData]);

  // ---------------------------------------------------------------------------
  // Handlers
  // ---------------------------------------------------------------------------

  /**
   * Add a narrative bullet (resolved from itemIds) to To Do and show a
   * confirmation toast. R7 Wave 12: synthesizes a NotificationItem from the
   * bullet's narrative + primary-entity data (no appnotification lookup).
   */
  const handleAddToTodo = React.useCallback(
    async (itemIds: string[]) => {
      const first = itemIds[0];
      if (!first) return;
      const bullet = bulletIndex.get(first);
      if (!bullet) return;
      const synthesized = bulletToNotificationItem(bullet, generatedAtIso ?? undefined);
      try {
        await createTodo(synthesized);
        const err = getTodoError(synthesized.id);
        if (err) {
          dispatchToast(
            <Toast>
              <ToastTitle>Could not add to To Do</ToastTitle>
              <ToastBody>{err}</ToastBody>
            </Toast>,
            { intent: 'error', timeout: 5000 }
          );
        } else {
          // R7 W12 feedback item 8: 15s timeout + "Open To Do" action link.
          // Navigates to the newly-created sprk_todo record via the same
          // Xrm.Navigation modal pattern the regarding-name link uses.
          const newTodoId = getCreatedId(synthesized.id);
          const openTodo = (): void => {
            if (!newTodoId) return;
            const navigateTo: ((page: object, options?: object) => Promise<unknown>) | undefined =
              xrm?.Navigation?.navigateTo;
            if (typeof navigateTo !== 'function') return;
            navigateTo(
              { pageType: 'entityrecord', entityName: 'sprk_todo', entityId: newTodoId },
              { target: 2, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } }
            ).catch(() => {
              /* user closed dialog */
            });
          };
          dispatchToast(
            <Toast>
              <ToastTitle>Added to To Do</ToastTitle>
              <ToastBody>{synthesized.title}</ToastBody>
              {newTodoId ? (
                <ToastFooter>
                  <Link appearance="default" onClick={openTodo}>
                    Open To Do
                  </Link>
                </ToastFooter>
              ) : null}
            </Toast>,
            { intent: 'success', timeout: 15000 }
          );
        }
      } catch (e) {
        dispatchToast(
          <Toast>
            <ToastTitle>Could not add to To Do</ToastTitle>
            <ToastBody>{e instanceof Error ? e.message : String(e)}</ToastBody>
          </Toast>,
          { intent: 'error', timeout: 5000 }
        );
      }
    },
    [bulletIndex, generatedAtIso, createTodo, getTodoError, getCreatedId, dispatchToast, xrm]
  );

  /**
   * Dismiss callback — kept in the contract for back-compat with NarrativeBullet's
   * onDismiss prop, but a no-op in the /render path (nothing to dismiss; the
   * source records aren't appnotification rows we can mark read).
   */
  const handleDismiss = React.useCallback((_itemIds: string[]) => {
    // R7 Wave 12: no appnotification target; the per-bullet dismiss menu
    // item is hidden by default in NarrativeBullet (onCheck/onRemove/onKeep
    // are not wired). Kept as a no-op so the contract surface is stable.
  }, []);

  /**
   * R4 task 046+047 / FR-18 + FR-19 — open a Dataverse record in a modal dialog.
   *
   * Unchanged from pre-cutover: dispatches Xrm.Navigation.navigateTo with
   * 80%×80% sizing and surfaces a non-blocking Toaster toast on rejection.
   */
  const handleOpenRecord = React.useCallback(
    (entityType: string, entityId: string) => {
      if (!entityType || !entityId) return;
      const dispatchAccessToast = (): void => {
        dispatchToast(
          <Toast>
            <ToastTitle>Cannot open record</ToastTitle>
            <ToastBody>You may not have access.</ToastBody>
          </Toast>,
          { intent: 'warning', timeout: 5000 }
        );
      };
      const navigateTo: ((page: object, options?: object) => Promise<unknown>) | undefined =
        xrm?.Navigation?.navigateTo;
      if (typeof navigateTo !== 'function') {
        dispatchAccessToast();
        return;
      }
      navigateTo(
        {
          pageType: 'entityrecord',
          entityName: entityType,
          entityId: entityId,
        },
        { target: 2, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } }
      ).catch(() => {
        dispatchAccessToast();
      });
    },
    [xrm, dispatchToast]
  );

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  // Loading / idle — initial render or in-flight /render fetch.
  if (renderStatus === 'idle' || renderStatus === 'loading') {
    return (
      <div className={styles.spinnerContainer}>
        <Spinner label="Loading daily briefing..." />
      </div>
    );
  }

  // Empty — /render succeeded but returned nothing. Distinct from unavailable.
  if (renderStatus === 'empty') {
    return (
      <div className={styles.container}>
        <DigestHeader
          totalUnreadCount={0}
          onRefresh={refreshBriefing}
          preferencesSlot={<PreferencesDropdown preferences={preferences} onUpdatePreferences={updatePreferences} />}
          onBrowsePlaybooks={onBrowsePlaybooks}
        />
        <div className={styles.scrollContent}>
          <EmptyState />
        </div>
        <Toaster toasterId={toasterId} position="bottom-end" />
      </div>
    );
  }

  // Unavailable — AI service down (503, rate limit, auth issue with backend).
  if (renderStatus === 'unavailable') {
    return (
      <div className={styles.container}>
        <DigestHeader
          totalUnreadCount={0}
          onRefresh={refreshBriefing}
          preferencesSlot={<PreferencesDropdown preferences={preferences} onUpdatePreferences={updatePreferences} />}
          onBrowsePlaybooks={onBrowsePlaybooks}
        />
        <div className={styles.scrollContent}>
          <MessageBar intent="warning" layout="multiline" className={styles.errorBar}>
            <MessageBarBody>
              <MessageBarTitle>Daily briefing temporarily unavailable.</MessageBarTitle>
              {unavailableReason ?? 'Please try again in a few minutes.'}
            </MessageBarBody>
          </MessageBar>
        </div>
        <Toaster toasterId={toasterId} position="bottom-end" />
      </div>
    );
  }

  // Error — unexpected failure (500, network error, parse error).
  if (renderStatus === 'error') {
    return (
      <div className={styles.container}>
        <DigestHeader
          totalUnreadCount={0}
          onRefresh={refreshBriefing}
          preferencesSlot={<PreferencesDropdown preferences={preferences} onUpdatePreferences={updatePreferences} />}
          onBrowsePlaybooks={onBrowsePlaybooks}
        />
        <div className={styles.scrollContent}>
          <MessageBar intent="error" layout="multiline" className={styles.errorBar}>
            <MessageBarBody>
              <MessageBarTitle>Could not load daily briefing.</MessageBarTitle>
              {renderError ?? 'Unexpected error.'}
            </MessageBarBody>
          </MessageBar>
        </div>
        <Toaster toasterId={toasterId} position="bottom-end" />
      </div>
    );
  }

  // Success — render TldrSection + filtered channelNarratives.
  const tldr = renderData?.tldr ?? null;

  return (
    <div className={styles.container}>
      <Toaster toasterId={toasterId} position="bottom-end" />
      <DigestHeader
        totalUnreadCount={totalVisibleBullets}
        onRefresh={refreshBriefing}
        preferencesSlot={<PreferencesDropdown preferences={preferences} onUpdatePreferences={updatePreferences} />}
        onBrowsePlaybooks={onBrowsePlaybooks}
      />
      <div className={styles.scrollContent}>
        <TldrSection
          tldr={tldr}
          isLoading={false}
          isUnavailable={false}
          unavailableReason={null}
          error={null}
          generatedAt={generatedAtIso}
        />
        <div className={styles.activitySection}>
          <ActivityNotesSection
            channelNarratives={filteredNarratives}
            onAddToTodo={handleAddToTodo}
            onDismiss={handleDismiss}
            isTodoCreated={isCreated}
            isTodoPending={isPending}
            getTodoError={getTodoError}
            isLoading={false}
            // R4 task 046+047 — single Open record path for both the
            // regarding-name link (FR-19) AND the overflow menu item (FR-18).
            onOpenRecord={handleOpenRecord}
          />
        </div>
        <CaughtUpFooter channelLabels={[]} />
      </div>
    </div>
  );
};
