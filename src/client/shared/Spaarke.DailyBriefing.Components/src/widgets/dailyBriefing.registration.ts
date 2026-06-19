/**
 * dailyBriefing.registration.ts — SectionRegistration FACTORY mounting the
 * full `DailyBriefingApp` (the canonical Pattern D dual-use widget).
 *
 * # Why this file exists (R2.1 hotfix, 2026-06-19)
 *
 * The R2 hoist (tasks 011–017) moved `DailyBriefingApp` into this package
 * as the single canonical component for both standalone and embedded surfaces
 * (FR-04: "components render identically across both hosts"). The standalone
 * code page mounts it directly. But the EMBEDDED factory in
 * `@spaarke/ui-components/.../sections/dailyBriefing/dailyBriefing.registration.ts`
 * was never updated to mount it — it still mounted the OLD R4-task-069
 * `DailyBriefingSection` (narrative-only, no TL;DR / Activity Notes / actions).
 * That's why the SpaarkeAi-embedded widget rendered a stripped-down view while
 * the standalone code page rendered the full UI. This file closes that gap.
 *
 * # API surface
 *
 * Drop-in replacement for `createDailyBriefingRegistration` from
 * `@spaarke/ui-components`. The options interface is preserved verbatim for
 * backward compat with existing call sites (LegalWorkspace shim,
 * `createLegalWorkspaceSectionRegistry` factory chain from Option D PR #397).
 * The factory:
 *   - Accepts `authenticatedFetch`, `tenantId`, `onRateLimitError`,
 *     `loadNotificationContext`
 *   - **IGNORES all of them** in the new architecture — `DailyBriefingApp`
 *     self-resolves Xrm via frame-walking and fetches data via
 *     `useBriefingNotifications(webApi)`. The Option D notification-loader
 *     seam from R2 task 002 / PR #397 was solving a problem the new
 *     architecture eliminates (the component reads `appnotification`
 *     directly; no external loader injection needed).
 *
 * The options remain on the type so consumers don't break. They'll be
 * deprecated in a follow-up PR once SpaarkeAi `main.tsx` is updated to stop
 * passing them through.
 *
 * # Telemetry constant
 *
 * `TELEMETRY_EVENT_DAILY_BRIEFING_429` is preserved at the same string value
 * so App Insights KQL queries continue to match. Re-exported here so the
 * LegalWorkspace shim can keep importing it from the same module path
 * (after switching to `@spaarke/daily-briefing-components/widgets`).
 *
 * Standards: ADR-006, ADR-012, ADR-021, ADR-022, ADR-025, ADR-028.
 */

import * as React from 'react';
import { SparkleRegular } from '@fluentui/react-icons';
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from '@spaarke/ui-components';
import { DailyBriefingApp } from '../components/DailyBriefingApp';

// ---------------------------------------------------------------------------
// Telemetry event constant (preserved for KQL parity)
// ---------------------------------------------------------------------------

/**
 * App Insights event name fired when BFF `/narrate` returns 429.
 *
 * Re-exported from this module so consumers can continue importing it from
 * the same location they import the factory. Same string value as the
 * pre-R2.1 export from `@spaarke/ui-components` — KQL queries unchanged.
 */
export const TELEMETRY_EVENT_DAILY_BRIEFING_429 =
  'spaarke-ai-error.daily-briefing.rate-limited';

// ---------------------------------------------------------------------------
// Narration request shape (re-export for type compat with prior callers)
// ---------------------------------------------------------------------------

/**
 * Shape of the request body posted to BFF `/narrate`. Preserved here only as
 * the parameter type of `loadNotificationContext` for backward compat with
 * call sites that still type that option (e.g., SpaarkeAi's
 * `loadSpaarkeAiNotificationContext` and `buildNarrationRequest`). The new
 * `DailyBriefingApp` does NOT consume this — it builds its own narrate
 * request internally via `useBriefingNarration`.
 *
 * Field shapes mirror the legacy `useDailyBriefing` types that lived in
 * `@spaarke/ui-components` before R2.1's Fix A retired them. The legacy
 * shape is preserved verbatim so existing builders (SpaarkeAi's
 * `buildNarrationRequest`) continue to type-check without changes.
 */
