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

import { authenticatedFetch } from "@spaarke/auth";
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
