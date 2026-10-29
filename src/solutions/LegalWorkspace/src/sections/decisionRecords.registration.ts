/**
 * decisionRecords.registration.ts — SectionRegistration for the Decision Records workspace section.
 *
 * spaarke-ontology-platform-r1 task 045 (D-47): the Console's Decision Record tab. Like the Worklist (task 156), the workspace dropdown
 * lists `sprk_workspacelayout` rows and each row mounts ONE LegalWorkspace section by id, so this section shim is what a
 * "Decision Records" layout row (sections:["decisionRecords"], seeded by `scripts/seed-decision-records-workspace-layout.ps1`)
 * mounts. It copies `worklist.registration.ts`.
 *
 * Renders the shared `DecisionRecordsWidget` (`@spaarke/ui-components`) with a service from `createWorklistService({ fetch, baseUrl })`
 * (D-15): the Signal read route (`?set=decided`) over LegalWorkspace's `authenticatedFetch` and `getBffBaseUrl()`. The route reads every
 * record AS THE CALLER, so a Decision Record on a Secure matter reaches only that matter's members and a no-core item only its owner
 * (D-33, D-35, D-123). This shim never reads a `sprk_signal` or `sprk_decisionrecord` row, through Xrm or Dataverse.
 *
 * Open launches the decision wizard through `launchDecisionWizard` (D-26), the in-app host the Console mounts once in `ThreePaneShell`.
 * In the standalone LegalWorkspace code page (no `DecisionWizardHost`) the launcher returns `{ opened: false, reason: 'no-host' }` and
 * the widget shows its notice; no host is added here. The wizard's Person / Lookup search is not wired: the wizard opens READ-ONLY on a
 * decided item and searches nothing.
 *
 * Declared with `React.createElement` per this directory's `.ts`-not-`.tsx` convention (esbuild does not parse JSX in `.ts`).
 *
 * Standards: ADR-012 (shared lib widget), ADR-021 (Fluent v9), ADR-022, ADR-028 (function-based auth contract).
 */

import * as React from "react";
import { tokens } from "@fluentui/react-components";
import { DocumentBulletListClockRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import {
  DecisionRecordsWidget,
  createWorklistService,
  launchDecisionWizard,
} from "@spaarke/ui-components";
import { getBffBaseUrl } from "../config/runtimeConfig";
import { authenticatedFetch } from "../services/authInit";

const DecisionRecordsSectionMount: React.FC = () => {
  const service = React.useMemo(
    () => createWorklistService({ fetch: authenticatedFetch, baseUrl: getBffBaseUrl() }),
    [],
  );

  // The Console's workspace tab scrolls as a whole; the list is a document-like page, so the host only pads it
  // (the same padding the Worklist section uses).
  return React.createElement(
    "div",
    {
      "data-testid": "decision-records-section-host",
      style: {
        width: "100%",
        minWidth: 0,
        boxSizing: "border-box",
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalL,
      },
    },
    React.createElement(DecisionRecordsWidget, { service, launchWizard: launchDecisionWizard }),
  );
};

export const decisionRecordsRegistration: SectionRegistration = {
  id: "decisionRecords",
  label: "Decision Records",
  description: "Decisions made on worklist items - open one to see it as it was recorded",
  icon: DocumentBulletListClockRegular,
  category: "data",
  // Document-like, scrolling surface — same viewport floor as the Worklist.
  defaultHeight: "720px",

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "decisionRecords",
      type: "content",
      title: "Decision Records",
      style: { overflow: "hidden" },
      renderContent: () => React.createElement(DecisionRecordsSectionMount),
    };
  },
};

export default decisionRecordsRegistration;
