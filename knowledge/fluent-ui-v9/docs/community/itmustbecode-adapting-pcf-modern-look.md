---
source: https://itmustbecode.com/adapting-pcf-controls-for-model-driven-apps-new-modern-look/
fetched: 2026-05-26
author: David Rivard (It Must Be Code!)
published: 2023-10-16
summary: Dual-look detection pattern — undocumented `context.fluentDesignLanguage` presence signals MDA "new look" (Fluent v9); conditional v8/v9 rendering. Unsupported by Microsoft but works.
loadWhen: PCF needs to render correctly under BOTH old + new MDA looks during transition.
notes: WebFetch capture; verify against live post before quoting verbatim.
---

# Adapting PCF Controls for Model-Driven Apps "New (Modern) Look" — David Rivard

## The "new look" architecture change

Model-driven apps now feature a "Try the new look" toggle that switches the interface to Microsoft's modern Fluent Design System (Fluent 2). This fundamentally changes the rendering library for form components:

| Look | Library |
|---|---|
| **Old Look** | Fluent UI React 8 (`@fluentui/react`) |
| **New Look** | Fluent UI React 9 (`@fluentui/react-components`) |

The difference is substantial. For example, choice fields (dropdown lists) display noticeably different styling and behavior between the two versions.

## Impact on PCF controls

PCF controls traditionally used Fluent UI React 8 to match form rendering. With the new look toggle, **PCF developers should transition to Fluent UI React 9**. Given users can switch between modes, controls should ideally render contextually — using the appropriate library version based on the active look.

> The transition between versions represents a complete architectural overhaul, not a simple update.

## Detecting user look preference

Via the PCF context, the **undocumented `fluentDesignLanguage` property** appears exclusively when the new look is enabled:

| Look | `context.fluentDesignLanguage` |
|---|---|
| Old Look | `undefined` |
| New Look | populated |

This enables conditional rendering of appropriate implementations.

## Proof of concept

The author built a basic PCF rendering an input field with dual implementations:

- **Old Look** — FluentUI React v8
- **New Look** — FluentUI React v9

The control adapts dynamically when users toggle the new-look switch.

**Source**: `drivardxrm/NewLookSwitchTest.PCF` on GitHub.

## Considerations / trade-offs

Implementing dual versions:

- Increased complexity in codebase maintenance
- Potentially larger bundle sizes
- Better user experience through seamless design integration

Offers backward compatibility during the transition period, **though unsupported by Microsoft**.

## Conclusion

Developers must weigh development costs against UX improvements. Substantial migration work awaits existing PCF control projects.
