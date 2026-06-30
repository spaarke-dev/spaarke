/**
 * composeEditor.registration.ts — SectionRegistration for the Compose editor workspace.
 *
 * Task 040 of `spaarkeai-compose-r1` (FR-02). The Compose workspace layout
 * (system row created in task 010, label "Compose", template `single-column`,
 * section `compose-editor`) needs this registration to mount when selected
 * via the SpaarkeAi workspace picker.
 *
 * R1 scope (this file):
 *   - Register the `compose-editor` ID so `SECTION_REGISTRY.get('compose-editor')`
 *     resolves (FR-02 acceptance + task POML acceptance-criteria).
 *   - Render an inline Fluent v9 `Skeleton` placeholder so the build stays
 *     green and selecting the Compose layout mounts SOMETHING (not silent
 *     factory-lookup failure).
 *
 * Task 042 (Phase 4): replace `ComposeWorkspacePlaceholder` below with an import
 * from a proper shared package (per `plan.md` Phase 4: "Implement ComposeWorkspace.tsx
 * (TipTap host + toolbar wrapper)"). The placeholder is intentionally inline
 * here (not a separate `.tsx` file under `src/solutions/SpaarkeAi/`) for two
 * reasons:
 *   1. Calendar Pattern D precedent (calendar.registration.ts): a thin shim
 *      that delegates rendering to a widget. Mirrors that shape.
 *   2. Avoids a circular dependency: `@spaarke/legal-workspace` is consumed by
 *      SpaarkeAi; importing a SpaarkeAi-side file here would invert the
 *      dependency graph. When task 042 lands the real `ComposeWorkspaceWidget`,
 *      it will live in a shared lib (`@spaarke/compose-components` or similar
 *      per `plan.md` Phase 4) — the same way Calendar lives in
 *      `@spaarke/events-components`.
 *
 * Standalone LegalWorkspace impact: this registration loads in the standalone
 * bundle too (no separate registry), but renders only if a layout references
 * `compose-editor`. The Compose layout row (sprk_workspacelayoutid
 * c09d26be-e173-f111-ab0e-7ced8ddc4a05) is `sprk_issystem=true`; standalone
 * LegalWorkspace's layout picker exposes it only if the org has the row. The
 * placeholder is intentionally minimal so any accidental mount in standalone
 * shows a clear "Compose editor (placeholder)" message rather than crashing.
 * FR-25 / NFR-10 bundle-size impact is one small registration + Skeleton —
 * negligible.
 *
 * Hot-path coordination: SECTION_REGISTRY is shared with 8 other active
 * SpaarkeAi-touching projects (per `projects/INDEX.md`). The reviewer
 * sequences this addition against any concurrent SECTION_REGISTRY edits.
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9 + dark mode via
 *            semantic tokens), ADR-028 (Spaarke Auth v2 — no token re-acquisition
 *            inside the section).
 */

import * as React from "react";
import { EditRegular } from "@fluentui/react-icons";
import {
  Skeleton,
  SkeletonItem,
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Placeholder component (R1 stub — replaced in task 042 by the real TipTap
// editor widget loaded from a shared lib).
//
// Dark-mode compliance (ADR-021): uses Fluent v9 semantic tokens only
// (`colorNeutralBackground1`, `colorNeutralForeground1`,
// `colorNeutralForeground2`, `colorNeutralStroke1`). No hard-coded hex.
// ---------------------------------------------------------------------------

const usePlaceholderStyles = makeStyles({
  container: {
    height: "100%",
    width: "100%",
    boxSizing: "border-box",
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    padding: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalM,
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  header: {
    display: "flex",
    flexDirection: "column",
    rowGap: tokens.spacingVerticalXS,
  },
  caption: {
    color: tokens.colorNeutralForeground2,
  },
  skeletonArea: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    rowGap: tokens.spacingVerticalS,
  },
});

/**
 * Compose editor placeholder. Replaced by `ComposeWorkspace` in task 042.
 * Renders a Fluent v9 `Skeleton` shell that resembles a paragraph-style editor
 * surface so reviewers see editor-shaped chrome even before TipTap is wired.
 *
 * NOTE: uses `React.createElement` (NOT JSX) because this file is `.ts`, not
 * `.tsx` — matching the convention of every other registration file in this
 * directory (calendar, todo, getStarted, etc.). esbuild does not parse JSX in
 * `.ts` files. When task 042 lands the real `ComposeWorkspace` (likely as
 * `.tsx` inside a shared lib), this file remains the thin registration shim
 * and imports the component.
 *
 * EXPORTED so tests + future task-042 swap paths can reference the symbol.
 */
export const ComposeWorkspacePlaceholder: React.FC = () => {
  const styles = usePlaceholderStyles();
  return React.createElement(
    "div",
    {
      className: styles.container,
      role: "region",
      "aria-label": "Compose editor (placeholder)",
    },
    React.createElement(
      "div",
      { className: styles.header },
      React.createElement(
        Text,
        { weight: "semibold" as const, size: 500 },
        "Compose",
      ),
      React.createElement(
        Text,
        { size: 200, className: styles.caption },
        "Editor placeholder — the TipTap editor lands in task 042.",
      ),
    ),
    React.createElement(
      Skeleton,
      { className: styles.skeletonArea, appearance: "translucent" as const },
      React.createElement(SkeletonItem, { shape: "rectangle" as const, size: 16 }),
      React.createElement(SkeletonItem, {
        shape: "rectangle" as const,
        size: 16,
        style: { width: "92%" },
      }),
      React.createElement(SkeletonItem, {
        shape: "rectangle" as const,
        size: 16,
        style: { width: "78%" },
      }),
      React.createElement(SkeletonItem, {
        shape: "rectangle" as const,
        size: 16,
        style: { width: "85%" },
      }),
      React.createElement(SkeletonItem, {
        shape: "rectangle" as const,
        size: 16,
        style: { width: "60%" },
      }),
    ),
  );
};

// ---------------------------------------------------------------------------
// Registration — Pattern D thin shim (mirrors calendar.registration.ts)
//
// Factory does NOT consume any `SectionFactoryContext` fields in R1 — the
// placeholder is self-contained. Task 042 will thread `ctx.bffBaseUrl`,
// `ctx.userId`, etc., into the real widget when it needs to call the Compose
// BFF endpoints (added in Phase 2, tasks 020-027).
// ---------------------------------------------------------------------------

export const composeEditorRegistration: SectionRegistration = {
  id: "compose-editor",
  label: "Compose",
  description: "TipTap editor for drafting and revising documents",
  icon: EditRegular,
  category: "productivity",
  // Compose is the dominant section in the Compose layout (single-column row 1
  // per task 010 sectionsjson). Give it a generous default height to match
  // a typical editor surface; the layout's row sizing still wins.
  defaultHeight: "720px",

  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "compose-editor",
      type: "content",
      title: "Compose",
      style: { overflow: "hidden" },
      renderContent: () => React.createElement(ComposeWorkspacePlaceholder),
    };
  },
};

export default composeEditorRegistration;
