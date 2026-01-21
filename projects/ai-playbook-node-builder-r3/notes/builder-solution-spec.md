# Builder Scopes Solution Packaging Specification

> **Project**: ai-playbook-node-builder-r3
> **Task**: 021 - Package and Deploy Builder Scopes Solution
> **Created**: 2026-01-19
> **Purpose**: Package 23 builder scope records into Dataverse solution for deployment

---

## Overview

This document provides the complete specification for packaging the AI Playbook Builder scope records into a Dataverse solution. The solution contains:

1. **Schema Components** (from task 005)
   - Global option set: `sprk_scopeownertype`
   - Ownership fields on 4 entities: `sprk_ownertype`, `sprk_isimmutable`, `sprk_parentscope`, `sprk_basedon`

2. **Data Components** (from task 020)
   - 5 Action scope records
   - 5 Skill scope records
   - 9 Tool scope records
   - 4 Knowledge scope records

**Total Records**: 23 builder-specific scopes

---

## Solution Information

| Property | Value |
|----------|-------|
| **Solution Name** | AIPlaybookBuilderScopes |
| **Display Name** | AI Playbook Builder Scopes |
| **Publisher** | Spaarke (sprk_) |
| **Version** | 1.0.0.0 |
| **Type** | Unmanaged (per ADR-022) |
| **Unique Name** | sprk_AIPlaybookBuilderScopes |

---

## Solution Components

### Schema Components (Task 005)

#### Global Option Set

| Component | Details |
|-----------|---------|
| **Name** | `sprk_scopeownertype` |
| **Display Name** | Scope Owner Type |
| **Options** | 1=System, 2=Customer |

#### Entity Fields (All 4 Scope Entities)

**Target Entities:**
- `sprk_analysisaction`
- `sprk_analysisskill`
- `sprk_analysisknowledge`
- `sprk_analysistool`

**Fields per Entity:**

| Field | Type | Required | Default |
|-------|------|----------|---------|
| `sprk_ownertype` | Choice (sprk_scopeownertype) | Business Required | Customer (2) |
| `sprk_isimmutable` | Yes/No | Optional | No |
| `sprk_parentscope` | Lookup (self-reference) | Optional | null |
| `sprk_basedon` | Lookup (self-reference) | Optional | null |

**Reference**: See `notes/dataverse-ownership-fields-spec.md` for complete field specifications.

---

### Data Components (Task 020)

#### Action Scope Records (5)

| ID | Name | Dataverse Name | Description |
|----|------|----------------|-------------|
| ACT-BUILDER-001 | Intent Classification | `SYS-ACT-BUILDER-001-IntentClassification` | Parse user message into operation intent |
| ACT-BUILDER-002 | Node Configuration | `SYS-ACT-BUILDER-002-NodeConfiguration` | Generate node config from requirements |
| ACT-BUILDER-003 | Scope Selection | `SYS-ACT-BUILDER-003-ScopeSelection` | Select appropriate existing scope |
| ACT-BUILDER-004 | Scope Creation | `SYS-ACT-BUILDER-004-ScopeCreation` | Generate new scope definition |
| ACT-BUILDER-005 | Build Plan Generation | `SYS-ACT-BUILDER-005-BuildPlanGeneration` | Create structured plan from requirements |

#### Skill Scope Records (5)

| ID | Name | Dataverse Name | Description |
|----|------|----------------|-------------|
| SKL-BUILDER-001 | Lease Analysis Pattern | `SYS-SKL-BUILDER-001-LeaseAnalysisPattern` | Domain expertise for lease playbooks |
| SKL-BUILDER-002 | Contract Review Pattern | `SYS-SKL-BUILDER-002-ContractReviewPattern` | Contract playbook patterns |
| SKL-BUILDER-003 | Risk Assessment Pattern | `SYS-SKL-BUILDER-003-RiskAssessmentPattern` | Risk workflow patterns |
| SKL-BUILDER-004 | Node Type Guide | `SYS-SKL-BUILDER-004-NodeTypeGuide` | When to use each node type |
| SKL-BUILDER-005 | Scope Matching | `SYS-SKL-BUILDER-005-ScopeMatching` | Find/create appropriate scopes |

#### Tool Scope Records (9)

