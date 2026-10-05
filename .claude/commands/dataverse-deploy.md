# /dataverse-deploy

Deploy PCF controls, solutions, and web resources to Dataverse using PAC CLI.

## Usage

```
/dataverse-deploy [control-name]
```

**Examples**:
- `/dataverse-deploy` - Show deployment decision tree
- `/dataverse-deploy SpeFileViewer` - Deploy specific PCF control
- `/dataverse-deploy --solution SprkDocuments` - Deploy full solution

## What This Command Does

This command executes the `dataverse-deploy` skill to handle Dataverse deployments, including:
- PCF control quick dev deploy (verified `build:prod`, then pack + import per the `pcf-deploy` skill)
- PCF control production release (solution workflow)
- Solution import/export
- Web resource deployment

## Prerequisites

Before running this command:
1. PAC CLI installed and authenticated (`pac auth list`)
2. Clean build of the control/solution
3. For PCF: Temporarily disable Central Package Management

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/dataverse-deploy/SKILL.md`
2. **Check PAC authentication** before any deployment
3. **Follow the decision tree** for correct deployment path
4. **Handle Central Package Management** (disable before, restore after)

## Skill Location

`.claude/skills/dataverse-deploy/SKILL.md`

## Quick Dev Deploy (Most Common)

For iterative PCF development (~60 seconds):

```bash
cd src/client/pcf/{ControlName}
rm -rf out/
pwsh -File ../../../../scripts/Invoke-PcfBuildProd.ps1 -PcfPath .   # STOP on a non-zero exit
# Then copy, pack and import per the pcf-deploy skill (Steps 3-5).
```

Do NOT use `pac pcf push`: it creates a temporary solution and rebuilds in **development** mode, discarding the
verified production build (a NEVER rule in the `pcf-deploy` skill). And never trust `npm run build:prod`'s exit
code on its own: `pcf-scripts` exits 0 when the webpack build fails.

## Decision Tree

```
Is this a production release?
├── YES → Use "PCF Production Release" (full solution workflow)
└── NO → Is the PCF embedded in a Custom Page?
    ├── YES → Use "PCF Custom Page Deploy" (complex)
    └── NO → Use "Quick Dev Deploy" (verified build + pack + import; never `pac pcf push`)
```

## Common Issues

- **Central Package Management error**: Rename `Directory.Packages.props` before push
- **Authentication expired**: Run `pac auth create --environment`
- **Bundle too large**: Use platform-library declarations (ADR-021)

## Related Commands

- `/code-review` - Review before deploying
- `/adr-check` - Validate ADR compliance
