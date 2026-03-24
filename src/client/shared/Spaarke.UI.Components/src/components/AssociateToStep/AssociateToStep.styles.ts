/**
 * AssociateToStep.styles.ts
 * Fluent UI v9 makeStyles definitions for AssociateToStep.
 *
 * All values use semantic design tokens — no hard-coded colors.
 * Dark mode is supported automatically via token resolution.
 *
 * @see ADR-021 — Fluent UI v9 design system; semantic tokens required
 */

import { makeStyles, tokens } from "@fluentui/react-components";

export const useAssociateToStepStyles = makeStyles({
  // ── Outer container ──────────────────────────────────────────────────────
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
  },

  // ── Header section (title + subtitle) ───────────────────────────────────
  header: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },

  title: {
    color: tokens.colorNeutralForeground1,
  },

  subtitle: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Form row: dropdown + button side-by-side ────────────────────────────
  formRow: {
    display: "flex",
    alignItems: "flex-end",
    gap: tokens.spacingHorizontalM,
    flexWrap: "wrap",
  },

  dropdownWrapper: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    flex: 1,
    minWidth: "160px",
    maxWidth: "300px",
  },

  fieldLabel: {
    color: tokens.colorNeutralForeground2,
  },

  // ── Selected record display card ─────────────────────────────────────────
  selectedRecord: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },

  selectedIcon: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },

  selectedName: {
    flex: 1,
    color: tokens.colorNeutralForeground1,
  },

  selectedType: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Helper text (link records later) ────────────────────────────────────
  skipHint: {
    color: tokens.colorNeutralForeground3,
  },
});
