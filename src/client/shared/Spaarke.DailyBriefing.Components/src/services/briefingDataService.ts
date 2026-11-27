/**
 * briefingDataService — the data and navigation seam of `DailyBriefingApp`
 * (spaarkeai-mcp-bff-api-r2 task 010, spec FR-16 / FR-09).
 *
 * Why it exists: the briefing has to render in two kinds of host. The Console, the standalone Daily
 * Briefing page and the SpaarkeAi workspace call the BFF with `@spaarke/auth`'s `authenticatedFetch`
 * and navigate with Xrm. A sandboxed MCP widget (Copilot Chat / Cowork) has no network, no MSAL and
 * no Xrm: its host supplies the briefing through an MCP tool call and navigates through the MCP Apps
 * host. So the component takes its data and its navigation as props.
 *
 * The contract:
 *  - Nothing injected → the component makes exactly today's calls (`createDefaultBriefingDataService`
 *    wraps them) and navigates with Xrm, as before.
 *  - A `BriefingDataService` injected → the component never calls `authenticatedFetch`, `getXrm` or
 *    `window.Xrm` for data, preferences or navigation. An action that needs a capability the host did
 *    not inject is hidden, not broken: inline To Do create, dismiss, email share, document preview
 *    (all of which need the BFF or Xrm directly), preferences save, and record links.
 *  - This module imports no Xrm and no MCP SDK (ADR-012: the package stays host-agnostic). The widget
 *    adapter that maps these methods onto MCP tool calls lives with the widget (task 034).
 *
 * Pattern: injected services with a default factory, as `src/client/widgets/spaarke-decisions/src/App.tsx`
 * does for the Decisions widget and `createWorklistService({ fetch })` does for the worklist.
 */

import type { ChannelFetchResult, DailyDigestPreferences } from '../types/notifications';
import {
  fetchAiBriefing,
  fetchBriefingLive,
  fetchBriefingNarration,
  type BriefingResult,
  type BriefingWindowParams,
  type NarrationResult,
} from './briefingService';

/**
 * How the host loads, and optionally saves, the user's Daily Digest preferences. Both are
 * host-side: the default path reads and writes `sprk_userpreference` through Xrm.WebApi, which an
 * injected service replaces. `save` is optional: without it the Preferences menu is not rendered
 * (a read-only host cannot persist a change, so offering the menu would be a broken control).
 */
export interface BriefingPreferencesService {
  /** Load the caller's preferences. A rejection leaves the defaults in place (non-fatal). */
  load(): Promise<DailyDigestPreferences>;
  /** Persist the caller's preferences. Absent → the Preferences menu is hidden. */
  save?(preferences: DailyDigestPreferences): Promise<void>;
}

/**
 * The briefing data the component reads. `renderBriefing` is the one call the component makes to
 * draw the briefing; `narrate` and `summarize` are the two other briefing calls in this package's
 * BFF client, carried on the seam so a host that needs them does not reach around it.
 */
export interface BriefingDataService {
  /** `POST /api/ai/daily-briefing/render` equivalent: the briefing the component draws. */
  renderBriefing(windows?: BriefingWindowParams): Promise<NarrationResult>;
  /** `POST /api/ai/daily-briefing/narrate` equivalent. */
  narrate?(channels: ChannelFetchResult[]): Promise<NarrationResult>;
  /** `POST /api/ai/daily-briefing/summarize` equivalent. */
  summarize?(channels: ChannelFetchResult[]): Promise<BriefingResult>;
  /** Preferences load/save. Absent → default preferences, and the Preferences menu is hidden. */
  preferences?: BriefingPreferencesService;
}

/**
 * Host-supplied navigation. Each handler is optional; a handler the host did not supply hides the
 * affordance that would call it, so the component never falls back to Xrm when it was given a data
 * service.
 */
export interface BriefingNavigationHandler {
  /**
   * Open a record. Absent (with an injected data service) → record links render as plain text and
   * the "Open record" menu item is not offered. A rejection shows the existing "Cannot open record"
   * toast.
   */
  openRecord?(entityType: string, entityId: string): void | Promise<void>;
  /** Open an external URL (the MCP Apps host's open-link request). */
  openUrl?(url: string): void | Promise<void>;
  /** The "Browse Playbooks" overflow item; wins over the legacy `onBrowsePlaybooks` prop when both are set. */
  browsePlaybooks?(): void;
}

/**
 * The service the component uses when none is injected: today's BFF calls, unchanged
 * (`briefingService`, via `authenticatedFetch`). Preferences are not part of the default service:
 * the default path reads them through Xrm.WebApi, which the component resolves itself.
 */
export function createDefaultBriefingDataService(): BriefingDataService {
  return {
    renderBriefing: windows => fetchBriefingLive(windows),
    narrate: channels => fetchBriefingNarration(channels),
    summarize: channels => fetchAiBriefing(channels),
  };
}