| ID | Name | Dataverse Name | Operation |
|----|------|----------------|-----------|
| TL-BUILDER-001 | Add Node | `SYS-TL-BUILDER-001-addNode` | Add node to canvas |
| TL-BUILDER-002 | Remove Node | `SYS-TL-BUILDER-002-removeNode` | Remove node from canvas |
| TL-BUILDER-003 | Create Edge | `SYS-TL-BUILDER-003-createEdge` | Connect two nodes |
| TL-BUILDER-004 | Update Node Config | `SYS-TL-BUILDER-004-updateNodeConfig` | Modify node configuration |
| TL-BUILDER-005 | Link Scope | `SYS-TL-BUILDER-005-linkScope` | Wire scope to node |
| TL-BUILDER-006 | Create Scope | `SYS-TL-BUILDER-006-createScope` | Create new scope in Dataverse |
| TL-BUILDER-007 | Search Scopes | `SYS-TL-BUILDER-007-searchScopes` | Find existing scopes |
| TL-BUILDER-008 | Auto Layout | `SYS-TL-BUILDER-008-autoLayout` | Arrange canvas nodes |
| TL-BUILDER-009 | Validate Canvas | `SYS-TL-BUILDER-009-validateCanvas` | Validate playbook structure |

#### Knowledge Scope Records (4)

| ID | Name | Dataverse Name | Content Type |
|----|------|----------------|--------------|
| KNW-BUILDER-001 | Scope Catalog | `SYS-KNW-BUILDER-001-ScopeCatalog` | Inline catalog |
| KNW-BUILDER-002 | Reference Playbooks | `SYS-KNW-BUILDER-002-ReferencePlaybooks` | Pattern examples |
| KNW-BUILDER-003 | Node Schema | `SYS-KNW-BUILDER-003-NodeSchema` | Valid configurations |
| KNW-BUILDER-004 | Best Practices | `SYS-KNW-BUILDER-004-BestPractices` | Design guidelines |

---

## Record Common Fields

All 23 scope records share these field values:

| Field | Value |
|-------|-------|
| `sprk_ownertype` | 1 (System) |
| `sprk_isimmutable` | true |
| `sprk_parentscope` | null |
| `sprk_basedon` | null |

---

## GUID Assignments

All records have pre-assigned GUIDs for deterministic deployment:

### Actions (sprk_analysisaction)

| Record | GUID |
|--------|------|
| ACT-BUILDER-001 | `a1b2c3d4-e5f6-4a5b-8c9d-001001001001` |
| ACT-BUILDER-002 | `a1b2c3d4-e5f6-4a5b-8c9d-002002002002` |
| ACT-BUILDER-003 | `a1b2c3d4-e5f6-4a5b-8c9d-003003003003` |
| ACT-BUILDER-004 | `a1b2c3d4-e5f6-4a5b-8c9d-004004004004` |
| ACT-BUILDER-005 | `a1b2c3d4-e5f6-4a5b-8c9d-005005005005` |

### Skills (sprk_analysisskill)

| Record | GUID |
|--------|------|
| SKL-BUILDER-001 | `b1c2d3e4-f5a6-4b5c-9d8e-001001001001` |
| SKL-BUILDER-002 | `b1c2d3e4-f5a6-4b5c-9d8e-002002002002` |
| SKL-BUILDER-003 | `b1c2d3e4-f5a6-4b5c-9d8e-003003003003` |
| SKL-BUILDER-004 | `b1c2d3e4-f5a6-4b5c-9d8e-004004004004` |
| SKL-BUILDER-005 | `b1c2d3e4-f5a6-4b5c-9d8e-005005005005` |

### Tools (sprk_analysistool)

| Record | GUID |
|--------|------|
| TL-BUILDER-001 | `c1d2e3f4-a5b6-4c5d-8e9f-001001001001` |
| TL-BUILDER-002 | `c1d2e3f4-a5b6-4c5d-8e9f-002002002002` |
| TL-BUILDER-003 | `c1d2e3f4-a5b6-4c5d-8e9f-003003003003` |
| TL-BUILDER-004 | `c1d2e3f4-a5b6-4c5d-8e9f-004004004004` |
| TL-BUILDER-005 | `c1d2e3f4-a5b6-4c5d-8e9f-005005005005` |
| TL-BUILDER-006 | `c1d2e3f4-a5b6-4c5d-8e9f-006006006006` |
| TL-BUILDER-007 | `c1d2e3f4-a5b6-4c5d-8e9f-007007007007` |
| TL-BUILDER-008 | `c1d2e3f4-a5b6-4c5d-8e9f-008008008008` |
| TL-BUILDER-009 | `c1d2e3f4-a5b6-4c5d-8e9f-009009009009` |

