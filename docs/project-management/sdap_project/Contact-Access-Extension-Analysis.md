# Contact-Based Access Extension Analysis
## Power Pages External User Support - Sprint 4+ Planning

**Document Status**: Planning / Deferred to Sprint 4+
**Created**: 2025-10-01
**Author**: Sprint 3 Analysis
**Related**: Sprint 3 Task 1.1 (Authorization Implementation)

---

## Executive Summary

This document analyzes the requirements for extending the Spaarke Unified Access Control (UAC) system to support **external users** (Contacts) via Power Pages, as defined in the UAC design specification.

**Decision**: **DEFER to Sprint 4+** - Focus Sprint 3 on Licensed Users only

**Rationale**:
- Sprint 3 fixes critical security for internal users (80% of use cases)
- Contact support adds significant complexity (identity resolution, Virtual Teams, delivery methods)
- Power Pages configuration is a prerequisite not yet complete
- Architecture is designed to extend cleanly in future sprint
- Lower risk to deliver incrementally: Licensed → Contact → Guest

---

## Table of Contents

1. [UAC Design Specification Review](#uac-design-specification-review)
2. [Current Implementation Status](#current-implementation-status)
3. [Gap Analysis](#gap-analysis)
4. [Architectural Impact](#architectural-impact)
5. [Sprint 4 Implementation Plan](#sprint-4-implementation-plan)
6. [Extension Points in Sprint 3 Code](#extension-points-in-sprint-3-code)
7. [Prerequisites & Dependencies](#prerequisites--dependencies)
8. [Risk Assessment](#risk-assessment)
9. [References](#references)

---

## UAC Design Specification Review

### Design Document Location
`c:\code_files\spaarke\docs\specs\Spaarke_Design Spec_Unified Access Control System.docx`

### Key Principles from Spec

**1. Single Source of Truth**
- All access control decisions made in Dataverse
- SPE containers have no user-level permissions
- Eliminates permission drift between systems

**2. Universal Access Model**
- Same security evaluation logic for all user types
- Consistent access resolution patterns
- Different authentication mechanisms per user type

**3. Application-Mediated Access**
- Users never directly access SPE
- All file operations through Spaarke API layer
- API validates permissions, fetches files with app credentials

**4. Role-Based Differentiation**
- Different user contexts (Licensed/Contact/Guest)
- Appropriate delivery methods per user type
- Role-specific restrictions enforced

---

### User Type Architecture

```
User Types in Spaarke:
├── Licensed Users (SystemUsers)
│   ├── Internal Employees
│   ├── Partners
│   └── Administrators
│
├── Contacts (External Users)       ← Sprint 4 Scope
│   ├── Outside Counsel
│   ├── Foreign Associates
│   ├── Clients
│   ├── Expert Witnesses
│   └── Vendors
│
└── Guests (Temporary Access)       ← Sprint 5+ Scope
    ├── Reviewers
    └── One-time Contributors
```

### Licensed Users (SystemUsers) - Sprint 3 ✅

**Identity Characteristics**:
- Azure Entra ID authentication
- Direct presence in Dataverse as SystemUser records
- Assigned security roles and business units
- Can be members of Access Teams
- Standard Dataverse security model

**Access Patterns**:
- Full Dataverse security evaluation (ownership, sharing, team membership)
- Any level of access (Read, Write, Delete, Share)
- Direct Office 365 integration
- Session-based access (4-8 hours)
- Delegation and impersonation support

**Implementation Status**: ✅ **Implemented in Sprint 3**

---

### Contacts (External Users) - Sprint 4 📅

**Identity Characteristics**:
- Authenticate through Power Pages (Azure AD B2C or local)
- Exist as Contact records in Dataverse
- **Cannot** be members of traditional Access Teams
- Access controlled through Web Roles, Table Permissions, and Virtual Teams
- May be associated with Account records (firms/organizations)

**Access Patterns**:
- Contact-specific security paths
- Role-based access profiles (Outside Counsel, Client, etc.)
- Time-bound access with automatic expiration
- Restricted delivery methods (view-only, watermarked)
- Enhanced audit trail for external access

**Implementation Status**: ❌ **NOT Implemented - Deferred to Sprint 4**

**Typical Roles**:

| Role | Access Level | Allowed Actions | Restrictions |
|------|-------------|-----------------|--------------|
| **Outside Counsel** | Write | Upload, Download, Edit | Cannot access admin docs |
| **Foreign Associate** | Read-only | View only | No privileged docs, watermarked |
| **Client** | Read-only | View, Download reports | No work product, no strategy docs |
| **Expert Witness** | Read-only | View specific technical docs | Limited scope |
| **Vendor** | Upload-only | Upload deliverables | Cannot download |

---

### Contact Access Profile System

**Entity Schema** (from design spec):

```csharp
public class ContactAccessProfile
{
    public Guid ProfileId { get; set; }
    public EntityReference ContactId { get; set; }
    public EntityReference MatterId { get; set; }

    // Role Definition
    public ContactRole AccessRole { get; set; }
    public AccessLevel DefaultAccessLevel { get; set; }

    // Scope Definition
    public List<string> AllowedDocumentCategories { get; set; }
    public List<string> RestrictedClassifications { get; set; }
    public bool AllDocumentsInMatter { get; set; }

    // Restrictions
    public bool AllowDownload { get; set; }
    public bool AllowUpload { get; set; }
    public bool AllowSharing { get; set; }
    public bool RequireWatermark { get; set; }
    public bool RequireNDA { get; set; }

    // Time Bounds
    public DateTime EffectiveFrom { get; set; }
    public DateTime? ExpiresOn { get; set; }

    // Audit
    public EntityReference GrantedBy { get; set; }
    public DateTime GrantedOn { get; set; }
    public string Status { get; set; }  // Active, Suspended, Expired, Revoked
}
```

**Pre-defined Role Templates**:

```csharp
ContactRole.OutsideCounsel:
  - DefaultAccessLevel: Write
  - AllowedCategories: Legal, Case Documents, Correspondence
  - RestrictedCategories: Financial, Administrative
  - AllowDownload: true
  - RequireWatermark: false
  - DefaultExpiration: 90 days

ContactRole.ForeignAssociate:
  - DefaultAccessLevel: Read
  - AllowedCategories: Public Documents, Filed Documents
  - RestrictedCategories: Privileged, Work Product, Strategy
  - AllowDownload: false (view-only)
  - RequireWatermark: true
  - DefaultExpiration: 30 days

ContactRole.Client:
  - DefaultAccessLevel: Read
  - AllowedCategories: Client Communications, Reports, Invoices
  - RestrictedCategories: Work Product, Internal, Strategy
  - AllowDownload: true
  - RequireWatermark: false
  - No expiration (lifetime of matter)
```

---

### Virtual Teams for Contacts

**Problem**: Contacts cannot be members of standard Dataverse Access Teams

**Solution**: Virtual Team pattern

```csharp
public class VirtualTeamMember
{
    public Guid Id { get; set; }
    public EntityReference TeamId { get; set; }      // References actual Access Team
    public EntityReference ContactId { get; set; }   // Contact member

    // Access Definition
    public string ContactRole { get; set; }
    public AccessLevel AccessLevel { get; set; }

    // Scope Restrictions
    public string AllowedCategories { get; set; }    // JSON array
    public string GeographicRestriction { get; set; } // For jurisdiction limits
    public string TimeRestriction { get; set; }      // Business hours, etc.

    // Management
    public EntityReference GrantedBy { get; set; }
    public DateTime GrantedOn { get; set; }
    public DateTime? ExpiresOn { get; set; }
    public string Status { get; set; }
}
```

**How It Works**:
1. Create Access Team in Dataverse (e.g., "Matter ABC Team")
2. Add Licensed Users as normal team members
3. Add Contacts as VirtualTeamMembers (custom entity)
4. When evaluating Contact access, check both:
   - Contact's Virtual Team memberships
   - Whether the record is shared with those teams

---

### File Delivery Methods

Different delivery mechanisms based on user type and access level:

```csharp
public enum FileDeliveryMethod
{
    DirectDownload,      // Licensed users with full access
    SecureDownload,      // Authorized external users (Outside Counsel)
    WatermarkedPDF,      // Restricted external (Foreign Associates, Clients)
    BrowserViewOnly,     // Minimal access (Guests)
    SecureUrl,           // Time-limited URL through proxy
    Denied               // No access
}
```

**Delivery Logic**:

```csharp
private FileDeliveryMethod DetermineDeliveryMethod(
    UnifiedIdentity identity,
    AccessDecision decision)
{
    // Licensed users get direct access
    if (identity.UserType == UserType.Licensed)
    {
        return decision.AccessLevel >= AccessLevel.Write
            ? FileDeliveryMethod.DirectDownload
            : FileDeliveryMethod.SecureUrl;
    }

    // Contacts get restricted access based on role
    if (identity.UserType == UserType.Contact)
    {
        if (decision.ContactRole == "Outside Counsel" && decision.AllowDownload)
            return FileDeliveryMethod.SecureDownload;

        if (decision.RequireWatermark)
            return FileDeliveryMethod.WatermarkedPDF;

        return FileDeliveryMethod.BrowserViewOnly;
    }

    // Guests get minimal access
    if (identity.UserType == UserType.Guest)
    {
        return FileDeliveryMethod.BrowserViewOnly;
    }

    return FileDeliveryMethod.Denied;
}
```

---

## Current Implementation Status

### What's Already Built (Sprint 3)

| Component | Status | Notes |
|-----------|--------|-------|
| **Authorization Infrastructure** | ✅ Complete | `IAuthorizationRule` pattern with rule chain |
| **AuthorizationService** | ✅ Complete | Orchestrates rule evaluation |
| **IAccessDataSource** | ✅ Complete | Abstraction for access queries |
| **DataverseAccessDataSource** | ✅ Complete | Uses `RetrievePrincipalAccess` for SystemUsers |
| **ResourceAccessHandler** | ✅ Complete | ASP.NET Core authorization handler |
| **ExplicitDenyRule** | ✅ Complete | Deny if AccessLevel.Deny |
| **ExplicitGrantRule** | ✅ Complete | Grant if AccessLevel.Grant |
| **TeamMembershipRule** | ✅ Complete | Basic team membership check |
| **Audit Logging** | ✅ Complete | Comprehensive logging with telemetry |
| **JWT Authentication** | ✅ Complete | Extracts userId from claims (oid) |

### What's NOT Built Yet

| Component | Status | Sprint |
|-----------|--------|--------|
| **UnifiedIdentity Model** | ❌ Not Started | Sprint 4 |
| **IdentityResolver** | ❌ Not Started | Sprint 4 |
| **ContactAccessProfile Entity** | ❌ Not Started | Sprint 4 |
| **VirtualTeamMember Entity** | ❌ Not Started | Sprint 4 |
| **ContactProfileRule** | ❌ Not Started | Sprint 4 |
| **VirtualTeamRule** | ❌ Not Started | Sprint 4 |
| **WebRoleRule** | ❌ Not Started | Sprint 4 |
| **ContactRestrictionRule** | ❌ Not Started | Sprint 4 |
| **FileDeliveryService** | ❌ Not Started | Sprint 4 |
| **Watermarking Integration** | ❌ Not Started | Sprint 4 |
| **View-Only Rendering** | ❌ Not Started | Sprint 4 |
| **Power Pages Integration** | ❌ Not Started | Sprint 4 |
| **Session Management** | ❌ Not Started | Sprint 5 |

---

## Gap Analysis

### Current Architecture (Sprint 3)

```
┌─────────────┐
│  Endpoint   │ (Licensed User JWT)
└──────┬──────┘
       │
       v
┌──────────────────────────────────┐
│ ResourceAccessHandler            │
│ • Extract userId (oid claim)     │
│ • Extract resourceId (route)     │
└──────┬───────────────────────────┘
       │
       v
┌──────────────────────────────────┐
│ AuthorizationService             │
└──────┬───────────────────────────┘
       │
       v
┌──────────────────────────────────┐
│ DataverseAccessDataSource        │
│ • RetrievePrincipalAccess        │
│   (SystemUser → Document)        │
└──────┬───────────────────────────┘
       │
       v
┌──────────────────────────────────┐
│ Dataverse Native Security        │
│ • Business Units                 │
│ • Security Roles                 │
│ • Team Membership                │
│ • Record Sharing                 │
└──────────────────────────────────┘
       │
       v
┌──────────────────────────────────┐
│ Authorization Rules              │
│ 1. ExplicitDenyRule              │
│ 2. ExplicitGrantRule             │
│ 3. TeamMembershipRule            │
└──────────────────────────────────┘
```

**Scope**: Licensed Users (SystemUser) only
**User Types**: 1 (Licensed)
**Authentication**: Azure AD
**Access Patterns**: Dataverse native security

---

### Target Architecture (Sprint 4)

```
┌─────────────┐
│  Endpoint   │ (Licensed JWT OR Contact JWT OR Guest Token)
└──────┬──────┘
       │
       v
┌──────────────────────────────────┐
│ ResourceAccessHandler            │
│ • Extract identifier (oid/email) │
│ • Extract resourceId (route)     │
└──────┬───────────────────────────┘
       │
       v
┌──────────────────────────────────┐
│ AuthorizationService             │
└──────┬───────────────────────────┘
       │
       v
┌──────────────────────────────────┐
│ UnifiedAccessDataSource          │ ← NEW (replaces DataverseAccessDataSource)
└──────┬───────────────────────────┘
       │
       v
┌──────────────────────────────────┐
│ IdentityResolver                 │ ← NEW
│ Determine UserType:              │
│ • SystemUser? (Licensed)         │
│ • Contact? (Power Pages)         │
│ • Guest? (Token)                 │
└──────┬───────────────────────────┘
       │
       ├─ Licensed ──────────────────────────────┐
       │                                         v
       │                         ┌──────────────────────────────┐
       │                         │ RetrievePrincipalAccess      │
       │                         │ (SystemUser → Document)      │
       │                         └──────────────────────────────┘
       │
       ├─ Contact ───────────────────────────────┐
       │                                         v
       │                         ┌──────────────────────────────┐
       │                         │ ContactAccessProfile Query   │ ← NEW
       │                         │ VirtualTeam Membership       │ ← NEW
       │                         │ WebRole Permissions          │ ← NEW
       │                         └──────────────────────────────┘
       │
       └─ Guest ────────────────────────────────┐
                                                v
                                ┌──────────────────────────────┐
                                │ GuestToken Validation        │ ← NEW
                                │ Ephemeral Permissions        │ ← NEW
                                └──────────────────────────────┘
       │
       v
┌──────────────────────────────────┐
│ Authorization Rules (Extended)   │ ← EXTENDED
│ 1. ExplicitDenyRule              │
│ 2. ContactProfileRule            │ ← NEW (Sprint 4)
│ 3. ExplicitGrantRule             │
│ 4. VirtualTeamRule               │ ← NEW (Sprint 4)
│ 5. TeamMembershipRule            │
│ 6. WebRoleRule                   │ ← NEW (Sprint 4)
│ 7. ContactRestrictionRule        │ ← NEW (Sprint 4)
└──────────────────────────────────┘
       │
       v
┌──────────────────────────────────┐
│ FileDeliveryService              │ ← NEW (Sprint 4)
│ • DirectDownload (Licensed)      │
│ • SecureDownload (Out. Counsel)  │
│ • WatermarkedPDF (Restricted)    │
│ • BrowserViewOnly (Guest)        │
└──────────────────────────────────┘
```

**Scope**: All user types
**User Types**: 3 (Licensed, Contact, Guest)
**Authentication**: Azure AD + Power Pages + Tokens
**Access Patterns**: Unified with role-based differentiation

---

## Architectural Impact

### Code Changes Required

#### 1. Data Model Extensions

**Extend `AccessSnapshot`** (Backward Compatible):

```csharp
// Current (Sprint 3)
public class AccessSnapshot
{
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }
    public AccessLevel AccessLevel { get; init; }
    public IEnumerable<string> TeamMemberships { get; init; } = Array.Empty<string>();
    public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

// Extended (Sprint 4) - All new fields nullable
public class AccessSnapshot
{
    // Existing fields (unchanged)
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }
    public AccessLevel AccessLevel { get; init; }
    public IEnumerable<string> TeamMemberships { get; init; } = Array.Empty<string>();
    public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;

    // New fields for Contact support
    public string? UserType { get; init; }           // "Licensed", "Contact", "Guest"
    public string? ContactRole { get; init; }        // "Outside Counsel", "Client", etc.
    public DateTimeOffset? ExpiresOn { get; init; }  // Time-bound access
    public bool? AllowDownload { get; init; }        // Contact restrictions
    public bool? RequireWatermark { get; init; }     // Delivery method hint
    public IEnumerable<string>? VirtualTeams { get; init; } // Contact virtual teams
}
```

**Create `UnifiedIdentity`** (New):

```csharp
public class UnifiedIdentity
{
    public required string Identifier { get; init; }  // Email, OID, ContactId, Token
    public required UserType UserType { get; init; }

    // Licensed User fields
    public Guid? SystemUserId { get; init; }
    public Guid? BusinessUnitId { get; init; }
    public IEnumerable<string>? SecurityRoles { get; init; }

    // Contact fields
    public Guid? ContactId { get; init; }
    public Guid? AccountId { get; init; }
    public IEnumerable<string>? WebRoles { get; init; }
    public IEnumerable<ContactAccessProfile>? AccessProfiles { get; init; }

    // Guest fields
    public string? GuestToken { get; init; }
    public GuestPermissions? GuestPermissions { get; init; }

    public DateTimeOffset AuthenticationTime { get; init; }
}

public enum UserType
{
    Licensed,
    Contact,
    Guest
}
```

---

#### 2. Service Layer Extensions

**Rename `DataverseAccessDataSource` → `UnifiedAccessDataSource`**:

```csharp
// Sprint 3
public class DataverseAccessDataSource : IAccessDataSource
{
    public async Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct)
    {
        // Only handles SystemUsers
        // Uses RetrievePrincipalAccess
    }
}

// Sprint 4
public class UnifiedAccessDataSource : IAccessDataSource
{
    private readonly IIdentityResolver _identityResolver;
    private readonly IDataverseService _dataverseService;

    public async Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct)
    {
        // 1. Resolve identity (SystemUser, Contact, or Guest)
        var identity = await _identityResolver.ResolveIdentityAsync(userId, ct);

        // 2. Route to appropriate access evaluator
        return identity.UserType switch
        {
            UserType.Licensed => await EvaluateLicensedUserAccess(identity, resourceId, ct),
            UserType.Contact => await EvaluateContactAccess(identity, resourceId, ct),
            UserType.Guest => await EvaluateGuestAccess(identity, resourceId, ct),
            _ => throw new NotSupportedException($"User type {identity.UserType} not supported")
        };
    }

    private async Task<AccessSnapshot> EvaluateLicensedUserAccess(...)
    {
        // Existing Sprint 3 logic: RetrievePrincipalAccess
    }

    private async Task<AccessSnapshot> EvaluateContactAccess(...)
    {
        // NEW: Query ContactAccessProfile, VirtualTeams, WebRoles
    }

    private async Task<AccessSnapshot> EvaluateGuestAccess(...)
    {
        // NEW: Validate guest token, check ephemeral permissions
    }
}
```

**Create `IdentityResolver`** (New):

```csharp
public class IdentityResolver : IIdentityResolver
{
    public async Task<UnifiedIdentity> ResolveIdentityAsync(string identifier, CancellationToken ct)
    {
        // Check if it's a SystemUser (GUID or email in systemuser table)
        var systemUser = await FindSystemUserAsync(identifier, ct);
        if (systemUser != null)
        {
            return new UnifiedIdentity
            {
                Identifier = identifier,
                UserType = UserType.Licensed,
                SystemUserId = systemUser.Id,
                BusinessUnitId = systemUser.BusinessUnitId,
                SecurityRoles = await GetSecurityRolesAsync(systemUser.Id, ct),
                AuthenticationTime = DateTimeOffset.UtcNow
            };
        }

        // Check if it's a Contact
        var contact = await FindContactAsync(identifier, ct);
        if (contact != null)
        {
            return new UnifiedIdentity
            {
                Identifier = identifier,
                UserType = UserType.Contact,
                ContactId = contact.Id,
                AccountId = contact.ParentAccountId,
                WebRoles = await GetWebRolesAsync(contact.Id, ct),
                AccessProfiles = await GetContactAccessProfilesAsync(contact.Id, ct),
                AuthenticationTime = DateTimeOffset.UtcNow
            };
        }

        // Check if it's a Guest token
        var guestAccess = await FindGuestAccessAsync(identifier, ct);
        if (guestAccess != null)
        {
            return new UnifiedIdentity
            {
                Identifier = identifier,
                UserType = UserType.Guest,
                GuestToken = guestAccess.Token,
                GuestPermissions = guestAccess.Permissions,
                AuthenticationTime = DateTimeOffset.UtcNow
            };
        }

        throw new UnauthorizedAccessException($"Unable to resolve identity for {identifier}");
    }
}
```

---

#### 3. Authorization Rules Extensions

**New Rules for Sprint 4**:

```csharp
public class ContactProfileRule : IAuthorizationRule
{
    public async Task<RuleResult> EvaluateAsync(
        AuthorizationContext context,
        AccessSnapshot snapshot,
        CancellationToken ct)
    {
        // Only applies to Contact users
        if (snapshot.UserType != "Contact")
            return Continue();

        // Check if Contact has an active profile granting access
        if (snapshot.AccessLevel == AccessLevel.Grant &&
            snapshot.ContactRole != null)
        {
            // Verify not expired
            if (snapshot.ExpiresOn.HasValue && snapshot.ExpiresOn.Value < DateTimeOffset.UtcNow)
            {
                return Deny("sdap.access.deny.contact_profile_expired");
            }

            return Allow($"sdap.access.allow.contact_profile.{snapshot.ContactRole}");
        }

        return Continue();
    }
}

public class VirtualTeamRule : IAuthorizationRule
{
    public async Task<RuleResult> EvaluateAsync(
        AuthorizationContext context,
        AccessSnapshot snapshot,
        CancellationToken ct)
    {
        if (snapshot.UserType != "Contact")
            return Continue();

        if (snapshot.VirtualTeams?.Any() == true)
        {
            // Contact is member of virtual teams that have access
            return Allow("sdap.access.allow.virtual_team");
        }

        return Continue();
    }
}

public class ContactRestrictionRule : IAuthorizationRule
{
    public async Task<RuleResult> EvaluateAsync(
        AuthorizationContext context,
        AccessSnapshot snapshot,
        CancellationToken ct)
    {
        if (snapshot.UserType != "Contact")
            return Continue();

        // Apply role-based restrictions
        if (snapshot.ContactRole == "Foreign Associate")
        {
            // Foreign Associates: Read-only, no privileged docs
            if (context.Operation == "write" || context.Operation == "delete")
                return Deny("sdap.access.deny.contact_readonly");
        }

        if (snapshot.ContactRole == "Client")
        {
            // Clients: No work product or strategy docs
            // (Would need document metadata to evaluate)
        }

        return Continue();
    }
}
```

---

#### 4. Dataverse Entity Changes

**New Entities to Create**:

1. **`sprk_contactaccessprofile`** (Contact Access Profile)
   - `sprk_contactid` (Lookup → Contact)
   - `sprk_matterid` (Lookup → sprk_matter)
   - `sprk_accessrole` (OptionSet: OutsideCounsel, Client, ForeignAssociate, etc.)
   - `sprk_accesslevel` (OptionSet: None, Read, Write, FullControl)
   - `sprk_allowedcategories` (MultiSelect OptionSet or Text)
   - `sprk_allowdownload` (Boolean)
   - `sprk_requirewatermark` (Boolean)
   - `sprk_effectivefrom` (DateTime)
   - `sprk_expireson` (DateTime)
   - `sprk_status` (OptionSet: Active, Suspended, Expired, Revoked)

2. **`sprk_virtualteammember`** (Virtual Team Member)
   - `sprk_teamid` (Lookup → team)
   - `sprk_contactid` (Lookup → contact)
   - `sprk_contactrole` (OptionSet)
   - `sprk_accesslevel` (OptionSet)
   - `sprk_allowedcategories` (Text/JSON)
   - `sprk_expireson` (DateTime)
   - `sprk_status` (OptionSet)

3. **Extend `contact`** (if needed)
   - `sprk_webroles` (Relationship to Power Pages Web Roles)
   - `sprk_defaultaccesslevel` (OptionSet)

---

## Sprint 4 Implementation Plan

### Prerequisites

**Before Starting Sprint 4**:

1. ✅ **Power Pages Environment Configured**
   - Power Pages site created
   - Authentication configured (Azure AD B2C or local)
   - Web Roles defined
   - Table Permissions configured for `sprk_document`

2. ✅ **Dataverse Schema Extended**
   - `sprk_contactaccessprofile` entity created
   - `sprk_virtualteammember` entity created
   - Contact entity extended (if needed)
   - Security roles updated

3. ✅ **Sprint 3 Validated**
   - Licensed user access working in production
   - Authorization system stable
   - Performance acceptable (< 200ms P95)

4. ✅ **External Users Identified**
   - List of Outside Counsel contacts
   - List of Client contacts
   - List of Foreign Associates
   - Access requirements documented

---

### Phase 4.1: Identity & Access Foundation (5-7 days)

**Tasks**:

1. **Create `UnifiedIdentity` Model**
   - File: `src/shared/Spaarke.Core/Auth/UnifiedIdentity.cs`
   - Includes: UserType, SystemUser fields, Contact fields, Guest fields

2. **Create `IIdentityResolver` Interface**
   - File: `src/shared/Spaarke.Core/Auth/IIdentityResolver.cs`
   - Method: `Task<UnifiedIdentity> ResolveIdentityAsync(string identifier, CancellationToken ct)`

3. **Implement `IdentityResolver`**
   - File: `src/shared/Spaarke.Dataverse/IdentityResolver.cs`
   - Logic: Check SystemUser → Contact → Guest

4. **Create `ContactAccessProfile` DTO**
   - File: `src/shared/Spaarke.Dataverse/Models/ContactAccessProfile.cs`
   - Maps to Dataverse entity

5. **Extend `AccessSnapshot`**
   - File: `src/shared/Spaarke.Dataverse/IAccessDataSource.cs`
   - Add nullable Contact fields (backward compatible)

6. **Rename `DataverseAccessDataSource` → `UnifiedAccessDataSource`**
   - Refactor existing class
   - Add identity resolution
   - Route to appropriate evaluator

**Acceptance Criteria**:
- Identity resolver correctly identifies Licensed vs. Contact vs. Guest
- Contact identity resolution queries Contact entity successfully
- AccessSnapshot includes Contact-specific fields
- Sprint 3 code (Licensed users) still works (backward compatible)

---

### Phase 4.2: Contact Access Rules (3-5 days)

**Tasks**:

7. **Implement `ContactProfileRule`**
   - File: `src/shared/Spaarke.Core/Auth/Rules/ContactProfileRule.cs`
   - Checks `ContactAccessProfile` entity
   - Validates expiration dates
   - Enforces role-based access

8. **Implement `VirtualTeamRule`**
   - File: `src/shared/Spaarke.Core/Auth/Rules/VirtualTeamRule.cs`
   - Queries `sprk_virtualteammember` entity
   - Checks if Contact is virtual member of team with access

9. **Implement `WebRoleRule`**
   - File: `src/shared/Spaarke.Core/Auth/Rules/WebRoleRule.cs`
   - Evaluates Power Pages Web Roles
   - Checks Table Permissions scope

10. **Implement `ContactRestrictionRule`**
    - File: `src/shared/Spaarke.Core/Auth/Rules/ContactRestrictionRule.cs`
    - Applies role-specific restrictions (read-only, category filters, etc.)
    - Checks document metadata for restricted classifications

11. **Update Rule Registration**
    - File: `src/api/Spe.Bff.Api/Infrastructure/DI/SpaarkeCore.cs`
    - Register new rules in correct order
    - Ensure precedence: Deny → Contact Profile → Grant → Virtual Team → Web Role

**Acceptance Criteria**:
- Contact with `ContactAccessProfile` can access documents in their matter
- Contact without profile is denied access
- Foreign Associate role enforces read-only access
- Client role cannot access work product documents
- Virtual Team membership grants access correctly

---

### Phase 4.3: Delivery Methods (5-7 days)

**Tasks**:

12. **Create `FileDeliveryService`**
    - File: `src/api/Spe.Bff.Api/Services/FileDeliveryService.cs`
    - Determines delivery method based on user type and restrictions
    - Generates appropriate URLs/streams

13. **Implement Direct Download**
    - For Licensed users with full access
    - Uses existing Graph API calls

14. **Implement Secure Download**
    - For authorized Contacts (Outside Counsel)
    - Time-limited SAS tokens
    - Audit logging

15. **Implement Watermarked PDF**
    - Integration with watermarking service (Azure Logic Apps or third-party)
    - Converts documents to PDF with user/date watermark
    - For Foreign Associates, Clients

16. **Implement Browser View-Only**
    - Office Online integration for in-browser viewing
    - No download option
    - For Guests and restricted Contacts

17. **Update File Endpoints**
    - File: `src/api/Spe.Bff.Api/Api/DocumentsEndpoints.cs`
    - Call `FileDeliveryService` based on authorization result
    - Return appropriate response (URL, stream, redirect)

**Acceptance Criteria**:
- Licensed users can download files directly
- Outside Counsel gets secure download link (4-hour expiry)
- Foreign Associates get watermarked PDF view
- Clients cannot download work product (403)
- All file access logged with user type and delivery method

---

### Phase 4.4: Power Pages Integration (3-5 days)

**Tasks**:

18. **Configure Power Pages Web Roles**
    - Create Web Roles: "Outside Counsel", "Client", "Foreign Associate"
    - Assign to Contacts in Dataverse

19. **Configure Table Permissions**
    - Create permissions for `sprk_document` table
    - Scope: Global with attributes (filter by ContactAccessProfile)
    - Read access for authenticated contacts

20. **Create Contact Portal Pages**
    - List view: My Documents (filtered by Contact access)
    - Detail view: Document viewer with file rendering
    - Upload form (if allowed by role)

21. **Test Contact Authentication Flow**
    - Contact logs into Power Pages
    - JWT includes Contact ID claim
    - BFF API receives Contact JWT
    - Identity resolver identifies Contact
    - Permissions evaluated via Contact profile
    - File delivered via appropriate method

22. **Integration Testing**
    - End-to-end tests for each Contact role
    - Verify access restrictions enforced
    - Verify file delivery methods work
    - Verify expiration handling

**Acceptance Criteria**:
- Contact can log into Power Pages successfully
- Contact sees only documents in their matters
- Outside Counsel can download authorized documents
- Foreign Associate gets read-only watermarked view
- Client cannot access restricted categories
- All access logged in audit trail

---

### Sprint 4 Deliverables

**Code Artifacts**:
- ✅ `UnifiedIdentity` model and `IdentityResolver`
- ✅ `UnifiedAccessDataSource` (replaces `DataverseAccessDataSource`)
- ✅ Extended `AccessSnapshot` with Contact fields
- ✅ 4 new authorization rules (ContactProfile, VirtualTeam, WebRole, ContactRestriction)
- ✅ `FileDeliveryService` with 5 delivery methods
- ✅ Updated file endpoints with delivery logic

**Dataverse Artifacts**:
- ✅ `sprk_contactaccessprofile` entity
- ✅ `sprk_virtualteammember` entity
- ✅ Web Roles configured
- ✅ Table Permissions configured

**Power Pages Artifacts**:
- ✅ Authentication configured
- ✅ Contact portal pages
- ✅ Document list/viewer

**Documentation**:
- ✅ Contact access management guide
- ✅ Role template reference
- ✅ Delivery method specifications
- ✅ Virtual Team setup guide

**Tests**:
- ✅ Unit tests for new rules
- ✅ Integration tests for Contact access flows
- ✅ End-to-end tests for each Contact role
- ✅ Performance tests (< 200ms target maintained)

---

## Extension Points in Sprint 3 Code

### Design Decisions That Facilitate Sprint 4

**1. `userId` Parameter is String (Not Guid)**
```csharp
public Task<AccessSnapshot> GetUserAccessAsync(string userId, ...)
```
✅ **Benefit**: Can accept SystemUser GUID, Contact GUID, email, or token
✅ **Sprint 4**: No signature changes needed

---

**2. `IAccessDataSource` Interface is Abstract**
```csharp
public interface IAccessDataSource
{
    Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct);
}
```
✅ **Benefit**: Implementation-agnostic
✅ **Sprint 4**: Swap `DataverseAccessDataSource` → `UnifiedAccessDataSource` via DI, no consumer changes

---

**3. `AccessSnapshot` is a Record (Immutable)**
```csharp
public class AccessSnapshot
{
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }
    // ...
}
```
✅ **Benefit**: Can extend with new optional properties without breaking existing code
✅ **Sprint 4**: Add nullable Contact fields, Sprint 3 code ignores them

---

**4. Authorization Rules are Independent**
```csharp
public interface IAuthorizationRule
{
    Task<RuleResult> EvaluateAsync(AuthorizationContext context, AccessSnapshot snapshot, CancellationToken ct);
}
```
✅ **Benefit**: Each rule is self-contained
✅ **Sprint 4**: Add new Contact rules without modifying existing ones

---

**5. Rule Chain Uses `IEnumerable<IAuthorizationRule>`**
```csharp
services.AddScoped<IAuthorizationRule, ExplicitDenyRule>();
services.AddScoped<IAuthorizationRule, ExplicitGrantRule>();
services.AddScoped<IAuthorizationRule, TeamMembershipRule>();
```
✅ **Benefit**: Rules evaluated in registration order
✅ **Sprint 4**: Insert Contact rules in appropriate positions:
```csharp
services.AddScoped<IAuthorizationRule, ExplicitDenyRule>();
services.AddScoped<IAuthorizationRule, ContactProfileRule>();  // NEW
services.AddScoped<IAuthorizationRule, ExplicitGrantRule>();
services.AddScoped<IAuthorizationRule, VirtualTeamRule>();     // NEW
services.AddScoped<IAuthorizationRule, TeamMembershipRule>();
services.AddScoped<IAuthorizationRule, WebRoleRule>();         // NEW
```

---

**6. `ResourceAccessHandler` Extracts User ID Generically**
```csharp
private string? ExtractUserId(ClaimsPrincipal user)
{
    // Try Azure AD object identifier (OID) first
    var oid = user.FindFirst("oid")?.Value;
    if (!string.IsNullOrWhiteSpace(oid)) return oid;

    // Fallback to standard NameIdentifier
    var nameId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!string.IsNullOrWhiteSpace(nameId)) return nameId;

    // Fallback to sub claim (OIDC)
    var sub = user.FindFirst("sub")?.Value;
    if (!string.IsNullOrWhiteSpace(sub)) return sub;

    return null;
}
```
✅ **Benefit**: Works with Azure AD (oid), Power Pages (NameIdentifier), or OIDC (sub)
✅ **Sprint 4**: Power Pages JWT with Contact GUID in NameIdentifier claim → works automatically

---

### Recommended Sprint 3 Code Additions (Optional)

**Add Placeholder Fields to `AccessSnapshot`** (now):

```csharp
public class AccessSnapshot
{
    // Existing required fields
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }
    public AccessLevel AccessLevel { get; init; }
    public IEnumerable<string> TeamMemberships { get; init; } = Array.Empty<string>();
    public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;

    // Sprint 4 fields (nullable, defaults to null for Sprint 3)
    public string? UserType { get; init; }           // "Licensed", "Contact", "Guest"
    public string? ContactRole { get; init; }        // "Outside Counsel", "Client", etc.
    public DateTimeOffset? ExpiresOn { get; init; }  // Time-bound access
    public bool? AllowDownload { get; init; }        // Contact restrictions
    public bool? RequireWatermark { get; init; }     // Delivery method hint
    public IEnumerable<string>? VirtualTeams { get; init; } // Contact virtual teams
}
```

**Benefit**: Sprint 3 code populates required fields only (backward compatible), Sprint 4 extension is seamless.

---

## Prerequisites & Dependencies

### Power Pages Setup

**Required Configuration**:

1. **Power Pages Site**:
   - Site created and published
   - Custom domain configured (optional)
   - SSL certificate installed

2. **Authentication**:
   - Azure AD B2C configured (for external users)
   - OR Local authentication provider configured
   - Registration workflow set up

3. **Web Roles**:
   - "Outside Counsel" role created
   - "Foreign Associate" role created
   - "Client" role created
   - Assigned to appropriate Contacts

4. **Table Permissions**:
   - `sprk_document` permissions configured
   - `contact` permissions configured
   - `sprk_contactaccessprofile` permissions configured
   - Scope and filters defined

**Validation**:
- [ ] Contact can register/log in successfully
- [ ] Contact's Web Role is assigned correctly
- [ ] Contact can view Contact entity records (self)
- [ ] Contact can view sprk_document records (via table permissions)

---

### Dataverse Schema

**Required Entities**:

1. **`sprk_contactaccessprofile`**:
   - Schema created (fields as per design spec)
   - Security roles grant read access to Contacts
   - Workflows/Power Automate for expiration handling (optional)

2. **`sprk_virtualteammember`**:
   - Schema created
   - Relationship to `team` and `contact`
   - Security roles configured

3. **Contact Entity Extensions** (if needed):
   - Custom fields for default access settings
   - Relationships to Web Roles

**Validation**:
- [ ] Entities created successfully
- [ ] Test data populated (sample profiles)
- [ ] Queries work (retrieve profiles for Contact)

---

### Azure Services

**Optional but Recommended**:

1. **Azure Logic Apps** (for watermarking):
   - Workflow: Receive document → Add watermark → Return PDF
   - Triggered from BFF API

2. **Azure Redis Cache** (for session management):
   - Premium tier recommended for production
   - Configured for distributed caching

3. **Application Insights** (for monitoring):
   - Custom events for Contact access
   - Dashboards for external user activity

---

## Risk Assessment

### Risks for Sprint 4

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| **Power Pages not configured in time** | Medium | High | Start Power Pages setup in parallel with Sprint 3 |
| **Contact authentication complexity** | Medium | Medium | Use Azure AD B2C template, follow MS docs |
| **Virtual Team pattern not performant** | Low | High | Index virtual team queries, cache memberships |
| **Watermarking integration delays** | Medium | Medium | Start with simple text overlay, iterate later |
| **Contact access rules too complex** | Medium | Medium | Start with basic roles, add granularity incrementally |
| **Security gaps in Contact access** | Low | Critical | Comprehensive security review before Sprint 4 deployment |
| **Backward compatibility break** | Low | High | Extensive testing of Sprint 3 code with Sprint 4 changes |

---

### Risk Mitigation Strategies

**1. Incremental Rollout**:
- Sprint 4.1: Identity resolution only (no file access yet)
- Sprint 4.2: Add Contact rules (read-only first)
- Sprint 4.3: Enable file delivery methods
- Sprint 4.4: Full Power Pages integration

**2. Feature Flags**:
```csharp
if (_configuration.GetValue<bool>("Features:ContactAccess"))
{
    // Use UnifiedAccessDataSource
}
else
{
    // Use DataverseAccessDataSource (Sprint 3)
}
```

**3. Parallel Testing**:
- Maintain Sprint 3 implementation alongside Sprint 4
- Run both in staging, compare results
- Switch via feature flag when validated

**4. Rollback Plan**:
- Keep Sprint 3 code in separate branch
- Document rollback steps
- Test rollback procedure before Sprint 4 deployment

---

## References

### Design Documents
- **UAC Design Spec**: `c:\code_files\spaarke\docs\specs\Spaarke_Design Spec_Unified Access Control System.docx`
- **Sprint 3 Task 1.1**: [Task-1.1-Authorization-Implementation.md](Sprint 3/Task-1.1-Authorization-Implementation.md)

### Architecture Decision Records
- **ADR-003**: Lean Authorization Seams - `docs/adr/ADR-003-lean-authorization-seams.md`
- **ADR-010**: DI Minimalism - `docs/adr/ADR-010-di-minimalism.md`

### Code References
- **Current Implementation**: `src/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs`
- **Authorization Rules**: `src/shared/Spaarke.Core/Auth/Rules/`
- **Resource Handler**: `src/api/Spe.Bff.Api/Infrastructure/Authorization/ResourceAccessHandler.cs`

### External Documentation
- **Power Pages Authentication**: https://learn.microsoft.com/power-pages/security/authentication/
- **Dataverse Web Roles**: https://learn.microsoft.com/power-pages/security/create-web-roles
- **Table Permissions**: https://learn.microsoft.com/power-pages/security/assign-table-permissions
- **Azure AD B2C**: https://learn.microsoft.com/azure/active-directory-b2c/

---

## Appendix: Contact Access Evaluation Pseudocode

### Full Contact Access Flow (Sprint 4)

```csharp
public async Task<AccessSnapshot> EvaluateContactAccess(
    UnifiedIdentity identity,
    Guid documentId,
    CancellationToken ct)
{
    var snapshot = new AccessSnapshot
    {
        UserId = identity.ContactId.ToString(),
        ResourceId = documentId.ToString(),
        UserType = "Contact",
        CachedAt = DateTimeOffset.UtcNow
    };

    // Step 1: Check Contact Access Profiles
    var profiles = await QueryContactAccessProfiles(identity.ContactId.Value, documentId, ct);

    foreach (var profile in profiles.Where(p => p.IsActive && !p.IsExpired))
    {
        if (profile.AllDocumentsInMatter || profile.AllowedDocumentIds.Contains(documentId))
        {
            if (profile.AccessLevel > snapshot.AccessLevel)
            {
                snapshot.AccessLevel = MapAccessLevel(profile.AccessLevel);
                snapshot.ContactRole = profile.AccessRole.ToString();
                snapshot.ExpiresOn = profile.ExpiresOn;
                snapshot.AllowDownload = profile.AllowDownload;
                snapshot.RequireWatermark = profile.RequireWatermark;
            }
        }
    }

    // Step 2: Check Virtual Team Membership
    var virtualTeams = await QueryVirtualTeamMemberships(identity.ContactId.Value, ct);
    foreach (var vTeam in virtualTeams)
    {
        var teamHasAccess = await CheckTeamAccess(vTeam.TeamId, documentId, ct);
        if (teamHasAccess)
        {
            snapshot.VirtualTeams = virtualTeams.Select(vt => vt.TeamName);
            if (vTeam.AccessLevel > snapshot.AccessLevel)
            {
                snapshot.AccessLevel = MapAccessLevel(vTeam.AccessLevel);
                snapshot.ContactRole = vTeam.ContactRole;
            }
        }
    }

    // Step 3: Check Power Pages Web Roles
    var webRoles = identity.WebRoles ?? Array.Empty<string>();
    var tablePermissions = await GetTablePermissions(webRoles, "sprk_document", ct);

    foreach (var permission in tablePermissions)
    {
        if (await DocumentMatchesScope(documentId, permission.ScopeFilter, ct))
        {
            if (permission.Read && snapshot.AccessLevel == AccessLevel.None)
            {
                snapshot.AccessLevel = AccessLevel.Grant; // Minimum: read
            }
        }
    }

    // Step 4: Apply Contact-specific restrictions
    if (snapshot.ContactRole == "Foreign Associate")
    {
        // Read-only enforcement
        snapshot.AccessLevel = AccessLevel.Grant; // Read only, no write
        snapshot.AllowDownload = false;
        snapshot.RequireWatermark = true;

        // Check document classification
        var doc = await GetDocument(documentId, ct);
        if (doc.Classification == "Privileged" || doc.Classification == "Work Product")
        {
            snapshot.AccessLevel = AccessLevel.Deny;
        }
    }
    else if (snapshot.ContactRole == "Client")
    {
        var doc = await GetDocument(documentId, ct);
        if (doc.Category == "Work Product" || doc.Category == "Internal Strategy")
        {
            snapshot.AccessLevel = AccessLevel.Deny;
        }
    }

    return snapshot;
}
```

---

**Document End**
