/**
 * briefingService.ts — calls the BFF AI briefing endpoint to generate
 * a prioritized narrative summary from structured notification data.
 *
 * Endpoint: POST /api/ai/daily-briefing/summarize
 * Auth: Bearer token via @spaarke/auth authenticatedFetch
 *
 * Constraints:
 *   - MUST label AI-generated content clearly (project constraint)
 *   - MUST extend BFF for AI briefing — no separate service (ADR-013)
 *   - Graceful fallback when AI is unavailable (503, circuit breaker)
 *
 * Hoist note (R2 task 012 / FR-09 — finalized in task 015):
 *   Originally lived at `src/solutions/DailyBriefing/src/services/briefingService.ts`.
 *   Hoisted to `@spaarke/daily-briefing-components/services` per Calendar
 *   (`@spaarke/events-components`) precedent — BFF client lives package-local.
 *   Logic is preserved verbatim — request shape, error handling, telemetry.
 *
 *   `authenticatedFetch` is now imported from the canonical `@spaarke/auth`
 *   entry point per ADR-028 (task 015 added `@spaarke/auth` as a peer dep on
 *   the package). Consumers still go through their host's auth-initializer
 *   factory before any call here fires (`main.tsx`'s `bootstrapAuth()` for the
 *   standalone code page, the SpaarkeAi embed's bootstrap for the widget).
 *
 *   `ChannelFetchResult` is now imported intra-package from
 *   `../types/notifications` (task 015 hoisted the notifications types file
 *   so the package no longer reaches back across the solution boundary).
 *   Original location becomes a re-export shim.
 */

import { authenticatedFetch } from '@spaarke/auth';
import type { ChannelFetchResult } from '../types/notifications';

// ---------------------------------------------------------------------------
// Request / Response DTOs (mirror DailyBriefingEndpoints.cs)
// ---------------------------------------------------------------------------

interface NotificationCategoryDto {
  name: string;
  count: number;
  unreadCount: number;
}

interface PriorityItemDto {
  category: string;
  title: string;
  dueDate?: string | null;
}

interface DailyBriefingSummaryRequest {
  categories: NotificationCategoryDto[];
  priorityItems: PriorityItemDto[];
  totalNotificationCount: number;
}

/** Response shape from POST /api/ai/daily-briefing/summarize. */
export interface DailyBriefingSummaryResponse {
  briefing: string;
  generatedAtUtc: string;
  categoryCount: number;
  priorityItemCount: number;
}

/** Result of an AI briefing fetch attempt. */
export type BriefingResult =
  | { status: 'success'; data: DailyBriefingSummaryResponse }
  | { status: 'unavailable'; reason: string }
  | { status: 'error'; message: string };

// ---------------------------------------------------------------------------
// Narration DTOs (mirror DailyBriefingNarrationEndpoints.cs)
// ---------------------------------------------------------------------------

interface NarrateRequest {
  categories: NotificationCategoryDto[];
  priorityItems: PriorityItemDto[];
  totalNotificationCount: number;
  channels: ChannelNarrationInput[];
}

interface ChannelNarrationInput {
  category: string;
  label: string;
  items: ChannelItemDto[];
}

interface ChannelItemDto {
  id: string;
  title: string;
  body: string;
  priority: string;
  regardingName: string;
  regardingEntityType: string;
  regardingId: string;
  createdOn: string;
}

export interface NarrateResponse {
  tldr: TldrResult;
  channelNarratives: ChannelNarrationResult[];
  generatedAtUtc: string;
  /**
   * R7 W12 feedback item 9 (2026-07-01) — cross-entity high-priority items.
   * Populated when any of the 7 flagged entities (matter, project, invoice,
   * document, workassignment, event, todo) has sprk_HighPriority=true or
   * sprk_Monitor=true. Widget renders a compact "High Priority" section
   * above the TL;DR with subtle red background. Absent/empty = no flagged
   * items → widget hides the section.
   */
  highPriorityItems?: HighPriorityItemResult[];
}

