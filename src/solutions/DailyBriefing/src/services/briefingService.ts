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
 */

import { authenticatedFetch } from "./authInit";
import type { ChannelFetchResult } from "../types/notifications";

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
  | { status: "success"; data: DailyBriefingSummaryResponse }
  | { status: "unavailable"; reason: string }
  | { status: "error"; message: string };

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
}

export interface TldrResult {
  briefing: string;
  topAction: string;
  categoryCount: number;
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
}

/** Result of a narration fetch attempt. */
export type NarrationResult =
  | { status: "success"; data: NarrateResponse }
  | { status: "unavailable"; reason: string }
  | { status: "error"; message: string };

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
    if (ch.status !== "success") continue;

    const { meta, items, unreadCount } = ch.group;
    categories.push({
      name: meta.label,
      count: items.length,
      unreadCount,
    });
    totalCount += items.length;

    // Collect high/urgent priority items (max 5 across all channels)
    for (const item of items) {
      if (
        (item.priority === "high" || item.priority === "urgent") &&
        priorityItems.length < 5
      ) {
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
export async function fetchAiBriefing(
  channels: ChannelFetchResult[]
): Promise<BriefingResult> {
  const request = buildRequest(channels);

  // Skip if no data to summarize
  if (request.categories.length === 0 && request.priorityItems.length === 0) {
    return { status: "unavailable", reason: "No notification data to summarize." };
  }

  try {
    const response = await authenticatedFetch("/api/ai/daily-briefing/summarize", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });

    const data = (await response.json()) as DailyBriefingSummaryResponse;
    return { status: "success", data };
  } catch (err: unknown) {
    // authenticatedFetch throws ApiError for non-2xx responses
    const error = err as { statusCode?: number; message?: string };

    // 503 = AI service unavailable (circuit breaker open)
    // 429 = rate limited
    if (error.statusCode === 503 || error.statusCode === 429) {
      return {
        status: "unavailable",
        reason: "AI briefing service is temporarily unavailable.",
      };
    }

    // 401 = auth issue (already retried by authenticatedFetch)
    if (error.statusCode === 401) {
      return {
        status: "unavailable",
        reason: "Authentication required for AI briefing.",
      };
    }

    console.error("[DailyBriefing] AI briefing fetch failed:", err);
    return {
      status: "error",
      message: error.message ?? "Failed to generate briefing.",
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
    if (ch.status !== "success") continue;

    const { meta, items, unreadCount } = ch.group;
    categories.push({
      name: meta.label,
      count: items.length,
      unreadCount,
    });
    totalCount += items.length;

    // Collect high/urgent priority items (max 5 across all channels)
    for (const item of items) {
      if (
        (item.priority === "high" || item.priority === "urgent") &&
        priorityItems.length < 5
      ) {
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
      items: items.map((item) => ({
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
 * Fetch AI-generated narration (TL;DR + per-channel bullets) from the BFF.
 *
 * Returns a NarrationResult discriminated union:
 *   - "success" — tldr, channel narratives, and metadata
 *   - "unavailable" — AI service temporarily unavailable (503, rate limit)
 *   - "error" — unexpected failure
 *
 * @param channels Channel fetch results from useNotificationData
 */
export async function fetchBriefingNarration(
  channels: ChannelFetchResult[]
): Promise<NarrationResult> {
  const request = buildNarrationRequest(channels);

  // Skip if no data to narrate
  if (request.categories.length === 0 && request.channels.length === 0) {
    return { status: "unavailable", reason: "No notification data to narrate." };
  }

  try {
    const response = await authenticatedFetch("/api/ai/daily-briefing/narrate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });

    const data = (await response.json()) as NarrateResponse;
    return { status: "success", data };
  } catch (err: unknown) {
    // authenticatedFetch throws ApiError for non-2xx responses
    const error = err as { statusCode?: number; message?: string };

    // 503 = AI service unavailable (circuit breaker open)
    // 429 = rate limited
    if (error.statusCode === 503 || error.statusCode === 429) {
      return {
        status: "unavailable",
        reason: "AI narration service is temporarily unavailable.",
      };
    }

    // 401 = auth issue (already retried by authenticatedFetch)
    if (error.statusCode === 401) {
      return {
        status: "unavailable",
        reason: "Authentication required for AI narration.",
      };
    }

    console.error("[DailyBriefing] AI narration fetch failed:", err);
    return {
      status: "error",
      message: error.message ?? "Failed to generate narration.",
    };
  }
}
