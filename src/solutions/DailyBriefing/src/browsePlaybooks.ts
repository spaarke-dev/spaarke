/**
 * The Daily Briefing Code Page's "Browse Playbooks" handler, passed to
 * `DailyBriefingApp` as `onBrowsePlaybooks` (R7 task 095 / FR-18: the shared
 * package stays Xrm-free per ADR-012; the host supplies the navigation). It
 * opens the existing `sprk_playbooklibrary` Code Page as an 85% modal, which
 * launches through Path A.5 per ADR-013. Without `Xrm.Navigation` (dev /
 * preview outside an MDA) it logs a warning and returns.
 *
 * Its own module (task 081 round 5, review R4-8) so the handler can be tested
 * behaviourally: `main.tsx` cannot be imported under jest (`import.meta.env`).
 * It delegates to the shared `openPlaybookLibrary`, which calls
 * `Xrm.Navigation.navigateTo` as a method — the Code Page used to call a
 * DETACHED `navigateTo`, which the platform rejects.
 */
import { openPlaybookLibrary } from "@spaarke/daily-briefing-components/utils";

export function browsePlaybooks(): void {
  openPlaybookLibrary("[DailyBriefing]");
}
