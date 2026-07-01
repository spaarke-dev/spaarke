/**
 * getStarted.registration.ts — SectionRegistration for the Get Started action-cards section.
 *
 * Migrates the Get Started section from hardcoded workspaceConfig.tsx into a
 * self-contained registration. The factory builds cards, click handlers, and
 * toolbar internally using SectionFactoryContext — no parent-scoped callbacks.
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9 icons)
 */

import * as React from "react";
import { Button } from "@fluentui/react-components";
import { OpenRegular, RocketRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ActionCardSectionConfig,
} from "@spaarke/ui-components";
import { ACTION_CARD_CONFIGS } from "../components/GetStarted/getStartedConfig";

/**
 * Section registration for Get Started (action-cards).
 *
 * The factory wires all 9 card click handlers using `ctx.onOpenWizard`:
 *   - create-new-matter       → sprk_creatematterwizard
 *   - create-new-project      → sprk_createprojectwizard
 *   - assign-to-counsel       → sprk_createworkassignmentwizard
 *   - create-new-todo         → sprk_createtodowizard
 *   - summarize-new-files     → sprk_summarizefileswizard
 *   - find-similar            → sprk_findsimilar
 *   - send-email-message      → sprk_playbooklibrary (intent=email-compose)
 *   - schedule-new-meeting    → sprk_playbooklibrary (intent=meeting-schedule)
 *   - browse-playbooks        → sprk_playbooklibrary (NO intent → browse mode)
 *                               R7 Wave 9 task 096 / FR-18 surface 3 of 3.
 *                               Mirrors task 094 (/playbooks hard slash in chat) and
 *                               task 095 (Browse Playbooks overflow on Daily Briefing).
 *                               Pattern parity: all three surfaces open the same
 *                               PlaybookLibraryShell in browse mode with
 *                               sprk_playbookconsumer chips per card grid row.
 *
 * Toolbar: a single "Open Playbook Library" expand button.
 */
export const getStartedRegistration: SectionRegistration = {
  id: "get-started",
  label: "Get Started",
  description: "Quick-action cards for common workflows",
  icon: RocketRegular,
  category: "overview",
  defaultHeight: "200px",

  factory: (ctx: SectionFactoryContext): ActionCardSectionConfig => {
    // Map ACTION_CARD_CONFIGS to the shared ActionCardConfig shape
    const cards = ACTION_CARD_CONFIGS.map((c) => ({
      id: c.id,
      label: c.label,
      icon: c.icon,
      ariaLabel: c.ariaLabel,
    }));

    // Card click handler map — each card wires itself using ctx.onOpenWizard
    const onCardClick: Partial<Record<string, () => void>> = {
      "create-new-matter": () =>
        ctx.onOpenWizard("sprk_creatematterwizard"),
      "create-new-project": () =>
        ctx.onOpenWizard("sprk_createprojectwizard"),
      "assign-to-counsel": () =>
        ctx.onOpenWizard("sprk_createworkassignmentwizard"),
      "create-new-todo": () =>
        ctx.onOpenWizard("sprk_createtodowizard"),
      "summarize-new-files": () =>
        ctx.onOpenWizard("sprk_summarizefileswizard"),
      "find-similar": () =>
        ctx.onOpenWizard("sprk_findsimilar"),
      "send-email-message": () =>
        ctx.onOpenWizard("sprk_playbooklibrary", "intent=email-compose"),
      "schedule-new-meeting": () =>
        ctx.onOpenWizard("sprk_playbooklibrary", "intent=meeting-schedule"),
      // Browse mode — NO intent param → PlaybookLibraryShell renders full card grid
      // with consumer-mapping chips (FR-18 surface 3 of 3). R7 Wave 9 task 096.
      "browse-playbooks": () =>
        ctx.onOpenWizard("sprk_playbooklibrary"),
    };

    // Toolbar: expand button opens the Get Started expand dialog (all 9 cards)
    const toolbar = React.createElement(
      Button,
      {
        appearance: "subtle" as const,
        size: "small" as const,
        icon: React.createElement(OpenRegular),
        onClick: () => ctx.onExpandSection?.("get-started"),
        "aria-label": "Show all actions",
      },
    );

    return {
      id: "get-started",
      type: "action-cards",
      title: "Get Started",
      cards,
      onCardClick,
      toolbar,
      maxVisible: 5,
      style: {},
    };
  },
};

export default getStartedRegistration;
