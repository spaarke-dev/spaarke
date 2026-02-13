/**
 * Grade utility functions for Report Card Metric Card
 * Converts grade values, resolves colors, handles template substitution
 */

import { tokens } from "@fluentui/react-components";
import * as React from "react";
import {
  GavelRegular,
  MoneyRegular,
  TargetRegular,
  QuestionCircleRegular,
} from "@fluentui/react-icons";

// ============= Interfaces =============

export interface IColorRule {
  range: [number, number];
  color: "blue" | "yellow" | "red";
}

export type GradeColorScheme = "blue" | "yellow" | "red" | "neutral";

export interface IGradeColorTokens {
  cardBackground: string;
  borderAccent: string;
  gradeText: string;
  iconColor: string;
  contextText: string;
  labelColor: string;
}

// ============= Constants =============

export const DEFAULT_COLOR_RULES: IColorRule[] = [
  { range: [0.85, 1.00], color: "blue" },
  { range: [0.70, 0.84], color: "yellow" },
  { range: [0.00, 0.69], color: "red" },
];

// ============= Grade Conversion =============

export function gradeValueToLetter(value: number | null): string {
  if (value === null || value === undefined) return "N/A";
  const clamped = Math.max(0, Math.min(1, value));
  if (clamped >= 1.00) return "A+";
  if (clamped >= 0.95) return "A";
  if (clamped >= 0.90) return "B+";
  if (clamped >= 0.85) return "B";
  if (clamped >= 0.80) return "C+";
  if (clamped >= 0.75) return "C";
  if (clamped >= 0.70) return "D+";
  if (clamped >= 0.65) return "D";
  return "F";
}

export function gradeValueToPercent(value: number | null): string {
  if (value === null || value === undefined) return "N/A";
  return Math.round(value * 100).toString();
}

// ============= Color Resolution =============

export function resolveGradeColorScheme(
  gradeValue: number | null,
  colorRules?: IColorRule[]
): GradeColorScheme {
  if (gradeValue === null || gradeValue === undefined) return "neutral";
  const rules = colorRules || DEFAULT_COLOR_RULES;
  for (const rule of rules) {
    const [min, max] = rule.range;
    if (gradeValue >= min && gradeValue <= max) return rule.color;
  }
  return "red";
}

export function getGradeColorTokens(scheme: GradeColorScheme): IGradeColorTokens {
  switch (scheme) {
    case "blue":
      return {
        cardBackground: tokens.colorBrandBackground2,
        borderAccent: tokens.colorBrandBackground,
        gradeText: tokens.colorBrandForeground1,
        iconColor: tokens.colorBrandForeground2,
        contextText: tokens.colorNeutralForeground2,
        labelColor: tokens.colorNeutralForeground2,
      };
    case "yellow":
      return {
        cardBackground: tokens.colorPaletteYellowBackground1,
        borderAccent: tokens.colorPaletteYellowBorderActive,
        gradeText: tokens.colorPaletteYellowForeground2,
        iconColor: tokens.colorPaletteYellowForeground2,
        contextText: tokens.colorNeutralForeground2,
        labelColor: tokens.colorNeutralForeground2,
      };
    case "red":
      return {
        cardBackground: tokens.colorPaletteRedBackground1,
        borderAccent: tokens.colorPaletteRedBorderActive,
        gradeText: tokens.colorPaletteRedForeground1,
        iconColor: tokens.colorPaletteRedForeground1,
        contextText: tokens.colorNeutralForeground2,
        labelColor: tokens.colorNeutralForeground2,
      };
    case "neutral":
    default:
      return {
        cardBackground: tokens.colorNeutralBackground3,
        borderAccent: tokens.colorNeutralStroke1,
        gradeText: tokens.colorNeutralForeground3,
        iconColor: tokens.colorNeutralForeground3,
        contextText: tokens.colorNeutralForeground3,
        labelColor: tokens.colorNeutralForeground3,
      };
  }
}

// ============= Template Substitution =============

export function resolveContextTemplate(
  template: string,
  gradeValue: number | null,
  areaName: string
): string {
  if (gradeValue === null || gradeValue === undefined) {
    return `No grade data available for ${areaName}`;
  }
  return template
    .replace(/\{grade\}/g, gradeValueToPercent(gradeValue))
    .replace(/\{area\}/g, areaName);
}

// ============= Icon Resolution =============

const AREA_ICON_MAP: Record<string, React.ComponentType<{ className?: string }>> = {
  guidelines: GavelRegular,
  budget: MoneyRegular,
  outcomes: TargetRegular,
};

const DEFAULT_ICON = QuestionCircleRegular;

export function resolveAreaIcon(
  areaIcon: string
): React.ComponentType<{ className?: string }> {
  const normalized = areaIcon.toLowerCase().trim();
  return AREA_ICON_MAP[normalized] || DEFAULT_ICON;
}
