/**
 * reconciliation.registration.ts — SectionRegistration for the Reconciliation
 * workspace section.
 *
 * email-communication-intelligence-r2 UAT Fix #6: LegalWorkspace section shim for
 * the email-reconciliation surface, so "Reconciliation" appears as a system widget
 * in the SpaarkeAi workspace dropdown alongside Matter / Project / Calendar / Email
 * (each of those is a single-widget `sprk_workspacelayout` row that mounts ONE
 * LegalWorkspace section by id — see `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`
 * §3.1). The matching `sprk_workspacelayout` "Reconciliation" row (sections:["reconciliation"])
 * is seeded by `scripts/seed-reconciliation-workspace-layout.ps1`.
 *
 * Renders the SAME shared `ReconciliationWorkspace` composition root (task 061,
 * `@spaarke/communication-components`) as the standalone `sprk_communicationreconciliation`
 * code page and the SpaarkeAi `communications-reconciliation` direct widget — Pattern D
 * dual-use, mirroring `email.registration.ts`. The row → props wiring
 * (`buildResolveReview` ADR-024 write path, `resolveRegarding` NFR-10 gate,
 * `RECONCILIATION_VIEWS` view-switcher) is the SHARED copy extracted to the lib in
 * Fix #6, so all three mounts stay identical.
 *
 * `ReconciliationWorkspace` is host-agnostic (ADR-012): this shim resolves the Xrm-backed
 * `dataverseClient` / `webApi` + the LegalWorkspace-local `authenticatedFetch` the same way
 * `email.registration.ts` does. Declared `React.FC` with `React.createElement` per this
 * directory's `.ts`-not-`.tsx` convention (esbuild does not parse JSX in `.ts`).
 *
 * Standards: ADR-012 (shared lib widget), ADR-021 (Fluent v9), ADR-022 (React-version-agnostic),
 *            ADR-028 (function-based auth contract), ADR-039/BFF §10 (surface identity in code).
 */

import * as React from "react";
import { TaskListSquareLtrRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { XrmDataverseClient, getXrm } from "@spaarke/ui-components";
import {
  ReconciliationWorkspace,
  RECONCILIATION_VIEWS,
  buildResolveReview,
  resolveRegarding,
  type EmailWorkspaceWebApi,
} from "@spaarke/communication-components";
import { authenticatedFetch } from "../services/authInit";

// ---------------------------------------------------------------------------
// ReconciliationSectionMount — resolves the Xrm-backed host adapters
// `ReconciliationWorkspace` requires and forwards the SHARED resolver wiring.
// Mirrors `EmailSectionMount` in `email.registration.ts`.
// ---------------------------------------------------------------------------
const ReconciliationSectionMount: React.FC = () => {
  const dataverseClient = React.useMemo(() => new XrmDataverseClient(), []);
  // Xrm.WebApi bridge — satisfies both the ADR-024 additive regarding write and the
  // embedded "Change / Link another" picker (same shape `email.registration.ts` passes).
  const webApi = React.useMemo(() => getXrm()?.WebApi as EmailWorkspaceWebApi | undefined, []);
  // UAT Fix #3: association-confirm does NOT remount — ReconciliationWorkspace refreshes the
  // confirmed row in-place — so the host callback is a no-op (kept for the review contract).
  const handleAssociationsChanged = React.useCallback(() => {}, []);
  const resolveReview = React.useMemo(
    () => (webApi ? buildResolveReview(webApi, handleAssociationsChanged) : undefined),
    [webApi, handleAssociationsChanged],
  );

  if (!webApi) {
    // No Dataverse host available — fail closed rather than mount a partially-wired
    // component (mirrors `XrmDataverseClient`'s throw-at-first-call contract).
    return null;
  }

  // Bounded-height host (owner UAT 2026-08-03 R5 item 1) — same pattern as the Email
  // section: a viewport FLOOR + CAP pins the surface so the grid + browse shell scroll
  // within a definite box rather than growing to fit all content.
  return React.createElement(
    "div",
    {
      "data-testid": "reconciliation-section-scroll-host",
      style: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        width: "100%",
        minWidth: 0,
        minHeight: "calc(100vh - 200px)",
        maxHeight: "calc(100vh - 200px)",
        overflow: "hidden",
      },
    },
    React.createElement(ReconciliationWorkspace, {
      dataverseClient,
      authenticatedFetch,
      resolveReview,
      resolveRegarding,
      views: RECONCILIATION_VIEWS,
    }),
  );
};

export const reconciliationRegistration: SectionRegistration = {
  id: "reconciliation",
  label: "Reconciliation",
  description: "Review and reconcile inbound email associations, triage, and follow-ups",
  icon: TaskListSquareLtrRegular,
  category: "data",
  // Tall grid + browse-shell surface — same rationale as Email (720px).
  defaultHeight: "720px",

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "reconciliation",
      type: "content",
      title: "Reconciliation",
      style: { overflow: "hidden" },
      renderContent: () => React.createElement(ReconciliationSectionMount),
    };
  },
};

export default reconciliationRegistration;
