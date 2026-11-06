# UI testing — Step 9.7 procedure

Read this at Step 9.7 when the task's tags include `pcf`, `frontend`, `fluent-ui` or `e2e-test`. When you finish, return to `SKILL.md` Step 10.

**Purpose**: Run browser-based UI tests for PCF controls and frontend components after deployment.

**Requires**: Claude Code started with `--chrome` flag

```
CHECK if UI testing applies:

IF ALL conditions met:
  - Task tags include: pcf, frontend, fluent-ui, e2e-test
  - Claude Code has Chrome integration (/chrome shows "Connected")
  - Deployment completed (dataverse-deploy or localhost running)
  - Task has <ui-tests> section OR UI-related <acceptance-criteria>

THEN:
  PROMPT: "UI tests defined for this task. Run browser-based testing? [Y/n]"

  IF user confirms:
    1. LOAD ui-test skill
    2. EXECUTE tests defined in task POML or acceptance criteria
    3. VERIFY ADR-021 dark mode compliance (for PCF/Fluent UI)
    4. CHECK console for runtime errors
    5. CAPTURE screenshots/GIFs if requested
    6. REPORT results

  IF user declines:
    → Document reason in current-task.md
    → Continue to Step 10

SKIP UI testing IF:
  - Task is backend-only (no pcf/frontend tags)
  - Claude Code not started with --chrome
  - No deployed environment available
  - User explicitly skips

UPDATE current-task.md:
  - Add "UI Testing" section:
    - Status: ✅ Passed / ⚠️ Issues found / ⏭️ Skipped (reason)
    - Tests run: {count}
    - Issues: {list if any}
```

A defect the UI tests find is a finding like any other: classify and fix it per Step 9.5 "Finding triage and review scope".

**Test Sources** (checked in order):

1. **Task POML `<ui-tests>` section** - Explicit test definitions
2. **Task POML `<acceptance-criteria>`** - UI-related criteria
3. **Project CLAUDE.md** - Environment URLs, navigation paths
4. **ADR-021** - Dark mode requirements (auto-applied for PCF/Fluent UI)

**Example UI Test in Task POML**:
```xml
<ui-tests>
  <test name="Component Renders">
    <url>https://org.crm.dynamics.com/main.aspx?...</url>
    <steps>
      <step>Verify control is visible</step>
      <step>Check console for errors</step>
    </steps>
  </test>
  <test name="Dark Mode (ADR-021)">
    <steps>
      <step>Toggle dark mode</step>
      <step>Verify colors adapt</step>
    </steps>
  </test>
</ui-tests>
```
