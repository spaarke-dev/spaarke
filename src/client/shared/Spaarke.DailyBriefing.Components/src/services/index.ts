/**
 * @spaarke/daily-briefing-components — services barrel
 *
 * UI-agnostic data access + AI narration calls. The narration service wraps
 * the BFF `/narrate` endpoint (no new endpoint; uses existing R1 endpoint per
 * spec MUST rule). BFF client lives package-local per Calendar
 * (`@spaarke/events-components`) precedent — no generic `@spaarke/bff-clients`
 * package yet.
 *
 * Populated by R2 task 012 (FR-09): hoisted `briefingService` (the BFF
 * `/summarize` + `/narrate` clients).
 *
 * Populated by R2 task 015 (FR-07): hoisted `preferencesService` from the
 * standalone DailyBriefing solution so the package no longer reaches back
 * across the solution boundary.
 *
 * ontology-platform-r1 task 010 / C-1 (2026-10-03): `notificationService`
 * (and the `useBriefingNotifications` / `useBriefingNarration` /
 * `useBriefingActions` hooks it existed solely to back) was deleted as dead
 * code — once those three hooks were removed, nothing in the live `/render`
 * data path (`briefingService.fetchBriefingLive` → `useBriefingRender`)
 * called into it.
 */

export {
  fetchAiBriefing,
  fetchBriefingNarration,
  type BriefingResult,
  type DailyBriefingSummaryResponse,
  type NarrationResult,
  type NarrateResponse,
  type TldrResult,
  type ChannelNarrationResult,
  type NarrativeBulletResult,
} from './briefingService';

export { fetchDigestPreferences, saveDigestPreferences } from './preferencesService';
