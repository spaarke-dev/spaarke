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
import type { TldrResolvableItem } from './TldrSection';
import { ActivityNotesSection } from './ActivityNotesSection';
import { CaughtUpFooter } from './CaughtUpFooter';
import { PreferencesDropdown } from './PreferencesDropdown';
import { HighPrioritySection } from './HighPrioritySection';
import { StatTiles, type StatTile } from './StatTiles';
import { SendEmailDialog, type ISendEmailPayload, RichFilePreviewDialog } from '@spaarke/ui-components';
import { extractEmailKey } from '@spaarke/ui-components/services';
import { useBriefingRender, useInlineTodoCreate, useBriefingPreferences } from '../hooks';
import { TOASTER_ID } from '../utils/toastUtils';
import { timeWindowToHours } from '../types/notifications';
import type { IWebApi, NotificationCategory, NotificationItem } from '../types/notifications';
import {
  emailBriefingToColleague,
  getDocumentPreviewUrl,
  type ChannelNarrationResult,
  type NarrativeBulletResult,
  type HighPriorityItemResult,
} from '../services/briefingService';

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
   * (`IConsumerRoutingService` → the BFF playbook-invocation boundary) per ADR-013.
   *
   * Optional — when omitted, the overflow menu is not rendered (back-compat
   * for non-Dataverse hosts).
   */
  onBrowsePlaybooks?: () => void;
}

/**
 * r5 email-share (2026-07-09) — open-dialog state for the shared SendEmailDialog.
 * `briefing` shares the whole briefing (server renders + sends via /email);
 * `item` shares one high-priority record (client-composed draft email activity).
 */
type EmailDialogState =
  | { mode: 'briefing' }
  | { mode: 'item'; item: HighPriorityItemResult; defaultSubject: string; defaultBody: string };

/**
 * Build a Dataverse record deep link from the record's STRUCTURED identity
 * (entityType + entityId) — never from narrative/display text. Returns '' when
 * the client URL OR either identity part is missing, so callers omit the link
 * cleanly rather than emit a dead relative URL (an emailed `/main.aspx?...` link
 * would be broken in any mail client). Exported for tests.
 */
export function buildRecordDeepLink(clientUrl: string, entityType: string, entityId: string): string {
  if (!clientUrl || !entityType || !entityId) return '';
  const id = entityId.replace(/[{}]/g, '');
  return `${clientUrl}/main.aspx?pagetype=entityrecord&etn=${encodeURIComponent(entityType)}&id=${encodeURIComponent(id)}`;
}

/**
 * r5 email-share #3 — build the Xrm.WebApi `email` (draft activity) record for a
 * single-item share. Pure + exported so the party payload (the runtime-risky
 * surface) is unit-testable without a live Dataverse. The From party is bound to
 * the caller's systemuser (participationtypemask 1) and the To party to the
 * picked internal user (mask 2). From is omitted when the caller's id is unknown
 * (Dataverse defaults it to the current user).
 */
export function buildEmailActivityRecord(
  senderSystemUserId: string,
  payload: { to: { id: string }; subject: string; body: string }
): Record<string, unknown> {
  const parties: Record<string, unknown>[] = [];
  if (senderSystemUserId) {
    parties.push({ 'partyid_systemuser@odata.bind': `/systemusers(${senderSystemUserId})`, participationtypemask: 1 });
  }
  parties.push({ 'partyid_systemuser@odata.bind': `/systemusers(${payload.to.id})`, participationtypemask: 2 });
  return {
    subject: payload.subject,
    description: payload.body,
    email_activity_parties: parties,
  };
}

/**
 * r5 email-share #3 — compose the draft email subject + body for a single
 * high-priority item, DETERMINISTICALLY from the item's structured fields
 * (name, kindLabel, description) plus a deep link built from entityType/entityId.
 * No narrative text is consulted (mirrors the deterministic-link rule the TL;DR
 * renderer follows). Exported so the composition is unit-testable without a mount.
 */