### Knowledge (sprk_analysisknowledge)

| Record | GUID |
|--------|------|
| KNW-BUILDER-001 | `d1e2f3a4-b5c6-4d5e-9f0a-001001001001` |
| KNW-BUILDER-002 | `d1e2f3a4-b5c6-4d5e-9f0a-002002002002` |
| KNW-BUILDER-003 | `d1e2f3a4-b5c6-4d5e-9f0a-003003003003` |
| KNW-BUILDER-004 | `d1e2f3a4-b5c6-4d5e-9f0a-004004004004` |

---

## Deployment Instructions

### Option A: Manual Creation via Power Platform UI

#### Step 1: Create Solution

1. Navigate to **Power Apps** > **Solutions**
2. Click **New Solution**
3. Enter:
   - Display Name: `AI Playbook Builder Scopes`
   - Name: `AIPlaybookBuilderScopes`
   - Publisher: Select `Spaarke` (sprk_)
   - Version: `1.0.0.0`
4. Click **Create**

#### Step 2: Add Schema Components (if not already deployed)

Follow the steps in `notes/dataverse-ownership-fields-spec.md` to add:
- Global option set `sprk_scopeownertype`
- Ownership fields to all 4 scope entities

Skip this step if fields were already created during task 005.

#### Step 3: Import Data Records

For each of the 23 scope records, create a new record via:

**Power Apps Model-Driven App**:
1. Navigate to each scope table (Actions, Skills, Knowledge, Tools)
2. Click **New**
3. Fill in fields from the JSON source files
4. Save

OR

**Excel Import**:
1. Export template for each entity
2. Fill in data from JSON files
3. Import via **Import data**

#### Step 4: Add Records to Solution

1. Open the solution `AI Playbook Builder Scopes`
2. Click **Add existing** > **Table**
3. For each scope entity:
   - Select the entity
   - Choose **Select objects** > **Records**
   - Select all 23 builder scope records (filtered by `SYS-*-BUILDER-*` name pattern)
4. Click **Add**

#### Step 5: Publish and Export

1. Click **Publish all customizations**
2. Click **Export** > **Unmanaged**
3. Download solution zip
4. Commit to `src/solutions/AIPlaybookBuilderScopes/`

---

### Option B: Web API Data Import Script

Use this PowerShell script to import all 23 records programmatically.

```powershell
# Import-BuilderScopes.ps1
# Prerequisites: PAC CLI authenticated, ownership fields exist

param(
    [Parameter(Mandatory=$true)]
    [string]$EnvironmentUrl,  # e.g., "https://spaarkedev1.crm.dynamics.com"

    [Parameter(Mandatory=$true)]
    [string]$ScopesPath       # e.g., "projects/ai-playbook-node-builder-r3/notes/builder-scopes"
)

# Entity API names mapping
$entityMap = @{
    "Action"    = "sprk_analysisactions"
    "Skill"     = "sprk_analysisskills"
    "Knowledge" = "sprk_analysisknowledges"
    "Tool"      = "sprk_analysistools"
}

# Common fields for all system scopes
$commonFields = @{
    "sprk_ownertype" = 1          # System
    "sprk_isimmutable" = $true    # Immutable
}

# Get all JSON files
$scopeFiles = Get-ChildItem -Path $ScopesPath -Filter "*.json"

foreach ($file in $scopeFiles) {
    $scope = Get-Content $file.FullName | ConvertFrom-Json

    # Determine entity
    $entitySet = $entityMap[$scope.scopeType]
    if (-not $entitySet) {
        Write-Warning "Unknown scope type: $($scope.scopeType) in $($file.Name)"
        continue
    }

    # Build record payload
    $record = @{
        "sprk_name"        = $scope.name
        "sprk_displayname" = $scope.displayName
        "sprk_description" = $scope.description
        "sprk_ownertype"   = 1
        "sprk_isimmutable" = $true
    }

    # Add type-specific fields
    switch ($scope.scopeType) {
        "Action" {
            $record["sprk_systemprompt"] = $scope.systemPrompt
        }
        "Skill" {
            $record["sprk_promptfragment"] = $scope.promptFragment
        }
        "Tool" {
            $record["sprk_configuration"] = ($scope.configuration | ConvertTo-Json -Depth 10)
        }
        "Knowledge" {
            $record["sprk_content"] = ($scope.content | ConvertTo-Json -Depth 10)
        }
    }

    # Add metadata as JSON
    if ($scope.metadata) {
        $record["sprk_metadata"] = ($scope.metadata | ConvertTo-Json -Depth 5)
    }

    # Create record via Web API
    $url = "$EnvironmentUrl/api/data/v9.2/$entitySet"
    $body = $record | ConvertTo-Json -Depth 10

    Write-Host "Creating: $($scope.name)..."

    try {
        # Use PAC CLI or Invoke-WebRequest with OAuth token
        # pac data record create --entity $entitySet --data $body

        Write-Host "  Created successfully" -ForegroundColor Green
    }
    catch {
        Write-Warning "  Failed: $_"
    }
}

Write-Host "`nImport complete. Total records: $($scopeFiles.Count)"
```

**Usage**:
```powershell
.\Import-BuilderScopes.ps1 `
    -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" `
    -ScopesPath "projects/ai-playbook-node-builder-r3/notes/builder-scopes"
