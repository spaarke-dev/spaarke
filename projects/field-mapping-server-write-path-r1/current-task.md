# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-19
> **Phase**: Project initialized (`/project-pipeline` Steps 0–4.2 complete). Task: none started.

## State

- Artifacts: README, plan.md, CLAUDE.md, notes/decisions.md, tasks/TASK-INDEX.md and 43 task POMLs (validator clean).
  Committed and pushed in `366759553`.
- `projects/INDEX.md` has the project's row (BFF=Y; root CLAUDE.md=Y via task 062).
- GitHub: issue #1164 under Epic #535 on Board #2. Status Active, Type Project, Project Type Mixed, Task Count 43,
  Tasks Planned 43. Label `field-mapping-server-write-path-r1` created.
- Target Date: not set (Step 4.5, waiting on the owner).

## Planning findings to carry into execution (written into the POMLs as escalation triggers)

- 004: the profile query selects `sprk_capabilitymod…` but the rule query selects `sprk_compatibilitymode`. Check the
  deployed name with Dataverse `describe` before editing.
- 005: the profile entity already reads `SyncMode`; the rule `$select` / entity do not.
- 018: `OfficeEndpoints.GetQuickCreateDefaultsAsync` uses the Office service's `ResolveDefaultAssigneeAsync`, which
  needs a new home before the deletion. Invoice quick-create bypasses the Office core today.
- 030: Office document save is create-then-update; the association moves into the create.
- 032: invoice create uses a client-generated GUID (lost-response cleanup), so the service may need an optional row id.
- 033: `SignalWriter` / `DecisionRecordWriter` use a restricted ontology writer identity. Escalate if the service would
  widen privileges.
- 034: communications use `CreateCommunicationRaceProofAsync` (unique-key dedupe). Decide on a service create option
  or a named exception.
- 017: `DecisionRouteCores.CreateChildAsync` calls `ChildRecordEndpoints.CreateAsync`, so decisions inherit the service
  through the route.
- Wizards: Event, To Do, Invoice, Report Card and Work Assignment already create via the BFF adapter. Only Matter and
  Project go through Xrm (task 020 route extension). The document wizards have no `applyFieldMappings`; they use a
  client cascade.

## Exact next action

1. The owner sets a Target Date (or skips it).
2. Start wave 1 in parallel through task-execute: **001** (engine), **006** (EntityMaps), **010** (W2 coordination
   gate).
3. W2 code (011+) waits until 010 records UAC-r2 sign-off.
