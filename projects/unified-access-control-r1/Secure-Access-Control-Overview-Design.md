# Secure Matter Security Architecture

## Purpose

This document defines the recommended architecture and security model for secure Matters and related resources within the Spaarke platform.

The design supports:
- Secure internal Matter access
- External collaboration via Power Pages SPA
- Secure SharePoint Embedded (SPE) document access
- AI and semantic search security trimming
- Centralized authorization orchestration through Spaarke UAC concepts

This document is intended as architectural guidance for implementation and refinement by Claude Code within the existing Spaarke codebase and ADR framework.

---

# Core Architectural Principle

A secure Matter represents an isolated security boundary.

That security boundary applies consistently across:
- Dataverse records
- SharePoint Embedded containers/files
- Power Pages external access
- BFF/API authorization
- AI indexing and semantic search
- Collaboration surfaces

The architecture must ensure:
- secure records are never accessible through standard Business Unit access
- secure documents are isolated from standard document repositories
- AI search results are security-trimmed
- external access is explicitly granted and revocable

---

# High-Level Security Model

## Dataverse

Dataverse remains the authoritative record system and primary row-level security boundary for Matter metadata and related records.

Secure Matters:
- must use User/Team-owned tables
- must NOT rely on Business Unit-level visibility
- must be isolated via Owner Teams and explicit grants

---

## Unified Access Control (UAC)

Spaarke UAC becomes the authoritative authorization orchestration layer.

The UAC layer governs:
- internal users
- security group teams
- external contacts
- document/file access
- AI access scopes

The UAC model synchronizes:
- Dataverse row access
- SharePoint Embedded access
- AI security metadata
- Power Pages access

---

# Matter Creation Flow

## Secure Matter Wizard

The Matter creation wizard supports:

### Inputs
- Matter metadata
- `Is Secure`
- Internal security groups/teams
- Internal users
- External contacts

### Behavior

When:
- `Is Secure = Yes`

the wizard initiates:
- security provisioning
- Owner Team provisioning
- SPE container provisioning
- external access provisioning
- AI security scope provisioning

This process should execute asynchronously using the standard job orchestration model.

---

# Dataverse Security Architecture

# Matter Table Ownership

The Matter table:
- must be User/Team owned
- must NOT be organization-owned

Each secure Matter row will:
- be owned by a dedicated secure Owner Team

Example:
- `Matter Secure Team - MAT-000123`

This team becomes the Dataverse security anchor for the Matter.

---

# Dataverse Security Roles

Normal users must NOT possess:
- Business Unit read access to Matters
- Organization read access to Matters

Otherwise secure Matters become discoverable.

Recommended baseline:
- User-level access only

Access to secure Matters is then granted through:
- Owner Team membership
- explicit sharing
- synchronized UAC grants

---

# Owner Team Provisioning

When a secure Matter is created:

The system provisions:
- Dataverse Owner Team
- secure team metadata linkage

The system then:
- assigns the Matter to the Owner Team
- adds explicitly selected users
- synchronizes selected security groups

Result:
Only authorized users may access the Matter row.

---

# Internal Access Management

## Recommended Approach

Dataverse native sharing should NOT be treated as the authoritative source of truth.

Instead:
Spaarke UAC tables become authoritative.

Example conceptual tables:
- `sprk_accessgrant`
- `sprk_accesssubject`
- `sprk_accessrole`

These grants synchronize:
- Dataverse team membership
- Dataverse sharing
- SPE access rules
- AI access scopes

---

# External Access Architecture

## External Users

External collaborators:
- are Contacts
- authenticate through Power Pages / Entra External ID / B2B

External access is governed through:
- `sprk_externalaccesscontrol`

This table becomes the authoritative source for:
- external Matter access
- external document access
- Power Pages workspace visibility
- external AI/search access

---

# sprk_externalaccesscontrol

## Purpose

Manages secure access for non-core users (Contacts) to secure Matters and related resources.

This table governs:
- which Contacts can access which secure Matters
- access levels
- expiration/revocation
- workspace visibility
- document/file permissions

---

## Recommended Responsibilities

The table should support:
- Contact lookup
- Matter lookup
- Access role/type
- Status
- Effective dates
- Expiration dates
- Revocation tracking
- Invitation status
- External workspace settings

---

## External Authorization Flow

Power Pages SPA requests:
- must flow through BFF authorization endpoints

The BFF:
- validates authenticated Contact identity
- evaluates `sprk_externalaccesscontrol`
- evaluates Matter secure access
- evaluates file/document access
- authorizes downstream SPE operations

External users should NEVER receive direct unrestricted SPE access.

---

# SharePoint Embedded (SPE) Security

# Secure Container Strategy

Secure Matters should receive:
- dedicated SPE containers

They should NOT use:
- shared Business Unit containers

This ensures:
- clean isolation boundaries
- simpler security evaluation
- easier lifecycle management
- safer external sharing
- safer AI indexing

---

# Document Security Inheritance

Documents associated with a secure Matter inherit the Matter security boundary.

This includes:
- Dataverse document rows
- SPE files
- AI indexing metadata
- semantic search visibility

---

# SPE Access Flow

All SPE access must flow through:
- BFF APIs
- authorization evaluation
- `SpeFileStore`

Authorization must occur BEFORE any SPE operation.

---

# AI and Semantic Search Security

# Core Requirement

Secure documents MUST NOT:
- appear in semantic search
- appear in AI grounding
- appear in recommendations
- appear in similarity search
- appear in embeddings retrieval

for unauthorized users.

---

# AI Security Trimming

Each indexed document/chunk should include:
- tenant ID
- Matter ID
- secure flag
- authorized principals
- Owner Team IDs
- external access identifiers
- container ID

---

# Query-Time Authorization

Azure AI Search is NOT the authorization layer.

The authorization layer is:
- Spaarke BFF
- UAC evaluation
- Dataverse access logic
- `sprk_externalaccesscontrol`

All AI retrieval must be post-filtered by authorization evaluation.

---

# Recommended Security Flow

```text
User/Contact Request
    →
BFF Endpoint
    →
Authorization Evaluation
    →
Dataverse/UAC Access Resolution
    →
SPE Access
    →
AI/Search Security Trimming
    →
Result Returned