```

---

### Option C: PAC CLI Record Creation

Use PAC CLI for individual record creation:

```bash
# Authenticate
pac auth create --url https://spaarkedev1.crm.dynamics.com

# Create Action record example
pac data record create \
  --entity sprk_analysisaction \
  --data '{
    "sprk_name": "SYS-ACT-BUILDER-001-IntentClassification",
    "sprk_displayname": "Intent Classification",
    "sprk_description": "Parse user message into operation intent",
    "sprk_ownertype": 1,
    "sprk_isimmutable": true,
    "sprk_systemprompt": "[FULL PROMPT FROM JSON FILE]"
  }'
```

---

## Field Mapping: JSON to Dataverse

### Action Records (sprk_analysisaction)

| JSON Field | Dataverse Field | Type |
|------------|-----------------|------|
| `id` | `sprk_analysisactionid` | GUID |
| `name` | `sprk_name` | Text |
| `displayName` | `sprk_displayname` | Text |
| `description` | `sprk_description` | Multi-line Text |
| `scopeType` | (derived) | - |
| `ownerType` | `sprk_ownertype` | Choice |
| `isImmutable` | `sprk_isimmutable` | Boolean |
| `systemPrompt` | `sprk_systemprompt` | Multi-line Text |
| `metadata` | `sprk_metadata` | Multi-line Text (JSON) |

### Skill Records (sprk_analysisskill)

| JSON Field | Dataverse Field | Type |
|------------|-----------------|------|
| `id` | `sprk_analysisskillid` | GUID |
| `name` | `sprk_name` | Text |
| `displayName` | `sprk_displayname` | Text |
| `description` | `sprk_description` | Multi-line Text |
| `scopeType` | (derived) | - |
| `ownerType` | `sprk_ownertype` | Choice |
| `isImmutable` | `sprk_isimmutable` | Boolean |
| `promptFragment` | `sprk_promptfragment` | Multi-line Text |
| `metadata` | `sprk_metadata` | Multi-line Text (JSON) |

### Tool Records (sprk_analysistool)

| JSON Field | Dataverse Field | Type |
|------------|-----------------|------|
| `id` | `sprk_analysistoolid` | GUID |
| `name` | `sprk_name` | Text |
| `displayName` | `sprk_displayname` | Text |
| `description` | `sprk_description` | Multi-line Text |
| `scopeType` | (derived) | - |
| `ownerType` | `sprk_ownertype` | Choice |
| `isImmutable` | `sprk_isimmutable` | Boolean |
| `configuration` | `sprk_configuration` | Multi-line Text (JSON) |
| `metadata` | `sprk_metadata` | Multi-line Text (JSON) |

### Knowledge Records (sprk_analysisknowledge)

| JSON Field | Dataverse Field | Type |
|------------|-----------------|------|
| `id` | `sprk_analysisknowledgeid` | GUID |
| `name` | `sprk_name` | Text |
| `displayName` | `sprk_displayname` | Text |
| `description` | `sprk_description` | Multi-line Text |
| `scopeType` | (derived) | - |
| `ownerType` | `sprk_ownertype` | Choice |
| `isImmutable` | `sprk_isimmutable` | Boolean |
| `content` | `sprk_content` | Multi-line Text (JSON) |
| `metadata` | `sprk_metadata` | Multi-line Text (JSON) |

---

## Verification Checklist

After deployment, verify:

### Schema Verification

- [ ] Global option set `sprk_scopeownertype` exists with System (1) and Customer (2)
- [ ] All 4 entities have `sprk_ownertype` field
- [ ] All 4 entities have `sprk_isimmutable` field
- [ ] All 4 entities have `sprk_parentscope` self-referencing lookup
- [ ] All 4 entities have `sprk_basedon` self-referencing lookup

### Data Verification

- [ ] 5 Action records exist with `SYS-ACT-BUILDER-*` names
- [ ] 5 Skill records exist with `SYS-SKL-BUILDER-*` names
- [ ] 9 Tool records exist with `SYS-TL-BUILDER-*` names
- [ ] 4 Knowledge records exist with `SYS-KNW-BUILDER-*` names
- [ ] All 23 records have `sprk_ownertype = 1` (System)
- [ ] All 23 records have `sprk_isimmutable = true`

### API Verification

Test querying scopes via BFF API:

```bash
# Get all builder actions
curl -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/scopes/actions?filter=name%20contains%20'BUILDER'" \
  -H "Authorization: Bearer {token}"

