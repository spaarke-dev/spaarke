/**
 * openPlaybookLibrary — open the `sprk_playbooklibrary` Code Page as an 85%
 * modal through the host `Xrm.Navigation` (R7 task 095 / FR-18 "Browse
 * Playbooks").
 *
 * One copy for the two Daily Briefing hosts (task 081 round 4, review F6): the
 * embedded registration (`widgets/dailyBriefing.registration.ts`) and the
 * standalone Code Page (`src/solutions/DailyBriefing/src/main.tsx`). The Code
 * Page's copy still detached `navigateTo` into a local and called it unbound,
 * which loses `this` — the real `Xrm.Navigation.navigateTo` then throws
 * (`Cannot read properties of undefined ('_clientApiExecutor')`, the R7 W12
 * rule). Here it is always called AS A METHOD on `Navigation`.
 *
 * Xrm comes from the shared walker with the 'navigation' capability: the
 * nearest frame that can navigateTo. Never throws: with no such frame it logs
 * a warning and returns.
 */

import { getXrm } from '@spaarke/ui-components';

type NavigateTo = (page: object, options?: object) => Promise<unknown>;

/** @param logPrefix console prefix identifying the host, e.g. `[DailyBriefing]`. */
export function openPlaybookLibrary(logPrefix: string): void {
  const nav = getXrm('navigation')?.Navigation as { navigateTo?: NavigateTo } | undefined;
  if (typeof nav?.navigateTo !== 'function') {
    console.warn(`${logPrefix} Xrm.Navigation unavailable — cannot open Playbook Library.`);
    return;
  }
  nav
    .navigateTo(
      { pageType: 'webresource', webresourceName: 'sprk_playbooklibrary', data: '' },
      { target: 2, width: { value: 85, unit: '%' }, height: { value: 85, unit: '%' }, title: 'Playbook Library' }
    )
    .catch((err: unknown) => {
      console.warn(`${logPrefix} Playbook Library navigation rejected:`, err);
    });
}
