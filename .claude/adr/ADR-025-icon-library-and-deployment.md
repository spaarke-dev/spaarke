# ADR-025: Icon Library and Deployment Strategy

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2026-02-17 |
| Authors | Spaarke Engineering |

---

## Related AI Context

**Related ADRs:**
- [ADR-021 Fluent UI Design System](./ADR-021-fluent-ui-design-system.md) — Parent design system decision
- [ADR-006 Prefer PCF over Web Resources](./ADR-006-prefer-pcf-over-webresources.md) — Web resource strategy context
- [ADR-012 Shared Component Library](./ADR-012-shared-component-library.md) — Component sharing approach

**Operational Guide:** [Icon Manager GUIDE.md](../../projects/spaarke-navigation-icons/GUIDE.md) — Full walkthrough for using the Icon Manager app, MCP server, deployment scripts, and Claude Code orchestration.

---

## Context

### Problem Statement

Spaarke applications use icons across multiple surfaces: Dataverse model-driven apps (entity icons, sitemap navigation, command bar), PCF controls, and the UX prototype. Without a standardized approach, we face:

- **Inconsistent iconography** across navigation, entity records, command bars, and status indicators
- **No lifecycle tracking** — no way to know which icons are approved, deployed, or pending
- **Manual, error-prone deployment** — uploading SVG web resources one at a time through the Power Apps maker portal
- **Duplicate icon data** — icon metadata scattered across multiple files with no single source of truth
- **Dark mode incompatibility** — custom or inconsistently-sourced icons that don't adapt to theme changes

### Requirements

1. All icons must come from a single, consistent source library
2. Icons must work in both light and dark mode (Dataverse themes)
3. Icons must be deployable as Dataverse web resources programmatically
4. A management tool must track icon lifecycle (draft, approved, deployed, rejected)
5. The icon inventory must be machine-readable for automation
6. Adding new icons must be repeatable without manual portal work

---

## Decision

### 1. Standardize on Fluent UI System Icons (20px Regular)

All Spaarke icons are sourced from [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons), standardized to:

| Property | Value | Rationale |
|----------|-------|-----------|
| Size | 20px | Optimal for Dataverse entity icons, sitemap, and command bar |
| Style | Regular | Consistent with Fluent UI v9 default; Filled used only for active/selected states |
| Format | SVG | Scalable, small file size, `currentColor` inheritance for theme adaptation |

**Why Fluent UI System Icons:**
- Native to the Microsoft ecosystem — consistent with Dynamics 365, Power Platform, and Teams
- Supports `currentColor` — automatically adapts to light/dark mode
- Available as both React components (`@fluentui/react-icons`) and raw SVGs
- Searchable via MCP server for AI-assisted icon discovery
- 4,000+ icons covering virtually all business application needs

### 2. Single Manifest as Source of Truth (`icon-manifest.json`)

A single JSON manifest file contains all icon metadata, eliminating scattered data files:

```json
{
  "version": "2.0.0",
  "icons": [
    {
      "id": "entity-matter",
      "name": "sprk_matter",
      "category": "Entity",
      "description": "Legal matter record type — core entity",
      "fluentComponent": "Briefcase20Regular",
      "fluentSvgName": "ic_fluent_briefcase_20_regular.svg",
      "localPath": "icons/entity/matter.svg",
      "webResourceName": "sprk_/icons/entity/matter.svg",
      "usageType": "entity",
      "entityLogicalName": "sprk_matter",
      "status": "Deployed"
    }
  ]
}
```

The manifest serves as input for:
- The **Icon Manager** prototype app (UI for review and status management)
- The **PowerShell deployment script** (reads manifest to create web resources)
- **Claude Code** (reads manifest to understand current icon state)

### 3. Four-Status Lifecycle

| Status | Meaning | Included in Export? |
|--------|---------|-------------------|
| **Draft** | New or proposed icon, under evaluation | No |
| **Approved** | Reviewed and accepted, ready for deployment | Yes |
| **Deployed** | Successfully pushed to Dataverse | No (already there) |
| **Rejected** | Not accepted for use | No |

This prevents re-deploying already-deployed icons and provides clear audit trail.

### 4. Two-Repository Architecture

| Repository | Purpose | Contents |
|------------|---------|----------|
| `spaarke-icons` | Canonical icon library | SVG files, manifest, deployment scripts, XML artifacts |
| `spaarke-prototype` | Icon Manager UI | Experiment with management workflow, status persistence, export tools |

The Icon Manager reads a working copy of the manifest. After changes, the updated manifest is downloaded and committed back to `spaarke-icons`.

### 5. Automated Deployment via PowerShell + Dataverse Web API

