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
