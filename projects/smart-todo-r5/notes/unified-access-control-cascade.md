# Unified Access-Control Cascade — Investigation & Project Proposal

> **Status**: Investigation for review (2026-08-18). Seeds a new focused project.
> **Author**: smart-todo-r5 UAT follow-up (item: "child access cascade is part of our unified access control system — think it all the way through").
> **Audience**: architecture review → decide + scope a dedicated project.

---

## 1. The requirement (generalized)

> The **members of a parent record** should be able to **access the parent's child records**.

This is a **cross-cutting** need, not a To Do feature. It applies today to `sprk_todo` (child of Matter / Event / Invoice / +8 more), and will recur for future parent→child relationships (Events, Invoices, and others). It is therefore a candidate **"unified access control"** capability: **one reusable mechanism** that any parent→child pair opts into, rather than N bespoke implementations.

---

## 2. The one load-bearing Dataverse fact

**Being referenced by a lookup column grants ZERO access in Dataverse.** Access comes only from: **ownership**, **security-role privilege**, **team membership**, **share (POA)**, or the **user hierarchy**. Cascade/parental features only *propagate access that already exists on the parent* — they cannot *manufacture* access for a principal that a lookup merely points at.

This single fact shapes everything below: our "member group" is defined by lookups (§4), but lookups confer no access, so a cascade must **actively grant** access to those principals — which is why the general case needs code, not just configuration.

---

## 3. Current state — what already exists in the codebase

