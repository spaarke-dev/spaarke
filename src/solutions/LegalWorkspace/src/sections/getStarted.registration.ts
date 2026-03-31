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
 * The factory wires all 7 card click handlers using `ctx.onOpenWizard`:
 *   - create-new-matter       → sprk_creatematterwizard
 *   - create-new-project      → sprk_createprojectwizard
 *   - assign-to-counsel       → sprk_createworkassignmentwizard
 *   - summarize-new-files     → sprk_summarizefileswizard
 *   - find-similar            → sprk_findsimilar
 *   - send-email-message      → sprk_playbooklibrary (intent=email-compose)
 *   - schedule-new-meeting    → sprk_playbooklibrary (intent=meeting-schedule)
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
      "summarize-new-files": () =>
        ctx.onOpenWizard("sprk_summarizefileswizard"),
      "find-similar": () =>
        ctx.onOpenWizard("sprk_findsimilar"),
      "send-email-message": () =>
        ctx.onOpenWizard("sprk_playbooklibrary", "intent=email-compose"),
      "schedule-new-meeting": () =>
        ctx.onOpenWizard("sprk_playbooklibrary", "intent=meeting-schedule"),
    };

    // Toolbar: expand button opens the Playbook Library dialog
    const toolbar = React.createElement(
      Button,
      {
        appearance: "subtle" as const,
        size: "small" as const,
        icon: React.createElement(OpenRegular),
        onClick: () => ctx.onOpenWizard("sprk_playbooklibrary"),
        "aria-label": "Open Playbook Library",
      },
    );

    return {
      id: "get-started",
      type: "action-cards",
      title: "Get Started",
      cards,
      onCardClick,
      toolbar,
      maxVisible: 4,
      style: { minHeight: "200px" },
    };
  },
};

export default getStartedRegistration;
