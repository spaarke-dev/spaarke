# ADR-003: Lean authorization with two seams (UAC data and file storage)

| Field | Value |
|-------|-------|
| Status | **Accepted, as amended** |
| Date | 2025-09-27 |
| Updated | 2026-09-04 (Amendment A1) |
| Authors | Spaarke Engineering |

> ⚠️ **READ [Amendment A1](#amendment-a1-2026-09-04-two-surface-authorization-and-the-unified-evaluator) BEFORE APPLYING ANY RULE BELOW.**
> A1 supersedes **four** of this ADR's rules — "two seams only", "new auth logic MUST be an
> `IAuthorizationRule`", "MUST NOT create new service layers for auth", and "cache UAC snapshots
> per-request only". The sections beneath still state them in their original 2025 form, deliberately:
> this ADR records what was decided *and* what changed. **A1 wins on every point it addresses.**
> Everything A1 does not address — fail-closed, machine-readable deny codes, authorize-before-storage,
> never cache *decisions* — is unchanged and still binding.

## Context

We need flexibility to enforce Dataverse-backed Unified Access Control (UAC) and integrate with SharePoint Embedded (SPE), without proliferating service interfaces. Over-abstracted layers impede clarity and testability.

## Decision

| Rule | Description |
|------|-------------|
| **Concrete AuthorizationService** | Evaluates ordered set of small `IAuthorizationRule` policies |
| **One UAC seam** | `IAccessDataSource` → `DataverseAccessDataSource` returns coarse snapshots scoped to a **single request/job execution** |
| **One storage seam** | `SpeFileStore` encapsulates Graph/SPE operations (no generic `IResourceStore`) |
| **Policy separation** | Rules contain policy only; SDK/HTTP usage remains in adapters |

## Consequences

**Positive:**
- Fewer classes, clearer responsibilities, faster unit tests, simple extension via new rules
- No leakage of provider details to higher layers

**Negative:**
- Slightly less generic than a policy engine, but far less boilerplate

## Alternatives Considered

Multiple service interfaces per concern and generic policy engines. **Rejected** as premature complexity and harder for AI-generated code to follow consistently.

## Operationalization

### Authorization Flow

| Step | Component |
|------|-----------|
| 1. Call AuthorizationService | Before any `SpeFileStore` operation |
| 2. Evaluate rules | Ordered rule chain |
| 3. Return decision | With machine-readable reason code |

### Initial Rules

| Rule | Purpose |
|------|---------|
| `ExplicitDenyRule` | Check explicit deny entries |
| `ExplicitGrantRule` | Check explicit grant entries |
| `TeamMembershipRule` | Verify team membership |
| `RoleScopeRule` | Check role-based scope |
| `LinkTokenRule` | Validate share links |

### Data Access

| Pattern | Implementation |
|---------|----------------|
| Snapshots | Fetched via `IAccessDataSource`, cached for the lifetime of a **single request/job execution** (never reused across requests/jobs) |
| Deny codes | Machine-readable (e.g., `sdap.access.deny.team_mismatch`) |

## Exceptions

Tenant-specific policies should be delivered as additional `IAuthorizationRule` implementations registered via DI, not new service layers.

## Success Metrics

| Metric | Target |
|--------|--------|
| Service/interface count | Reduced |
| Access check defects | Lower rate |
| Query performance | Stable |
| Authorization behavior | Predictable |

## Compliance

**Architecture tests:** `ADR003_AuthorizationTests.cs` validates seam boundaries.

**Code review checklist:**
- [ ] New auth logic implemented as `IAuthorizationRule`
- [ ] No direct Graph/SPE calls outside `SpeFileStore`
- [ ] Snapshots cached per request (not per call)
- [ ] Deny results include reason codes

## AI-Directed Coding Guidance

- If you need new authorization behavior, add an `IAuthorizationRule` (do not add a new service layer).
- Call authorization before invoking `SpeFileStore` operations.
- Do not cache authorization decisions; cache only the data snapshots used to evaluate rules.
- Treat UAC snapshots as **request/job scoped** only: do not store/reuse them beyond the current HTTP request or a single background job execution.

---

## Amendment A1 (2026-09-04): Two-surface authorization and the unified evaluator

> **Status**: Accepted (resolution path **B — amendment**, per root CLAUDE.md §6.5).
> **Driver project**: `unified-access-control-r2` (register item H-5; drift evidence register §G row 2).
> **Rationale + model**: [`projects/unified-access-control-r2/design.md`](../../projects/unified-access-control-r2/design.md) §4.
> **Sequencing**: this amendment merges **before** the evaluator it sanctions (task 032). The code
> lands under an ADR that permits it — not in violation of one.

### Why

This ADR was written in 2025 for a single enforcement surface reached through one rule chain. Three
things are now true that were not then, and each was verified in code before this amendment was
written — not inferred from the docs, which had drifted:

1. **There are two enforcement surfaces, not one.** Where a read goes through the BFF, the BFF filter
   is the *entire* security boundary: BFF reads are app-only, so Dataverse row-level security is inert
   on that path. Native MDA forms, grids and views *are* Dataverse-enforced. **The dividing line is
   "does this read go through the BFF?", not "is this the MDA?"** — an MDA-hosted PCF that reads via
   the BFF sits on the BFF surface. This was demonstrated, not theorised: a user denied Read on all
   442 documents by Dataverse saw a matter's full document list, and opened and downloaded the files,
   through an MDA form's embedded PCF.

2. **A `HashSet<Guid>` cannot carry a right.** The existing external stack answers "which ids?" and
   structurally cannot answer "with what rights?" — which is why matters and work assignments have no
   level today. Adding rights is not a new abstraction layer; it is the missing return value.

3. **The rules as written already do not describe the code.** `CachedAccessDataSource` caches access
   data in `IDistributedCache` at 2-minute (roles, teams) and 60-second (per-resource) TTLs — that is
   cross-request *and* cross-instance, in direct contradiction of "per-request only". The external
   stack (`CallerPrincipalResolver` + `AccessibleRecordSetService`) is a service layer, not a rule.
   A rule nobody follows is not a guardrail; it is a trap for the next reader.

### Superseded rules (exactly four)

| Retired rule | Replaced by |
|---|---|
| "Use **two seams only**" | **Two enforcement *surfaces***: Dataverse-native (MDA native forms/grids/views) and the BFF evaluator (SPA, Teams, **and MDA-embedded PCFs reading via the BFF**). The `IAccessDataSource` / `SpeFileStore` seams still exist; they are no longer the whole architecture. |
| "**MUST** implement new auth logic as `IAuthorizationRule`" | New authorization logic MAY be a rule **or** a service participating in the evaluator. The rule chain is **retained and still sanctioned** — see "What A1 does not do" below. |
| "**MUST NOT** create new service layers for auth (use rules)" | A service layer is permitted **where it is the evaluator or one of its terms**. This is not a general licence to add auth services: anything new still owes root CLAUDE.md §11's three-question justification. |
| "**MUST** cache UAC snapshots per-request only" / "**MUST NOT** reuse UAC snapshots across requests" | Access **data** MAY be cached across requests under a short, explicit TTL with a key that includes the caller identity **and** the credential mode (SP vs OBO). **Caching a decision remains forbidden.** This ratifies existing, reviewed behaviour rather than licensing new caching. |

### The evaluator contract (new, binding)

The unified evaluator returns **`(recordId → rights)`** — a map, never a bare id set.

```
ADDITIVE TERMS — union, HIGHEST WINS (max)
  1. Dataverse answer     (Type 1 only; impersonated read)
  2. Explicit grant       (carries a level)
  3. Derived member       (allow-listed lookups -> contact + org identities)
  4. Org expansion        (org identity -> active contacts)
  5. Inheritance          (child takes its core ancestor's rights, 1 hop)

VETOES — applied AFTER the max, IN THIS ORDER
  6. Deny list            (ethical wall + per-child revocation)  -> None
  7. Restricted           (sprk_accesspermission = Restricted)   -> None for ALL contacts

PRE-MAX SUPPRESSION
  8. Secure               (sprk_issecure = true) suppresses terms 3 and 4
                          BEFORE the max, for EVERY principal kind
```

Four properties of that contract are binding, and each exists because the obvious alternative fails:

- **`"No Access"` is a VETO, never a level.** Modelled as a level, `max()` would simply ignore it, and
  an ethical wall would fail silently in precisely the case it was built for.
- **Secure suppresses *before* the max**, not after. After the max the suppressed term has already
  won, and the suppression is a no-op on the only inputs that matter.
- **Veto order is deny-list → Restricted**, and it is load-bearing, not stylistic.
- **Allow-lists are first-class and cover org-typed lookups too**, not only contact-typed ones.
  Unrestricted org expansion confers access from *any* organization named on a record — including
  opposing counsel. (The allow-list's own promotion to a first-class per-surface concept is
  **ADR-034**'s amendment, not this one.)

### What A1 does NOT do

- **It does not orphan `OperationAccessRule`.** That is the single live `IAuthorizationRule`
  implementation (`Spaarke.Core/Auth/Rules/OperationAccessRule.cs`, registered at
  `Infrastructure/DI/SpaarkeCore.cs:96`); it backs the granular operation-permission model and
  **remains valid and registered**. A1 stops *mandating* the rule shape for all new auth logic; it
  does not deprecate the chain or the rule in it. Verified before amending, because retiring a MUST
  that a live consumer depends on would be an amendment that breaks running code.
- **It does not weaken fail-closed anywhere.** Every error path still denies. The evaluator fails
  closed on every term it cannot evaluate, and a secure record with no container of its own fails
  closed rather than falling back.
- **It does not touch deny codes** — machine-readable codes remain required on every denial.
- **It does not touch authorize-before-`SpeFileStore`** — still required, unchanged.
- **It does not permit caching decisions** — only data, as above.
- **It does not license new auth services generally.** Root CLAUDE.md §11 still applies to each one.

### Compliance

The 2025 code-review checklist item "New auth logic implemented as `IAuthorizationRule`" is retired by
this amendment; the remaining three checklist items stand. `ADR003_AuthorizationTests.cs` continues to
validate the seam boundaries A1 preserves.

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-003 Concise](../../.claude/adr/ADR-003-authorization-seams.md) - ~85 lines
- [Auth Constraints](../../.claude/constraints/auth.md) - MUST/MUST NOT rules

**When to load this full ADR**: Historical context, rule implementation details, exception policies.

**Update (2026-01-06)**: The initial rules list (ExplicitDenyRule, TeamMembershipRule, etc.) has been superseded by a single `OperationAccessRule` model backed by `OperationAccessPolicy`. See `docs/architecture/uac-access-control.md` for current implementation.
