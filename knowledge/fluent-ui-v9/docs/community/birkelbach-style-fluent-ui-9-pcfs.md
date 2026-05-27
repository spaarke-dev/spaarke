---
source: https://dianabirkelbach.wordpress.com/2025/02/25/style-your-fluent-ui-9-pcfs-for-power-apps/
fetched: 2026-05-26
author: Diana Birkelbach (Dianamics PCF Lady)
published: 2025-02-25
summary: PCF + Fluent v9 styling to match modern Power Apps controls. Appearance/size props, portal re-wrap pattern, Canvas-vs-MDA disabled-state (filled-darker + readOnly + neutral-stroke theme), Universal Input pattern.
loadWhen: Canvas-vs-MDA decisions OR disabled-state styling drift. Quick reference covered by patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md.
notes: WebFetch capture; verify code blocks against the live post before quoting verbatim.
---

# Style Your Fluent UI 9 PCFs for Power Apps — Diana Birkelbach

Diana Birkelbach explores styling Fluent UI 9 PCFs to match modern Power Apps controls. References her previous work on theming and focuses on achieving the "modern controls" look in Canvas Apps and the "new look" in Model-Driven Apps.

## Fluent UI 9 fundamentals

### Appearance property

Input controls support multiple appearance options:

- `outline`
- `underline`
- `filled-darker`
- `filled-lighter`

`filled-darker` closely resembles modern Power Apps controls and is **recommended** for adaptation.

### Size property

Controls offer `small | medium (default) | large`. **Use built-in sizes** over custom CSS for width / height / font-size.

### CSS styling with Griffel

```ts
import { makeStyles } from '@fluentui/react-components';

const useStyles = makeStyles({
  root: {
    color: 'red',
    '> div': { color: 'green' },
    ':hover': { color: 'blue' }
  }
});

function Component() {
  const classes = useStyles();
  return <div className={classes.root} />;
}
```

### Use color tokens, not hard-coded colors

Access tokens via `context.fluentDesignLanguage.theme.colorStatusSuccessBackground1`. Ensures colors adapt across light/dark modes and custom themes.

### React portal theme provider (CRITICAL gotcha)

Portal components (`Popover`, `Tooltip`, `Toast`, `Dialog`, `Menu`) render outside normal DOM boundaries. **They require rewrapping in `FluentProvider`** to apply theming:

```tsx
<FluentProvider theme={theme}>
  <Popover>
    <PopoverTrigger disableButtonEnhancement>
      {/* content */}
    </PopoverTrigger>
    <PopoverSurface>
      <FluentProvider theme={theme}>
        {/* nested content */}
      </FluentProvider>
    </PopoverSurface>
  </Popover>
</FluentProvider>
```

## Canvas Apps & Custom Pages

Apply `filled-darker` and use the native `disabled` property:

```tsx
export const HelloWorld: React.FC<IHelloWorldProps> = ({
  name, isDisabled, theme, isCanvasApp,
}) => {
  const styles = useStyles();
  return (
    <Input
      value={name}
      appearance='filled-darker'
      className={styles.root}
      disabled={isDisabled}
    />
  );
};
```

```tsx
public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
  const props: IHelloWorldProps = {
    name: context.parameters.sampleProperty.raw ?? "",
    isDisabled: context.mode.isControlDisabled,
    theme: context.fluentDesignLanguage?.tokenTheme
  };
  return React.createElement(HelloWorld, props);
}
```

## Model-Driven Apps (Forms)

MDA requires different handling for disabled states — use **`readOnly`** with a **modified theme** using neutral stroke colors (NOT the native `disabled` prop, which looks wrong in MDA):

```tsx
export const HelloWorld: React.FC<IHelloWorldProps> = ({
  name, isDisabled, theme, isCanvasApp,
}) => {
  const styles = useStyles();
  const myTheme = isDisabled
    ? {
        ...theme,
        colorCompoundBrandStroke:         theme?.colorNeutralStroke1,
        colorCompoundBrandStrokeHover:    theme?.colorNeutralStroke1Hover,
        colorCompoundBrandStrokePressed:  theme?.colorNeutralStroke1Pressed,
        colorCompoundBrandStrokeSelected: theme?.colorNeutralStroke1Selected,
      }
    : theme;

  return (
    <FluentProvider theme={myTheme} className={styles.root}>
      <Input
        value={name}
        appearance='filled-darker'
        className={styles.root}
        readOnly={isDisabled}
      />
    </FluentProvider>
  );
};
```

### Dark mode

Test by appending `&flags=themeOption%3Ddarkmode` to MDA URLs.

## Universal Input Control (works for both Canvas + MDA)

Add a manifest property:

```xml
<property name="isCanvas" display-name-key="isCanvas"
          description-key="isCanvas" of-type="Enum" usage="input" required="true"
          pfx-default-value="'YES'">
  <value name="Yes" display-name-key="Yes">YES</value>
  <value name="No"  display-name-key="No" default="true">NO</value>
</property>
```

Conditional logic:

```tsx
const myTheme = isDisabled && isCanvasApp === false
  ? { ...theme, colorCompoundBrandStroke: theme?.colorNeutralStroke1 }
  : theme;

return (
  <FluentProvider theme={myTheme} className={styles.root}>
    <Input
      value={name}
      appearance='filled-darker'
      className={styles.root}
      readOnly={isDisabled}
      disabled={isDisabled && isCanvasApp === true}
    />
  </FluentProvider>
);
```

## Combobox

For Canvas Apps, use the standard disabled Combobox. For MDA, render an Input with `readOnly` instead:

```tsx
export const HelloWorldCombobox: React.FC<IHelloWorldComboboxProps> = ({
  name, isDisabled, theme, isCanvasApp,
}) => {
  const styles = useStyles();
  const myTheme = isDisabled && isCanvasApp === false
    ? { ...theme, colorCompoundBrandStroke: theme?.colorNeutralStroke1 }
    : theme;

  return (
    <FluentProvider theme={myTheme} className={styles.root}>
      {!isDisabled || isCanvasApp === true ? (
        <Combobox
          appearance='filled-darker'
          className={styles.root}
          readOnly={isDisabled}
          disabled={isDisabled && isCanvasApp === true}
        >
          <Option>Test</Option>
        </Combobox>
      ) : (
        <Input
          value={name}
          appearance='filled-darker'
          className={styles.root}
          readOnly={isDisabled}
        />
      )}
    </FluentProvider>
  );
};
```

## Reference implementation

Full source: [`brasov2de/PCFTraining/Extended/Fluent9Styled`](https://github.com/brasov2de/PCFTraining/tree/main/Extended/Fluent9Styled).