export interface HighPriorityItemResult {
  /** Dataverse logical name (e.g., "sprk_matter"). */
  entityType: string;
  /** GUID of the record. */
  entityId: string;
  /** Display name of the record. */
  name: string;
  /** ISO 8601 due date, or undefined for entities with no meaningful due date. */
  dueDate?: string;
  /** True if sprk_highpriority = Yes on the source record. */
  highPriority: boolean;
  /** True if sprk_monitor = Yes on the source record. */
  monitor: boolean;
  /** Short entity-kind label (e.g., "Matter", "Task", "To Do"). */
  kindLabel: string;
  /** Description / subject text from the record. Empty when unavailable. */
  description?: string;
  /** Server-computed action classification: 'Overdue' | 'DueToday' | 'DueSoon' | 'Recent' | 'None'. */
  action?: string;
  /** Reason the item appears here: 'HighPriority' | 'Monitor' | 'Both' | ''. */
  reason?: string;
  /** ISO 8601 modifiedon timestamp (used for 'Recent' action label). */
  modifiedOn?: string;
}

export interface TldrResult {
  /** 2-3 sentence executive summary (R2.2 replaces the prior single `briefing` blob). */
  summary: string;
  /** 3-5 short key-takeaway bullet strings (no leading "- "). */
  keyTakeaways: string[];
  /** ONE-sentence top action for today. */
  topAction: string;
  /** Count of notification categories that fed the summary. */
  categoryCount: number;
  /** Count of priority items that fed the summary. */
  priorityItemCount: number;
}

export interface ChannelNarrationResult {
  category: string;
  bullets: NarrativeBulletResult[];
}

export interface NarrativeBulletResult {
  narrative: string;
  itemIds: string[];
  primaryEntityType: string;
  primaryEntityId: string;
  primaryEntityName: string;
  /**
   * R7 W12 feedback items 2/3/4 (2026-07-01) — per-bullet entity references.
   * Ordered by first-appearance in narrative text (mentioned refs), then by
   * channel order (implicit refs). Widget renders:
   * - `mentioned=true` refs: wrap `entityName` in narrative text as clickable Link.
   * - `mentioned=false` refs: append trailing `[N]` citations.
   * Empty array (or field absent) => plain-text bullet, no citations.
   */
  references?: NarrativeBulletReferenceResult[];
}

export interface NarrativeBulletReferenceResult {
  /** 1-based citation index for trailing `[N]` markers. */
  index: number;
  /** Dataverse logical name of the target entity (e.g., "sprk_matter"). */
  entityType: string;
  /** GUID of the target record. */
  entityId: string;
  /** Display name of the target record. */
  entityName: string;
  /** True if `entityName` appears in the narrative text. */
  mentioned: boolean;
}

/** Result of a narration fetch attempt. */
export type NarrationResult =
  | { status: 'success'; data: NarrateResponse }
  | { status: 'unavailable'; reason: string }
  | { status: 'error'; message: string };

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Build the summarization request from channel fetch results.
 * Extracts category counts and top priority items (high/urgent, max 5).
 */
