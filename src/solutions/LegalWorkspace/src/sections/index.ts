/**
 * sections/index.ts — Barrel re-export for all section registrations.
 */

export {
  SECTION_REGISTRY,
  getSectionById,
  getSectionsByCategory,
} from "../sectionRegistry";
export { getStartedRegistration } from "./getStarted.registration";
export { quickSummaryRegistration } from "./quickSummary.registration";
export { latestUpdatesRegistration } from "./latestUpdates.registration";
export { todoRegistration } from "./todo.registration";
export { documentsRegistration } from "./documents.registration";
export { dailyBriefingRegistration } from "./dailyBriefing/dailyBriefing.registration";
export { calendarRegistration } from "./calendar.registration";
