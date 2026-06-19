/**
 * @spaarke/daily-briefing-components — widgets barrel
 *
 * Higher-level composition widget(s) for the Daily Briefing surface — wires
 * together components + hooks + services for a specific host (workspace widget,
 * LegalWorkspace section). Pattern D dual-use convention per Calendar
 * (`CalendarWorkspaceWidget`) and Smart Todo (`SmartTodoWidget`) precedent.
 *
 * Populated by R2.1 hotfix (2026-06-19): the section-registration factory
 * (previously in `@spaarke/ui-components/.../sections/dailyBriefing/`) lives
 * here now and mounts the full `DailyBriefingApp` for the embedded path,
 * closing the Pattern D dual-use gap left by R2 task 018.
 */

export {
  createDailyBriefingRegistration,
  TELEMETRY_EVENT_DAILY_BRIEFING_429,
} from './dailyBriefing.registration';

export type {
  CreateDailyBriefingRegistrationOptions,
  NarrateRequest,
} from './dailyBriefing.registration';
