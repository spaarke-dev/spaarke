# AI Assistant Theming Guide

> **Task 055**: Dark mode verification and theming documentation
>
> **Last Updated**: January 2026

This guide documents the theming approach for AI Assistant components, ensuring ADR-021 compliance with Fluent UI v9 design tokens.

---

## Overview

The AI Assistant components use Fluent UI v9's design token system for consistent theming across light and dark modes. All colors, spacing, and typography values come from semantic tokens that automatically adapt to the current theme.

## ADR-021 Compliance

Per [ADR-021: Fluent UI v9 Design System](../../adr/ADR-021-fluent-ui-v9-design-system.md), all PCF controls must:

- Use Fluent UI v9 components and tokens exclusively
- Support both light and dark themes automatically
- Avoid hard-coded color values
- Use semantic tokens for all visual properties

## Token Categories

### Background Colors

| Token | Usage | Light Mode | Dark Mode |
|-------|-------|------------|-----------|
| `colorNeutralBackground1` | Primary surface | White | #1f1f1f |
| `colorNeutralBackground2` | Secondary surface | #fafafa | #2d2d2d |
| `colorNeutralBackground3` | Tertiary surface | #f5f5f5 | #383838 |
| `colorBrandBackground` | Primary actions | #0078d4 | #3b82f6 |
| `colorSubtleBackground` | Hover states | transparent | transparent |
| `colorSubtleBackgroundHover` | Hover surfaces | #f5f5f5 | #2d2d2d |

### Foreground Colors

| Token | Usage | Light Mode | Dark Mode |
|-------|-------|------------|-----------|
| `colorNeutralForeground1` | Primary text | #242424 | #ffffff |
| `colorNeutralForeground2` | Secondary text | #616161 | #d6d6d6 |
| `colorNeutralForeground3` | Tertiary text | #9e9e9e | #a3a3a3 |
| `colorNeutralForegroundOnBrand` | Text on brand | #ffffff | #ffffff |
| `colorBrandForeground1` | Brand links | #0078d4 | #3b82f6 |

### Border Colors

| Token | Usage |
|-------|-------|
| `colorNeutralStroke1` | Primary borders |
| `colorNeutralStroke2` | Secondary borders |
| `colorBrandStroke1` | Brand accent borders |
| `colorPaletteRedBorder1` | Error borders |

### Shadows

| Token | Usage |
|-------|-------|
| `shadow2` | Subtle elevation |
| `shadow4` | Card elevation |
| `shadow8` | Dropdown elevation |
| `shadow16` | Modal elevation |
| `shadow28` | Dialog elevation |

## Component Theming

### AiAssistantModal

```tsx
const useStyles = makeStyles({
  container: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    boxShadow: tokens.shadow16,
  },
  header: {
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
});
```

### ChatHistory

```tsx
const useStyles = makeStyles({
  userMessage: {
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  assistantMessage: {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
  },
});
```

### ErrorDisplay

```tsx
const useStyles = makeStyles({
  errorContainer: {
    backgroundColor: tokens.colorPaletteRedBackground1,
    color: tokens.colorPaletteRedForeground1,
    borderColor: tokens.colorPaletteRedBorder1,
  },
  warningContainer: {
    backgroundColor: tokens.colorPaletteYellowBackground1,
    color: tokens.colorPaletteYellowForeground1,
  },
});
```

### TypingIndicator

```tsx
const useStyles = makeStyles({
  dot: {
    backgroundColor: tokens.colorNeutralForeground3,
  },
  container: {
    backgroundColor: tokens.colorNeutralBackground3,
  },
});
```

## Spacing Tokens

All spacing uses Fluent UI spacing tokens:

| Token | Value | Usage |
|-------|-------|-------|
| `spacingVerticalXXS` | 2px | Minimal spacing |
| `spacingVerticalXS` | 4px | Icon padding |
| `spacingVerticalS` | 8px | Button padding |
| `spacingVerticalM` | 12px | Section spacing |
| `spacingVerticalL` | 16px | Group spacing |
| `spacingVerticalXL` | 20px | Major sections |
| `spacingVerticalXXL` | 24px | Page sections |

## Typography Tokens

| Token | Usage |
|-------|-------|
| `fontSizeBase100` | Caption text |
| `fontSizeBase200` | Small text |
| `fontSizeBase300` | Body text (default) |
| `fontSizeBase400` | Large body text |
| `fontSizeBase500` | Subtitle |
| `fontSizeBase600` | Title |
| `fontWeightRegular` | Normal text |
| `fontWeightSemibold` | Emphasis |
| `fontWeightBold` | Strong emphasis |

## Border Radius Tokens

| Token | Value | Usage |
|-------|-------|-------|
| `borderRadiusSmall` | 2px | Small elements |
| `borderRadiusMedium` | 4px | Buttons, inputs |
| `borderRadiusLarge` | 6px | Cards, containers |
| `borderRadiusXLarge` | 8px | Modals, dialogs |
| `borderRadiusCircular` | 50% | Avatars, icons |

## Dark Mode Testing Checklist

When testing AI Assistant components in dark mode:

- [ ] Background colors adapt correctly
- [ ] Text is readable (sufficient contrast)
- [ ] Icons are visible
- [ ] Borders are visible but not harsh
- [ ] Shadows appear natural
- [ ] Focus indicators are visible
- [ ] Error states are distinguishable
- [ ] Loading animations are visible
- [ ] Scrollbars adapt to theme

## Common Issues and Solutions

### Issue: Hard-coded colors

❌ **Wrong**:
```tsx
color: '#333333',
backgroundColor: 'white',
```

✅ **Correct**:
```tsx
color: tokens.colorNeutralForeground1,
backgroundColor: tokens.colorNeutralBackground1,
```

### Issue: Missing hover states

❌ **Wrong**:
```tsx
':hover': {
  backgroundColor: '#f0f0f0',
}
```

✅ **Correct**:
```tsx
':hover': {
  backgroundColor: tokens.colorSubtleBackgroundHover,
}
```

### Issue: Inconsistent elevation

❌ **Wrong**:
```tsx
boxShadow: '0 4px 8px rgba(0,0,0,0.2)',
```

✅ **Correct**:
```tsx
boxShadow: tokens.shadow8,
```

## FluentProvider Setup

Ensure the FluentProvider wraps the component tree with the appropriate theme:

```tsx
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

// Detect theme from Dataverse/system
const theme = isDarkMode ? webDarkTheme : webLightTheme;

<FluentProvider theme={theme}>
  <AiAssistantModal>
    {/* Components automatically use correct tokens */}
  </AiAssistantModal>
</FluentProvider>
```

## Related Files

- [AiAssistantModal.tsx](../../src/client/pcf/PlaybookBuilderHost/control/components/AiAssistant/AiAssistantModal.tsx)
- [ChatHistory.tsx](../../src/client/pcf/PlaybookBuilderHost/control/components/AiAssistant/ChatHistory.tsx)
- [ErrorDisplay.tsx](../../src/client/pcf/PlaybookBuilderHost/control/components/AiAssistant/ErrorDisplay.tsx)
- [TypingIndicator.tsx](../../src/client/pcf/PlaybookBuilderHost/control/components/AiAssistant/TypingIndicator.tsx)

## References

- [ADR-021: Fluent UI v9 Design System](../adr/ADR-021-fluent-ui-v9-design-system.md)
- [Fluent UI v9 Tokens](https://react.fluentui.dev/?path=/docs/concepts-developer-theming--page)
- [Fluent UI v9 Theme Designer](https://react.fluentui.dev/?path=/docs/themedesigner--page)