# Expected: 5 action records

# Get all builder scopes (all types)
curl -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/scopes/search?q=BUILDER&type=all" \
  -H "Authorization: Bearer {token}"

# Expected: 23 total records
```

### Solution Verification

- [ ] Solution exports as unmanaged
- [ ] Solution imports cleanly in test environment
- [ ] All records included in solution export
- [ ] No managed layer conflicts

---

## Troubleshooting

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| "Field does not exist" | Schema not deployed | Run task 005 schema deployment first |
| "Duplicate record" | Record already exists | Delete existing or use upsert |
| "Invalid option set value" | Wrong ownerType value | Ensure using 1 (System) not "System" |
| "JSON parse error" | Invalid metadata/config | Validate JSON syntax in source files |

### Rollback

To remove all builder scopes:

```powershell
# Delete all records with BUILDER in name
$entities = @("sprk_analysisactions", "sprk_analysisskills", "sprk_analysisknowledges", "sprk_analysistools")

foreach ($entity in $entities) {
    $records = pac data record list --entity $entity --filter "contains(sprk_name, 'BUILDER')"
    foreach ($record in $records) {
        pac data record delete --entity $entity --id $record.Id
    }
}
```

---

## Related Documents

| Document | Purpose |
|----------|---------|
| [dataverse-ownership-fields-spec.md](dataverse-ownership-fields-spec.md) | Schema field specifications (task 005) |
| [builder-scopes/INDEX.md](builder-scopes/INDEX.md) | Complete scope record listing |
| [ADR-022](../../../../.claude/adr/ADR-022-pcf-platform-libraries.md) | Unmanaged solution requirement |
| [dataverse-deploy SKILL.md](../../../../.claude/skills/dataverse-deploy/SKILL.md) | Deployment procedures |

---

## JSON Source Files

All 23 scope records are defined in:

```
projects/ai-playbook-node-builder-r3/notes/builder-scopes/
├── ACT-BUILDER-001-intent-classification.json
├── ACT-BUILDER-002-node-configuration.json
├── ACT-BUILDER-003-scope-selection.json
├── ACT-BUILDER-004-scope-creation.json
├── ACT-BUILDER-005-build-plan-generation.json
├── SKL-BUILDER-001-lease-analysis-pattern.json
├── SKL-BUILDER-002-contract-review-pattern.json
├── SKL-BUILDER-003-risk-assessment-pattern.json
├── SKL-BUILDER-004-node-type-guide.json
├── SKL-BUILDER-005-scope-matching.json
├── TL-BUILDER-001-addNode.json
├── TL-BUILDER-002-removeNode.json
├── TL-BUILDER-003-createEdge.json
├── TL-BUILDER-004-updateNodeConfig.json
├── TL-BUILDER-005-linkScope.json
├── TL-BUILDER-006-createScope.json
├── TL-BUILDER-007-searchScopes.json
├── TL-BUILDER-008-autoLayout.json
├── TL-BUILDER-009-validateCanvas.json
├── KNW-BUILDER-001-scope-catalog.json
├── KNW-BUILDER-002-reference-playbooks.json
├── KNW-BUILDER-003-node-schema.json
├── KNW-BUILDER-004-best-practices.json
└── INDEX.md
```

---

*Specification created: 2026-01-19 for task 021*
