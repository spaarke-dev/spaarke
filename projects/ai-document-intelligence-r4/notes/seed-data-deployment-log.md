# Seed Data Deployment Log

> **Project**: AI Document Intelligence R4
> **Date**: 2026-01-04
> **Environment**: https://spaarkedev1.crm.dynamics.com

---

## Deployment Summary

| Entity | Expected | Deployed | Verified |
|--------|----------|----------|----------|
| ActionType (lookup) | 5 | 5 | ✅ |
| SkillType (lookup) | 5 | 5 | ✅ |
| KnowledgeType (lookup) | 5 | 5 | ✅ |
| ToolType (lookup) | 4 | 4 | ✅ |
| **Total Lookups** | **19** | **19** | ✅ |
| Action | 8 | 8 | ✅ |
| Tool | 8 | 8 | ✅ |
| Knowledge | 10 | 10 | ✅ |
| Skill | 10 | 10 | ✅ |
| **Total Scope Records** | **36** | **36** | ✅ |

---

## Deployment Scripts Created

| Script | Purpose | Status |
|--------|---------|--------|
| `Deploy-TypeLookups.ps1` | Populate 4 type lookup tables | ✅ Run |
| `Deploy-Actions.ps1` | Create 8 Action records | ✅ Run |
| `Deploy-Tools.ps1` | Create 8 Tool records | ✅ Run |
| `Deploy-Knowledge.ps1` | Create 10 Knowledge records | ✅ Run |
| `Deploy-Skills.ps1` | Create 10 Skill records | ✅ Run |

## Verification Scripts Created

| Script | Purpose |
|--------|---------|
| `Verify-TypeLookups.ps1` | Verify all type records |
| `Verify-Actions.ps1` | Verify 8 Action records |
| `Verify-Tools.ps1` | Verify 8 Tool records |
| `Verify-Knowledge.ps1` | Verify 10 Knowledge records |
| `Verify-Skills.ps1` | Verify 10 Skill records |

## Query Scripts Created

| Script | Purpose |
|--------|---------|
| `Query-ActionTypes.ps1` | Get ActionType GUIDs for FK references |
| `Query-SkillTypes.ps1` | Get SkillType GUIDs for FK references |
| `Query-KnowledgeTypes.ps1` | Get KnowledgeType GUIDs for FK references |
| `Query-ToolTypes.ps1` | Get ToolType GUIDs for FK references |

---

## Seed Data JSON Files

| File | Records | Content |
|------|---------|---------|
| `type-lookups.json` | 19 | Type categories with "01 - Name" prefix convention |
| `actions.json` | 8 | Actions with SystemPrompts for AI operations |
| `tools.json` | 8 | Tools with JSON configurations for handlers |
| `knowledge.json` | 10 | Mix of Inline and RAG-based knowledge sources |
| `skills.json` | 10 | Skills with detailed PromptFragments |

---

## Deployment Notes

### Type Naming Convention
Type tables lack a `sortorder` field, so we used a "01 - Name" prefix convention to enable ordering:
- `01 - Document Analysis`
- `02 - Contract Specific`
- etc.

### Foreign Key Binding
All scope records use OData binding syntax for type lookups:
```json
"sprk_ActionTypeId@odata.bind": "/sprk_analysisactiontypes(GUID)"
```

### Authentication
All scripts use Azure CLI for token acquisition:
```powershell
$token = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv
```

---

## Re-running Deployments

To re-run deployments (idempotent - skips existing records):
```powershell
cd scripts/seed-data
.\Deploy-TypeLookups.ps1
.\Deploy-Actions.ps1
.\Deploy-Tools.ps1
.\Deploy-Knowledge.ps1
.\Deploy-Skills.ps1
```

To force overwrite existing records:
```powershell
.\Deploy-Actions.ps1 -Force
```

To preview changes without deploying:
```powershell
.\Deploy-Actions.ps1 -DryRun
```

---

*Deployment completed: 2026-01-04*
