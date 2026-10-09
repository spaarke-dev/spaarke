# CreatePrivilegeRibbons — create and wizard buttons follow the Create privilege

> **Owner**: unified-access-control-r2 task 180 — owner round 89 item 2 ("every custom Create / wizard ribbon button
> hides when the user lacks the Create privilege on the target table; Dataverse's standard privilege rule, no code").
> **Live state**: NOT deployed (main-session gate G180-1 below). The inventory and the repo-vs-dev comparison are in
> `projects/unified-access-control-r2/notes/task-180-create-button-privileges.md`.

## The rule

Every Spaarke ribbon command that creates a record, or opens a wizard that does, carries one display rule for the table
it creates:

```xml
<DisplayRule Id="sprk.CreatePrivilege.<table>.DisplayRule">
  <EntityPrivilegeRule EntityName="<table>" PrivilegeType="Create" PrivilegeDepth="Basic" />
</DisplayRule>
```

`PrivilegeDepth="Basic"` means "holds Create at any depth". A display rule hides the button (a user who cannot create the
record no longer sees a button that fails at save). Dataverse evaluates it; there is no script. The platform's own New
buttons are untouched: the platform already rules them.

Which commands: those whose action calls a function in [`create-launchers.json`](create-launchers.json) (function →
table it creates). The FUNCTION decides the table, so a new button that calls an existing launcher is found and ruled
without a change here; a new launcher is added to that file. `notCreate` records the launchers that were reviewed and
create nothing (Summarize Files, Find Similar) or are ruled already (the secure-child "New", the platform subgrid New).
A wizard that creates several tables is ruled on its primary table; the secondary tables are listed in the notes file.

## Files

| File | Role |
|---|---|
| `create-launchers.json` | The ONE list: launcher function → table, the reviewed non-create launchers, and the host tables `-Verify` reads. |
| `Merge-CreatePrivilegeRule.ps1` | Pure file transformation (no Dataverse call) of a RibbonDiff.xml / customizations.xml / snippet: adds the rule reference to each create command and the rule definition to its `<RibbonDiffXml>`. Idempotent; keeps every other node; throws if a command is lost. `-Check` reports only. |
| `Set-CreatePrivilegeRibbon.ps1` | Dry run (checked-in sources; with `-EnvironmentUrl` also the live create commands, read-only) / `-Apply` / `-Verify`. |

`scripts/Generate-CreateTodoRibbonXmlForTenEntities.ps1` writes the ten `createtodo-button.xml` snippets with the rule
already in them (and with a well-formed comment: the snippets did not parse as XML before this task).

## Deployment (MANUAL GATE G180-1 — main session)

`-Apply` exports four dedicated ribbon solutions fresh, refuses before importing anything if an export lacks a command
the environment holds or if a table with a live create command is in none of them, merges, imports with publish, and
verifies. **Do not run it while another ribbon import into the same tables is in flight** (tasks 175/179's
`Set-AccessRibbon.ps1`, `Deploy-SecureChildNewCommands.ps1`): each re-imports a table's whole ribbon.

```powershell
cd infrastructure/dataverse/ribbon/CreatePrivilegeRibbons
pwsh ./Set-CreatePrivilegeRibbon.ps1                                                          # 1. dry run, sources
pwsh ./Set-CreatePrivilegeRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com     # 2. + live read (read-only)
pwsh ./Set-CreatePrivilegeRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply      # 3. LIVE
pwsh ./Set-CreatePrivilegeRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify `
    -BeforeList <WorkDir printed by -Apply>/before.json                                       # 4. read-only, re-runnable
```

Then the live gate (G180-2): sign in as a user whose only role cannot create (for example a copy of Spaarke Basic User
with Create removed on matter, project, work assignment, event, to do, document and analysis) and check that the Matter,
Project and Work Assignment forms and home grids show no Spaarke create command (Create Project, Create Event, Create To
Do, Upload Documents, Playbook Library, New Matter / Project / Event / Work Assignment, New Upload, Add Multiple,
New Analysis) while Summarize Files and Find Similar still show. Then as a Spaarke Basic User, check they all show.