| Building block | Where | What it does |
|---|---|---|
| **Membership resolver** (ADR-034) | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipResolverService.cs` | Generic, **metadata-driven**: for ANY entity, auto-discovers identity-bearing Lookup fields and resolves associated principals. **Direction: user → records** ("which records is this user a member of"). |
| Field discovery | `MembershipFieldDiscoveryService.cs` | Keeps Lookups whose target is one of 6 identity tables: `systemuser, contact, team, businessunit, account, sprk_organization`. |
| Identity normalization | `IdentityNormalizationService.cs` | systemuser → `{systemUserId, contactId?, teamIds[], businessUnitId?, accountId?, organizationIds[]}`. |
| **Grant seam (POA)** | `IDataverseAccessGrantService` / `DataverseWebApiService.GrantAccessAsync` | Programmatic record share (GrantAccess). Used today by the Communication thread-access feature. |
| Reconciliation | `MembershipReconciliationJob.cs` | Nightly rebuild of `sprk_userentityassociation` (user↔record index); Service-Bus event sync (operator-gated, default off). |

**Gaps for our requirement:**
- The resolver answers **user → records**, not **record → members-then-cascade-to-children**. There is **no** access-team, `sprk_matterteammember`, event-attendee, or parent↔child access bridge. `sprk_userentityassociation` is a user↔record *index*, not a cascade.
- The `sprk_todo` **"Access Permission: Standard"** field seen on the form is **NOT a sprk_todo field** — it is `sprk_communication.sprk_accesspermission` (Standard/Limited/Restricted), rendered by a shared PCF, and **wired to no server-side access logic** (zero `.cs` references). `sprk_todo` access today = standard `ownerid` + `owningbusinessunit` only.

**Good news:** the two hard pieces already exist — a membership resolver (who are a record's members) and a POA grant seam (how to share a record). The missing piece is the **cascade wiring + trigger**.

---

## 4. What "member group" means per parent (it is already defined — by lookups)

There is **no dedicated member entity** per parent. A parent's "members" = the **union of identities its Lookup columns reference**, auto-discovered by the ADR-034 resolver. Examples:

- **`sprk_event`**: `ownerid` / `owningteam` / `owninguser` / `owningbusinessunit`; `sprk_assignedto`→contact, `sprk_assignedattorney`, `sprk_assignedparalegal`, `sprk_assignedfirm`→organization; `sprk_regardingcontact`→contact, `sprk_regardingaccount`→account; completed/approved/reassigned-by contacts.
- **`sprk_matter`**: the 8 `sprk_assigned*` lookups (`assignedattorney1/2`, `assignedparalegal1/2`, `assignedlawfirm1/2`→org, `assignedtointernal`→systemuser, `assignedtoexternal`→contact) + owner/owning-team/owning-BU.

So we do **not** need to invent a "member group" definition — the platform already resolves it. The question is only **which subset** confers child access (all members? only `sprk_assigned*`? owner+team?) — a policy decision for the project (§7).

---

## 5. Config-only options (no code) — verdict

| Option | Solves "parent members → child records (incl. children created later)"? | Exact limitation |
|---|---|---|
| **Parental 1:N relationship** (Reparent+Share = Cascade All) | **PARTIAL YES** | Grants the parent's **owner + owning-team** access to children **including future ones** (create-time inheritance — the "created-after-share never inherits" belief is FALSE for a *parental* relationship). **BUT:** only **ONE** relationship per table-pair can be parental, and a child cannot be the "many" side of two cascade relationships → **at most ONE of `sprk_todo`'s ~11 parent lookups can be parental.** Covers owner+team only; **`sprk_assigned*` principals and role-based access never cascade.** |
| Plain Share cascade (non-parental) | **NO** | Fires only at the moment of an explicit share *action* on the parent; children created afterward are not shared. |
| Access Team Template on child | **NO** | Template is config, but **members are never auto-populated** from the parent — they must be added by code/flow. |
| Hierarchical security (Manager/Position) | **NO** | Governs the *user* management hierarchy, not record parent-child. |
| BU / owning-team scoping | **NO (not from lookups)** | No OOB feature maps lookup principals to a team/BU. |

**Config-only bottom line:** the *only* pure-config win is **one parental relationship on one chosen parent path** (e.g. `sprk_regardingmatter → sprk_todo`), giving that parent's **owner + owning-team** access to its child To Dos (present and future). Every other part of the requirement — the other ~10 parent types, and every `sprk_assigned*`/regarding principal — **requires code.**

---

## 6. Design landscape for the code feature

### 6.1 Grant seams (all Web API, all app-only capable)
- **`GrantAccess` / `ModifyAccess` / `RevokeAccess`** — POA share per (principal, record). Precise; **one POA row per grant** (the bloat vector).
- **`AddUserToRecordTeam`** — per-record access-team membership (rights from a template).
- **`AddMembersTeam`** — owner-team membership.

**When to use which:** owning-team assignment is cheapest at scale (one grant covers every team-owned row, dynamic membership, no per-record POA rows) — best when "members" map to a stable team. Explicit POA share is precise but writes a row per grant. Access teams give per-record dynamic members with template rights (still one POA row per member per record).

### 6.2 Trigger options (plugin-free — the org uses no plugins)
| Trigger | Latency | Reliability | Volume / POA impact | Fit |
|---|---|---|---|---|
| **Power Automate flow** on child Create → unbound `GrantAccess` | sec–min | run history + retry | connector throttling; POA row per grant | easiest to build/own |
| **Dataverse webhook / service-endpoint → BFF** on Create | sub-second | own retry / dead-letter | best for volume + batching | fits Spaarke's BFF-centric model (ADR-034 already here) |
| **Scheduled/batch reconciliation** (BFF/Function) | min–hours | most resilient | can batch; **only clean way to propagate removals** | safety-net + removal path |

**Recommended shape:** a **real-time trigger** (webhook→BFF, reusing the ADR-034 resolver + `GrantAccessAsync`) for the **ADD-on-create** path, plus a **reconciliation batch** for parent-membership **changes/removals** that the event path structurally misses.

### 6.3 Pitfalls to design around
- **POA table bloat** — 11 parents × many To Dos × many members multiplies fast. Mitigation (Microsoft guidance): prefer **team ownership** over per-user shares, **share to teams not users**, minimize BUs, lifecycle-manage access-team members. **Direct POA delete is unsupported** — revoke via the security model.
- **Created-after gap** — real only for non-parental relationships; closed by a parental relationship (owner+team) and by the code trigger (lookup principals).
- **Sharing is additive-only, never restricts** — to *remove* a member's child access you must revoke the **source** (share/team/owner), not add anything.
- **Membership-change propagation** — nothing OOB watches lookup columns; add/remove of a parent member does not auto-propagate to already-created children. Needs a flow-on-parent-update + reconciliation. Switching a relationship's cascade to None fires the "inherited access rights cleanup" job.

---

## 7. Open decisions for the project

1. **Policy — which members confer child access?** All resolved principals, or only the access-conferring subset (`sprk_assigned*`, matching the existing `FilterToAccessConferringContactRoles`/NFR-05 convention), or owner+team only?
2. **Mechanism — team-ownership-sync vs per-record POA share vs access teams?** Recommendation lean (from research): treat as a **code feature** and prefer **owning-team sync** (POA-light, dynamic membership) over per-user share-cascade, given the 11-lookup fan-out and bloat cost.
3. **Trigger — Power Automate flow vs webhook→BFF?** Webhook→BFF reuses ADR-034 + the grant seam and fits BFF governance (§10) — but adds BFF surface (placement justification required).
4. **Scope of the reconciliation/removal path** (parent-side edits, member removals).
5. **Do we enable the one allowed parental relationship at all** (interim owner+team coverage for the primary parent) or skip it to avoid POA bloat the code path would manage differently?
6. **BFF placement** (§10 BFF Hygiene) — new endpoints/services + publish-size + placement justification.

---

## 8. Proposed project

**Name (suggested):** `unified-access-control-cascade-r1`
**Shape:** BFF feature (this project is BFF=N, so it must be its own project with §10 placement justification). **Low-invention** — reuses `IMembershipResolverService` (ADR-034) + `IDataverseAccessGrantService` (POA grant). New work = the cascade policy, the trigger (webhook→BFF or flow), and the reconciliation/removal path.
**Deliverables:** design.md (policy + mechanism + trigger decisions from §7), an ADR (or ADR-034 amendment) for the record→members cascade, the BFF wiring + tests, and a reconciliation job.
**Next step:** `/design-to-spec` → `/project-pipeline`.

---

## 9. Interim option for To Do NOW (config-only, answer to the "anything we can do now?" question)

The **only** no-code, no-conflict option available today is a **single parental 1:N relationship** on the primary parent path (most likely `sprk_regardingmatter → sprk_todo`). It gives that **matter's owner + owning-team** access to its child To Dos (present and future) — pure relationship configuration, no code.

**Caveats (why this is a decision, not a freebie):**
- Covers **one** parent type only (Matter) and **owner + owning-team** only — not `sprk_assigned*` individuals, not Events/Invoices.
- Introduces **POA cascade-sharing bloat** that the future unified feature may deliberately avoid (favoring owning-team sync).
- It is a real design commitment (one-parental-per-child limit is spent on this path).

**Recommendation:** because "must not conflict" is best guaranteed by deciding holistically, fold this parental-relationship choice into the project (§7 decision #5) rather than enabling it ad-hoc. If matter-case coverage is wanted immediately, enabling the single parental relationship is safe and reversible (switching cascade back to Referential fires the inherited-access cleanup job) — but know it only covers owner+team on the Matter path.

---

## Sources
- Existing code: ADR-034 (`.claude/adr/ADR-034-user-record-membership.md`), `Services/Ai/Membership/**`, `IDataverseAccessGrantService`, `docs/data-model/sprk_event-related-tables.md` / `sprk_matter-related-tables.md`.
- Microsoft Learn: configure-entity-relationship-cascading-behavior; create-edit-entity-relationships (parental constraints); security-sharing-assigning (inheritance); manage-principalobjectaccess-storage (POA bloat + the User2-creates-Case example).