function buildRequest(channels: ChannelFetchResult[]): DailyBriefingSummaryRequest {
  const categories: NotificationCategoryDto[] = [];
  const priorityItems: PriorityItemDto[] = [];
  let totalCount = 0;

  for (const ch of channels) {
    if (ch.status !== 'success') continue;

    const { meta, items, unreadCount } = ch.group;
    categories.push({
      name: meta.label,
      count: items.length,
      unreadCount,
    });
    totalCount += items.length;

    // Collect high/urgent priority items (max 5 across all channels)
    for (const item of items) {
      if ((item.priority === 'high' || item.priority === 'urgent') && priorityItems.length < 5) {
        priorityItems.push({
          category: meta.label,
          title: item.title,
          dueDate: null, // Due dates are not stored in notification items
        });
      }
    }
  }

  return { categories, priorityItems, totalNotificationCount: totalCount };
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Fetch an AI-generated briefing summary from the BFF endpoint.
 *
 * Returns a BriefingResult discriminated union:
 *   - "success" — briefing text and metadata
 *   - "unavailable" — AI service temporarily unavailable (503, rate limit)
 *   - "error" — unexpected failure
 *
 * @param channels Channel fetch results from useNotificationData
 */
export async function fetchAiBriefing(channels: ChannelFetchResult[]): Promise<BriefingResult> {
  const request = buildRequest(channels);

  // Skip if no data to summarize
  if (request.categories.length === 0 && request.priorityItems.length === 0) {
    return { status: 'unavailable', reason: 'No notification data to summarize.' };
  }

  try {
    const response = await authenticatedFetch('/api/ai/daily-briefing/summarize', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });

    const data = (await response.json()) as DailyBriefingSummaryResponse;
    return { status: 'success', data };
  } catch (err: unknown) {
    // authenticatedFetch throws ApiError for non-2xx responses
    const error = err as { statusCode?: number; message?: string };

    // 503 = AI service unavailable (circuit breaker open)
    // 429 = rate limited
    if (error.statusCode === 503 || error.statusCode === 429) {
      return {
        status: 'unavailable',
        reason: 'AI briefing service is temporarily unavailable.',
      };
    }

    // 401 = auth issue (already retried by authenticatedFetch)
    if (error.statusCode === 401) {
      return {
        status: 'unavailable',
        reason: 'Authentication required for AI briefing.',
      };
    }

    console.error('[DailyBriefing] AI briefing fetch failed:', err);
    return {
      status: 'error',
      message: error.message ?? 'Failed to generate briefing.',
    };
  }
}

// ---------------------------------------------------------------------------
// Narration helpers
// ---------------------------------------------------------------------------

/**
 * Build the narration request from channel fetch results.
 * Includes categories, priority items, AND full item data per channel.
 */
function buildNarrationRequest(channels: ChannelFetchResult[]): NarrateRequest {
  const categories: NotificationCategoryDto[] = [];
  const priorityItems: PriorityItemDto[] = [];
  const channelInputs: ChannelNarrationInput[] = [];
  let totalCount = 0;

  for (const ch of channels) {
    if (ch.status !== 'success') continue;

    const { meta, items, unreadCount } = ch.group;
    categories.push({
      name: meta.label,
      count: items.length,
      unreadCount,
    });
    totalCount += items.length;

    // Collect high/urgent priority items (max 5 across all channels)
    for (const item of items) {
      if ((item.priority === 'high' || item.priority === 'urgent') && priorityItems.length < 5) {
        priorityItems.push({
          category: meta.label,
          title: item.title,
          dueDate: null,
        });
      }
    }

    // Build channel input with full item data
    channelInputs.push({
      category: meta.category,
      label: meta.label,
      items: items.map(item => ({
        id: item.id,
        title: item.title,
        body: item.body,
        priority: item.priority,
        regardingName: item.regardingName,
        regardingEntityType: item.regardingEntityType,
        regardingId: item.regardingId,
        createdOn: item.createdOn,
      })),
    });
  }

  return {
    categories,
    priorityItems,
    totalNotificationCount: totalCount,
    channels: channelInputs,
  };
}

// ---------------------------------------------------------------------------
// Public API — Narration
// ---------------------------------------------------------------------------

/**
 * R7 Wave 11 T118 narrator spike (2026-06-30): route through /render endpoint instead of
 * /narrate when this flag is on. /render runs live Dataverse queries server-side (no
 * dependency on appNotification rows + no widget-side notification-context loader needed).
 * Same response shape as /narrate — fully backward-compatible for downstream consumers.
 * Toggle to `false` to fall back to /narrate (appNotification-derived payload).
 */
const USE_LIVE_RENDER = true;

/**
 * Fetch AI-generated narration (TL;DR + per-channel bullets) from the BFF.
 *
 * Returns a NarrationResult discriminated union:
 *   - "success" — tldr, channel narratives, and metadata
 *   - "unavailable" — AI service temporarily unavailable (503, rate limit)
 *   - "error" — unexpected failure
 *
 * @param channels Channel fetch results from useNotificationData
 *                 (ignored when USE_LIVE_RENDER is on)
 */
