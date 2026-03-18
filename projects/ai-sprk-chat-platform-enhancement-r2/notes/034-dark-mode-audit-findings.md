# Dark Mode Compliance Audit — Phase 3 Group C Components (R2-034)

**Date**: 2026-03-17
**Auditor**: Claude Code (automated)
**ADR**: ADR-021 — Fluent UI v9 Design System

---

## Scope

Audited all SprkChat components created or modified in Phase 3 Group C tasks (R2-030 through R2-033), plus all other SprkChat components referenced in the task specification.

### Files Audited

| File | Task | Result |
|------|------|--------|
| `SprkChatMessageRenderer.tsx` | R2-030 | PASS |
| `SprkChat.tsx` | R2-031 | PASS |
| `SprkChatTypingIndicator.tsx` | R2-031 | PASS |
| `SprkChatCitationPopover.tsx` | R2-032 | PASS |
| `PlanPreviewCard.tsx` | R2-033 | PASS |
| `SlashCommandMenu.tsx` | R2-036 | PASS |
| `SprkChatUploadZone.tsx` | R2-041 | PASS |
| `SprkChatDocumentStatus.tsx` | R2-031 | PASS |
| `SprkChatExportWord.tsx` | R2-044 | PASS |
| `ActionConfirmationDialog.tsx` | R2-039 | **VIOLATION FOUND** |
| `renderMarkdown.ts` (service) | R2-030 | PASS |

---

## Violations Found

### 1. ActionConfirmationDialog.tsx — Hard-coded rgba overlay

**Location**: `ActionConfirmationDialog.tsx`, line 58 (overlay style)

**Before** (violation):
```typescript
backgroundColor: 'rgba(0, 0, 0, 0.4)',
```

**After** (fixed):
```typescript
backgroundColor: tokens.colorBackgroundOverlay,
```

**Rationale**: `tokens.colorBackgroundOverlay` is the Fluent v9 semantic token for modal/dialog backdrop overlays. It adapts correctly between light mode, dark mode, and high-contrast themes. The hard-coded `rgba(0, 0, 0, 0.4)` would appear as a dark overlay regardless of theme, which is incorrect in high-contrast mode.

---

## Compliance Summary

### makeStyles Token Usage

All audited files correctly use `tokens.*` from `@fluentui/react-components` for:
- **Colors**: `colorNeutralForeground1/2/3`, `colorNeutralBackground1/2/3`, `colorBrandForeground1/2`, `colorStatusSuccessForeground1`, `colorStatusDangerForeground1`, `colorNeutralStroke2`, `colorPaletteBerryForeground1`
- **Spacing**: `spacingVertical*`, `spacingHorizontal*`
- **Typography**: `fontSizeBase*`, `lineHeightBase*`, `fontWeightSemibold`, `fontWeightBold`, `fontFamilyBase`, `fontFamilyMonospace`
- **Borders**: `borderRadiusMedium`, `borderRadiusSmall`, `borderRadiusCircular`, `borderRadiusXLarge`
- **Shadows**: `shadow16`
- **Focus**: `colorStrokeFocus2`

### renderMarkdown CSS (SPRK_MARKDOWN_CSS)

The injected CSS stylesheet uses Fluent v9 CSS custom properties (`var(--colorNeutralForeground1)`, etc.) throughout. These are set by `FluentProvider` and automatically adapt when themes switch. No hard-coded colors found.

### Typing Indicator Animation

`SprkChatTypingIndicator.tsx` correctly uses Fluent v9 motion tokens:
- `tokens.durationUltraSlow` for animation duration
- `tokens.curveEasyEase` for timing function
- `tokens.durationNormal` and `tokens.durationSlow` for stagger delays

### Inline Styles

Reviewed all `style={{...}}` usages across SprkChat files:
- `SprkChatHighlightRefine.tsx`: Uses `top`/`left` positioning values only (no colors)
- `SprkChatPredefinedPrompts.tsx`: Uses `display`, `alignItems`, and `tokens.spacingHorizontalXS` (compliant)
- `QuickActionChips.tsx`: Uses `height: '100%'` only (no colors)
- `SprkChatUploadZone.tsx`: Uses `position: 'relative'` only (no colors)

No inline color violations found.

---

## Areas Requiring No Follow-up

All Fluent v9 semantic tokens used in the audited components have light, dark, and high-contrast variants. No custom token gaps were identified. The component library is fully dark-mode compatible.

---

## Pre-existing TypeScript Errors (Not Related to Audit)

Two pre-existing type errors exist in `useSseStream.ts` and `types.ts` related to `IDocumentStreamSseEvent` and `pendingDocumentStreamEvent` properties. These are from other in-progress tasks (not Phase 3 Group C) and are outside the scope of this audit.