export interface NotificationCategoryDto {
  name: string;
  count: number;
  unreadCount: number;
}

export interface PriorityItemDto {
  category: string;
  title: string;
  dueDate: string | null;
}

export interface ChannelItemDto {
  id: string;
  title: string;
  body: string;
  priority: string;
  regardingName: string;
  regardingEntityType: string;
  regardingId: string;
  createdOn: string;
}

export interface ChannelNarrationInput {
  category: string;
  label: string;
  items: ChannelItemDto[];
}

export interface NarrateRequest {
  categories: NotificationCategoryDto[];
  priorityItems: PriorityItemDto[];
  totalNotificationCount: number;
  channels: ChannelNarrationInput[];
}

// ---------------------------------------------------------------------------
// Factory options (backward-compat surface)
// ---------------------------------------------------------------------------

/**
 * Inputs accepted by `createDailyBriefingRegistration`. ALL options are
 * accepted for backward compat with the pre-R2.1 surface but are IGNORED
 * by the factory implementation — the mounted `DailyBriefingApp` is
 * self-contained.
 *
 * To be deprecated in a follow-up cleanup PR.
 */
export interface CreateDailyBriefingRegistrationOptions {
  /**
   * @deprecated R2.1 — Ignored. `DailyBriefingApp` does not need an external
   * fetch wrapper; it uses `useBriefingNotifications(webApi)` which reads
   * `appnotification` directly via `Xrm.WebApi`.
   */
  authenticatedFetch?: (url: string, init?: RequestInit) => Promise<Response>;
  /**
   * @deprecated R2.1 — Ignored. Tenant-scoped cache key resolution now happens
   * inside the hoisted hooks via the `webApi` / Xrm context.
   */
  tenantId?: string;
  /**
   * @deprecated R2.1 — Ignored. Rate-limit telemetry is now logged via
   * `console.warn` inside `useBriefingNarration` (the new package). Custom
   * App Insights routing will return in a follow-up if a real consumer needs
   * it; until then `console.warn` keeps the failure mode observable.
   */
  onRateLimitError?: (properties: Record<string, unknown>) => void;
  /**
   * @deprecated R2.1 — Ignored. The Option D notification-loader seam from
   * R2 task 002 / PR #397 was solving a problem the new architecture
   * eliminates. `DailyBriefingApp` reads `appnotification` directly via
   * `useBriefingNotifications(webApi)`; no external loader injection needed.
   */
  loadNotificationContext?: () => Promise<NarrateRequest | null>;
}

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

/**
 * Create a Daily Briefing `SectionRegistration` that mounts the FULL
 * `DailyBriefingApp` (TL;DR + Activity Notes + per-item actions). Drop-in
 * replacement for `createDailyBriefingRegistration` from `@spaarke/ui-components`.
 *
 * All options accepted for backward compat are ignored — the component is
 * self-contained. See file-level docblock for migration details.
 */
export function createDailyBriefingRegistration(
  _options: CreateDailyBriefingRegistrationOptions = {},
): SectionRegistration {
  return {
    id: 'daily-briefing',
    label: 'Daily Briefing',
    description: 'AI-curated highlights from your day',
    icon: SparkleRegular,
    category: 'ai',
    // Was 325px under the old narrative-only DailyBriefingSection (FR-15).
    // DailyBriefingApp's content (TL;DR + Activity Notes + actions) is taller;
    // medium-large keeps it usable. Embedding hosts can override via
    // SectionRegistration metadata if they need a different default.
    defaultHeight: '480px',

    factory(_context: SectionFactoryContext): ContentSectionConfig {
      return {
        id: 'daily-briefing',
        type: 'content',
        title: 'Daily Briefing',
        style: {},
        renderContent: () => React.createElement(DailyBriefingApp, { params: {} }),
      };
    },
  };
}

export default createDailyBriefingRegistration;
