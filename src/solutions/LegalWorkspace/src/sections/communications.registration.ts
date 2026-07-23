/**
 * communications.registration.ts — SectionRegistration for the Communications workspace section.
 *
 * ai-spaarke-ai-workspace-UI-r2 FR-09 (2026-07-01): fifth Dataverse-entity-view
 * section, originally sharing `<DataverseEntityViewWidget>` with documents/
 * matters/projects/invoices/workAssignments.
 *
 * messaging-communication-app-r2 task 030 (FR-12, 2026-07-19): UPGRADED IN
 * PLACE — `renderContent` now mounts the rich Pattern D
 * `CommunicationsWorkspaceWidget` (filter-chip toolbar + card strip + embedded
 * DataGrid) from the NEW shared lib `@spaarke/communication-components`,
 * instead of the bare `DataverseEntityViewWidget`. Section id
 * (`communications`), `widgetType` (`communications-list`), and the reused
 * `sprk_gridconfiguration` GUID are UNCHANGED (NFR-05 — single default, no
 * second widget/config). Per-instance `pageSize`/`availableViews` overrides
 * are no longer forwarded here — the rich widget owns its own grid wiring
 * (channel + date filter chips compose with the framework's own config-driven
 * chip/view resolution); a future task can re-add override plumbing to
 * `CommunicationsWorkspaceWidgetProps` if a per-instance need arises.
 *
 * Pattern D (dual-use): also registered as Direct widget `communications-list`
 * in `@spaarke/ai-widgets/widgets/workspace/register-workspace-widgets.ts`.
 *
 * Row-click behavior: Layout 1 (OOB modal via `Xrm.Navigation.navigateTo` at
 * 85% × 85%) via the DataGrid framework's `defaultRecordOpen` — unified in
 * Phase 1 (task 002) per FR-03/FR-20; `CommunicationsWorkspaceWidget` does not
 * override it (see that file's module docblock).
 *
 * Standards: ADR-012 (shared lib widget), ADR-021 (Fluent v9), ADR-022 (React 19),
 *            ADR-028 (Xrm.WebApi — no token snapshots).
 */

import * as React from "react";
import { MailRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { CommunicationsWorkspaceWidget } from "@spaarke/communication-components";

/**
 * GUID of the `sprk_gridconfiguration` Dataverse row for the Communications view.
 * Created 2026-07-01 by ai-spaarke-ai-workspace-UI-r2 task 010; see
 * `projects/ai-spaarke-ai-workspace-UI-r2/notes/communications-config-record.md`.
 * REUSED (not cloned) by messaging-communication-app-r2 task 030 — NFR-05.
 */
// spaarkedev1 sprk_gridconfiguration: 'Active Communications (Workspace)' (created 2026-07-01)
const COMMUNICATIONS_CONFIG_ID = "e1826c4c-9575-f111-ab0e-7ced8ddc4a05";

export const communicationsRegistration: SectionRegistration = {
  id: "communications",
  label: "Communications",
  description: "Email, Teams, SMS, and notifications related to your work",
  icon: MailRegular,
  category: "data",
  defaultHeight: "480px",
  // spaarke-dataset-grid-framework-r2 FR-08 (task 005, 2026-07-02): replaces the
  // prior tactical 80vh clamp wrapper inside renderContent. The framework now
  // applies the clamp to the SectionPanel via `contentSizing: 'clamped'` (see
  // `buildDynamicWorkspaceConfig`), producing visible Fluent scrollbars +
  // lazy-load-on-scroll behavior for the dense entity-list grid.
  contentSizing: "clamped",

  factory(context: SectionFactoryContext): ContentSectionConfig {
    // spaarke-dataset-grid-framework-r2 DEF-005 / DEF-005b+c (2026-07-02, FR-03
    // end-to-end wiring): honor a per-instance configId override from the
    // LayoutJsonRow SectionInstance. Bare-string entries + omitted overrides
    // fall through to the baked-in default configId.
    const effectiveConfigId =
      context.sectionInstance?.configIdOverride ?? COMMUNICATIONS_CONFIG_ID;
    return {
      id: "communications",
      type: "content",
      title: "Communications",
      style: { overflow: "hidden" },
      renderContent: () =>
        React.createElement(CommunicationsWorkspaceWidget, {
          configId: effectiveConfigId,
        }),
    };
  },
};

export default communicationsRegistration;
