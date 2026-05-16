/**
 * useChatHistoryFilter
 *
 * Client-side session filtering hook with a 200ms debounce on the search query.
 *
 * Filters sessions by title and lastMessagePreview using a case-insensitive
 * substring match. Returns all sessions when the debounced query is empty.
 *
 * Debounce is implemented via useEffect + setTimeout + clearTimeout — no
 * external debounce library required (per project constraint).
 *
 * NOT PCF-safe — uses React 19 hooks (useTransition for search filtering).
 *
 * @see ADR-012 — Shared Component Library
 */

import { useState, useEffect, useTransition } from "react";
import type { ChatSession } from "./ChatHistoryPanel.types";

/**
 * Filters an array of ChatSession objects by a debounced search query.
 *
 * @param sessions - Full list of sessions to filter.
 * @param searchQuery - Raw (un-debounced) search string from the UI.
 * @returns Filtered sessions whose title or lastMessagePreview contains the
 *          debounced query (case-insensitive). Returns all sessions when the
 *          debounced query is empty.
 *
 * @example
 * ```tsx
 * const [query, setQuery] = React.useState("");
 * const filtered = useChatHistoryFilter(sessions, query);
 * ```
 */
export function useChatHistoryFilter(
  sessions: ChatSession[],
  searchQuery: string
): ChatSession[] {
  // Debounced query — updated 200ms after the raw query settles.
  const [debouncedQuery, setDebouncedQuery] = useState<string>(searchQuery);

  // useTransition lets React defer the filter computation so the search
  // input remains responsive during typing (React 19 concurrent feature).
  const [, startTransition] = useTransition();

  // ── 200ms debounce via useEffect + setTimeout ──────────────────────────────
  useEffect(() => {
    const handle = setTimeout(() => {
      startTransition(() => {
        setDebouncedQuery(searchQuery);
      });
    }, 200);

    // Cleanup: cancel the previous timeout if searchQuery changes before it fires.
    return () => {
      clearTimeout(handle);
    };
  }, [searchQuery]);

  // ── Filter ────────────────────────────────────────────────────────────────

  if (!debouncedQuery.trim()) {
    // Empty or whitespace-only query → return all sessions.
    return sessions;
  }

  const lower = debouncedQuery.toLowerCase();

  return sessions.filter((session) => {
    const titleMatch = session.title.toLowerCase().includes(lower);
    const previewMatch =
      session.lastMessagePreview !== undefined &&
      session.lastMessagePreview.toLowerCase().includes(lower);
    return titleMatch || previewMatch;
  });
}
