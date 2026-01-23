# AI Semantic Search UI R2

> **Status**: Ready for Implementation
> **Start Date**: 2026-01-20
> **Branch**: `work/ai-semantic-search-ui-r2`
> **Depends On**: ai-semantic-search-foundation-r1 (completed)

---

## Overview

Build a **PCF control for semantic search** that enables users to search documents using natural language with filters. The control integrates with the Semantic Search Foundation API (`/api/ai/search/semantic`) and can be deployed on:
- Toolbar/command bar (via Custom Page dialog)
- Form sections (matter-scoped)
- Dedicated Custom Pages (full experience)

## Key Deliverables

| Deliverable | Description |
|-------------|-------------|
| **SemanticSearchControl** | PCF control with search input, filter panel, infinite scroll results |
| **Result Cards** | Document cards with similarity scores, metadata, highlighted snippets |
| **Filter Panel** | Dynamic filters from Dataverse metadata (Document Type, Matter Type, Date Range, File Type) |
| **Dark Mode Support** | Full ADR-021 compliance via FluentProvider |
| **Solution Package** | Deployable Dataverse solution |

## Technical Stack

| Layer | Technology |
|-------|------------|
| UI Framework | Fluent UI v9 (`@fluentui/react-components`) |
| Platform | PCF with React 16 (platform libraries) |
| Styling | Griffel (`makeStyles`) with design tokens |
| Authentication | MSAL (silent + popup fallback) |
| API | BFF Semantic Search endpoint (`/api/ai/search/semantic`) |

## Directory Structure

```
src/client/pcf/SemanticSearchControl/
├── SemanticSearchControl.pcfproj
├── SemanticSearchControl/
│   ├── ControlManifest.Input.xml
│   ├── index.ts                       # PCF entry point (React 16)
│   ├── SemanticSearchControl.tsx      # Main component
│   ├── components/                    # UI components
│   ├── hooks/                         # React hooks
│   ├── services/                      # API and auth services
│   └── types/                         # TypeScript interfaces
├── Solution/
├── package.json
└── featureconfig.json
```

## Quick Start

```bash
# Navigate to control directory
cd src/client/pcf/SemanticSearchControl

# Install dependencies
npm install

# Build the control (production mode)
npm run build:prod

# Copy build artifacts to Solution folder
cp out/controls/SemanticSearchControl/bundle.js Solution/Controls/sprk_Sprk.SemanticSearchControl/
cp out/controls/SemanticSearchControl/ControlManifest.xml Solution/Controls/sprk_Sprk.SemanticSearchControl/

# Pack solution
cd Solution && powershell -File pack.ps1

# Import to Dataverse
pac solution import --path "bin/SpaarkeSemanticSearch_v1.0.0.zip" --publish-changes
```

**Full deployment guide**: [DEPLOYMENT.md](DEPLOYMENT.md)

## API Endpoint

**POST** `/api/ai/search/semantic`

```json
{
  "query": "find contracts about payment terms",
  "scope": "matter",
  "scopeId": "guid-of-matter",
  "filters": {
    "documentTypes": ["contract", "amendment"],
    "dateRange": { "from": "2025-01-01", "to": null }
  },
  "options": {
    "limit": 25,
    "offset": 0,
    "includeHighlights": true
  }
}
```

## Key Constraints

| ADR | Requirement |
|-----|-------------|
| ADR-006 | PCF over webresources |
| ADR-012 | Shared component library |
| ADR-021 | Fluent UI v9 design system |
| ADR-022 | React 16 APIs, platform libraries |

## Related Documents

| Document | Purpose |
|----------|---------|
| [spec.md](spec.md) | Full implementation specification |
| [design.md](design.md) | Original design document |
| [plan.md](plan.md) | Implementation plan with phases |
| [CLAUDE.md](CLAUDE.md) | AI context and constraints |
| [DEPLOYMENT.md](DEPLOYMENT.md) | Deployment guide and troubleshooting |
| [FINAL-DEPLOYMENT-PLAN.md](FINAL-DEPLOYMENT-PLAN.md) | User testing enablement checklist |
| [TASK-INDEX.md](tasks/TASK-INDEX.md) | Task tracking |

---

*Project initialized: 2026-01-20*
