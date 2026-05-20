/**
 * dailyBriefing.registration.ts — SectionRegistration for the Daily Briefing section.
 *
 * Adds the AI-curated Daily Briefing as a selectable section in the Workspace
 * Layout Wizard's Step 2 (Section Selection) and renders inside the WorkspaceShell
 * via WorkspaceGrid's dynamic config builder.
 *
 * Implements FR-15. Available in both standalone LegalWorkspace AND the SpaarkeAi
 * WorkspacePane embed (FR-25) — because this is a LegalWorkspace section, both
 * consumers pick it up via the shared `SECTION_REGISTRY`.
 *
 * Constraints:
 *   - ADR-012: Section lives with LegalWorkspace sections (coupled to the
 *     LegalWorkspace section-registration contract).
 *   - ADR-013: Data is consumed via the existing BFF `/api/ai/daily-briefing/narrate`
 *     endpoint inside `useDailyBriefing` — no new BFF service.
 *   - ADR-014 / ADR-016 / ADR-028: TTL cache + rate-limit-aware error shape + auth
 *     are all encapsulated inside `useDailyBriefing`.
 *   - ADR-021: All styling via Fluent v9 tokens inside DailyBriefingSection.
 *   - ADR-025: Icon (`SparkleRegular`) sourced from `@fluentui/react-icons` v9.
 *
 * Default height: "medium" per FR-15 → mapped to "325px" to match sibling AI
 * "Latest Updates" content sections (visually consistent medium pane).
 */

import * as React from "react";
import { SparkleRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { DailyBriefingSection } from "./DailyBriefingSection";

// ---------------------------------------------------------------------------
// Registration
// ---------------------------------------------------------------------------

/**
 * Daily Briefing section registration.
 *
 * The factory is intentionally minimal — the section component owns its data
 * fetching (via `useDailyBriefing`) and renders without parent-scoped wiring.
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
