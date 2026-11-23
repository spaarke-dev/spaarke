# task-create — `<ui-tests>` structure (Step 3.65)

> **Parent skill**: [task-create](../SKILL.md) Step 3.65. Read this when a task is tagged `pcf`, `frontend`, `fluent-ui` or `e2e-test`.
> **Last Reviewed**: 2026-11-03

The rules (concrete visual direction, console-error check, ADR-021 dark-mode test, `{placeholder}` values) are in SKILL.md Step 3.65. This file gives the structure to write.

## What each `<ui-tests>` section contains

- Test name and description
- Target URL, with `{org}` (and other `{placeholder}`s) for environment-specific values
- Step-by-step test actions
- Expected outcomes
- ADR-021 dark-mode checks for Fluent UI components

Tests are specific to the component being built. `task-execute` Step 9.7 executes them through the `ui-test` skill when Claude Code runs with Chrome integration (`--chrome`).

## Structure

Escape `&` in URLs as `&amp;` — the POML must stay well-formed XML.

```xml
<ui-tests>
  <test name="Component Renders">
    <url>https://{org}.crm.dynamics.com/main.aspx?appid={app-id}&amp;pagetype=entityrecord&amp;etn=account</url>
    <steps>
      <step>Navigate to Account form</step>
      <step>Verify {component-name} control is visible</step>
      <step>Check console for JavaScript errors</step>
    </steps>
    <expected>Control renders without console errors</expected>
  </test>

  <test name="Dark Mode Compliance (ADR-021)">
    <steps>
      <step>Toggle dark mode in D365 settings</step>
      <step>Verify background colors adapt (no white in dark mode)</step>
      <step>Verify text colors adapt (no black in dark mode)</step>
      <step>Verify icons use currentColor</step>
    </steps>
    <expected>All colors use Fluent UI v9 semantic tokens per ADR-021</expected>
  </test>

  <test name="User Interaction">
    <steps>
      <step>Click primary action button</step>
      <step>Verify loading indicator appears</step>
      <step>Verify response displays correctly</step>
    </steps>
    <expected>Interaction completes within 3 seconds</expected>
  </test>
</ui-tests>
```
