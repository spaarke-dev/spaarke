---
source: https://dianabirkelbach.wordpress.com/2024/01/13/standard-or-custom-theming-for-pcf-using-fluent-ui-v9/
fetched: 2026-05-26
author: Diana Birkelbach (Dianamics PCF Lady)
published: 2024-01-13
summary: Standard (tokenTheme) vs custom (createLightTheme/createDarkTheme + tinycolor2-generated BrandVariants) theming for PCFs. Dark-mode detection via isDarkTheme.
loadWhen: designing a Spaarke brand-palette theme OR adding dark-mode detection. Covered at pattern level by patterns/ui/fluent-v9-theming.md.
notes: WebFetch capture; verify against live post before quoting verbatim.
---

# Standard or Custom Theming for PCF using Fluent UI v9 — Diana Birkelbach

## Theming in Canvas Apps and Custom Pages

Modern controls in Canvas Apps and Custom Pages can have themes applied through app settings. These controls follow the Fluent 2 design system. Users can select from multiple predefined themes including Steel and other Microsoft-branded options.

## Theming in Model-Driven Apps

Two approaches:

- **Classic controls**: Traditional theming customization has been available for some time
- **New look**: Modern theming via XML WebResource — currently limited to app header styling only

## Applying theming to PCFs

Retrieve the current theme through `context.fluentDesignLanguage.tokenTheme` and wrap components with `FluentProvider`:

```tsx
const myTheme = context.fluentDesignLanguage.tokenTheme;

<FluentProvider theme={myTheme}>
  <DataGrid>
    {/* ... */}
  </DataGrid>
</FluentProvider>
```

## Creating custom themes

### Using brand variants

The **Fluent UI v9 Theme Designer** generates 16 color variants from a base color. These `BrandVariants` can be used with `createLightTheme()` or `createDarkTheme()`.

### Generating themes from base colors

Rather than manually defining all 16 colors, developers can use the **`tinycolor2`** npm library to dynamically generate brand ramps. Birkelbach implemented a `generateBrandVariants()` function that lightens and darkens a base color to create the full palette.

This enables customizable theming in model-driven forms through PCF parameters, allowing users to set a "base palette color" similar to Canvas App modern controls.

### Dark mode support

Check `context.fluentDesignLanguage.isDarkTheme` and conditionally apply:

```ts
isLightTheme
  ? createLightTheme(generateBrandVariants(basePaletteColor))
  : createDarkTheme(generateBrandVariants(basePaletteColor));
```

## Reference implementation

Full source: Birkelbach's `ToDosDataGridFluent9` GitHub repository — both standard and custom theming applied to a ToDo dataset PCF.

## Takeaway

> "Out of the box, the platform provides now a theme for PCF. We just have to use it."
