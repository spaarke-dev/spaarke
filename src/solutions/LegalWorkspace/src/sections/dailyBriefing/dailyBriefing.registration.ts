/**
 * dailyBriefing.registration.ts — LegalWorkspace re-export shim.
 *
 * Hoisted to `@spaarke/ui-components` in task 069 as a FACTORY
 * (`createDailyBriefingRegistration`). This shim preserves the pre-069
 * STATIC export shape `dailyBriefingRegistration: SectionRegistration` so
 * `sectionRegistry.ts` and `sections/index.ts` continue working unchanged.
 *
 * The shim uses the LegalWorkspace-LOCAL `DailyBriefingSection` shim
 * (which already closes over local `authenticatedFetch` + `trackEvent`),
 * so no auth deps are passed to the factory pattern — the local component
 * wrapper handles all wiring identically to the pre-069 implementation.
 * This preserves FR-25 / NFR-10 (standalone byte-identical render).
 *
 * See task 067/069 hoist precedent and:
 *   - `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/sections/dailyBriefing/dailyBriefing.registration.ts`
 *   - ADR-012 (shared components).
 */

import * as React from "react";
import { SparkleRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { DailyBriefingSection } from "./DailyBriefingSection";

/**
 * Daily Briefing section registration (LegalWorkspace shim).
 *
 * Mirrors the pre-069 static const shape exactly. The factory delegates
 * rendering to the local `DailyBriefingSection` shim, which closes over
 * LegalWorkspace-local `authenticatedFetch` + tenant ID resolver + local
 * `trackEvent` telemetry — preserving the standalone behavior byte-for-byte.
 */
export const dailyBriefingRegistration: SectionRegistration = {
  id: "daily-briefing",
  label: "Daily Briefing",
  description: "AI-curated highlights from your day",
  icon: SparkleRegular,
  category: "ai",
  // "medium" per FR-15 — mapped to 325px (matches Latest Updates sibling)
  defaultHeight: "325px",

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "daily-briefing",
      type: "content",
      title: "Daily Briefing",
      style: {},
      renderContent: () => React.createElement(DailyBriefingSection),
    };
  },
};

export default dailyBriefingRegistration;