export function buildItemEmailDraft(
  item: HighPriorityItemResult,
  clientUrl: string
): { subject: string; body: string } {
  const kind = item.kindLabel ? `${item.kindLabel}: ` : '';
  const subject = `${kind}${item.name || 'Record'}`.slice(0, 200);
  const link = buildRecordDeepLink(clientUrl, item.entityType, item.entityId);
  const lines: string[] = [item.name || 'Record'];
  if (item.description) lines.push('', item.description);
  if (link) lines.push('', `Open the record: ${link}`);
  lines.push('', '— Shared from the Daily Briefing');
  return { subject, body: lines.join('\n') };
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
  // Trim narrative to fit sprk_todo.sprk_name (200-char default max length; slice to 197 so
  // the appended "..." keeps the stored value ≤ 200). The created To Do maps this title to
  // sprk_name in useInlineTodoCreate (R5 task 037 / FR-C8).
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

  // Preferences (sprk_userpreference) — drive BOTH the client-side channel-disabled
  // filter AND (r5 settings-wiring, 2026-07-09) the server-side collector date windows.
  // Loaded first so the render call can pass the user's Display Parameters.
  const { preferences, updatePreferences } = useBriefingPreferences(webApi, userId);

  // Translate the user's Display Parameters into the /render window params. Memoized on the
  // primitive fields so a Settings Save re-fetches the briefing with the new windows.
  const briefingWindows = React.useMemo(
    () => ({
      dueWithinDays: preferences.dueWithinDays,
      recencyHours: timeWindowToHours(preferences.timeWindow),
    }),
    [preferences.dueWithinDays, preferences.timeWindow]
  );

  // ---------------------------------------------------------------------------
  // Data source — single /render call (R7 Wave 12 cutover).
  //
  // No appnotification dependency. /render queries Dataverse server-side via
  // DailyBriefingCollector across 6 entity types (sprk_event, sprk_document,
  // sprk_matter, sprk_project, sprk_todo) and narrates the result. The user's
  // Display Parameters (briefingWindows) bound the per-channel date windows.
  // ---------------------------------------------------------------------------
  const {
    status: renderStatus,
    data: renderData,
    unavailableReason,
    error: renderError,
    refetch: refreshBriefing,
  } = useBriefingRender(briefingWindows);

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

  // R5 task 014 (FR-A5) — TL;DR anchor resolution map: itemId -> click-through target.
  // Deliberately sourced from the UNFILTERED `renderData.channelNarratives` (not
  // `filteredNarratives`) — the TL;DR's itemRefs[] were grounded server-side against the
  // full request's items[], and a channel the user has since disabled in preferences is
  // still a valid, real item the TL;DR may have named. Binary resolution only checks
  // "does this itemId exist", never "is this channel currently visible".
  const tldrResolvableItems = React.useMemo<Record<string, TldrResolvableItem>>(() => {
    const map: Record<string, TldrResolvableItem> = {};
    for (const channel of renderData?.channelNarratives ?? []) {
      for (const bullet of channel.bullets) {
        if (!bullet.primaryEntityType || !bullet.primaryEntityId) continue;
        const ids = bullet.itemIds && bullet.itemIds.length > 0 ? bullet.itemIds : [bullet.primaryEntityId];
        for (const id of ids) {
          map[id] = { entityType: bullet.primaryEntityType, entityId: bullet.primaryEntityId };
        }
      }
    }
    return map;
  }, [renderData]);

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
            // R7 W12 fix (2026-07-01): call xrm.Navigation.navigateTo as a method
            // (not destructured) — the platform's implementation relies on `this`
            // to access its internal _clientApiExecutor. Destructuring breaks it.
            if (typeof xrm?.Navigation?.navigateTo !== 'function') return;
            xrm.Navigation.navigateTo(
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
      // R7 W12 fix (2026-07-01): call xrm.Navigation.navigateTo as a method (not
      // destructured) — the platform's implementation relies on `this` to access
      // its internal _clientApiExecutor. Destructuring like
      //   `const navigateTo = xrm.Navigation.navigateTo`
      //   `navigateTo(...)`
      // throws `Cannot read properties of undefined ('_clientApiExecutor')`.
      if (typeof xrm?.Navigation?.navigateTo !== 'function') {
        dispatchAccessToast();
        return;
      }
      xrm.Navigation.navigateTo(
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
  // r5 email-share (2026-07-09) — #2 Email Briefing (server-send) + #3 Email Item
  // (client-composed draft email activity). Reuses the shared SendEmailDialog.
  // ---------------------------------------------------------------------------

  const [emailDialog, setEmailDialog] = React.useState<EmailDialogState | null>(null);

  // Operator UAT (2026-07-09, item D) — file-preview modal state. Set by a
  // Documents-channel row's open-document icon / menu; drives the shared
  // RichFilePreviewDialog below.
  const [previewDoc, setPreviewDoc] = React.useState<{ documentId: string; documentName: string } | null>(null);
  const handlePreviewDocument = React.useCallback((documentId: string, documentName: string) => {
    if (!documentId) return;
    setPreviewDoc({ documentId, documentName });
  }, []);

  // Dataverse client URL for deep links (e.g. https://org.crm.dynamics.com).
  const clientUrl = React.useMemo<string>(() => {
    try {
      return xrm?.Utility?.getGlobalContext()?.getClientUrl?.() ?? '';
    } catch {
      return '';
    }
  }, [xrm]);

  // Recipient picker — active internal systemusers with an email (matches the
  // server-side egress guard, which requires an active systemuser). Formats each
  // result as "Full Name (email)" so SendEmailDialog's extractEmailKey resolves it.
  const handleSearchUsers = React.useCallback(
    async (query: string): Promise<ISendEmailPayload['to'][]> => {
      if (!webApi || !query || query.trim().length < 2) return [];
      const safe = query.trim().replace(/'/g, "''");
      const options =
        `?$select=systemuserid,fullname,internalemailaddress` +
        `&$filter=contains(fullname,'${safe}') and isdisabled eq false` +
        `&$orderby=fullname asc&$top=10`;
      try {
        const result = await webApi.retrieveMultipleRecords('systemuser', options);
        return (result.entities ?? [])
          .filter((e: Record<string, unknown>) => !!e['internalemailaddress'])
          .map((e: Record<string, unknown>) => ({
            id: String(e['systemuserid'] ?? ''),
            name: `${String(e['fullname'] ?? '')} (${String(e['internalemailaddress'])})`,
          }));
      } catch (err) {
        console.warn('[DailyBriefing] user search failed:', err);
        return [];
      }
    },
    [webApi]
  );

  const handleEmailBriefing = React.useCallback(() => setEmailDialog({ mode: 'briefing' }), []);

  const handleEmailItem = React.useCallback(
    (item: HighPriorityItemResult) => {
      const { subject, body } = buildItemEmailDraft(item, clientUrl);
      setEmailDialog({ mode: 'item', item, defaultSubject: subject, defaultBody: body });
    },
    [clientUrl]
  );

  // Single onSend for both modes. Throwing keeps SendEmailDialog open + shows the
  // error; resolving closes it. Success dispatches a confirmation toast.
  const handleEmailSend = React.useCallback(
    async (payload: ISendEmailPayload): Promise<void> => {
      if (!emailDialog) return;

      if (emailDialog.mode === 'briefing') {
        const recipientEmail = extractEmailKey(payload.to.name);
        if (!recipientEmail) {
          throw new Error("Could not resolve the selected user's email address.");
        }
        const result = await emailBriefingToColleague(recipientEmail);
        if (result.status !== 'success') {
          throw new Error(result.message);
        }
        dispatchToast(
          <Toast>
            <ToastTitle>Briefing sent</ToastTitle>
            <ToastBody>Your Daily Briefing was emailed to {payload.to.name}.</ToastBody>
          </Toast>,
          { intent: 'success', timeout: 6000 }
        );
        return;
      }

      // mode === 'item' — create the email activity, then SEND it so the user is
      // done after the dialog (UAT 2026-07-09: no "open from activities" step). If
      // the send action fails (e.g. the sender mailbox isn't approved to send in
      // this environment), the created activity remains a draft and we say so.
      if (!webApi) {
        throw new Error('Dataverse is not available.');
      }
      const created = await webApi.createRecord('email', buildEmailActivityRecord(userId, payload));
      let sent = false;
      try {
        // Same-origin Dataverse Web API (the code page is served from the org URL);
        // cookie auth via credentials:'include' — mirrors the EntityDefinitions fetch
        // in useInlineTodoCreate. SendEmail is the bound action that delivers it.
        const resp = await fetch(`/api/data/v9.2/emails(${created.id})/Microsoft.Dynamics.CRM.SendEmail`, {
          method: 'POST',
          credentials: 'include',
          headers: {
            'Content-Type': 'application/json',
            'OData-MaxVersion': '4.0',
            'OData-Version': '4.0',
            Accept: 'application/json',
          },
          body: JSON.stringify({ IssueSend: true }),
        });
        sent = resp.ok;
        if (!resp.ok) {
          console.warn('[DailyBriefing] SendEmail action failed:', resp.status, await resp.text().catch(() => ''));
        }
      } catch (sendErr) {
        console.warn('[DailyBriefing] SendEmail action threw:', sendErr);
      }
      dispatchToast(
        <Toast>
          <ToastTitle>{sent ? 'Email sent' : 'Draft email created'}</ToastTitle>
          <ToastBody>
            {sent
              ? `Your email to ${payload.to.name} was sent.`
              : `A draft to ${payload.to.name} was saved — open it from your activities to send.`}
          </ToastBody>
        </Toast>,
        { intent: 'success', timeout: 8000 }
      );
    },
    [emailDialog, webApi, userId, dispatchToast]
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

  // Success — render HighPriority (if any) + TldrSection + filtered channelNarratives.
  const tldr = renderData?.tldr ?? null;
  const highPriorityItems = renderData?.highPriorityItems ?? [];

  // Deterministic KPI tiles (task 021 redesign). Every count is derived from the
  // already-deterministic render data — no LLM, no fabrication (FR-A4 posture).
  // These are INDEPENDENT lenses, not a total-and-subsets hierarchy:
  //   Updates = the activity-feed bullet count (the sections below)
  //   Overdue / Documents / Matters & Projects = subsets of that feed (the overdue,
  //     documents, and matters+projects channels respectively)
  //   Critical = the SEPARATE high-priority flagged-records section (own data source,
  //     bypasses the narrator) — so it can legitimately exceed "Updates". It was
  //     labelled "Open items" before (2026-07-09 operator UAT): that implied a grand
  //     total and made "Critical > Open items" read as a contradiction. "Updates" is
  //     the honest label; summing the two sources is deliberately avoided (records can
  //     appear in both → double-count would violate the by-construction accuracy rule).
  // Match the EXACT channel-category keys emitted by DailyBriefingCollector (server constants
  // ChannelOverdueTasks="overdue-tasks", ChannelMatters="matters") — not a loose /overdue/ /matter/
  // regex, which could false-match a future channel (e.g. "overdue-documents", "matter-invoices")
  // and silently inflate a count. Sum across ALL channels with the key (defensive against >1),
  // case-insensitive. If the server renames a key the tile shows 0 (visible in UAT) rather than a
  // wrong non-zero — the accuracy-safe failure mode. Keep these keys in sync with the collector.
  const OVERDUE_CATEGORY_KEY = 'overdue-tasks';
  const MATTERS_CATEGORY_KEY = 'matters';
  const PROJECTS_CATEGORY_KEY = 'projects';
  const DOCUMENTS_CATEGORY_KEY = 'documents';
  const sumBulletsForCategory = (key: string): number =>
    filteredNarratives
      .filter(cn => cn.category?.toLowerCase() === key)
      .reduce((total, cn) => total + cn.bullets.length, 0);
  const overdueCount = sumBulletsForCategory(OVERDUE_CATEGORY_KEY);
  const documentsCount = sumBulletsForCategory(DOCUMENTS_CATEGORY_KEY);
  // Operator UAT (2026-07-09): combine Matters + Projects into ONE tile — both are
  // matter-like engagement records the attorney tracks together. Sum the two channel
  // counts; a record can't appear in both (distinct entity types), so no double-count.
  const mattersAndProjectsCount =
    sumBulletsForCategory(MATTERS_CATEGORY_KEY) + sumBulletsForCategory(PROJECTS_CATEGORY_KEY);
  const statTiles: StatTile[] = [
    { label: 'Updates', value: totalVisibleBullets, tone: 'neutral' },
    { label: 'Overdue', value: overdueCount, tone: overdueCount > 0 ? 'danger' : 'neutral' },
    { label: 'Critical', value: highPriorityItems.length, tone: highPriorityItems.length > 0 ? 'warning' : 'neutral' },
    { label: 'Documents', value: documentsCount, tone: 'neutral' },
    { label: 'Matters & Projects', value: mattersAndProjectsCount, tone: 'brand' },
  ];

  return (
    <div className={styles.container}>
      <Toaster toasterId={toasterId} position="bottom-end" />
      <DigestHeader
        lastUpdated={generatedAtIso}
        onRefresh={refreshBriefing}
        preferencesSlot={<PreferencesDropdown preferences={preferences} onUpdatePreferences={updatePreferences} />}
        onBrowsePlaybooks={onBrowsePlaybooks}
        onEmailBriefing={handleEmailBriefing}
      />
      <div className={styles.scrollContent}>
        {/* Task 021 redesign: deterministic KPI tiles at the top. */}
        <StatTiles tiles={statTiles} />
        {/* Operator order (2026-07-09): Today's summary above Critical Today. */}
        <TldrSection
          tldr={tldr}
          isLoading={false}
          isUnavailable={false}
          unavailableReason={null}
          error={null}
          generatedAt={generatedAtIso}
          resolvableItems={tldrResolvableItems}
          onOpenRecord={handleOpenRecord}
        />
        <HighPrioritySection items={highPriorityItems} onOpenRecord={handleOpenRecord} onEmailItem={handleEmailItem} />
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
            // Item D (2026-07-09): file-preview modal for Documents rows.
            onPreviewDocument={handlePreviewDocument}
          />
        </div>
        <CaughtUpFooter channelLabels={[]} />
      </div>
      {emailDialog && (
        <SendEmailDialog
          open={true}
          onClose={() => setEmailDialog(null)}
          title={emailDialog.mode === 'briefing' ? 'Email Briefing' : 'Email Item'}
          defaultSubject={
            emailDialog.mode === 'briefing'
              ? `Daily Briefing — ${new Date().toLocaleDateString()}`
              : emailDialog.defaultSubject
          }
          defaultBody={
            emailDialog.mode === 'briefing'
              ? 'Sharing my Daily Briefing with you — the full briefing is included in this email.'
              : emailDialog.defaultBody
          }
          onSearchUsers={handleSearchUsers}
          onSend={handleEmailSend}
          maxWidth="720px"
          height="70vh"
        />
      )}
      {previewDoc && (
        <RichFilePreviewDialog
          open={true}
          documentId={previewDoc.documentId}
          documentName={previewDoc.documentName}
          onClose={() => setPreviewDoc(null)}
          // Same BFF endpoint the Semantic Search PCF uses — SPE preview embed URL.
          fetchPreviewUrl={() => getDocumentPreviewUrl(previewDoc.documentId)}
          // "Open file": open the SPE preview URL in a new tab (both modes).
          onOpenFile={async () => {
            const url = await getDocumentPreviewUrl(previewDoc.documentId);
            if (url && typeof window !== 'undefined') window.open(url, '_blank', 'noopener');
          }}
          // "Open record": open the sprk_document record via the shared record-modal path.
          onOpenRecord={() => handleOpenRecord('sprk_document', previewDoc.documentId)}
          // "Email": reuse the per-item email draft flow, targeting the document.
          onEmailDocument={() =>
            handleEmailItem({
              entityType: 'sprk_document',
              entityId: previewDoc.documentId,
              name: previewDoc.documentName,
              kindLabel: 'Document',
              highPriority: false,
              monitor: false,
            })
          }
          // "Copy link": copy a deep link to the document record.
          onCopyLink={() => {
            const link = buildRecordDeepLink(clientUrl, 'sprk_document', previewDoc.documentId);
            if (link && typeof navigator !== 'undefined' && navigator.clipboard) {
              navigator.clipboard.writeText(link).catch(() => {
                /* clipboard denied — non-fatal */
              });
            }
            dispatchToast(
              <Toast>
                <ToastTitle>Link copied</ToastTitle>
                <ToastBody>A link to the document record was copied to your clipboard.</ToastBody>
              </Toast>,
              { intent: 'success', timeout: 3000 }
            );
          }}
        />
      )}
    </div>
  );
};
