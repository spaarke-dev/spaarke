/**
 * latestUpdates.registration.ts — SectionRegistration for the Latest Updates section.
 *
 * Migrates the Latest Updates (ActivityFeed content) section from the monolithic
 * workspaceConfig to the dynamic Section Registry pattern (WKSP-001). The factory
 * constructs onOpenAll and onCreateNew handlers using SectionFactoryContext navigation
 * helpers instead of receiving them from the parent.
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9)
 */

import * as React from "react";
import { ClockRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { ActivityFeed } from "../components/ActivityFeed/ActivityFeed";

// ---------------------------------------------------------------------------
// Registration
// ---------------------------------------------------------------------------

export const latestUpdatesRegistration: SectionRegistration = {
  id: "latest-updates",
  label: "Latest Updates",
  description: "Recent activity feed with flagging",
  icon: ClockRegular,
  category: "data",
  defaultHeight: "325px",

  factory(context: SectionFactoryContext): ContentSectionConfig {
    const onOpenAll = () =>
      context.onNavigate({ type: "view", entity: "sprk_event" });

    const onCreateNew = () =>
      context.onOpenWizard("sprk_createeventwizard");

    return {
      id: "latest-updates",
      type: "content",
      title: "Latest Updates",
      style: {},
      renderContent: () =>
        React.createElement(ActivityFeed, {
          embedded: true,
          webApi: context.webApi as any,
          userId: context.userId,
          scope: context.scope,
          businessUnitId: context.businessUnitId,
          textOnlyFilter: true,
          gridLayout: true,
          hideOverflowMenu: true,
          onCountChange: context.onBadgeCountChange,
          onRefetchReady: context.onRefetchReady,
          onOpenAll,
          onCreateNew,
        }),
    };
  },
};

export default latestUpdatesRegistration;
