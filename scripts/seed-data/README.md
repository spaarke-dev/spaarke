# AI Seed Data Deployment Scripts

> **Purpose**: Deploy AI analysis playbooks, actions, tools, skills, knowledge, and output types to Dataverse

---

## Customer stamps: `ai-content/` (fixed primary keys, seeded by H12a)

On a customer stamp, the AI content comes from `ai-content/`, not from the scripts below. Provisioning handler H12a
(`DataverseWebApiAiContentSeeder`) writes it **create-if-missing with the authoring environment's primary keys**. Clients
hard-code playbook ids, and Binding (`sprk_playbookconsumer`) ids are ledger and chip identities. Write order: types →
tools → skills → knowledge → actions → playbooks → playbook N:N → nodes → node N:N → canvas → Bindings. A set whose lookups
or links point at a row it does not carry is refused before anything is written. Existing rows are never updated;
differences are reported in the run notes.

- **Source**: `ai-content/Export-AiContent.ps1` reads spaarkedev1 (GET only) and writes the JSON. Its header lists what is
  exported and what is left out (model-deployment pins, run/index state, links to runtime records), and why. The output is
  deterministic: re-export, then review the diff in a PR.
- **Which playbooks**: the `$Playbooks` list in the script (owner decision 2, 2026-11-26), each with the reason it ships.
- **Embedded**: `Sprk.Provisioning.ControlPlane.Core.csproj` embeds every `ai-content/**/*.json`. The content hash is part of
  H12a's idempotency key, so a merged re-export re-seeds on the next upgrade run.

---

## Quick Start

### Deploy Everything (Recommended)

```powershell
cd c:\code_files\spaarke\scripts\seed-data

# Dry run first (see what would be deployed)
.\Deploy-All-AI-SeedData.ps1 -DryRun

# Deploy for real
.\Deploy-All-AI-SeedData.ps1

# Force recreate all records
.\Deploy-All-AI-SeedData.ps1 -Force
```

This master script deploys everything in the correct order:
1. Type Lookups
2. Actions
3. Tools
4. Knowledge
5. Skills
6. Playbooks
7. Output Types

---

## Individual Deployment Scripts

If you need to deploy just one component:

```powershell
.\Deploy-TypeLookups.ps1
.\Deploy-Actions.ps1
.\Deploy-Tools.ps1
.\Deploy-Knowledge.ps1
.\Deploy-Skills.ps1
.\Deploy-Playbooks.ps1
.\Deploy-OutputTypes.ps1
```

**⚠️ Important**: Maintain dependency order! Playbooks depend on Actions/Tools/Knowledge/Skills.

---

## Verification Scripts

After deployment, verify the data:

```powershell
.\Verify-TypeLookups.ps1
.\Verify-Actions.ps1
.\Verify-Tools.ps1
.\Verify-Knowledge.ps1
.\Verify-Skills.ps1
.\Verify-Playbooks.ps1
```

---

## Prerequisites

1. **Azure CLI authenticated**:
   ```powershell
   az login
   ```

2. **Correct environment URL** (default: Dev):
   - Dev: `https://spaarkedev1.crm.dynamics.com`
   - To use different environment:
     ```powershell
     .\Deploy-All-AI-SeedData.ps1 -EnvironmentUrl "https://your-env.crm.dynamics.com"
     ```

3. **Required permissions**:
   - Create/Read/Write access to:
     - `sprk_analysisactions`
     - `sprk_analysistools`
     - `sprk_analysisknowledges`
     - `sprk_analysisskills`
     - `sprk_analysisplaybooks`
     - `sprk_aioutputtypes`

---

## Data Files

### MVP Playbooks (Current)

| File | Records | Description |
|------|---------|-------------|
| `type-lookups.json` | 4 | Action, tool, skill, knowledge type lookups |
| `actions.json` | 6 | Analysis actions (Extract Entities, Classify, etc.) |
| `tools.json` | 6 | AI tools (Entity Extractor, Classifier, etc.) |
| `knowledge.json` | 5 | Knowledge sources (Contract Terms, Risk Categories) |
| `skills.json` | 3 | AI skills (Contract Analysis, Risk Assessment) |
| `playbooks.json` | 4 | Complete workflows (Quick Review, Contract Analysis, **Document Profile**, Risk Scan) |
| `output-types.json` | 5 | Field mappings for Document Profile (TL;DR, Summary, Keywords, Type, Entities) |

### Document Profile Playbook (PB-011)

**Special system playbook** used by UniversalQuickCreate PCF for auto-generating document summaries on upload.

**Output Types** (populated on `sprk_document`):
- `sprk_tldr` - Ultra-concise summary (1-2 sentences)
- `sprk_summary` - Comprehensive summary (2-4 paragraphs)
- `sprk_keywords` - Comma-separated keywords
- `sprk_documenttype` - Classified type (Contract, NDA, Invoice, etc.)
- `sprk_entities` - Extracted named entities (JSON)

---

## Troubleshooting

### Error: "Failed to get access token"

**Solution**: Run `az login` and authenticate

### Error: "Playbook '{name}' not found"

**Cause**: Dependencies not deployed in order

**Solution**: Run `.\Deploy-All-AI-SeedData.ps1` which handles dependencies automatically

### Warning: "Action/Tool/Skill not found"

**Cause**: Referenced component doesn't exist yet

**Solution**:
1. Deploy dependencies first: Actions, Tools, Knowledge, Skills
2. Then deploy Playbooks

### Playbook exists but 404 on API

**Cause**: Playbook created but not properly visible to API

**Solution**:
1. Verify playbook exists: `.\Verify-Playbooks.ps1`
2. Check playbook name is exact match: "Document Profile" (case-sensitive)
3. Try querying directly:
   ```powershell
   $token = az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query 'accessToken' -o tsv
   $uri = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_analysisplaybooks?`$filter=sprk_name eq 'Document Profile'"
   Invoke-RestMethod -Uri $uri -Headers @{ 'Authorization' = "Bearer $token" } -Method Get
   ```

---

## Development

### Adding New Playbooks

1. Edit `playbooks.json` - add new playbook definition
2. Update scopes (skills, actions, knowledge, tools)
3. Run `.\Deploy-Playbooks.ps1`
4. Verify with `.\Verify-Playbooks.ps1`

### Adding New Output Types

1. Edit `output-types.json` - add new output type
2. Add to `playbookAssociations` section
3. Run `.\Deploy-OutputTypes.ps1`

---

## Related Documentation

- **Playbook Architecture**: `docs/architecture/playbook-architecture.md`
- **JPS Authoring Guide**: `docs/guides/JPS-AUTHORING-GUIDE.md`
- **Scope Catalog**: `.claude/catalogs/scope-model-index.json`
- **API Endpoints**: `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEndpoints.cs`

---

**Last Updated**: 2026-01-07
