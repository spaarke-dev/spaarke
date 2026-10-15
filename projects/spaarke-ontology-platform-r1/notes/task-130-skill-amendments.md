# Task 130 (D-83): skill amendments for the main session (sub-agents cannot write .claude/)

Apply these to `.claude/skills/dataverse-deploy/SKILL.md`, `pcf-deploy/SKILL.md`, `ribbon-edit/SKILL.md`. Line numbers are as read on 2026-10-08 in this worktree; match on the quoted old text. Code is in PR for branch `fix/scoped-publish-130` (`scripts/lib/Publish-SolutionComponents.ps1`, `scripts/Import-SolutionScoped.ps1`).

## The one command (use this text wherever a skill needs an import)

```
pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath <zip> -SolutionUniqueName <name>
```
It runs `pac solution import --force-overwrite` with NO publish, reads the solution's components, POSTs one `PublishXml` for exactly those components, and reads web resources and app modules back. `-PlanOnly` (read-only) prints the ParameterXml that would be published. It never publishes tenant-wide (see the binding block below).

## Binding block to add near the top of dataverse-deploy (after the first intro section)

> **Never publish tenant-wide (owner decision D-83).** Publishing every customization in the environment also publishes other people's unpublished work, so no deploy may use the all-customizations publish action, the publish-all and publish commands of `pac`, or the publish-after-import flag of `pac solution import` (the exact banned forms are listed in `tests/scripts/Publish-SolutionComponents.Tests.ps1`). Import with `scripts/Import-SolutionScoped.ps1` (import without publish, then `PublishXml` of exactly the solution's components). `PublishXml` accepts entities, web resources, option sets, site maps, dashboards and app modules. A PCF control's new build is served only after its bundle web resources are published (the script does this). If a component type cannot be published through `PublishXml`, STOP and ask the owner; never fall back to a tenant-wide publish. CI (`scoped-publish-lint`) fails a PR that reintroduces one.

## dataverse-deploy/SKILL.md

| Line | Old | New |
|---|---|---|
| 99 | `pac solution import --path bin/{SolutionName}_vX.Y.Z.zip --publish-changes` | `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath bin/{SolutionName}_vX.Y.Z.zip -SolutionUniqueName {SolutionName}` |
| 169 | same as 99 | same replacement as 99 |
| 437 | `pac solution import --path Solution_vX.Y.Z.zip --publish-changes` | `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath Solution_vX.Y.Z.zip -SolutionUniqueName {SolutionName}` |
| 465-471 (Scenario 3 code block) | `# Import and publish in one step` + the two `pac solution import ... --publish-changes` commands | `# Import, then publish only the imported components` / `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath "./{SolutionName}.zip" -SolutionUniqueName {SolutionName}` (the script already passes `--force-overwrite`) |
| 476-481 (Post-Import Verification) | `# If not auto-published, publish manually` / `pac solution publish` | `# The script reads web resources and app modules back and fails if any is still unpublished. To see what would be published without importing:` / `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -SolutionUniqueName {SolutionName} -PlanOnly` |
| 499 | `pac solution import --path SpaarkeCore_modified.zip --publish-changes` | `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath SpaarkeCore_modified.zip -SolutionUniqueName SpaarkeCore` |
| 506-514 (Scenario 5 "Publish Customizations") | whole block: `# Publish all customizations` / `pac solution publish` / `# Or use publish-all for everything` / `pac solution publish-all` | Replace the section body with: "**Use when**: a change was made outside a solution import (a Web API PATCH of a web resource, view, form or entity). Publish only that component: `POST /api/data/v9.2/PublishXml` with `{ParameterXml}` built by `New-PublishParameterXml` in `scripts/lib/Publish-SolutionComponents.ps1` (for example `-WebResources <id>` or `-Entities sprk_event`). **Entity publishes are entity-wide:** `PublishXml` has no per-view or per-form element, so publishing an entity also publishes every pending view and form of that entity. The script STOPS before any import or publish when that would also publish someone else's pending change (D-103); see the section below. Never a tenant-wide publish." |
| 597 | `pac solution import --path "out/PowerAppsTools_sprk/bin/Debug/PowerAppsTools_sprk.zip" --publish-changes` | `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath "out/PowerAppsTools_sprk/bin/Debug/PowerAppsTools_sprk.zip" -SolutionUniqueName PowerAppsTools_sprk` |
| 672 (command cheat sheet) | `pac solution publish                    # Publish customizations` | delete the line (add: `# no tenant-wide publish: use scripts/Import-SolutionScoped.ps1`) |
| 776 (troubleshooting row "Web resource import succeeds but resource doesn't appear") | `After import, ALWAYS run \`pac solution publish-customizations\` (or click "Publish All Customizations" in maker portal).` | `Publish the web resource only: re-run scripts/Import-SolutionScoped.ps1, or POST PublishXml with <webresources> for it. Never the maker portal's all-customizations publish button.` |

## pcf-deploy/SKILL.md

