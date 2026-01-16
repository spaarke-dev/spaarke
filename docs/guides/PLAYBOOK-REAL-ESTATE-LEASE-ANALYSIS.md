# Playbook Configuration Guide: Real Estate Lease Agreement Analysis

> **Version**: 1.0
> **Last Updated**: January 15, 2026
> **Playbook ID**: PB-LEASE-001
> **Category**: Contract Analysis / Compliance

---

## Table of Contents

- [Overview](#overview)
- [Use Case Description](#use-case-description)
- [Playbook Architecture](#playbook-architecture)
- [Node Definitions](#node-definitions)
- [Scope Configurations](#scope-configurations)
  - [Actions](#actions)
  - [Skills](#skills)
  - [Knowledge Sources](#knowledge-sources)
  - [Tools](#tools)
- [Node Flow and Connections](#node-flow-and-connections)
- [Output Assembly](#output-assembly)
- [Delivery Configuration](#delivery-configuration)
- [Canvas JSON Configuration](#canvas-json-configuration)
- [Testing and Validation](#testing-and-validation)

---

## Overview

This guide provides step-by-step instructions for configuring a Playbook that analyzes Real Estate Lease Agreements against company standard terms and conditions. The playbook generates a comprehensive Agreement Summary document with compliance scoring, risk flagging, and multi-format delivery (Word, PDF, Email).

### Key Capabilities

| Capability | Description |
|------------|-------------|
| **Compliance Analysis** | Compare lease terms against company standards with risk coefficients |
| **Risk Flagging** | Visual indicators (🔴 Red ≥75%, 🟡 Yellow 50-74%) for out-of-compliance terms |
| **Multi-Section Output** | 9 structured sections covering all key lease aspects |
| **Similar Document Search** | RAG-powered search for related agreements in the system |
| **Multi-Format Delivery** | Word, PDF, and Email output options |

---

## Use Case Description

### Business Context

Legal and procurement teams need to quickly assess incoming lease agreements for compliance with company standards before execution. Manual review is time-consuming and inconsistent. This playbook automates the initial analysis, flagging high-risk provisions for attorney review.

### Invocation

- **Trigger**: User-initiated from Document record in Dataverse
- **Input**: Document entity with associated file in SharePoint Embedded (SPE)
- **Prerequisite**: Document must be uploaded and text-extractable (PDF, DOCX, TXT)

### Output Sections

| # | Section | Purpose |
|---|---------|---------|
| 1 | TL;DR Summary | Executive overview (2-3 sentences) |
| 2 | Compliance Analysis | Terms out of compliance with risk coefficients |
| 3 | Parties | Landlord, Tenant, Guarantors, etc. |
| 4 | Financial Terms | Rent, deposits, escalations, CAM charges |
| 5 | Term & Termination | Lease duration, renewal options, termination rights |
| 6 | Jurisdiction Terms | State/local specific provisions and variances |
| 7 | Liability & Indemnification | Insurance requirements, liability caps, indemnity |
| 8 | Other Notable Terms | Assignment, subletting, alterations, etc. |
| 9 | Similar Documents | Related agreements from document repository |

---

## Playbook Architecture

### High-Level Flow Diagram

```
                                    ┌─────────────────────────────────────┐
                                    │         USER INVOCATION             │
                                    │   (Document Record in Dataverse)    │
                                    └─────────────────────────────────────┘
                                                      │
                                                      ▼
                              ┌─────────────────────────────────────────────┐
                              │              NODE 1: START                   │
                              │         Document Text Extraction             │
                              │    Extract text from SPE file (PDF/DOCX)     │
                              └─────────────────────────────────────────────┘
                                                      │
                                                      ▼
                              ┌─────────────────────────────────────────────┐
                              │           NODE 2: AI ANALYSIS                │
                              │          TL;DR Summary Generation            │
                              │     Quick executive overview of lease        │
                              └─────────────────────────────────────────────┘
                                                      │
                    ┌─────────────────────────────────┼─────────────────────────────────┐
                    │                                 │                                 │
                    ▼                                 ▼                                 ▼
    ┌───────────────────────────┐   ┌───────────────────────────┐   ┌───────────────────────────┐
    │    NODE 3: AI ANALYSIS    │   │    NODE 4: AI ANALYSIS    │   │    NODE 5: AI ANALYSIS    │
    │    Compliance Analysis    │   │     Party Extraction      │   │   Financial Terms         │
    │  Compare vs. Standards    │   │  Identify all parties     │   │  Rent, deposits, etc.     │
    └───────────────────────────┘   └───────────────────────────┘   └───────────────────────────┘
                    │                                 │                                 │
                    │                                 │                                 │
                    ▼                                 │                                 │
    ┌───────────────────────────┐                    │                                 │
    │    NODE 6: AI ANALYSIS    │                    │                                 │
    │  Term & Termination       │                    │                                 │
    │  Duration, renewals       │                    │                                 │
    └───────────────────────────┘                    │                                 │
                    │                                 │                                 │
                    ▼                                 │                                 │
    ┌───────────────────────────┐                    │                                 │
    │    NODE 7: AI ANALYSIS    │                    │                                 │
    │  Jurisdiction Analysis    │                    │                                 │
    │  State-specific terms     │                    │                                 │
    └───────────────────────────┘                    │                                 │
                    │                                 │                                 │
                    ▼                                 │                                 │
    ┌───────────────────────────┐                    │                                 │
    │    NODE 8: AI ANALYSIS    │                    │                                 │
    │  Liability & Indemnity    │                    │                                 │
    │  Insurance, caps, etc.    │                    │                                 │
    └───────────────────────────┘                    │                                 │
                    │                                 │                                 │
                    ▼                                 │                                 │
    ┌───────────────────────────┐                    │                                 │
    │    NODE 9: AI ANALYSIS    │                    │                                 │
    │  Other Notable Terms      │                    │                                 │
    │  Assignment, subletting   │                    │                                 │
    └───────────────────────────┘                    │                                 │
                    │                                 │                                 │
                    └─────────────────────────────────┼─────────────────────────────────┘
                                                      │
                                                      ▼
                              ┌─────────────────────────────────────────────┐
                              │          NODE 10: RAG SEARCH                 │
                              │        Similar Document Finder               │
                              │   Search for related leases in repository    │
                              └─────────────────────────────────────────────┘
                                                      │
                                                      ▼
                              ┌─────────────────────────────────────────────┐
                              │         NODE 11: ASSEMBLE OUTPUT             │
                              │       Compile All Sections into Report       │
                              │    Merge results into structured document    │
                              └─────────────────────────────────────────────┘
                                                      │
                                                      ▼
                              ┌─────────────────────────────────────────────┐
                              │         NODE 12: DELIVER OUTPUT              │
                              │      Generate Word, PDF, Send Email          │
                              │   Store output, notify user, attach file     │
                              └─────────────────────────────────────────────┘
                                                      │
                                                      ▼
                                              ┌─────────────┐
                                              │     END     │
                                              └─────────────┘
```

### Execution Strategy

| Phase | Nodes | Execution |
|-------|-------|-----------|
| **Phase 1** | Node 1 (Start) | Sequential - Must complete first |
| **Phase 2** | Node 2 (TL;DR) | Sequential - Provides context for later nodes |
| **Phase 3** | Nodes 3-9 | **Parallel** - Independent analysis nodes |
| **Phase 4** | Node 10 (RAG Search) | Sequential - After analysis complete |
| **Phase 5** | Nodes 11-12 | Sequential - Assembly and delivery |

---

## Node Definitions

### Node 1: Document Text Extraction (START)

| Property | Value |
|----------|-------|
| **Node ID** | `node_start_001` |
| **Type** | `start` (implicit) |
| **Label** | "Extract Document Text" |
| **Purpose** | Extract plain text from uploaded document file |

**Configuration:**
```json
{
  "id": "node_start_001",
  "type": "start",
  "position": { "x": 400, "y": 50 },
  "data": {
    "label": "Extract Document Text",
    "type": "start",
    "outputVariable": "documentText",
    "description": "Extracts text content from the source document (PDF, DOCX, TXT) using Azure Document Intelligence or native text extraction."
  }
}
```

**Output:**
| Variable | Type | Format |
|----------|------|--------|
| `documentText` | `string` | Plain text extracted from document |
| `documentMetadata` | `object` | `{ pageCount, wordCount, language, fileType }` |

---

### Node 2: TL;DR Summary Generation

| Property | Value |
|----------|-------|
| **Node ID** | `node_tldr_002` |
| **Type** | `aiAnalysis` |
| **Label** | "Generate TL;DR Summary" |
| **Action** | ACT-LEASE-001 |
| **Skills** | SKL-LEASE-001 (Real Estate Domain) |
| **Model** | `gpt-4o` (higher quality for summary) |
| **Timeout** | 120 seconds |

**Configuration:**
```json
{
  "id": "node_tldr_002",
  "type": "aiAnalysis",
  "position": { "x": 400, "y": 150 },
  "data": {
    "label": "Generate TL;DR Summary",
    "type": "aiAnalysis",
    "outputVariable": "tldrSummary",
    "actionId": "ACT-LEASE-001",
    "skillIds": ["SKL-LEASE-001"],
    "knowledgeIds": [],
    "toolId": "TL-SUMMARY-001",
    "modelDeploymentId": "gpt-4o",
    "timeoutSeconds": 120,
    "retryCount": 2
  }
}
```

**Output:**
| Variable | Type | Format |
|----------|------|--------|
| `tldrSummary` | `object` | See output schema below |

**Output Schema:**
```json
{
  "tldrSummary": {
    "summary": "string (2-3 sentences)",
    "leaseType": "string (Commercial|Residential|Industrial|Retail|Office)",
    "overallRiskLevel": "string (Low|Medium|High|Critical)",
    "keyHighlights": ["string", "string", "string"]
  }
}
```

**Example Output:**
```json
{
  "tldrSummary": {
    "summary": "This is a 10-year commercial office lease between Acme Corp (Tenant) and Metropolis Properties LLC (Landlord) for 15,000 SF at 123 Main Street, Chicago, IL. The lease includes a 3% annual escalation clause and requires $450,000 security deposit with personal guaranty.",
    "leaseType": "Office",
    "overallRiskLevel": "Medium",
    "keyHighlights": [
      "Above-market security deposit requirement",
      "Personal guaranty extends 24 months post-term",
      "No early termination rights for tenant"
    ]
  }
}
```

---

### Node 3: Compliance Analysis

| Property | Value |
|----------|-------|
| **Node ID** | `node_compliance_003` |
| **Type** | `aiAnalysis` |
| **Label** | "Analyze Compliance vs. Standards" |
| **Action** | ACT-LEASE-002 |
| **Skills** | SKL-LEASE-001, SKL-LEASE-002 (Compliance Scoring) |
| **Knowledge** | KNL-LEASE-001 (Company Standard Terms) |
| **Model** | `gpt-4o` |
| **Timeout** | 300 seconds |

**Configuration:**
```json
{
  "id": "node_compliance_003",
  "type": "aiAnalysis",
  "position": { "x": 100, "y": 300 },
  "data": {
    "label": "Analyze Compliance vs. Standards",
    "type": "aiAnalysis",
    "outputVariable": "complianceAnalysis",
    "actionId": "ACT-LEASE-002",
    "skillIds": ["SKL-LEASE-001", "SKL-LEASE-002"],
    "knowledgeIds": ["KNL-LEASE-001"],
    "toolId": "TL-COMPLIANCE-001",
    "modelDeploymentId": "gpt-4o",
    "timeoutSeconds": 300,
    "retryCount": 2
  }
}
```

**Output Schema:**
```json
{
  "complianceAnalysis": {
    "totalTermsAnalyzed": "number",
    "compliantCount": "number",
    "nonCompliantCount": "number",
    "provisions": [
      {
        "provisionName": "string",
        "category": "string (Financial|Legal|Operational|Insurance)",
        "leaseValue": "string (what the lease says)",
        "standardValue": "string (company standard)",
        "variancePercentage": "number (0-100)",
        "riskCoefficient": "string (Compliant|Yellow|Red)",
        "riskLevel": "string (Low|Medium|High|Critical)",
        "recommendation": "string",
        "clauseReference": "string (Section/Page reference)"
      }
    ],
    "summaryByCategory": {
      "Financial": { "compliant": 0, "yellow": 0, "red": 0 },
      "Legal": { "compliant": 0, "yellow": 0, "red": 0 },
      "Operational": { "compliant": 0, "yellow": 0, "red": 0 },
      "Insurance": { "compliant": 0, "yellow": 0, "red": 0 }
    }
  }
}
```

**Risk Coefficient Logic:**
| Variance % | Coefficient | Visual | Description |
|------------|-------------|--------|-------------|
| 0-49% | `Compliant` | ✅ | Within acceptable range |
| 50-74% | `Yellow` | 🟡 | Requires review, negotiation recommended |
| 75-100%+ | `Red` | 🔴 | Critical deviation, legal review required |

**Example Output:**
```json
{
  "complianceAnalysis": {
    "totalTermsAnalyzed": 24,
    "compliantCount": 18,
    "nonCompliantCount": 6,
    "provisions": [
      {
        "provisionName": "Security Deposit",
        "category": "Financial",
        "leaseValue": "6 months base rent ($450,000)",
        "standardValue": "2 months base rent",
        "variancePercentage": 200,
        "riskCoefficient": "Red",
        "riskLevel": "High",
        "recommendation": "Negotiate reduction to 3 months maximum with step-down after Year 3",
        "clauseReference": "Section 4.2, Page 8"
      },
      {
        "provisionName": "Annual Rent Escalation",
        "category": "Financial",
        "leaseValue": "3% fixed annual increase",
        "standardValue": "CPI-capped at 2.5%",
        "variancePercentage": 60,
        "riskCoefficient": "Yellow",
        "riskLevel": "Medium",
        "recommendation": "Request CPI-based escalation with 3% cap",
        "clauseReference": "Section 3.3, Page 6"
      }
    ],
    "summaryByCategory": {
      "Financial": { "compliant": 4, "yellow": 2, "red": 2 },
      "Legal": { "compliant": 6, "yellow": 1, "red": 1 },
      "Operational": { "compliant": 5, "yellow": 0, "red": 0 },
      "Insurance": { "compliant": 3, "yellow": 1, "red": 0 }
    }
  }
}
```

---

### Node 4: Party Extraction

| Property | Value |
|----------|-------|
| **Node ID** | `node_parties_004` |
| **Type** | `aiAnalysis` |
| **Label** | "Extract Parties" |
| **Action** | ACT-LEASE-003 |
| **Skills** | SKL-LEASE-001 |
| **Model** | `gpt-4o-mini` |
| **Timeout** | 90 seconds |

**Configuration:**
```json
{
  "id": "node_parties_004",
  "type": "aiAnalysis",
  "position": { "x": 400, "y": 300 },
  "data": {
    "label": "Extract Parties",
    "type": "aiAnalysis",
    "outputVariable": "partiesExtraction",
    "actionId": "ACT-LEASE-003",
    "skillIds": ["SKL-LEASE-001"],
    "knowledgeIds": [],
    "toolId": "TL-ENTITY-001",
    "modelDeploymentId": "gpt-4o-mini",
    "timeoutSeconds": 90,
    "retryCount": 2
  }
}
```

**Output Schema:**
```json
{
  "partiesExtraction": {
    "landlord": {
      "name": "string",
      "type": "string (Individual|Corporation|LLC|Partnership|Trust)",
      "address": "string",
      "stateOfFormation": "string",
      "contactInfo": "string (if available)"
    },
    "tenant": {
      "name": "string",
      "type": "string",
      "address": "string",
      "stateOfFormation": "string",
      "contactInfo": "string"
    },
    "guarantors": [
      {
        "name": "string",
        "type": "string (Individual|Corporate)",
        "guarantyType": "string (Full|Limited|Good Guy)",
        "guarantyLimit": "string (amount or duration)"
      }
    ],
    "propertyManager": {
      "name": "string",
      "address": "string",
      "contactInfo": "string"
    },
    "brokers": [
      {
        "name": "string",
        "representedParty": "string (Landlord|Tenant)",
        "commission": "string"
      }
    ]
  }
}
```

---

### Node 5: Financial Terms Extraction

| Property | Value |
|----------|-------|
| **Node ID** | `node_financial_005` |
| **Type** | `aiAnalysis` |
| **Label** | "Extract Financial Terms" |
| **Action** | ACT-LEASE-004 |
| **Skills** | SKL-LEASE-001, SKL-LEASE-003 (Financial Analysis) |
| **Knowledge** | KNL-LEASE-001 |
| **Model** | `gpt-4o` |
| **Timeout** | 180 seconds |

**Configuration:**
```json
{
  "id": "node_financial_005",
  "type": "aiAnalysis",
  "position": { "x": 700, "y": 300 },
  "data": {
    "label": "Extract Financial Terms",
    "type": "aiAnalysis",
    "outputVariable": "financialTerms",
    "actionId": "ACT-LEASE-004",
    "skillIds": ["SKL-LEASE-001", "SKL-LEASE-003"],
    "knowledgeIds": ["KNL-LEASE-001"],
    "toolId": "TL-FINANCIAL-001",
    "modelDeploymentId": "gpt-4o",
    "timeoutSeconds": 180,
    "retryCount": 2
  }
}
```

**Output Schema:**
```json
{
  "financialTerms": {
    "baseRent": {
      "initialAmount": "number",
      "frequency": "string (Monthly|Quarterly|Annual)",
      "perSqFt": "number",
      "escalationType": "string (Fixed|CPI|Market)",
      "escalationRate": "string",
      "escalationSchedule": [
        { "year": 1, "monthlyRent": 75000, "annualRent": 900000, "perSqFt": 60.00 }
      ]
    },
    "securityDeposit": {
      "amount": "number",
      "form": "string (Cash|LOC|Both)",
      "returnConditions": "string",
      "interestBearing": "boolean"
    },
    "additionalRent": {
      "cam": { "estimated": "number", "capType": "string", "capAmount": "string" },
      "taxes": { "baseYear": "string", "estimatedAmount": "number" },
      "insurance": { "estimatedAmount": "number" },
      "utilities": { "includedInRent": "boolean", "estimated": "number" }
    },
    "tenantImprovements": {
      "allowanceAmount": "number",
      "perSqFt": "number",
      "disbursementMethod": "string",
      "deadlineForUse": "string"
    },
    "freeRent": {
      "months": "number",
      "conditions": "string"
    },
    "totalLeaseValue": {
      "baseRentTotal": "number",
      "estimatedAdditionalRent": "number",
      "grandTotal": "number"
    }
  }
}
```

---

### Node 6: Term & Termination Analysis

| Property | Value |
|----------|-------|
| **Node ID** | `node_term_006` |
| **Type** | `aiAnalysis` |
| **Label** | "Analyze Term & Termination" |
| **Action** | ACT-LEASE-005 |
| **Skills** | SKL-LEASE-001 |
| **Model** | `gpt-4o-mini` |
| **Timeout** | 120 seconds |

**Configuration:**
```json
{
  "id": "node_term_006",
  "type": "aiAnalysis",
  "position": { "x": 100, "y": 500 },
  "data": {
    "label": "Analyze Term & Termination",
    "type": "aiAnalysis",
    "outputVariable": "termAnalysis",
    "actionId": "ACT-LEASE-005",
    "skillIds": ["SKL-LEASE-001"],
    "knowledgeIds": [],
    "toolId": "TL-TERM-001",
    "modelDeploymentId": "gpt-4o-mini",
    "timeoutSeconds": 120,
    "retryCount": 2
  }
}
```

**Output Schema:**
```json
{
  "termAnalysis": {
    "leaseTerm": {
      "commencementDate": "string (ISO date)",
      "expirationDate": "string (ISO date)",
      "durationMonths": "number",
      "durationYears": "number"
    },
    "renewalOptions": [
      {
        "optionNumber": 1,
        "durationMonths": "number",
        "noticeRequiredDays": "number",
        "rentBasis": "string (FMV|Fixed Increase|CPI)",
        "rentDetails": "string"
      }
    ],
    "earlyTermination": {
      "tenantRights": {
        "allowed": "boolean",
        "conditions": "string",
        "penalty": "string",
        "noticeRequired": "string"
      },
      "landlordRights": {
        "allowed": "boolean",
        "conditions": "string"
      }
    },
    "holdover": {
      "rentMultiplier": "number (e.g., 1.5 = 150%)",
      "terms": "string"
    },
    "keyDates": [
      { "date": "string", "event": "string", "action": "string" }
    ]
  }
}
```

---

### Node 7: Jurisdiction Analysis

| Property | Value |
|----------|-------|
| **Node ID** | `node_jurisdiction_007` |
| **Type** | `aiAnalysis` |
| **Label** | "Analyze Jurisdiction Terms" |
| **Action** | ACT-LEASE-006 |
| **Skills** | SKL-LEASE-001, SKL-LEASE-004 (Jurisdiction) |
| **Knowledge** | KNL-LEASE-002 (Jurisdiction Requirements) |
| **Model** | `gpt-4o` |
| **Timeout** | 180 seconds |

**Configuration:**
```json
{
  "id": "node_jurisdiction_007",
  "type": "aiAnalysis",
  "position": { "x": 100, "y": 650 },
  "data": {
    "label": "Analyze Jurisdiction Terms",
    "type": "aiAnalysis",
    "outputVariable": "jurisdictionAnalysis",
    "actionId": "ACT-LEASE-006",
    "skillIds": ["SKL-LEASE-001", "SKL-LEASE-004"],
    "knowledgeIds": ["KNL-LEASE-002"],
    "toolId": "TL-JURISDICTION-001",
    "modelDeploymentId": "gpt-4o",
    "timeoutSeconds": 180,
    "retryCount": 2
  }
}
```

**Output Schema:**
```json
{
  "jurisdictionAnalysis": {
    "propertyLocation": {
      "address": "string",
      "city": "string",
      "state": "string",
      "county": "string",
      "zipCode": "string"
    },
    "governingLaw": {
      "state": "string",
      "venue": "string",
      "arbitrationRequired": "boolean"
    },
    "jurisdictionSpecificTerms": [
      {
        "term": "string",
        "requirement": "string",
        "leaseCompliance": "string (Compliant|Non-Compliant|Partial)",
        "notes": "string"
      }
    ],
    "localRequirements": {
      "licensesRequired": ["string"],
      "disclosuresRequired": ["string"],
      "tenantProtections": ["string"]
    },
    "variancesFromStandard": [
      {
        "area": "string",
        "standardApproach": "string",
        "jurisdictionRequirement": "string",
        "recommendation": "string"
      }
    ]
  }
}
```

---

### Node 8: Liability & Indemnification Analysis

| Property | Value |
|----------|-------|
| **Node ID** | `node_liability_008` |
| **Type** | `aiAnalysis` |
| **Label** | "Analyze Liability & Indemnification" |
| **Action** | ACT-LEASE-007 |
| **Skills** | SKL-LEASE-001, SKL-LEASE-005 (Liability) |
| **Knowledge** | KNL-LEASE-001 |
| **Model** | `gpt-4o` |
| **Timeout** | 180 seconds |

**Configuration:**
```json
{
  "id": "node_liability_008",
  "type": "aiAnalysis",
  "position": { "x": 100, "y": 800 },
  "data": {
    "label": "Analyze Liability & Indemnification",
    "type": "aiAnalysis",
    "outputVariable": "liabilityAnalysis",
    "actionId": "ACT-LEASE-007",
    "skillIds": ["SKL-LEASE-001", "SKL-LEASE-005"],
    "knowledgeIds": ["KNL-LEASE-001"],
    "toolId": "TL-LIABILITY-001",
    "modelDeploymentId": "gpt-4o",
    "timeoutSeconds": 180,
    "retryCount": 2
  }
}
```

**Output Schema:**
```json
{
  "liabilityAnalysis": {
    "insuranceRequirements": {
      "generalLiability": {
        "required": "boolean",
        "minimumCoverage": "string",
        "perOccurrence": "string",
        "aggregate": "string"
      },
      "propertyInsurance": {
        "required": "boolean",
        "coverage": "string",
        "replacementCost": "boolean"
      },
      "workersComp": { "required": "boolean" },
      "umbrellaExcess": { "required": "boolean", "minimum": "string" },
      "additionalInsured": {
        "landlordRequired": "boolean",
        "waiverOfSubrogation": "boolean"
      }
    },
    "indemnification": {
      "tenantIndemnifiesLandlord": {
        "scope": "string (Broad|Mutual|Limited)",
        "exceptions": ["string"],
        "capOnLiability": "string"
      },
      "landlordIndemnifiesTenant": {
        "scope": "string",
        "exceptions": ["string"]
      },
      "mutualIndemnification": "boolean"
    },
    "limitationOfLiability": {
      "consequentialDamagesWaiver": "boolean",
      "landlordLiabilityCap": "string",
      "tenantLiabilityCap": "string"
    },
    "defaultAndRemedies": {
      "tenantDefaultEvents": ["string"],
      "curePeriods": { "monetary": "string", "nonMonetary": "string" },
      "landlordRemedies": ["string"],
      "tenantRemedies": ["string"]
    },
    "complianceIssues": [
      {
        "issue": "string",
        "riskLevel": "string",
        "recommendation": "string"
      }
    ]
  }
}
```

---

### Node 9: Other Notable Terms

| Property | Value |
|----------|-------|
| **Node ID** | `node_notable_009` |
| **Type** | `aiAnalysis` |
| **Label** | "Extract Other Notable Terms" |
| **Action** | ACT-LEASE-008 |
| **Skills** | SKL-LEASE-001 |
| **Model** | `gpt-4o-mini` |
| **Timeout** | 120 seconds |

**Configuration:**
```json
{
  "id": "node_notable_009",
  "type": "aiAnalysis",
  "position": { "x": 100, "y": 950 },
  "data": {
    "label": "Extract Other Notable Terms",
    "type": "aiAnalysis",
    "outputVariable": "notableTerms",
    "actionId": "ACT-LEASE-008",
    "skillIds": ["SKL-LEASE-001"],
    "knowledgeIds": [],
    "toolId": "TL-NOTABLE-001",
    "modelDeploymentId": "gpt-4o-mini",
    "timeoutSeconds": 120,
    "retryCount": 2
  }
}
```

**Output Schema:**
```json
{
  "notableTerms": {
    "assignmentAndSubletting": {
      "assignmentAllowed": "boolean",
      "subletAllowed": "boolean",
      "landlordConsentRequired": "boolean",
      "consentStandard": "string (Sole Discretion|Reasonable|Not Unreasonably Withheld)",
      "profitSharingRequired": "boolean",
      "profitSharingPercentage": "number",
      "affiliateTransferExempt": "boolean"
    },
    "alterationsAndImprovements": {
      "tenantRightToAlter": "string",
      "landlordApprovalRequired": "boolean",
      "approvalThreshold": "string",
      "removalAtExpiration": "string"
    },
    "signsAndBranding": {
      "exteriorSignageAllowed": "boolean",
      "buildingDirectoryListing": "boolean",
      "specifications": "string"
    },
    "parkingAndAccess": {
      "spacesAllocated": "number",
      "reservedSpaces": "number",
      "monthlyRate": "string",
      "after hoursAccess": "string"
    },
    "exclusivityAndUseRestrictions": {
      "permittedUse": "string",
      "exclusiveUseGranted": "boolean",
      "exclusiveUseDetails": "string",
      "restrictedUses": ["string"]
    },
    "environmentalProvisions": {
      "hazmatRestrictions": "string",
      "tenantEnvironmentalReps": "boolean",
      "baselineAssessmentRequired": "boolean"
    },
    "coTenancyAndKickout": {
      "coTenancyClause": "boolean",
      "kickoutRight": "boolean",
      "details": "string"
    },
    "otherNoteworthyItems": [
      {
        "topic": "string",
        "summary": "string",
        "significance": "string (Low|Medium|High)"
      }
    ]
  }
}
```

---

### Node 10: Similar Document Search (RAG)

| Property | Value |
|----------|-------|
| **Node ID** | `node_similar_010` |
| **Type** | `aiCompletion` |
| **Label** | "Find Similar Documents" |
| **Action** | ACT-SEARCH-001 |
| **Knowledge** | KNL-LEASE-003 (Document Index) |
| **Tool** | TL-RAG-SEARCH-001 |
| **Model** | `text-embedding-ada-002` (for search) |
| **Timeout** | 60 seconds |

**Configuration:**
```json
{
  "id": "node_similar_010",
  "type": "aiCompletion",
  "position": { "x": 400, "y": 1100 },
  "data": {
    "label": "Find Similar Documents",
    "type": "aiCompletion",
    "outputVariable": "similarDocuments",
    "actionId": "ACT-SEARCH-001",
    "skillIds": [],
    "knowledgeIds": ["KNL-LEASE-003"],
    "toolId": "TL-RAG-SEARCH-001",
    "modelDeploymentId": "text-embedding-ada-002",
    "timeoutSeconds": 60,
    "retryCount": 1,
    "configuration": {
      "searchType": "semantic",
      "topK": 10,
      "minSimilarityScore": 0.75,
      "filterByDocumentType": "Lease Agreement"
    }
  }
}
```

**Output Schema:**
```json
{
  "similarDocuments": {
    "totalFound": "number",
    "documents": [
      {
        "documentId": "string (GUID)",
        "documentName": "string",
        "similarityScore": "number (0-1)",
        "documentType": "string",
        "parties": {
          "landlord": "string",
          "tenant": "string"
        },
        "propertyAddress": "string",
        "leaseTermYears": "number",
        "executionDate": "string",
        "keyDifferences": ["string"],
        "relevanceReason": "string"
      }
    ],
    "searchCriteria": {
      "propertyType": "string",
      "geographicArea": "string",
      "dealSizeRange": "string"
    }
  }
}
```

---

### Node 11: Assemble Output

| Property | Value |
|----------|-------|
| **Node ID** | `node_assemble_011` |
| **Type** | `assembleOutput` |
| **Label** | "Compile Agreement Summary" |
| **Purpose** | Merge all analysis outputs into final document structure |
| **Timeout** | 30 seconds |

**Configuration:**
```json
{
  "id": "node_assemble_011",
  "type": "assembleOutput",
  "position": { "x": 400, "y": 1250 },
  "data": {
    "label": "Compile Agreement Summary",
    "type": "assembleOutput",
    "outputVariable": "compiledReport",
    "inputVariables": [
      "tldrSummary",
      "complianceAnalysis",
      "partiesExtraction",
      "financialTerms",
      "termAnalysis",
      "jurisdictionAnalysis",
      "liabilityAnalysis",
      "notableTerms",
      "similarDocuments"
    ],
    "templateId": "TPL-LEASE-SUMMARY-001",
    "outputFormat": "structured",
    "timeoutSeconds": 30
  }
}
```

**Output Schema:**
```json
{
  "compiledReport": {
    "metadata": {
      "reportId": "string (GUID)",
      "generatedAt": "string (ISO timestamp)",
      "documentId": "string (source document GUID)",
      "documentName": "string",
      "playbookId": "string",
      "playbookVersion": "string"
    },
    "sections": {
      "tldr": { /* from tldrSummary */ },
      "compliance": { /* from complianceAnalysis */ },
      "parties": { /* from partiesExtraction */ },
      "financial": { /* from financialTerms */ },
      "term": { /* from termAnalysis */ },
      "jurisdiction": { /* from jurisdictionAnalysis */ },
      "liability": { /* from liabilityAnalysis */ },
      "notable": { /* from notableTerms */ },
      "similar": { /* from similarDocuments */ }
    },
    "executionMetrics": {
      "totalDurationMs": "number",
      "nodeExecutions": [
        { "nodeId": "string", "durationMs": "number", "tokensUsed": "number" }
      ],
      "totalTokensUsed": "number",
      "estimatedCost": "number"
    }
  }
}
```

---

### Node 12: Deliver Output

| Property | Value |
|----------|-------|
| **Node ID** | `node_deliver_012` |
| **Type** | `deliverOutput` |
| **Label** | "Generate & Deliver Report" |
| **Outputs** | Word, PDF, Email |
| **Timeout** | 120 seconds |

**Configuration:**
```json
{
  "id": "node_deliver_012",
  "type": "deliverOutput",
  "position": { "x": 400, "y": 1400 },
  "data": {
    "label": "Generate & Deliver Report",
    "type": "deliverOutput",
    "inputVariable": "compiledReport",
    "outputVariable": "deliveryResult",
    "deliveryOptions": {
      "word": {
        "enabled": true,
        "templateId": "TPL-WORD-LEASE-001",
        "fileName": "{{documentName}}_Analysis_{{date}}.docx",
        "saveToSpe": true,
        "speContainerId": "{{sourceContainerId}}"
      },
      "pdf": {
        "enabled": true,
        "generateFromWord": true,
        "fileName": "{{documentName}}_Analysis_{{date}}.pdf",
        "saveToSpe": true
      },
      "email": {
        "enabled": true,
        "templateId": "TPL-EMAIL-LEASE-001",
        "recipients": "{{currentUser.email}}",
        "ccRecipients": "",
        "subject": "Lease Analysis Complete: {{documentName}}",
        "attachWord": true,
        "attachPdf": true
      }
    },
    "timeoutSeconds": 120,
    "retryCount": 1
  }
}
```

**Output Schema:**
```json
{
  "deliveryResult": {
    "success": "boolean",
    "outputs": {
      "word": {
        "generated": "boolean",
        "fileId": "string (SPE file ID)",
        "fileName": "string",
        "fileSizeBytes": "number",
        "speUrl": "string"
      },
      "pdf": {
        "generated": "boolean",
        "fileId": "string",
        "fileName": "string",
        "fileSizeBytes": "number",
        "speUrl": "string"
      },
      "email": {
        "sent": "boolean",
        "messageId": "string",
        "recipients": ["string"],
        "sentAt": "string (ISO timestamp)"
      }
    },
    "analysisOutputId": "string (Dataverse record ID)",
    "errors": []
  }
}
```

---

## Scope Configurations

### Actions

Actions define the system prompt templates that instruct the LLM on behavior and response format.

#### ACT-LEASE-001: TL;DR Summary Generation

```json
{
  "id": "ACT-LEASE-001",
  "sprk_name": "Lease TL;DR Summary",
  "sprk_description": "Generate a concise executive summary of a real estate lease agreement",
  "actionType": "03 - Summarization",
  "sprk_systemprompt": "You are an expert real estate attorney specializing in commercial lease analysis. Your task is to provide a concise TL;DR summary of a lease agreement.\n\n## Instructions\n\n1. Read the entire lease document carefully\n2. Identify the most critical information: parties, property, term, and financial highlights\n3. Assess overall risk level based on deviation from market standards\n4. Highlight 3 key points that a decision-maker must know\n\n## Response Format\n\nProvide your response as valid JSON with this structure:\n```json\n{\n  \"summary\": \"2-3 sentence executive summary\",\n  \"leaseType\": \"Commercial|Residential|Industrial|Retail|Office\",\n  \"overallRiskLevel\": \"Low|Medium|High|Critical\",\n  \"keyHighlights\": [\"highlight 1\", \"highlight 2\", \"highlight 3\"]\n}\n```\n\n## Guidelines\n\n- Be concise but comprehensive\n- Use plain language accessible to business executives\n- Flag any unusual or concerning provisions\n- Focus on financial and legal risk factors"
}
```

#### ACT-LEASE-002: Compliance Analysis

```json
{
  "id": "ACT-LEASE-002",
  "sprk_name": "Lease Compliance Analysis",
  "sprk_description": "Compare lease terms against company standard terms and calculate variance/risk coefficients",
  "actionType": "05 - Analysis",
  "sprk_systemprompt": "You are a compliance analyst specializing in real estate lease agreements. Your task is to compare the provided lease against company standard terms and identify deviations.\n\n## Company Standard Terms\n\nThe following standards will be provided in the knowledge context. Compare each lease provision against these standards.\n\n## Risk Coefficient Calculation\n\nFor each provision that deviates from standard:\n1. Calculate variance percentage: |lease_value - standard_value| / standard_value × 100\n2. Assign risk coefficient:\n   - Compliant (✅): 0-49% variance\n   - Yellow (🟡): 50-74% variance - requires review\n   - Red (🔴): 75%+ variance - critical deviation\n\n## Response Format\n\nProvide your response as valid JSON matching the compliance analysis schema.\n\n## Categories to Analyze\n\n1. **Financial**: Rent, deposits, escalations, CAM, TI allowance\n2. **Legal**: Governing law, dispute resolution, liability caps\n3. **Operational**: Maintenance, alterations, access, parking\n4. **Insurance**: Coverage requirements, additional insured, waivers\n\n## Guidelines\n\n- Be thorough - analyze ALL material provisions\n- Provide specific clause references (Section X.X, Page Y)\n- Include actionable recommendations for non-compliant items\n- Consider cumulative risk across categories"
}
```

#### ACT-LEASE-003: Party Extraction

```json
{
  "id": "ACT-LEASE-003",
  "sprk_name": "Lease Party Extraction",
  "sprk_description": "Extract and structure all parties to the lease agreement",
  "actionType": "01 - Extraction",
  "sprk_systemprompt": "You are a legal document specialist. Extract all parties mentioned in this lease agreement.\n\n## Parties to Identify\n\n1. **Landlord**: Property owner or lessor entity\n2. **Tenant**: Lessee entity\n3. **Guarantors**: Personal or corporate guarantors\n4. **Property Manager**: If different from landlord\n5. **Brokers**: Real estate brokers for either party\n\n## Information to Extract\n\nFor each party:\n- Legal name (exactly as written)\n- Entity type (Individual, Corporation, LLC, Partnership, Trust)\n- Address (notice address if specified)\n- State of formation (for entities)\n- Contact information (if provided)\n- Role-specific details (e.g., guaranty type and limits)\n\n## Response Format\n\nProvide your response as valid JSON matching the parties extraction schema.\n\n## Guidelines\n\n- Extract names EXACTLY as they appear in the document\n- Note any aliases or \"d/b/a\" designations\n- For guarantors, specify guaranty type (Full, Limited, Good Guy)\n- Include all named parties, even if minor roles"
}
```

#### ACT-LEASE-004: Financial Terms Extraction

```json
{
  "id": "ACT-LEASE-004",
  "sprk_name": "Lease Financial Terms",
  "sprk_description": "Extract and calculate all financial terms from the lease",
  "actionType": "01 - Extraction",
  "sprk_systemprompt": "You are a financial analyst specializing in real estate. Extract all financial terms from this lease agreement.\n\n## Financial Categories\n\n### Base Rent\n- Initial amount (monthly and annual)\n- Per square foot rate\n- Escalation type and schedule\n- Build complete rent schedule for full term\n\n### Security Deposit\n- Amount and form (cash, LOC)\n- Return conditions\n- Interest provisions\n\n### Additional Rent (Operating Expenses)\n- CAM charges and caps\n- Real estate taxes (base year)\n- Insurance charges\n- Utility responsibilities\n\n### Concessions\n- Tenant improvement allowance\n- Free rent periods\n- Moving allowances\n\n## Calculations Required\n\n1. Total base rent over lease term\n2. Estimated total additional rent\n3. Grand total lease value\n4. Effective rent per square foot\n\n## Response Format\n\nProvide your response as valid JSON matching the financial terms schema.\n\n## Guidelines\n\n- Show all calculations\n- Convert all figures to consistent units (monthly/annual)\n- Note any caps, floors, or adjustments\n- Flag any unusual financial provisions"
}
```

#### ACT-LEASE-005: Term & Termination Analysis

```json
{
  "id": "ACT-LEASE-005",
  "sprk_name": "Lease Term Analysis",
  "sprk_description": "Analyze lease duration, renewal options, and termination provisions",
  "actionType": "05 - Analysis",
  "sprk_systemprompt": "You are a lease administration specialist. Analyze the term and termination provisions of this lease.\n\n## Term Analysis\n\n1. **Primary Term**: Commencement date, expiration date, duration\n2. **Renewal Options**: Number, duration, notice requirements, rent basis\n3. **Early Termination**: Tenant rights, landlord rights, penalties\n4. **Holdover**: Rent multiplier and terms\n\n## Key Dates Calendar\n\nCreate a calendar of all critical dates:\n- Commencement\n- Rent commencement (if different)\n- Option exercise deadlines\n- Termination notice deadlines\n- Expiration\n\n## Response Format\n\nProvide your response as valid JSON matching the term analysis schema.\n\n## Guidelines\n\n- Calculate exact durations in months and years\n- Flag any acceleration clauses\n- Note conditions that could alter the term\n- Identify any tenant-favorable termination rights"
}
```

#### ACT-LEASE-006: Jurisdiction Analysis

```json
{
  "id": "ACT-LEASE-006",
  "sprk_name": "Lease Jurisdiction Analysis",
  "sprk_description": "Analyze jurisdiction-specific terms and compliance requirements",
  "actionType": "05 - Analysis",
  "sprk_systemprompt": "You are a real estate attorney with multi-jurisdictional expertise. Analyze the jurisdiction-specific aspects of this lease.\n\n## Analysis Areas\n\n1. **Property Location**: Full address and jurisdiction\n2. **Governing Law**: State law, venue for disputes\n3. **Local Requirements**: Licenses, disclosures, tenant protections\n4. **Jurisdiction-Specific Terms**: State-mandated provisions\n5. **Variances**: How this lease differs from standard approach due to local law\n\n## Jurisdiction Knowledge\n\nReference the provided knowledge base for jurisdiction-specific requirements. Flag any provisions that may conflict with local law.\n\n## Response Format\n\nProvide your response as valid JSON matching the jurisdiction analysis schema.\n\n## Guidelines\n\n- Identify the specific state and local jurisdiction\n- Note any unusual dispute resolution mechanisms\n- Flag provisions that may be unenforceable locally\n- Recommend jurisdiction-specific modifications"
}
```

#### ACT-LEASE-007: Liability & Indemnification Analysis

```json
{
  "id": "ACT-LEASE-007",
  "sprk_name": "Lease Liability Analysis",
  "sprk_description": "Analyze insurance requirements, indemnification, and liability provisions",
  "actionType": "05 - Analysis",
  "sprk_systemprompt": "You are a risk management specialist. Analyze the liability and indemnification provisions of this lease.\n\n## Analysis Areas\n\n### Insurance Requirements\n- General liability (per occurrence, aggregate)\n- Property insurance\n- Workers compensation\n- Umbrella/excess coverage\n- Additional insured requirements\n- Waiver of subrogation\n\n### Indemnification\n- Tenant indemnification of landlord (scope, exceptions)\n- Landlord indemnification of tenant\n- Mutual vs. one-sided indemnification\n- Caps on liability\n\n### Limitation of Liability\n- Consequential damages waivers\n- Liability caps for each party\n\n### Default and Remedies\n- Events of default\n- Cure periods\n- Available remedies\n\n## Response Format\n\nProvide your response as valid JSON matching the liability analysis schema.\n\n## Guidelines\n\n- Compare insurance requirements to company standards\n- Flag any broad indemnification without carve-outs\n- Identify missing liability protections\n- Recommend risk mitigation strategies"
}
```

#### ACT-LEASE-008: Notable Terms Extraction

```json
{
  "id": "ACT-LEASE-008",
  "sprk_name": "Lease Notable Terms",
  "sprk_description": "Extract other notable provisions not covered by specialized analysis",
  "actionType": "01 - Extraction",
  "sprk_systemprompt": "You are a commercial lease specialist. Extract notable provisions that haven't been covered by other analysis sections.\n\n## Categories to Review\n\n1. **Assignment & Subletting**: Rights, consent standards, profit sharing\n2. **Alterations**: Tenant rights, approval thresholds, removal requirements\n3. **Signage**: Exterior signs, building directory, specifications\n4. **Parking**: Allocated spaces, rates, reserved spaces\n5. **Use Restrictions**: Permitted use, exclusivity, prohibited uses\n6. **Environmental**: Hazmat restrictions, tenant representations\n7. **Co-Tenancy/Kickout**: Retail-specific provisions\n8. **Other**: Any other noteworthy provisions\n\n## Response Format\n\nProvide your response as valid JSON matching the notable terms schema.\n\n## Guidelines\n\n- Focus on provisions with business impact\n- Note any unusual restrictions\n- Identify potential operational issues\n- Flag provisions that differ significantly from market standard"
}
```

#### ACT-SEARCH-001: Similar Document Search

```json
{
  "id": "ACT-SEARCH-001",
  "sprk_name": "Similar Document Search",
  "sprk_description": "Search for similar lease agreements in the document repository",
  "actionType": "04 - Search",
  "sprk_systemprompt": "Based on the analyzed lease characteristics, search for similar documents in the repository.\n\n## Search Criteria\n\nUse semantic search with these dimensions:\n1. Property type (Office, Retail, Industrial, etc.)\n2. Geographic area (same state/region)\n3. Deal size (similar square footage and rent)\n4. Landlord (same or similar landlords)\n5. Lease structure (similar term length)\n\n## Relevance Scoring\n\nRank results by relevance considering:\n- Document type match\n- Property type match\n- Geographic proximity\n- Deal size similarity\n- Recency (prefer recent agreements)\n\n## Response Format\n\nReturn top 10 most similar documents with relevance reasons."
}
```

---

### Skills

Skills provide specialized prompt fragments that enhance the base Action prompts.

#### SKL-LEASE-001: Real Estate Lease Domain

```json
{
  "id": "SKL-LEASE-001",
  "sprk_name": "Real Estate Lease Domain Knowledge",
  "sprk_description": "Domain expertise for commercial real estate lease analysis",
  "skillType": "01 - Document Analysis",
  "sprk_promptfragment": "## Real Estate Lease Domain Context\n\n### Lease Structure\nCommercial leases typically include these sections:\n- Premises description and permitted use\n- Term and renewal options\n- Rent and escalation provisions\n- Operating expenses (CAM, taxes, insurance)\n- Security deposit and guaranty\n- Maintenance and repairs\n- Insurance and indemnification\n- Default and remedies\n- Assignment and subletting\n- Miscellaneous provisions\n\n### Key Metrics\n- Rentable vs. Usable Square Feet (Load Factor)\n- Effective Rent (accounting for concessions)\n- Net Effective Rent per RSF\n- Total Occupancy Cost\n\n### Lease Types\n- **Gross Lease**: Landlord pays operating expenses\n- **Net Lease**: Tenant pays some operating expenses\n- **NNN (Triple Net)**: Tenant pays taxes, insurance, CAM\n- **Modified Gross**: Hybrid with base year stops\n\n### Industry Terminology\n- CAM: Common Area Maintenance\n- TI: Tenant Improvements\n- LOI: Letter of Intent\n- NER: Net Effective Rent\n- RSF: Rentable Square Feet\n- USF: Usable Square Feet"
}
```

#### SKL-LEASE-002: Compliance Scoring

```json
{
  "id": "SKL-LEASE-002",
  "sprk_name": "Compliance Scoring Logic",
  "sprk_description": "Methodology for calculating compliance variance and risk coefficients",
  "skillType": "02 - Compliance",
  "sprk_promptfragment": "## Compliance Scoring Methodology\n\n### Variance Calculation\n\nFor quantitative terms:\n```\nVariance % = |Lease Value - Standard Value| / Standard Value × 100\n```\n\nFor qualitative terms, use this scoring:\n- **Fully Aligned**: 0% variance\n- **Minor Deviation**: 25% variance\n- **Moderate Deviation**: 50% variance\n- **Significant Deviation**: 75% variance\n- **Major Deviation**: 100%+ variance\n\n### Risk Coefficient Assignment\n\n| Variance Range | Coefficient | Color | Action Required |\n|---------------|-------------|-------|------------------|\n| 0-49% | Compliant | ✅ Green | None |\n| 50-74% | Yellow | 🟡 Yellow | Review recommended |\n| 75-100%+ | Red | 🔴 Red | Legal review required |\n\n### Category Weighting\n\nWhen calculating overall risk:\n- Financial terms: 35% weight\n- Legal terms: 30% weight\n- Insurance terms: 20% weight\n- Operational terms: 15% weight\n\n### Aggregation Rules\n\n- Any single Red item = Overall High Risk minimum\n- 3+ Yellow items in same category = Elevated to Red\n- Overall score = Weighted average of category scores"
}
```

#### SKL-LEASE-003: Financial Analysis

```json
{
  "id": "SKL-LEASE-003",
  "sprk_name": "Lease Financial Analysis",
  "sprk_description": "Financial calculation methods for lease analysis",
  "skillType": "03 - Financial",
  "sprk_promptfragment": "## Financial Analysis Methods\n\n### Rent Schedule Calculation\n\nFor fixed escalations:\n```\nYear N Rent = Year 1 Rent × (1 + Escalation Rate)^(N-1)\n```\n\nFor CPI escalations:\n- Use assumed CPI rate if not specified\n- Note cap and floor if applicable\n\n### Effective Rent Calculation\n\n```\nNet Effective Rent = (Total Rent - Concessions) / Lease Term Months\n\nConcessions include:\n- Free rent months × monthly rent\n- TI allowance amortized\n- Moving allowance\n```\n\n### Total Occupancy Cost\n\n```\nTotal Occupancy Cost = Base Rent + Estimated CAM + Taxes + Insurance + Utilities\n```\n\n### Key Ratios\n\n- **Rent-to-Revenue**: Compare to industry benchmarks (typically 5-10%)\n- **Security Deposit Months**: Standard is 1-2 months\n- **TI Allowance per RSF**: Compare to market ($30-80 for office)\n\n### Present Value Analysis\n\nIf comparing lease options, calculate NPV using:\n- Discount rate: Company WACC or 8% default\n- Include all cash flows over full term"
}
```

#### SKL-LEASE-004: Jurisdiction Knowledge

```json
{
  "id": "SKL-LEASE-004",
  "sprk_name": "Jurisdiction-Specific Knowledge",
  "sprk_description": "State and local jurisdiction requirements for commercial leases",
  "skillType": "04 - Legal",
  "sprk_promptfragment": "## Jurisdiction Considerations\n\n### Common State Variations\n\n**California**:\n- Civil Code §1950.5: Security deposit limits (commercial exempt)\n- Prop 65 disclosure requirements\n- Earthquake disclosure requirements\n- Energy benchmarking (SF, LA)\n\n**New York**:\n- NYC Commercial Rent Tax (certain areas)\n- Local Law 97 emissions compliance\n- Loft Law considerations\n- Rent stabilization (residential conversions)\n\n**Texas**:\n- No state income tax (affects guaranty value)\n- Property tax protests common\n- Hurricane/flood disclosure\n\n**Florida**:\n- Hurricane preparedness requirements\n- Environmental Phase I common\n- Radon disclosure\n\n### Universal Considerations\n\n- ADA compliance responsibility\n- Fire code and life safety\n- Environmental contamination liability\n- Mechanics lien exposure\n\n### Dispute Resolution\n\n- Note governing law state\n- Venue requirements\n- Mandatory arbitration clauses\n- Jury trial waivers (enforceability varies)"
}
```

#### SKL-LEASE-005: Liability Analysis

```json
{
  "id": "SKL-LEASE-005",
  "sprk_name": "Liability Analysis Framework",
  "sprk_description": "Framework for analyzing lease liability and insurance provisions",
  "skillType": "05 - Risk",
  "sprk_promptfragment": "## Liability Analysis Framework\n\n### Insurance Adequacy Assessment\n\n**Minimum Acceptable Limits**:\n- General Liability: $1M per occurrence / $2M aggregate\n- Property: Full replacement cost\n- Umbrella: $5M minimum for large spaces\n- Workers Comp: Statutory limits\n\n### Indemnification Analysis\n\n**Red Flags**:\n- Tenant indemnifies for landlord's negligence\n- No cap on indemnification liability\n- Indemnification survives lease termination indefinitely\n- Includes consequential damages\n\n**Best Practice**:\n- Mutual indemnification for own negligence\n- Carve-out for gross negligence/willful misconduct\n- Cap tied to insurance limits or rent\n- Reasonable survival period (1-2 years)\n\n### Default Risk Assessment\n\n**Tenant-Favorable**:\n- Long cure periods (30+ days monetary, 60+ days non-monetary)\n- Multiple notices required\n- Right to cure even after termination notice\n\n**Landlord-Favorable**:\n- Short cure periods\n- Cross-default with other leases\n- Acceleration of rent\n- Personal guaranty enforcement"
}
```

---

### Knowledge Sources

Knowledge sources provide domain-specific context to the LLM.

#### KNL-LEASE-001: Company Standard Lease Terms

```json
{
  "id": "KNL-LEASE-001",
  "sprk_name": "Company Standard Lease Terms",
  "knowledgeType": "Reference Library",
  "sprk_description": "Company's standard acceptable terms for commercial real estate leases",
  "sprk_content": "## Company Standard Lease Terms\n\n### Financial Standards\n\n| Term | Standard | Acceptable Range | Hard Limit |\n|------|----------|------------------|------------|\n| Security Deposit | 2 months rent | 1-3 months | 4 months max |\n| Annual Escalation | CPI capped at 2.5% | 2-3% fixed | 4% max |\n| TI Allowance | $50/RSF | $40-60/RSF | Negotiate if <$30 |\n| Free Rent | 1 month/year of term | Pro-rata | - |\n| CAM Cap | 5% annual increase | 3-6% | 8% max |\n\n### Legal Standards\n\n| Term | Standard | Requirement |\n|------|----------|-------------|\n| Governing Law | Property state | Required |\n| Indemnification | Mutual | Required |\n| Liability Cap | 12 months rent | Negotiate if unlimited |\n| Personal Guaranty | None | Avoid if possible |\n| Assignment | Consent not unreasonably withheld | Required |\n| Affiliate Transfer | Permitted without consent | Required |\n\n### Insurance Standards\n\n| Coverage | Minimum |\n|----------|----------|\n| General Liability | $1M/$2M |\n| Property | Replacement cost |\n| Umbrella | $5M |\n| Additional Insured | Landlord required |\n| Waiver of Subrogation | Mutual |\n\n### Operational Standards\n\n| Term | Standard |\n|------|----------|\n| Renewal Options | 2 x 5-year minimum |\n| Early Termination | After Year 5 with penalty |\n| Holdover Rate | 125% maximum |\n| After-Hours HVAC | Available at cost |\n| Parking Ratio | 3/1000 RSF minimum |"
}
```

#### KNL-LEASE-002: Jurisdiction Requirements

```json
{
  "id": "KNL-LEASE-002",
  "sprk_name": "Jurisdiction-Specific Requirements",
  "knowledgeType": "Reference Library",
  "sprk_description": "State and local requirements for commercial leases by jurisdiction",
  "sprk_content": "## Jurisdiction Requirements Database\n\n### California\n- Prop 65 warning signage required\n- Seismic disclosure for pre-1978 buildings\n- Energy benchmarking (AB 802) for buildings >50,000 SF\n- Local business license required\n- ADA accessibility compliance critical\n\n### New York\n- NYC Commercial Rent Tax if south of 96th St Manhattan\n- Local Law 97 carbon emissions compliance\n- Certificate of Occupancy verification required\n- Sprinkler system disclosure required\n- Commercial Tenant Harassment Protection Law\n\n### Texas\n- No state income tax (affects guarantor analysis)\n- Property tax protests customary (budget variance)\n- Flood zone disclosure required\n- Wind/hail insurance allocation important\n- Mechanic's lien fund requirements\n\n### Illinois\n- Chicago Tenant Bill of Rights (commercial limited)\n- Energy benchmarking ordinance (Chicago)\n- Prevailing wage for TI work over $50K\n- Radon disclosure for certain buildings\n\n### Florida\n- Hurricane preparedness/insurance requirements\n- Flood zone disclosure required\n- Construction lien law strict\n- Documentary stamp tax on lease\n- Sinkhole disclosure (certain counties)"
}
```

#### KNL-LEASE-003: Document Index (RAG)

```json
{
  "id": "KNL-LEASE-003",
  "sprk_name": "Lease Document Index",
  "knowledgeType": "RAG Index",
  "sprk_description": "Azure AI Search index of historical lease agreements for similarity matching",
  "sprk_contenturl": "https://spaarke-search-dev.search.windows.net/indexes/lease-documents",
  "sprk_configuration": {
    "indexName": "lease-documents",
    "semanticConfiguration": "lease-semantic-config",
    "vectorField": "contentVector",
    "filterableFields": ["documentType", "propertyType", "state", "landlordName", "executionYear"],
    "facetFields": ["propertyType", "state", "leaseTermYears"]
  }
}
```

---

### Tools

Tools are the executable handlers that process prompts and return structured results.

#### TL-SUMMARY-001: Summary Handler

```json
{
  "id": "TL-SUMMARY-001",
  "sprk_name": "Lease Summary Generator",
  "toolType": "Summary",
  "sprk_handlerclass": "LeaseSummaryHandler",
  "sprk_configuration": {
    "maxTokens": 1000,
    "temperature": 0.3,
    "responseFormat": "json_object"
  }
}
```

#### TL-COMPLIANCE-001: Compliance Analyzer

```json
{
  "id": "TL-COMPLIANCE-001",
  "sprk_name": "Compliance Analyzer",
  "toolType": "Analysis",
  "sprk_handlerclass": "ComplianceAnalyzerHandler",
  "sprk_configuration": {
    "maxTokens": 4000,
    "temperature": 0.2,
    "responseFormat": "json_object",
    "requiresKnowledge": true
  }
}
```

#### TL-ENTITY-001: Entity Extractor

```json
{
  "id": "TL-ENTITY-001",
  "sprk_name": "Party Extractor",
  "toolType": "Extraction",
  "sprk_handlerclass": "EntityExtractorHandler",
  "sprk_configuration": {
    "maxTokens": 2000,
    "temperature": 0.1,
    "responseFormat": "json_object",
    "entityTypes": ["Organization", "Person", "Address", "Date"]
  }
}
```

#### TL-FINANCIAL-001: Financial Analyzer

```json
{
  "id": "TL-FINANCIAL-001",
  "sprk_name": "Financial Terms Extractor",
  "toolType": "Extraction",
  "sprk_handlerclass": "FinancialAnalyzerHandler",
  "sprk_configuration": {
    "maxTokens": 3000,
    "temperature": 0.1,
    "responseFormat": "json_object",
    "calculateTotals": true
  }
}
```

#### TL-TERM-001: Term Analyzer

```json
{
  "id": "TL-TERM-001",
  "sprk_name": "Term & Termination Analyzer",
  "toolType": "Analysis",
  "sprk_handlerclass": "TermAnalyzerHandler",
  "sprk_configuration": {
    "maxTokens": 2000,
    "temperature": 0.2,
    "responseFormat": "json_object",
    "generateCalendar": true
  }
}
```

#### TL-JURISDICTION-001: Jurisdiction Analyzer

```json
{
  "id": "TL-JURISDICTION-001",
  "sprk_name": "Jurisdiction Analyzer",
  "toolType": "Analysis",
  "sprk_handlerclass": "JurisdictionAnalyzerHandler",
  "sprk_configuration": {
    "maxTokens": 2500,
    "temperature": 0.2,
    "responseFormat": "json_object",
    "requiresKnowledge": true
  }
}
```

#### TL-LIABILITY-001: Liability Analyzer

```json
{
  "id": "TL-LIABILITY-001",
  "sprk_name": "Liability & Insurance Analyzer",
  "toolType": "Analysis",
  "sprk_handlerclass": "LiabilityAnalyzerHandler",
  "sprk_configuration": {
    "maxTokens": 3000,
    "temperature": 0.2,
    "responseFormat": "json_object",
    "requiresKnowledge": true
  }
}
```

#### TL-NOTABLE-001: Notable Terms Extractor

```json
{
  "id": "TL-NOTABLE-001",
  "sprk_name": "Notable Terms Extractor",
  "toolType": "Extraction",
  "sprk_handlerclass": "NotableTermsHandler",
  "sprk_configuration": {
    "maxTokens": 2500,
    "temperature": 0.2,
    "responseFormat": "json_object"
  }
}
```

#### TL-RAG-SEARCH-001: Similar Document Finder

```json
{
  "id": "TL-RAG-SEARCH-001",
  "sprk_name": "Similar Document Finder",
  "toolType": "Search",
  "sprk_handlerclass": "RagSearchHandler",
  "sprk_configuration": {
    "searchService": "spaarke-search-dev",
    "indexName": "lease-documents",
    "topK": 10,
    "minScore": 0.75,
    "useSemanticSearch": true,
    "useVectorSearch": true,
    "hybridSearch": true
  }
}
```

#### TL-DOCUMENT-GEN-001: Document Generator

```json
{
  "id": "TL-DOCUMENT-GEN-001",
  "sprk_name": "Document Generator",
  "toolType": "Output",
  "sprk_handlerclass": "DocumentGeneratorHandler",
  "sprk_configuration": {
    "wordTemplateContainer": "templates",
    "outputContainer": "generated-reports",
    "supportedFormats": ["docx", "pdf"],
    "emailEnabled": true
  }
}
```

---

## Node Flow and Connections

### Edge Definitions

```json
{
  "edges": [
    {
      "id": "edge_start_tldr",
      "source": "node_start_001",
      "target": "node_tldr_002",
      "type": "smoothstep",
      "animated": true,
      "label": "Document Text"
    },
    {
      "id": "edge_tldr_compliance",
      "source": "node_tldr_002",
      "target": "node_compliance_003",
      "type": "smoothstep",
      "animated": true
    },
    {
      "id": "edge_tldr_parties",
      "source": "node_tldr_002",
      "target": "node_parties_004",
      "type": "smoothstep",
      "animated": true
    },
    {
      "id": "edge_tldr_financial",
      "source": "node_tldr_002",
      "target": "node_financial_005",
      "type": "smoothstep",
      "animated": true
    },
    {
      "id": "edge_compliance_term",
      "source": "node_compliance_003",
      "target": "node_term_006",
      "type": "smoothstep",
      "animated": true
    },
    {
      "id": "edge_term_jurisdiction",
      "source": "node_term_006",
      "target": "node_jurisdiction_007",
      "type": "smoothstep",
      "animated": true
    },
    {
      "id": "edge_jurisdiction_liability",
      "source": "node_jurisdiction_007",
      "target": "node_liability_008",
      "type": "smoothstep",
      "animated": true
    },
    {
      "id": "edge_liability_notable",
      "source": "node_liability_008",
      "target": "node_notable_009",
      "type": "smoothstep",
      "animated": true
    },
    {
      "id": "edge_parties_assemble",
      "source": "node_parties_004",
      "target": "node_similar_010",
      "type": "smoothstep",
      "animated": true,
      "style": { "strokeDasharray": "5,5" }
    },
    {
      "id": "edge_financial_assemble",
      "source": "node_financial_005",
      "target": "node_similar_010",
      "type": "smoothstep",
      "animated": true,
      "style": { "strokeDasharray": "5,5" }
    },
    {
      "id": "edge_notable_similar",
      "source": "node_notable_009",
      "target": "node_similar_010",
      "type": "smoothstep",
      "animated": true
    },
    {
      "id": "edge_similar_assemble",
      "source": "node_similar_010",
      "target": "node_assemble_011",
      "type": "smoothstep",
      "animated": true,
      "label": "All Results"
    },
    {
      "id": "edge_assemble_deliver",
      "source": "node_assemble_011",
      "target": "node_deliver_012",
      "type": "smoothstep",
      "animated": true,
      "label": "Compiled Report"
    }
  ]
}
```

### Execution Order and Dependencies

```
Execution Phase 1 (Sequential - Required First)
└── Node 1: Start (Document Text Extraction)
    └── Output: documentText, documentMetadata

Execution Phase 2 (Sequential - Context Setting)
└── Node 2: TL;DR Summary
    ├── Input: documentText
    └── Output: tldrSummary

Execution Phase 3 (Parallel - Independent Analysis)
├── Node 3: Compliance Analysis
│   ├── Input: documentText, KNL-LEASE-001
│   └── Output: complianceAnalysis
├── Node 4: Party Extraction
│   ├── Input: documentText
│   └── Output: partiesExtraction
└── Node 5: Financial Terms
    ├── Input: documentText, KNL-LEASE-001
    └── Output: financialTerms

Execution Phase 4 (Sequential Chain - Dependent Analysis)
├── Node 6: Term & Termination
│   ├── Input: documentText
│   └── Output: termAnalysis
├── Node 7: Jurisdiction Analysis
│   ├── Input: documentText, termAnalysis.propertyLocation, KNL-LEASE-002
│   └── Output: jurisdictionAnalysis
├── Node 8: Liability Analysis
│   ├── Input: documentText, jurisdictionAnalysis, KNL-LEASE-001
│   └── Output: liabilityAnalysis
└── Node 9: Notable Terms
    ├── Input: documentText
    └── Output: notableTerms

Execution Phase 5 (Sequential - Search)
└── Node 10: Similar Document Search
    ├── Input: tldrSummary, partiesExtraction, financialTerms, KNL-LEASE-003
    └── Output: similarDocuments

Execution Phase 6 (Sequential - Assembly & Delivery)
├── Node 11: Assemble Output
│   ├── Input: All previous outputs
│   └── Output: compiledReport
└── Node 12: Deliver Output
    ├── Input: compiledReport
    └── Output: deliveryResult (Word, PDF, Email)
```

---

## Output Assembly

### Report Template Structure

The final Agreement Summary document follows this structure:

```markdown
# Lease Agreement Analysis Report

**Document**: {{documentName}}
**Analysis Date**: {{generatedAt}}
**Overall Risk Level**: {{overallRiskLevel}} {{riskIndicator}}

---

## 1. TL;DR Summary

{{tldrSummary.summary}}

**Lease Type**: {{tldrSummary.leaseType}}
**Key Highlights**:
{{#each tldrSummary.keyHighlights}}
- {{this}}
{{/each}}

---

## 2. Compliance Analysis

### Summary
- **Terms Analyzed**: {{complianceAnalysis.totalTermsAnalyzed}}
- **Compliant**: {{complianceAnalysis.compliantCount}} ✅
- **Non-Compliant**: {{complianceAnalysis.nonCompliantCount}} (🟡 {{yellowCount}} / 🔴 {{redCount}})

### Out-of-Compliance Provisions

{{#each complianceAnalysis.provisions where riskCoefficient != 'Compliant'}}
#### {{provisionName}} {{riskIndicator}}

| Aspect | Details |
|--------|---------|
| **Category** | {{category}} |
| **Lease Value** | {{leaseValue}} |
| **Company Standard** | {{standardValue}} |
| **Variance** | {{variancePercentage}}% |
| **Risk Level** | {{riskLevel}} |
| **Reference** | {{clauseReference}} |

**Recommendation**: {{recommendation}}

{{/each}}

### Compliance by Category

| Category | ✅ Compliant | 🟡 Yellow | 🔴 Red |
|----------|-------------|-----------|--------|
| Financial | {{summaryByCategory.Financial.compliant}} | {{...yellow}} | {{...red}} |
| Legal | {{summaryByCategory.Legal.compliant}} | {{...yellow}} | {{...red}} |
| Operational | {{summaryByCategory.Operational.compliant}} | {{...yellow}} | {{...red}} |
| Insurance | {{summaryByCategory.Insurance.compliant}} | {{...yellow}} | {{...red}} |

---

## 3. Parties

### Landlord
- **Name**: {{partiesExtraction.landlord.name}}
- **Type**: {{partiesExtraction.landlord.type}}
- **Address**: {{partiesExtraction.landlord.address}}

### Tenant
- **Name**: {{partiesExtraction.tenant.name}}
- **Type**: {{partiesExtraction.tenant.type}}
- **Address**: {{partiesExtraction.tenant.address}}

{{#if partiesExtraction.guarantors.length}}
### Guarantors
{{#each partiesExtraction.guarantors}}
- **{{name}}** ({{type}}): {{guarantyType}} guaranty {{#if guarantyLimit}}- Limited to {{guarantyLimit}}{{/if}}
{{/each}}
{{/if}}

---

## 4. Financial Terms Summary

### Base Rent

| Year | Monthly Rent | Annual Rent | $/RSF |
|------|-------------|-------------|-------|
{{#each financialTerms.baseRent.escalationSchedule}}
| {{year}} | ${{formatNumber monthlyRent}} | ${{formatNumber annualRent}} | ${{perSqFt}} |
{{/each}}

**Escalation**: {{financialTerms.baseRent.escalationType}} - {{financialTerms.baseRent.escalationRate}}

### Security Deposit
- **Amount**: ${{formatNumber financialTerms.securityDeposit.amount}}
- **Form**: {{financialTerms.securityDeposit.form}}

### Additional Rent (Estimated Annual)
- **CAM**: ${{formatNumber financialTerms.additionalRent.cam.estimated}}
- **Taxes**: ${{formatNumber financialTerms.additionalRent.taxes.estimatedAmount}}
- **Insurance**: ${{formatNumber financialTerms.additionalRent.insurance.estimatedAmount}}

### Concessions
- **TI Allowance**: ${{formatNumber financialTerms.tenantImprovements.allowanceAmount}} (${{financialTerms.tenantImprovements.perSqFt}}/RSF)
- **Free Rent**: {{financialTerms.freeRent.months}} months

### Total Lease Value
- **Base Rent (Full Term)**: ${{formatNumber financialTerms.totalLeaseValue.baseRentTotal}}
- **Estimated Additional Rent**: ${{formatNumber financialTerms.totalLeaseValue.estimatedAdditionalRent}}
- **Grand Total**: ${{formatNumber financialTerms.totalLeaseValue.grandTotal}}

---

## 5. Term & Termination

### Primary Term
- **Commencement**: {{formatDate termAnalysis.leaseTerm.commencementDate}}
- **Expiration**: {{formatDate termAnalysis.leaseTerm.expirationDate}}
- **Duration**: {{termAnalysis.leaseTerm.durationYears}} years ({{termAnalysis.leaseTerm.durationMonths}} months)

### Renewal Options
{{#each termAnalysis.renewalOptions}}
- **Option {{optionNumber}}**: {{durationMonths}} months at {{rentBasis}} ({{noticeRequiredDays}} days notice)
{{/each}}

### Early Termination
- **Tenant Rights**: {{#if termAnalysis.earlyTermination.tenantRights.allowed}}Yes - {{termAnalysis.earlyTermination.tenantRights.conditions}}{{else}}None{{/if}}
- **Landlord Rights**: {{#if termAnalysis.earlyTermination.landlordRights.allowed}}Yes - {{termAnalysis.earlyTermination.landlordRights.conditions}}{{else}}Standard default remedies only{{/if}}

### Key Dates Calendar
{{#each termAnalysis.keyDates}}
| {{formatDate date}} | {{event}} | {{action}} |
{{/each}}

---

## 6. Jurisdiction-Specific Terms

### Property Location
{{jurisdictionAnalysis.propertyLocation.address}}
{{jurisdictionAnalysis.propertyLocation.city}}, {{jurisdictionAnalysis.propertyLocation.state}} {{jurisdictionAnalysis.propertyLocation.zipCode}}

### Governing Law
- **State**: {{jurisdictionAnalysis.governingLaw.state}}
- **Venue**: {{jurisdictionAnalysis.governingLaw.venue}}
- **Arbitration Required**: {{jurisdictionAnalysis.governingLaw.arbitrationRequired}}

### Jurisdiction-Specific Requirements
{{#each jurisdictionAnalysis.jurisdictionSpecificTerms}}
| {{term}} | {{requirement}} | {{leaseCompliance}} |
{{/each}}

### Variances from Standard Approach
{{#each jurisdictionAnalysis.variancesFromStandard}}
- **{{area}}**: {{jurisdictionRequirement}} (Standard: {{standardApproach}})
  - *Recommendation*: {{recommendation}}
{{/each}}

---

## 7. Liability & Indemnification

### Insurance Requirements

| Coverage | Required | Minimum |
|----------|----------|---------|
| General Liability | {{liabilityAnalysis.insuranceRequirements.generalLiability.required}} | {{...minimumCoverage}} |
| Property | {{liabilityAnalysis.insuranceRequirements.propertyInsurance.required}} | {{...coverage}} |
| Workers Comp | {{liabilityAnalysis.insuranceRequirements.workersComp.required}} | Statutory |
| Umbrella | {{liabilityAnalysis.insuranceRequirements.umbrellaExcess.required}} | {{...minimum}} |

### Indemnification Structure
- **Scope**: {{liabilityAnalysis.indemnification.tenantIndemnifiesLandlord.scope}}
- **Mutual**: {{liabilityAnalysis.indemnification.mutualIndemnification}}
- **Cap**: {{liabilityAnalysis.indemnification.tenantIndemnifiesLandlord.capOnLiability}}

### Compliance Issues
{{#each liabilityAnalysis.complianceIssues}}
- **{{issue}}** ({{riskLevel}}): {{recommendation}}
{{/each}}

---

## 8. Other Notable Terms

### Assignment & Subletting
- **Assignment**: {{#if notableTerms.assignmentAndSubletting.assignmentAllowed}}Permitted{{else}}Prohibited{{/if}} ({{notableTerms.assignmentAndSubletting.consentStandard}})
- **Subletting**: {{#if notableTerms.assignmentAndSubletting.subletAllowed}}Permitted{{else}}Prohibited{{/if}}
- **Affiliate Transfers**: {{#if notableTerms.assignmentAndSubletting.affiliateTransferExempt}}Exempt from consent{{else}}Consent required{{/if}}
{{#if notableTerms.assignmentAndSubletting.profitSharingRequired}}
- **Profit Sharing**: {{notableTerms.assignmentAndSubletting.profitSharingPercentage}}% to Landlord
{{/if}}

### Parking
- **Allocated Spaces**: {{notableTerms.parkingAndAccess.spacesAllocated}}
- **Reserved**: {{notableTerms.parkingAndAccess.reservedSpaces}}
- **Rate**: {{notableTerms.parkingAndAccess.monthlyRate}}

### Use & Exclusivity
- **Permitted Use**: {{notableTerms.exclusivityAndUseRestrictions.permittedUse}}
{{#if notableTerms.exclusivityAndUseRestrictions.exclusiveUseGranted}}
- **Exclusive Use**: {{notableTerms.exclusivityAndUseRestrictions.exclusiveUseDetails}}
{{/if}}

### Other Noteworthy Items
{{#each notableTerms.otherNoteworthyItems}}
- **{{topic}}** ({{significance}}): {{summary}}
{{/each}}

---

## 9. Similar Documents in System

{{#if similarDocuments.totalFound}}
Found **{{similarDocuments.totalFound}}** similar lease agreements:

| Document | Similarity | Landlord | Property | Term |
|----------|------------|----------|----------|------|
{{#each similarDocuments.documents limit=10}}
| [{{documentName}}]({{documentId}}) | {{formatPercent similarityScore}} | {{parties.landlord}} | {{propertyAddress}} | {{leaseTermYears}} yrs |
{{/each}}

{{else}}
No similar documents found in the system.
{{/if}}

---

## Report Metadata

| Metric | Value |
|--------|-------|
| **Report ID** | {{compiledReport.metadata.reportId}} |
| **Generated** | {{formatDateTime compiledReport.metadata.generatedAt}} |
| **Source Document** | {{compiledReport.metadata.documentName}} |
| **Playbook** | {{compiledReport.metadata.playbookId}} v{{compiledReport.metadata.playbookVersion}} |
| **Total Duration** | {{compiledReport.executionMetrics.totalDurationMs}}ms |
| **Tokens Used** | {{compiledReport.executionMetrics.totalTokensUsed}} |
| **Estimated Cost** | ${{formatNumber compiledReport.executionMetrics.estimatedCost 4}} |

---

*This report was generated automatically by the Spaarke AI Analysis Platform. The analysis is provided for informational purposes and should be reviewed by qualified legal counsel before making business decisions.*
```

---

## Delivery Configuration

### Word Template (TPL-WORD-LEASE-001)

The Word template uses content controls mapped to the compiled report JSON structure:

```xml
<!-- Word Template Content Controls -->
<w:sdtContent>
  <w:tag w:val="tldrSummary.summary"/>
  <w:tag w:val="complianceAnalysis.provisions"/>
  <!-- ... additional mappings ... -->
</w:sdtContent>
```

### PDF Generation

PDF is generated from the Word document using the document conversion service:

```csharp
// DocumentGeneratorHandler.cs
var pdfBytes = await _conversionService.ConvertToPdfAsync(wordDocument);
```

### Email Template (TPL-EMAIL-LEASE-001)

```html
<!DOCTYPE html>
<html>
<head>
  <style>
    .risk-red { color: #d13438; font-weight: bold; }
    .risk-yellow { color: #ffaa00; font-weight: bold; }
    .risk-green { color: #107c10; }
  </style>
</head>
<body>
  <h1>Lease Analysis Complete: {{documentName}}</h1>

  <div class="summary">
    <h2>Quick Summary</h2>
    <p>{{tldrSummary.summary}}</p>
    <p><strong>Overall Risk:</strong>
      <span class="risk-{{toLowerCase overallRiskLevel}}">{{overallRiskLevel}}</span>
    </p>
  </div>

  <div class="compliance-summary">
    <h2>Compliance Overview</h2>
    <ul>
      <li>✅ Compliant: {{complianceAnalysis.compliantCount}} terms</li>
      <li>🟡 Review Required: {{yellowCount}} terms</li>
      <li>🔴 Critical Issues: {{redCount}} terms</li>
    </ul>
  </div>

  <div class="attachments">
    <h2>Attached Reports</h2>
    <p>Please find the full analysis report attached in Word and PDF format.</p>
  </div>

  <div class="footer">
    <p><em>Generated by Spaarke AI Analysis Platform</em></p>
    <p>Report ID: {{reportId}} | Generated: {{generatedAt}}</p>
  </div>
</body>
</html>
```

---

## Canvas JSON Configuration

### Complete Playbook Canvas JSON

```json
{
  "version": 1,
  "nodes": [
    {
      "id": "node_start_001",
      "type": "start",
      "position": { "x": 400, "y": 50 },
      "data": {
        "label": "Extract Document Text",
        "type": "start",
        "outputVariable": "documentText"
      }
    },
    {
      "id": "node_tldr_002",
      "type": "aiAnalysis",
      "position": { "x": 400, "y": 150 },
      "data": {
        "label": "Generate TL;DR Summary",
        "type": "aiAnalysis",
        "outputVariable": "tldrSummary",
        "actionId": "ACT-LEASE-001",
        "skillIds": ["SKL-LEASE-001"],
        "knowledgeIds": [],
        "toolId": "TL-SUMMARY-001",
        "modelDeploymentId": "gpt-4o",
        "timeoutSeconds": 120,
        "retryCount": 2
      }
    },
    {
      "id": "node_compliance_003",
      "type": "aiAnalysis",
      "position": { "x": 100, "y": 300 },
      "data": {
        "label": "Analyze Compliance vs. Standards",
        "type": "aiAnalysis",
        "outputVariable": "complianceAnalysis",
        "actionId": "ACT-LEASE-002",
        "skillIds": ["SKL-LEASE-001", "SKL-LEASE-002"],
        "knowledgeIds": ["KNL-LEASE-001"],
        "toolId": "TL-COMPLIANCE-001",
        "modelDeploymentId": "gpt-4o",
        "timeoutSeconds": 300,
        "retryCount": 2
      }
    },
    {
      "id": "node_parties_004",
      "type": "aiAnalysis",
      "position": { "x": 400, "y": 300 },
      "data": {
        "label": "Extract Parties",
        "type": "aiAnalysis",
        "outputVariable": "partiesExtraction",
        "actionId": "ACT-LEASE-003",
        "skillIds": ["SKL-LEASE-001"],
        "knowledgeIds": [],
        "toolId": "TL-ENTITY-001",
        "modelDeploymentId": "gpt-4o-mini",
        "timeoutSeconds": 90,
        "retryCount": 2
      }
    },
    {
      "id": "node_financial_005",
      "type": "aiAnalysis",
      "position": { "x": 700, "y": 300 },
      "data": {
        "label": "Extract Financial Terms",
        "type": "aiAnalysis",
        "outputVariable": "financialTerms",
        "actionId": "ACT-LEASE-004",
        "skillIds": ["SKL-LEASE-001", "SKL-LEASE-003"],
        "knowledgeIds": ["KNL-LEASE-001"],
        "toolId": "TL-FINANCIAL-001",
        "modelDeploymentId": "gpt-4o",
        "timeoutSeconds": 180,
        "retryCount": 2
      }
    },
    {
      "id": "node_term_006",
      "type": "aiAnalysis",
      "position": { "x": 100, "y": 500 },
      "data": {
        "label": "Analyze Term & Termination",
        "type": "aiAnalysis",
        "outputVariable": "termAnalysis",
        "actionId": "ACT-LEASE-005",
        "skillIds": ["SKL-LEASE-001"],
        "knowledgeIds": [],
        "toolId": "TL-TERM-001",
        "modelDeploymentId": "gpt-4o-mini",
        "timeoutSeconds": 120,
        "retryCount": 2
      }
    },
    {
      "id": "node_jurisdiction_007",
      "type": "aiAnalysis",
      "position": { "x": 100, "y": 650 },
      "data": {
        "label": "Analyze Jurisdiction Terms",
        "type": "aiAnalysis",
        "outputVariable": "jurisdictionAnalysis",
        "actionId": "ACT-LEASE-006",
        "skillIds": ["SKL-LEASE-001", "SKL-LEASE-004"],
        "knowledgeIds": ["KNL-LEASE-002"],
        "toolId": "TL-JURISDICTION-001",
        "modelDeploymentId": "gpt-4o",
        "timeoutSeconds": 180,
        "retryCount": 2
      }
    },
    {
      "id": "node_liability_008",
      "type": "aiAnalysis",
      "position": { "x": 100, "y": 800 },
      "data": {
        "label": "Analyze Liability & Indemnification",
        "type": "aiAnalysis",
        "outputVariable": "liabilityAnalysis",
        "actionId": "ACT-LEASE-007",
        "skillIds": ["SKL-LEASE-001", "SKL-LEASE-005"],
        "knowledgeIds": ["KNL-LEASE-001"],
        "toolId": "TL-LIABILITY-001",
        "modelDeploymentId": "gpt-4o",
        "timeoutSeconds": 180,
        "retryCount": 2
      }
    },
    {
      "id": "node_notable_009",
      "type": "aiAnalysis",
      "position": { "x": 100, "y": 950 },
      "data": {
        "label": "Extract Other Notable Terms",
        "type": "aiAnalysis",
        "outputVariable": "notableTerms",
        "actionId": "ACT-LEASE-008",
        "skillIds": ["SKL-LEASE-001"],
        "knowledgeIds": [],
        "toolId": "TL-NOTABLE-001",
        "modelDeploymentId": "gpt-4o-mini",
        "timeoutSeconds": 120,
        "retryCount": 2
      }
    },
    {
      "id": "node_similar_010",
      "type": "aiCompletion",
      "position": { "x": 400, "y": 1100 },
      "data": {
        "label": "Find Similar Documents",
        "type": "aiCompletion",
        "outputVariable": "similarDocuments",
        "actionId": "ACT-SEARCH-001",
        "skillIds": [],
        "knowledgeIds": ["KNL-LEASE-003"],
        "toolId": "TL-RAG-SEARCH-001",
        "modelDeploymentId": "text-embedding-ada-002",
        "timeoutSeconds": 60,
        "retryCount": 1
      }
    },
    {
      "id": "node_assemble_011",
      "type": "assembleOutput",
      "position": { "x": 400, "y": 1250 },
      "data": {
        "label": "Compile Agreement Summary",
        "type": "assembleOutput",
        "outputVariable": "compiledReport",
        "inputVariables": [
          "tldrSummary",
          "complianceAnalysis",
          "partiesExtraction",
          "financialTerms",
          "termAnalysis",
          "jurisdictionAnalysis",
          "liabilityAnalysis",
          "notableTerms",
          "similarDocuments"
        ],
        "templateId": "TPL-LEASE-SUMMARY-001",
        "timeoutSeconds": 30
      }
    },
    {
      "id": "node_deliver_012",
      "type": "deliverOutput",
      "position": { "x": 400, "y": 1400 },
      "data": {
        "label": "Generate & Deliver Report",
        "type": "deliverOutput",
        "inputVariable": "compiledReport",
        "outputVariable": "deliveryResult",
        "deliveryOptions": {
          "word": { "enabled": true, "templateId": "TPL-WORD-LEASE-001" },
          "pdf": { "enabled": true, "generateFromWord": true },
          "email": { "enabled": true, "templateId": "TPL-EMAIL-LEASE-001" }
        },
        "timeoutSeconds": 120
      }
    }
  ],
  "edges": [
    { "id": "e1", "source": "node_start_001", "target": "node_tldr_002", "type": "smoothstep", "animated": true },
    { "id": "e2", "source": "node_tldr_002", "target": "node_compliance_003", "type": "smoothstep", "animated": true },
    { "id": "e3", "source": "node_tldr_002", "target": "node_parties_004", "type": "smoothstep", "animated": true },
    { "id": "e4", "source": "node_tldr_002", "target": "node_financial_005", "type": "smoothstep", "animated": true },
    { "id": "e5", "source": "node_compliance_003", "target": "node_term_006", "type": "smoothstep", "animated": true },
    { "id": "e6", "source": "node_term_006", "target": "node_jurisdiction_007", "type": "smoothstep", "animated": true },
    { "id": "e7", "source": "node_jurisdiction_007", "target": "node_liability_008", "type": "smoothstep", "animated": true },
    { "id": "e8", "source": "node_liability_008", "target": "node_notable_009", "type": "smoothstep", "animated": true },
    { "id": "e9", "source": "node_parties_004", "target": "node_similar_010", "type": "smoothstep", "animated": true, "style": { "strokeDasharray": "5,5" } },
    { "id": "e10", "source": "node_financial_005", "target": "node_similar_010", "type": "smoothstep", "animated": true, "style": { "strokeDasharray": "5,5" } },
    { "id": "e11", "source": "node_notable_009", "target": "node_similar_010", "type": "smoothstep", "animated": true },
    { "id": "e12", "source": "node_similar_010", "target": "node_assemble_011", "type": "smoothstep", "animated": true },
    { "id": "e13", "source": "node_assemble_011", "target": "node_deliver_012", "type": "smoothstep", "animated": true }
  ]
}
```

---

## Testing and Validation

### Test Cases

| Test ID | Scenario | Expected Result |
|---------|----------|-----------------|
| TC-001 | Standard office lease (10 pages) | All 9 sections populated, <60s total |
| TC-002 | Complex retail lease with co-tenancy | Notable terms includes co-tenancy details |
| TC-003 | Lease with non-standard jurisdiction (CA) | Jurisdiction section highlights CA requirements |
| TC-004 | Lease with multiple guarantors | Parties section lists all guarantors |
| TC-005 | Lease with high-risk provisions | Compliance shows Red flags, email sent |
| TC-006 | Similar document exists in system | Section 9 returns relevant matches |
| TC-007 | PDF file (scanned) | Document Intelligence extracts text successfully |

### Validation Checklist

Before deploying this playbook:

- [ ] All Actions created in `sprk_analysisactions`
- [ ] All Skills created in `sprk_analysisskills`
- [ ] All Knowledge sources created in `sprk_analysisknowledge`
- [ ] Tool handlers implemented and registered
- [ ] Word template uploaded to SPE templates container
- [ ] Email template configured in notification system
- [ ] Azure AI Search index populated with historical leases
- [ ] Playbook record created with canvas JSON
- [ ] Form button/ribbon configured to invoke playbook
- [ ] End-to-end test with sample lease document

---

## Related Documentation

- [AI Playbook Architecture](../architecture/AI-PLAYBOOK-ARCHITECTURE.md)
- [AI Tool Framework Guide](./SPAARKE-AI-ARCHITECTURE.md)
- [Document Intelligence Integration](./DOCUMENT-INTELLIGENCE-INTEGRATION.md)
- [ADR-013: AI Architecture](../.claude/adr/ADR-013-ai-architecture.md)

---

**Last Updated**: January 15, 2026
**Author**: AI Architecture Team
**Review Status**: Draft - Pending Implementation
