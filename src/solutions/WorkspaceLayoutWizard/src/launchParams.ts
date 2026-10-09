/**
 * launchParams.ts — the Workspace Layout Wizard's launch `data` contract.
 *
 * Extracted from `main.tsx` (task 113, ontology-platform-r1 D-26) so the code page and the in-app
 * host parse the SAME `mode=…&layoutId=…&bffBaseUrl=…` string with the SAME rules. Pure: no Xrm,
 * no DOM, no side effects (importing it must never mount the code page).
 */

import type { LayoutTemplateId } from "@spaarke/ui-components";

/** Wizard mode determines the wizard behavior */
export type WizardMode = "create" | "edit" | "saveAs";

/** Parsed data parameters from the URL / Xrm.Page.data */
export interface DataParams {
  mode: WizardMode;
  layoutId: string | null;
  /** Template ID from the source layout (saveAs mode) */
  layoutTemplateId: string | null;
  /** JSON-encoded sections from the source layout (saveAs mode) */
  sectionsJson: string | null;
  /** Display name of the source layout (saveAs mode) */
  sourceName: string | null;
  /**
   * Optional comma-separated list of `LayoutTemplateId` values used by the
   * SpaarkeAi `WorkspacePaneMenu` (task 032) to restrict the wizard's Step 1
   * template selector to a 6-template subset per FR-14. When absent the
   * wizard renders all 9 canonical templates for FR-25 backwards-compat.
   *
   * Values are NOT validated here against the `LayoutTemplateId` union —
   * `TemplateStep` simply skips any IDs that aren't in `LAYOUT_TEMPLATES`,
   * so an unknown value is a no-op (visually equivalent to absence).
   */
  templateFilter: readonly LayoutTemplateId[] | undefined;
  /**
   * Optional step id to open the wizard at (R2 UAT §3.1 + §4.1). Accepts
   * "choose-layout" | "configure-sections" | "review-save". When absent,
   * App.tsx defaults to review-save for edit/saveAs and to first step for
   * create. Used by SpaarkeAi gear-icon flow to force Choose Layout on edit.
   */
  startAtStep: string | null;
}

/**
 * Parse the launch data string.
 * Expected format: "mode=create" or "mode=edit&layoutId=<guid>"
 * SaveAs format: "mode=saveAs&layoutId=<guid>&layoutTemplateId=<id>&sectionsJson=<json>&name=<name>"
 */
export function parseLayoutWizardData(dataString: string): DataParams {
  const parsed = new URLSearchParams(dataString);
  const modeParam = parsed.get("mode");
  const mode: WizardMode =
    modeParam === "edit" || modeParam === "saveAs" ? modeParam : "create";
  const layoutId = parsed.get("layoutId") || null;
  const layoutTemplateId = parsed.get("layoutTemplateId") || null;
  const sectionsJson = parsed.get("sectionsJson") || null;
  const sourceName = parsed.get("name") || null;

  // ---------------------------------------------------------------------------
  // templateFilter — optional comma-separated LayoutTemplateId list (FR-14)
  //
  // When present, restricts Step 1's template selector to the listed IDs.
  // When absent / empty, undefined is returned so the wizard renders all 9
  // canonical templates (FR-25 backwards-compat for standalone LegalWorkspace).
  // The value is cast to `readonly LayoutTemplateId[]` — TemplateStep is
  // already defensive against unknown IDs (it intersects with LAYOUT_TEMPLATES).
  // ---------------------------------------------------------------------------
  const templateFilterRaw = parsed.get("templateFilter") || "";
  const templateFilter: readonly LayoutTemplateId[] | undefined =
    templateFilterRaw
      ? (templateFilterRaw
          .split(",")
          .map((s) => s.trim())
          .filter((s) => s.length > 0) as readonly LayoutTemplateId[])
      : undefined;

  // startAtStep — R2 UAT §3.1 + §4.1 (2026-07-03). Passed through as opaque
  // string; App.tsx validates against its STEP_ constants.
  const startAtStep = parsed.get("startAtStep") || null;

  return { mode, layoutId, layoutTemplateId, sectionsJson, sourceName, templateFilter, startAtStep };
}