Deployment uses `Import-SpaarkeIcons.ps1` which:

1. Reads `icon-manifest.json` for all icon metadata
2. Authenticates via OAuth2 device code flow (MSAL.PS)
3. Creates/updates web resources (type 11 = SVG) via Dataverse Web API v9.2
4. Associates entity icons using `PUT` to `EntityDefinitions` with `MSCRM.MergeLabels` header
5. Publishes all customizations

**Authentication:** Device code flow was chosen over interactive browser auth because it works reliably from terminal contexts (including Claude Code sessions). The user authenticates once per session.

### 6. Icon Categories

| Category | Count | Dataverse Usage |
|----------|-------|-----------------|
| Navigation | 15 | Sitemap SubArea icons (`$webresource:sprk_/icons/nav/*.svg`) |
| Entity | 33 | Entity metadata icons (`IconSmallName`, `IconVectorName`, etc.) |
| Command | 36 | Command bar and toolbar icons (web resources referenced by ribbon XML) |
| Status | 9 | Status indicators (referenced in PCF controls and custom pages) |

**93 total icons** covering all current Spaarke application needs.

---

## Consequences

### Positive

- **Consistent visual language** across all Spaarke surfaces — Dataverse, PCF, prototype
- **Dark mode works automatically** — SVG `currentColor` inherits theme foreground color
- **Repeatable deployment** — script-based, no manual portal work, idempotent
- **Auditable lifecycle** — every icon has a tracked status in the manifest
- **AI-assistable** — Claude Code can search icons via MCP server, update the manifest, and orchestrate deployment
- **Single source of truth** — one manifest file, no data duplication

### Negative

- **Standard entity icons cannot be changed** — Microsoft prevents modifying icons on `account`, `contact`, `email`, `task`, `appointment`, `annotation` via Web API (returns 400 Bad Request). Custom web resources are still created but cannot be associated.
- **Sitemap icons require manual step** — Navigation icon web resources are deployed automatically, but sitemap SubArea references must be set via the Power Apps sitemap editor or solution XML.
- **Thumbnail map requires manual sync** — When adding new icons, the React component import and map entry in `icon-thumbnail-map.tsx` must be added manually (React components cannot be dynamically resolved from JSON strings).
- **Two-repo manifest sync** — The working copy in the prototype and the canonical copy in `spaarke-icons` must be kept in sync manually (download from app → commit to repo).

### Known Limitations

| Limitation | Workaround |
|------------|------------|
| Standard entity icons immutable via API | Icons deployed as web resources; association skipped for standard entities |
| Custom entities must exist before association | Script creates web resources regardless; re-run after entities are created |
| Sitemap not automatable via script | Use Power Apps sitemap editor; XML snippets provided by Icon Manager |
| MSAL interactive auth blocked in some terminals | Device code flow used instead |
| PowerShell 5.1 lacks MSAL.PS support | Require PowerShell 7+ (`pwsh`) |

---

## Alternatives Considered

### A: Manual icon upload via Power Apps maker portal

Upload SVG files one at a time through the web resource editor. Rejected because:
- Extremely tedious for 93 icons
- No lifecycle tracking
- No repeatability across environments
- Error-prone (naming conventions, solution associations)

### B: PAC CLI solution packaging

Package icons into a Dataverse solution ZIP and import via `pac solution import`. Rejected because:
- Requires full solution packaging infrastructure
- Overkill for icon-only deployments
- Harder to deploy incrementally (all-or-nothing import)
- We may adopt this later for full solution CI/CD, but the Web API approach is better for iterative icon management

### C: Custom icon SVGs (not Fluent UI)

Design custom icons specific to Spaarke. Rejected because:
- Design effort disproportionate to value
- Would not adapt to Dataverse dark mode automatically
- Inconsistent with Microsoft ecosystem visual language
- Maintenance burden for future icon needs

### D: Fluent UI v8 icons

Use the older `@fluentui/react-icons-mdl2` package. Rejected because:
- v8 icons are legacy and not receiving new additions
- Inconsistent with our Fluent UI v9 design system (ADR-021)
- Different visual style than modern Microsoft apps

---

## Implementation References

- **Icon Manager experiment:** `spaarke-prototype/projects/spaarke-navigation-icons/`
- **Canonical icon library:** `spaarke-icons/` repository
- **Deployment script:** `spaarke-icons/deploy/Import-SpaarkeIcons.ps1`
- **Manifest schema:** `spaarke-icons/icon-manifest.json` (v2.0.0)
- **Operational guide:** `spaarke-prototype/projects/spaarke-navigation-icons/GUIDE.md`
- **FluentUI Icons MCP:** `@keenmate/fluentui-icons-mcp` (configured in Claude Code)
