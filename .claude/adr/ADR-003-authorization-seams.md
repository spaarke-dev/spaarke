# ADR-003: Lean Authorization Seams (Concise)

> **Status**: Accepted, as amended
> **Domain**: Authorization
> **Last Updated**: 2026-09-04 (Amendment A1 — `unified-access-control-r2`, path B)

---

## Decision

**Two enforcement SURFACES**, one evaluator.

| Surface | Enforced by |
|---|---|
| MDA — **native** forms, grids, views | Dataverse natively (role depth × owner/BU/team + sharing). No code. |
| MDA — **embedded PCFs that read via the BFF** | ⚠️ The BFF evaluator. **Same exposure as SPA** — *not* Dataverse. |
| SPA / Teams | The BFF evaluator — the only boundary. |

**Ask "does this read go through the BFF?", NOT "is this the MDA?"** BFF reads are app-only, so
Dataverse row-level security is inert on that path. Proven, not theorised: a user denied Read on all
442 documents saw and downloaded a matter's files through an MDA form's embedded PCF.

The **unified evaluator** returns **`(recordId → rights)`** — a map, never a bare id set. A
`HashSet<Guid>` structurally cannot carry a level, which is why matters and work assignments have none
today.

Storage still goes through `SpeFileStore`; access data still goes through `IAccessDataSource`.

---

## Constraints

### ✅ MUST

- **MUST** return `(recordId → rights)` from the evaluator — never a bare id set
- **MUST** compose additive terms by **highest-wins `max()`**: dataverse-answer, explicit-grant,
  derived-member, org-expansion, inheritance (child ← core ancestor, **1 hop**)
- **MUST** apply vetoes **after** the max, **in order**: deny-list → Restricted (→ `None`)
- **MUST** let **Secure** (`sprk_issecure`) suppress derived-member + org-expansion **BEFORE** the max,
  for **every** principal kind
- **MUST** treat `"No Access"` as a **veto**, never a level (as a level, `max()` ignores it and an
  ethical wall fails silently in exactly the case it exists for)
- **MUST** allow-list **org-typed** lookups as well as contact-typed ones (unrestricted org expansion
  confers access from *any* organization on the record — including opposing counsel)
- **MUST** fail **closed** on every error path and every term that cannot be evaluated
- **MUST** call authorization before `SpeFileStore` operations
- **MUST** include machine-readable deny codes (e.g. `sdap.access.deny.team_mismatch`)
- **MUST** key any cached access data by caller identity **and** credential mode (SP vs OBO)

### ❌ MUST NOT

- **MUST NOT** cache authorization **decisions** (cache **data** only)
- **MUST NOT** model `"No Access"` as a level, or apply Secure suppression after the max
- **MUST NOT** make direct Graph/SPE calls outside `SpeFileStore`
- **MUST NOT** add a new auth service without root CLAUDE.md §11's three-question justification

---

## Amendment A1 (2026-09-04) — what changed

Path **B** amendment (root CLAUDE.md §6.5), driver `unified-access-control-r2`. **Four** rules retired
because they no longer described the code — verified in source, not inferred from docs:

| Retired | Now |
|---|---|
| "two seams only" | Two enforcement **surfaces** (above); the seams still exist |
| "new auth logic **MUST** be an `IAuthorizationRule`" | MAY be a rule **or** an evaluator term |
| "**MUST NOT** create new service layers for auth" | Permitted **where it is the evaluator or a term**; §11 still applies |
| "cache UAC snapshots **per-request only**" | Short explicit TTL across requests is OK; **decisions** still never cached |

Why: `CachedAccessDataSource` already caches in `IDistributedCache` at 2 min / 60 s — cross-request
*and* cross-instance — and the external stack (`CallerPrincipalResolver` + `AccessibleRecordSetService`)
is a service, not a rule. A rule nobody follows is a trap for the next reader, not a guardrail.

**`OperationAccessRule` is NOT orphaned** — the single live `IAuthorizationRule`
(`Spaarke.Core/Auth/Rules/OperationAccessRule.cs`, registered `SpaarkeCore.cs:96`) remains valid and
registered. A1 stops *mandating* the rule shape; it does not deprecate the chain.

**Unchanged and still binding**: fail-closed, deny codes, authorize-before-storage, never cache
decisions.

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-007](ADR-007-spefilestore.md) | SpeFileStore as storage seam |
| [ADR-008](ADR-008-endpoint-filters.md) | Endpoint filters call auth |
| [ADR-009](ADR-009-redis-caching.md) | Cache data, not decisions |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | Supplies the validated identity feeding the evaluator |
| [ADR-034](ADR-034-user-record-membership.md) | Membership resolution + the access-conferring allow-list |

**See**: [UAC access-control pattern](../patterns/auth/uac-access-control.md)

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-003-lean-authorization-seams.md](../../docs/adr/ADR-003-lean-authorization-seams.md) — Amendment A1 carries the full rationale.

**The model + its reasoning**: [`projects/unified-access-control-r2/design.md`](../../projects/unified-access-control-r2/design.md) §4.

**Update (2026-01-06)**: the initial rules list (`ExplicitDenyRule`, `TeamMembershipRule`, …) was superseded by a single `OperationAccessRule` backed by `OperationAccessPolicy`. See [UAC Access Control](../../docs/architecture/uac-access-control.md).
