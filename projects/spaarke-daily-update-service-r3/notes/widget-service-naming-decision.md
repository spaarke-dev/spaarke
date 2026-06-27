# Widget service naming decision — R3 task 020

**Decision (2026-06-24)**: Option (a) RENAME with one-cycle transitional aliases.

- Canonical names (in `notificationService.ts`): `markBriefingChecked`, `markAllBriefingsChecked`, `markBriefingRemoved`, `extendBriefingTtl`.
- Old names (`markNotificationRead`, `markAllNotificationsRead`) re-exported as `@deprecated` aliases pointing at the new canonical functions. Lives at the bottom of `notificationService.ts` and in `src/services/index.ts`.
- Aliases exist solely so this task does not break `useBriefingActions.ts` + `DailyBriefingApp.smoke.test.tsx` (out of scope for task 020). Tasks 030 (hook) and 031 (UI) MUST rewire imports to the canonical names and delete the alias block — see TODO comment in `notificationService.ts` near the aliases.
