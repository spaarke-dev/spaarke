# Task 005 — Playbook Seed Data Reference

> Created: 2026-03-17
> Purpose: Documents the trigger metadata seed values applied (or planned) for each playbook.
> Script: `scripts/Seed-PlaybookTriggerMetadata.ps1`
> Depends on: R2-001 (schema fields must exist)
> Consumed by: R2-016 (playbook embedding pipeline)

## Overview

The seed script populates four fields on `sprk_analysisplaybook` records:

| Field | Type | Content |
|-------|------|---------|
| `sprk_triggerphrases` | Multiline text (max 4000) | Newline-delimited natural language phrases |
| `sprk_recordtype` | Single line text (max 100) | Record type: "matter", "project", "event", etc. |
| `sprk_entitytype` | Single line text (max 100) | Dataverse entity logical name |
| `sprk_tags` | Multiline text (max 2000) | Comma-delimited classification tags |

## Seed Data by Playbook

### Playbook Catalog (PB-001 through PB-010 from playbook-architecture.md)

---

#### Quick Document Review (PB-001)

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `document,review,quick,triage,general` |
| **triggerPhrases** | |

```
review this document
give me a quick review
what does this document say
scan this document for me
do a quick review of this file
summarize and review this document
I need a quick document review
can you look over this document
tell me what this document is about
quick scan of the uploaded file
```

---

#### Full Contract Analysis (PB-002)

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `contract,analysis,legal,comprehensive,clauses,risk` |
| **triggerPhrases** | |

```
analyze this contract
do a full contract review
review the contract in detail
I need a thorough contract analysis
break down this contract for me
what are the key terms in this contract
identify risks in this contract
give me a comprehensive contract review
review the agreement and flag issues
analyze the clauses in this contract
```

---

#### NDA Review (PB-003)

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `nda,confidentiality,legal,review,privacy` |
| **triggerPhrases** | |

```
review this NDA
analyze the non-disclosure agreement
check this NDA for issues
is this NDA standard
review the confidentiality agreement
what are the key terms in this NDA
flag risks in this NDA
I need an NDA review
look over this non-disclosure agreement
check the confidentiality terms
```

---

#### Lease Review (PB-004)

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `lease,real-estate,commercial,legal,review,property` |
| **triggerPhrases** | |

```
review this lease
analyze the lease agreement
what are the lease terms
check this commercial lease
review the rental agreement
I need a lease review
identify issues in this lease
break down the lease terms
review the office lease agreement
analyze the landlord tenant obligations
```

---

#### Employment Contract (PB-005)

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `employment,HR,contract,legal,compensation,non-compete` |
| **triggerPhrases** | |

```
review this employment agreement
analyze the employment contract
check the offer letter terms
review the contractor agreement
what are the compensation terms
check the non-compete clause
I need an employment contract review
review the IP assignment provisions
analyze the severance terms
look over this hiring agreement
```

---

#### Invoice Validation (PB-006)

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `invoice,financial,validation,accounts-payable,vendor` |
| **triggerPhrases** | |

```
validate this invoice
check the invoice details
process this invoice
review the vendor invoice
verify the invoice amounts
I need invoice validation
check this bill for errors
review the payment terms on this invoice
validate the line items
is this invoice correct
```

---

#### SLA Analysis (PB-007)

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `sla,service-level,compliance,legal,metrics,availability` |
| **triggerPhrases** | |

```
analyze this SLA
review the service level agreement
check the SLA terms
what are the SLA commitments
review the uptime guarantees
I need an SLA analysis
check the service credits
review the performance metrics in this SLA
analyze the service level objectives
what happens if the SLA is breached
```

---

#### Due Diligence Review (PB-008)

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `due-diligence,risk,review,compliance,assessment` |
| **triggerPhrases** | |

```
run due diligence on this document
I need a due diligence review
check this for due diligence
perform a due diligence analysis
review this document for due diligence purposes
classify and assess this document
what risks does this document present
due diligence scan of this file
```

---

#### Compliance Review (PB-009)

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `compliance,policy,regulatory,risk,review,governance` |
| **triggerPhrases** | |