export async function fetchBriefingNarration(channels: ChannelFetchResult[]): Promise<NarrationResult> {
  // R7 Wave 11 T118: short-circuit to live-render path when enabled. The `channels` arg
  // becomes informational only (server-side collector queries Dataverse directly).
  if (USE_LIVE_RENDER) {
    return fetchBriefingLive();
  }

  const request = buildNarrationRequest(channels);

  // Skip if no data to narrate
  if (request.categories.length === 0 && request.channels.length === 0) {
    return { status: 'unavailable', reason: 'No notification data to narrate.' };
  }

  try {
    const response = await authenticatedFetch('/api/ai/daily-briefing/narrate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });

    const data = (await response.json()) as NarrateResponse;
    // R2.2 hotfix — defensive normalization of TldrResult.keyTakeaways.
    // The BFF should always emit this as an empty array (TldrResult record
    // default), but if any deploy is mid-flight or the response omits the
    // field for any reason, render code on `keyTakeaways.length` would
    // crash. Normalize at the boundary so downstream components never see
    // undefined for this field.
    if (data?.tldr) {
      data.tldr.keyTakeaways = Array.isArray(data.tldr.keyTakeaways) ? data.tldr.keyTakeaways : [];
      data.tldr.summary = data.tldr.summary ?? '';
      data.tldr.topAction = data.tldr.topAction ?? '';
    }
    return { status: 'success', data };
  } catch (err: unknown) {
    // authenticatedFetch throws ApiError for non-2xx responses
    const error = err as { statusCode?: number; message?: string };

    // 503 = AI service unavailable (circuit breaker open)
    // 429 = rate limited
    if (error.statusCode === 503 || error.statusCode === 429) {
      return {
        status: 'unavailable',
        reason: 'AI narration service is temporarily unavailable.',
      };
    }

    // 401 = auth issue (already retried by authenticatedFetch)
    if (error.statusCode === 401) {
      return {
        status: 'unavailable',
        reason: 'Authentication required for AI narration.',
      };
    }

    console.error('[DailyBriefing] AI narration fetch failed:', err);
    return {
      status: 'error',
      message: error.message ?? 'Failed to generate narration.',
    };
  }
}

// ---------------------------------------------------------------------------
// Live-render path (R7 Wave 11 T118 narrator spike, 2026-06-30)
// ---------------------------------------------------------------------------

/**
 * Fetch the AI Daily Briefing via the live `/render` endpoint. Server-side
 * collector runs Dataverse queries directly across 6 entity types (sprk_event,
 * sprk_document, sprk_matter, sprk_project, sprk_todo) — bypasses the
 * `appnotification` table entirely. Returns the SAME NarrateResponse shape as
 * `/narrate` so downstream rendering code is unchanged.
 *
 * No request body needed — the user is identified from their OBO token's
 * AAD oid claim, which the server maps to a Dataverse systemuserid.
 *
 * Exported (R7 Wave 12 cutover, 2026-06-30) so `useBriefingRender` can call
 * `/render` unconditionally on mount, without the appnotification load gate
 * that `useBriefingNarration` had.
 */
export async function fetchBriefingLive(): Promise<NarrationResult> {
  try {
    const response = await authenticatedFetch('/api/ai/daily-briefing/render', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{}',
    });

    const data = (await response.json()) as NarrateResponse;
    // Defensive normalization (mirrors fetchBriefingNarration).
    if (data?.tldr) {
      data.tldr.keyTakeaways = Array.isArray(data.tldr.keyTakeaways) ? data.tldr.keyTakeaways : [];
      data.tldr.summary = data.tldr.summary ?? '';
      data.tldr.topAction = data.tldr.topAction ?? '';
    }
    return { status: 'success', data };
  } catch (err: unknown) {
    const error = err as { statusCode?: number; message?: string };

    if (error.statusCode === 503 || error.statusCode === 429) {
      return {
        status: 'unavailable',
        reason: 'AI briefing service is temporarily unavailable.',
      };
    }
    if (error.statusCode === 401 || error.statusCode === 403) {
      return {
        status: 'unavailable',
        reason: 'Sign-in required to view your daily briefing.',
      };
    }
    console.error('[DailyBriefing] live render failed:', err);
    return {
      status: 'error',
      message: error.message ?? 'Failed to render briefing.',
    };
  }
}