| Line | Old | New |
|---|---|---|
| 298 | `pac solution import --path bin/{SolutionName}_vX.Y.Z.zip --publish-changes` | `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath bin/{SolutionName}_vX.Y.Z.zip -SolutionUniqueName {SolutionName}` |
| 391 | `4. **Import**: \`pac solution import --path "Solution/bin/SpaarkeSemanticSearch_v{X.Y.Z}.zip" --publish-changes\`` | `4. **Import**: \`pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath "Solution/bin/SpaarkeSemanticSearch_v{X.Y.Z}.zip" -SolutionUniqueName SpaarkeSemanticSearch\`` |

Add a short "PCF publish" note under step 4: "An imported PCF build is NOT served until its bundle web resources (`cc_<Namespace>.<Control>/bundle.js` and `styles.css`) are published (task 129, 2026-10-08: the import rewrote bundle.js but the published copy stayed old; a `PublishXml` of `<webresources>` served the new bundle). The script does that. `customcontrols.version` can keep reading the old version after that publish: see the open item in the task 130 report; do not treat it as a failed deploy. Always bump the `ControlManifest.Input.xml` version: Microsoft Learn says model-driven apps invalidate their cache on a version bump."

## ribbon-edit/SKILL.md

| Line | Old | New |
|---|---|---|
| 216 | `pac solution import --path "infrastructure\dataverse\ribbon\temp\{SolutionName}_modified.zip" --publish-changes` | `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath "infrastructure\dataverse\ribbon\temp\{SolutionName}_modified.zip" -SolutionUniqueName {SolutionName}` (publishes the ribbon's entity only; the effective ribbon can lag the publish by up to about 75 s) |
| 377 (troubleshooting) | `Run \`pac solution publish\` or publish from Power Apps maker portal` | `Re-run scripts/Import-SolutionScoped.ps1 (publishes the ribbon's entity only), then wait up to about 75 s; never a tenant-wide publish` |

## Docs and CLAUDE.md files also changed in the code PR (no .claude action)
`src/client/pcf/CLAUDE.md`, `docs/guides/*`, `docs/procedures/production-release.md`, `infrastructure/**`. `.claude` is already in the lint roots. The three skill files are on a temporary allow-list, `tests/scripts/publish-lint-allowlist.txt` (reason: this note is not applied yet). After applying this note, delete those three lines from the allow-list in the same commit, so the skills are linted. The new-text cells in the tables above and the binding block contain none of the banned forms; the old-text cells quote them, which is why this note itself is not linted.


## Add to the dataverse-deploy skill: workflows, resume, read-back, probe method (round 4)

**A solution that contains a workflow or cloud flow (component type 29).** The scoped import refuses it by default: the `RetrieveUnpublished` probe answers "record does not exist" (error 0x80040217) for workflows, so the function is bound to the table and a draft layer cannot be ruled out, and a workflow is activated by the import, not by `PublishXml`. When the solution really contains one, run the import with `-AllowWorkflows`:
`pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath <zip> -SolutionUniqueName <name> -AllowWorkflows`
This adds `--activate-plugins` to the pac import (the pac help text for that option is "Activate plug-ins and workflows on the solution"), publishes the rest as usual, and afterwards reads `workflows(id).statecode` for every workflow in the solution; it fails unless each is 1 (Activated).

**If a publish request fails part-way.** The error prints the resume command; run it, it does not re-import:
`pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -SolutionUniqueName <name> -PublishOnly`
Requests go out in this order so nothing goes live before what it uses: option sets and web resources, entities, the application ribbon, site maps and dashboards, app modules last.

**Read-back and its known limit.** After every request the script compares web resources, app modules, app settings, site maps and (for each published entity) system forms, saved queries and charts with their `RetrieveUnpublished` copies. Option sets and the application ribbon have no read-back surface: check them by effect.

**Probe method (why a type is on the no-publish list).** Call `<set>(<fake id>)/Microsoft.Dynamics.CRM.RetrieveUnpublished()`. Only error code 0x80060888 ("Resource not found for the segment") proves the table is not bound to the unpublished-layer function. Error 0x80040217 ("... Does Not Exist") means the function is bound and only the record is missing, so the table has a draft layer. The reviewer re-probed every no-publish type with a fake id and all returned 0x80060888; I re-probed field permissions (0x80060888), app settings and workflows (0x80040217).


## Add to the dataverse-deploy skill: pending collateral stops the publish (D-103, round 8)

Publishing an entity also publishes every pending (unpublished) view, form and chart of that entity, and publishing an app setting publishes its whole parent app. If that would publish someone else's pending change, `scripts/Import-SolutionScoped.ps1` STOPS before `pac solution import` and before any `PublishXml`, lists each entity or app and each pending component, and prints the exact re-run command with the opt-in flag `-AllowPendingCollateral`. Do not add the flag unless the owner has said publishing those items is intended. With the flag the script warns, lists and continues. `-PublishOnly` applies the same rule, and the resume command printed after a failed request carries the flag only if the original run had it. The solution's own installed items and parent apps that are part of the solution are not collateral.