```
check this for compliance
review compliance of this document
is this document compliant
run a compliance check
review the policy compliance
I need a compliance review
check this contract against our policies
analyze regulatory compliance
flag compliance issues in this document
```

---

#### Risk-Focused Scan (PB-010)

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `risk,scan,red-flags,quick,legal,compliance` |
| **triggerPhrases** | |

```
scan this for risks
what are the red flags
do a risk scan
identify risks in this document
flag problematic clauses
I need a risk assessment
check for red flags in this contract
quick risk scan of this file
highlight the risky terms
what should I be worried about in this document
```

---

### Scope-Model Composition Playbooks (from scope-model-index.json)

These may exist in Dataverse under their composition names (PB-001 through PB-010) rather than the playbook-architecture names. The seed script includes both naming variants to maximize coverage.

---

#### Standard Contract Review

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `contract,review,standard,legal,terms` |
| **triggerPhrases** | 8 phrases covering standard contract review requests |

---

#### NDA Deep Review

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `nda,deep-review,confidentiality,legal,detailed` |
| **triggerPhrases** | 8 phrases covering thorough NDA analysis requests |

---

#### Commercial Lease Analysis

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `lease,commercial,real-estate,analysis,NNN,legal` |
| **triggerPhrases** | 8 phrases covering commercial lease review requests |

---

#### SLA Compliance Review

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `sla,compliance,service-level,review,obligations` |
| **triggerPhrases** | 8 phrases covering SLA compliance checks |

---

#### Employment Agreement Review

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `employment,agreement,legal,HR,review,contract` |
| **triggerPhrases** | 8 phrases covering employment agreement analysis |

---

#### Statement of Work Analysis

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `sow,statement-of-work,project,deliverables,legal` |
| **triggerPhrases** | 8 phrases covering SOW and work order analysis |

---

#### IP Assignment Review

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `IP,intellectual-property,assignment,legal,review` |
| **triggerPhrases** | 8 phrases covering IP and invention assignment review |

---

#### Termination Risk Assessment

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `termination,risk,assessment,legal,exit,notice` |
| **triggerPhrases** | 8 phrases covering termination clause and exit risk analysis |

---

#### Quick Legal Scan

| Field | Value |
|-------|-------|
| **recordType** | `matter` |
| **entityType** | `sprk_analysisoutput` |
| **tags** | `legal,scan,quick,triage,red-flags,general` |
| **triggerPhrases** | 8 phrases covering fast legal triage and scanning |

---

## Design Decisions

1. **All playbooks use `recordType = "matter"`**: In the current Spaarke deployment, all document analysis playbooks operate within the context of a matter. Future playbooks for projects or events would use different record types.

2. **All playbooks use `entityType = "sprk_analysisoutput"`**: Analysis results are stored in the `sprk_analysisoutput` entity. The entity type indicates where the playbook's output is persisted, not the input entity.

3. **Dual naming coverage**: The seed data includes both the playbook-architecture.md names (e.g., "Quick Document Review") and the scope-model-index.json composition names (e.g., "Standard Contract Review") to handle whichever naming convention was used during deployment.

4. **Trigger phrase diversity**: Phrases use varied vocabulary, sentence structure, and specificity levels. They range from formal ("I need a thorough contract analysis") to casual ("break down this contract for me") to reflect real user behavior.

5. **Tag conventions**: Tags are lowercase, hyphenated for multi-word terms, and cover both the document domain (e.g., "contract", "nda") and the analysis intent (e.g., "review", "risk", "compliance").

## Usage

```powershell
# Preview what would be seeded (no auth required)
.\scripts\Seed-PlaybookTriggerMetadata.ps1 -DryRun

# Seed dev environment (requires az login)
.\scripts\Seed-PlaybookTriggerMetadata.ps1

# Re-run is safe — only updates empty fields
.\scripts\Seed-PlaybookTriggerMetadata.ps1

# Force overwrite all values (use after updating seed data)
.\scripts\Seed-PlaybookTriggerMetadata.ps1 -Force
```
