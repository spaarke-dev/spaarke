/**
 * calendar.registration.ts — SectionRegistration for the Calendar workspace.
 *
 * Task 115 (Round 9, 2026-05-22). The "Calendar" system workspace surfaces
 * all events + tasks the user has access to via the shared
 * `@spaarke/events-components` library (hoisted in task 114). The actual
 * widget lives in the shared lib (`CalendarWorkspaceWidget`) so that:
 *
 *   - Standalone EventsPage code page (`sprk_eventspage`) and this embedded
 *     widget consume the same Events components — architectural unity.
 *   - The section factory in LegalWorkspace stays a thin REGISTRATION shim,
 *     mirroring the dailyBriefing pattern from task 069 / 110.
 *   - No LegalWorkspace-internal coupling is introduced (per the
 *     componentization audit in task 113 + `BUILD-A-NEW-WORKSPACE-WIDGET.md`).
 *
 * Side-pane behavior is intentionally overridden inside the widget:
 * `Xrm.Navigation.navigateTo` modal at 80% × 80% instead of
 * `Xrm.App.sidePanes`. See `CalendarWorkspaceWidget.tsx` for the seam.
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9 tokens),
 *            ADR-022 (React 19), ADR-028 (auth via Xrm.WebApi).
 */

import * as React from "react";
import { CalendarLtr24Regular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { CalendarWorkspaceWidget } from "@spaarke/events-components";

/**
 * Calendar section registration (LegalWorkspace shim).
 *
 * Delegates rendering entirely to `CalendarWorkspaceWidget` from
 * `@spaarke/events-components`. No factory-context props are forwarded —
 * the widget is self-contained (uses `Xrm.WebApi` directly via the shared
 * services hoisted in task 114).
 */
export const calendarRegistration: SectionRegistration = {
  id: "calendar",
  label: "Calendar",
  description: "All events + tasks you have access to",
  icon: CalendarLtr24Regular,
  category: "data",
  // Tall section — the widget houses a 3-month strip, toolbar, view selector,
  // AND grid. Avoid clipping by giving the section room to breathe.
  defaultHeight: "720px",

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "calendar",
      type: "content",
      title: "Calendar",
      style: { overflow: "hidden" },
      renderContent: () => React.createElement(CalendarWorkspaceWidget),
    };
  },
};

export default calendarRegistration;
