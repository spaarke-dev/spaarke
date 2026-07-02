/**
 * composeEditor.registration.ts — SectionRegistration for the Compose editor workspace.
 *
 * Task 040 of `spaarkeai-compose-r1` (FR-02) created this file as an inline
 * Skeleton placeholder pending shared-lib packaging. Task 093 (Phase 7 pivot,
 * 2026-07-01) swaps the placeholder for the REAL `<ComposeWorkspace>` widget
 * from `@spaarke/compose-components` (moved there in task 091), resolving
 * FU-3 (`projects/spaarkeai-compose-r1/notes/defer-issues.md`).
 *
 * The Compose workspace layout (system row created in task 010, label
 * "Compose", template `single-column`, section `compose-editor`) mounts here
 * when selected via the SpaarkeAi workspace picker OR when the ribbon
 * "Open in Compose" modal launches SpaarkeAi with `?composeMode=editor`
 * (task 092's App.tsx canonical mount).
 *
 * Document context threading:
 *   The section factory consumes `useComposeLaunch()` from
 *   `@spaarke/compose-components` (hoisted from SpaarkeAi's ThreePaneShell in
 *   task 093). When SpaarkeAi's ThreePaneShell provides a value (Path A
 *   modal launch), the document ref + drive id flow through to
 *   ComposeWorkspace's `initialDocumentRef` / `driveId` props. When the value
 *   is null (standalone LegalWorkspace mount or user-picked the Compose
 *   layout without a document context), the workspace opens on its empty
 *   state — user browses/searches for a document via the empty-state
 *   affordances (see `ComposeEmptyState` — task 044).
 *
 * Component justification (CLAUDE.md §11):
 *   Existing: `ComposeWorkspacePlaceholder` (Skeleton stub) — replaced.
 *   Extension: not applicable — swap-in of the real widget is the
 *   deliverable per POML 093 + FR-S1 supplement scope.
 *   Cost-of-doing-nothing: users selecting the "Compose" workspace layout
 *   see a stub instead of the editor; the ribbon Path A → three-pane flow
 *   ends at a Skeleton; FR-S1 fails.
 *
 * Bundle-size impact:
 *   Task 092 measured SpaarkeAi Vite bundle at 3991 kB (gzip 1088 kB) with
 *   ComposeWorkspace tree-shaken. This change re-eagerises the compose
 *   chain (TipTap StarterKit + 11 extensions + mammoth + docx) via the
 *   section factory — expect bundle to grow back to ~4877 kB (gzip 1357 kB)
 *   pre-091 baseline, or slightly higher due to task 092's
 *   ComposeLaunchContext + task 093's mount bridge. This is EXPECTED and
 *   was accounted for in the task 092 completion notes.
 *
 * Standalone LegalWorkspace impact:
 *   This registration loads in the standalone bundle too. The Compose
 *   layout row (`sprk_workspacelayoutid=c09d26be-e173-f111-ab0e-7ced8ddc4a05`)
 *   is `sprk_issystem=true`; standalone LegalWorkspace's layout picker
 *   exposes it only if the org has the row. `useComposeLaunch()` returns
 *   null there (no ThreePaneShell in the tree), so ComposeWorkspace opens
 *   on the empty state — user picks a document via Browse / Search
 *   affordances. The full editor + save + summarize path works from that
 *   entry.
 *
 * Convention note:
 *   The file remains `.ts` (not `.tsx`) matching the convention of every
 *   other registration file in this directory (calendar, todo, getStarted,
 *   etc.). esbuild does not parse JSX in `.ts` files, so React tree
 *   construction uses `React.createElement`. The inner mount bridge
 *   `ComposeSectionMount` is declared as `React.FC` to keep the hook call
 *   inside a functional component (React rule of hooks).
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9 tokens +
 *            dark mode via semantic tokens), ADR-028 (Spaarke Auth v2 —
 *            ComposeWorkspace internally uses `authenticatedFetch` from
 *            `@spaarke/auth`; no token re-acquisition here).
 */

import * as React from "react";
import { EditRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import {
  ComposeWorkspace,
  useComposeLaunch,
} from "@spaarke/compose-components";

// ---------------------------------------------------------------------------
// ComposeSectionMount — inner functional component that bridges the Section
// factory context (bffBaseUrl) with the ComposeLaunchContext (document ref +
// drive id from the ribbon modal launcher) into ComposeWorkspace props.
//
// Kept inside this file (not moved to a separate module) so the registration
// stays a single-file Pattern D shim — matches the calendar / dailyBriefing
// registration file shape. The functional-component wrapper is required to
// call `useComposeLaunch()` per React's rule of hooks.
//
// Bridge fields:
//   - bffBaseUrl        — always from factory context (workspace-scoped)
//   - initialDocumentRef — from ComposeLaunchContext.document (null on
//                          standalone / picker mount → empty state)
//   - driveId           — from ComposeLaunchContext.driveId (empty string
//                          when no launch context → ComposeWorkspace's
//                          Load call resolves at runtime)
//   - tenantId          — empty string (not present on either context; BFF
//                          authorizes via auth claims, not this prop)
//   - initialSessionId  — empty string (fresh ChatSession per mount by
//                          convention; matches SpaarkeAi's pre-093 Path A
//                          direct-mount shape)
// ---------------------------------------------------------------------------

interface ComposeSectionMountProps {
  bffBaseUrl: string;
}

const ComposeSectionMount: React.FC<ComposeSectionMountProps> = ({ bffBaseUrl }) => {
  const composeLaunch = useComposeLaunch();
  return React.createElement(ComposeWorkspace, {
    bffBaseUrl,
    driveId: composeLaunch?.driveId ?? "",
    tenantId: "",
    initialDocumentRef: composeLaunch?.document ?? null,
    initialSessionId: "",
  });
};

// ---------------------------------------------------------------------------
// Registration — Pattern D thin shim (mirrors calendar.registration.ts)
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

  factory(context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "compose-editor",
      type: "content",
      title: "Compose",
      style: { overflow: "hidden" },
      renderContent: () =>
        React.createElement(ComposeSectionMount, {
          bffBaseUrl: context.bffBaseUrl,
        }),
    };
  },
};

export default composeEditorRegistration;
