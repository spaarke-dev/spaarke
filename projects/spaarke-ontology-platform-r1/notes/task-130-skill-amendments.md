# Task 130 (D-83): skill amendments for the main session (sub-agents cannot write .claude/)

Apply these to `.claude/skills/dataverse-deploy/SKILL.md`, `pcf-deploy/SKILL.md`, `ribbon-edit/SKILL.md`. Line numbers are as read on 2026-10-08 in this worktree; match on the quoted old text. Code is in PR for branch `fix/scoped-publish-130` (`scripts/lib/Publish-SolutionComponents.ps1`, `scripts/Import-SolutionScoped.ps1`).

## The one command (use this text wherever a skill needs an import)

```
pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath <zip> -SolutionUniqueName <name>
```
It runs `pac solution import --force-overwrite` with NO publish, reads the solution's components, POSTs one `PublishXml` for exactly those components, and reads web resources and app modules back. `-PlanOnly` (read-only) prints the ParameterXml that would be published. Never `PublishAllXml`, `pac solution publish`, `pac solution publish-all`, `--publish-changes`.

## Binding block to add near the top of dataverse-deploy (after the first intro section)

> **Never publish tenant-wide (owner decision D-83).** `PublishAllXml`, `pac solution publish`, `pac solution publish-all` and `pac solution import --publish-changes` publish every customization in the environment, including other people's unpublished work. Import with `scripts/Import-SolutionScoped.ps1` (import without publish, then `PublishXml` of exactly the solution's components). `PublishXml` accepts entities, web resources, option sets, site maps, dashboards and app modules. A PCF control's new build is served only after its bundle web resources are published (the script does this). If a component type cannot be published through `PublishXml`, STOP and ask the owner; do not fall back to a publish-all. CI (`scoped-publish-lint`) fails a PR that reintroduces one.

## dataverse-deploy/SKILL.md

| Line | Old | New |
|---|---|---|
| 99 | `pac solution import --path bin/{SolutionName}_vX.Y.Z.zip --publish-changes` | `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath bin/{SolutionName}_vX.Y.Z.zip -SolutionUniqueName {SolutionName}` |
| 169 | same as 99 | same replacement as 99 |
| 437 | `pac solution import --path Solution_vX.Y.Z.zip --publish-changes` | `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath Solution_vX.Y.Z.zip -SolutionUniqueName {SolutionName}` |
| 465-471 (Scenario 3 code block) | `# Import and publish in one step` + the two `pac solution import ... --publish-changes` commands | `# Import, then publish only the imported components` / `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath "./{SolutionName}.zip" -SolutionUniqueName {SolutionName}` (the script already passes `--force-overwrite`) |
| 476-481 (Post-Import Verification) | `# If not auto-published, publish manually` / `pac solution publish` | `# The script reads web resources and app modules back and fails if any is still unpublished. To see what would be published without importing:` / `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -SolutionUniqueName {SolutionName} -PlanOnly` |
| 499 | `pac solution import --path SpaarkeCore_modified.zip --publish-changes` | `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath SpaarkeCore_modified.zip -SolutionUniqueName SpaarkeCore` |
| 506-514 (Scenario 5 "Publish Customizations") | whole block: `# Publish all customizations` / `pac solution publish` / `# Or use publish-all for everything` / `pac solution publish-all` | Replace the section body with: "**Use when**: a change was made outside a solution import (a Web API PATCH of a web resource, view, form or entity). Publish only that component: `POST /api/data/v9.2/PublishXml` with `{ParameterXml}` built by `New-PublishParameterXml` in `scripts/lib/Publish-SolutionComponents.ps1` (for example `-WebResources <id>` or `-Entities sprk_event`). **Entity publishes are entity-wide:** `PublishXml` has no per-view or per-form element, so publishing an entity also publishes every pending view and form of that entity. Run `Get-EntityPublishCollateral` first (it lists them) and stop if any belongs to someone else. Never a tenant-wide publish." |
| 597 | `pac solution import --path "out/PowerAppsTools_sprk/bin/Debug/PowerAppsTools_sprk.zip" --publish-changes` | `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -ZipPath "out/PowerAppsTools_sprk/bin/Debug/PowerAppsTools_sprk.zip" -SolutionUniqueName PowerAppsTools_sprk` |
| 672 (command cheat sheet) | `pac solution publish                    # Publish customizations` | delete the line (add: `# no tenant-wide publish: use scripts/Import-SolutionScoped.ps1`) |
| 776 (troubleshooting row "Web resource import succeeds but resource doesn't appear") | `After import, ALWAYS run \`pac solution publish-customizations\` (or click "Publish All Customizations" in maker portal).` | `Publish the web resource only: re-run scripts/Import-SolutionScoped.ps1, or POST PublishXml with <webresources> for it. Never "Publish All Customizations".` |

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
`src/client/pcf/CLAUDE.md`, `docs/guides/*`, `docs/procedures/production-release.md`, `infrastructure/**`. After this note is applied, add `.claude` to the `git grep` roots in `tests/scripts/Publish-SolutionComponents.Tests.ps1` (the pathspec list in the lint Describe: append `.claude`), so the skills are linted too.
