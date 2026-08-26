# Investigation 09 — Dataverse Per-Record / Row-Level Security Restriction

> **Researched**: 2026-08-20 (researcher subagent)
> **For**: `unified-access-control-r2` — Secure Project design (is BU-per-secure-project the right mechanism?)
> **Primary sources**: Microsoft Learn (`wp-security-cds` ms.date 2025-06-03, site-updated 2026-08-14; `modernized-business-units-security` ms.date 2025-03-13, site-updated 2026-08-14; `manage-principalobjectaccess-storage` ms.date 2023-09-20, site-updated 2026-08-14), Power Platform blog 2025-08-07, Power Platform release plans 2025w2/2026w1.

---

## Headline answers

1. **There is NO per-record DENY in Dataverse — GA or preview — as of 2026-08.** The model remains strictly additive. The only forward-looking signal is a **filtered-view (predicate-based) row-level security model** mentioned in Microsoft's 2025-08-07 blog as "expanding to other workloads" from D365 F&O / Power Pages — it has **no maker-facing Dataverse feature, no Learn doc, and no release-plan entry** as of today.
2. **Restriction is achieved by scoping the baseline, never by subtracting.** The canonical confidential-records pattern = keep standing role privileges narrow (User-level, or BU-level with the record parked in a BU the user can't reach), then grant additively via sharing/teams.
3. **BU-per-secure-project is a legitimate, Microsoft-shaped mechanism** — and **matrix data access (modernized BUs, GA)** makes it much cheaper than the classic model because neither users nor owners have to move BUs. However, **one "secure" BU + owner-team-per-project** achieves the same isolation with far fewer BUs and is worth evaluating as the alternative.
4. **Impersonation honours the full access model** (roles, BU depth, teams, sharing/POA, hierarchy) — with one load-bearing caveat: effective privileges are the **intersection** of the app user's and the impersonated user's.

---

## Q1 — Per-record security mechanisms, enumerated (all GRANT-only)

Source of truth: [Security concepts in Microsoft Dataverse (`wp-security-cds`)](https://learn.microsoft.com/en-us/power-platform/admin/wp-security-cds) + [How access to a record is determined](https://learn.microsoft.com/en-us/power-platform/admin/how-record-access-determined).

Two binding quotes from `wp-security-cds` (current as of 2026-08-14):

> "A key concept of Dataverse security to understand is **all privilege grants are accumulative with the greatest amount of access prevailing. If you gave broad organization level read access to all contact records, you can't go back and hide a single record.**"

> "**Security is always additive offering the least restrictive permission of any of their entitlements.**"

| Mechanism | Grant or restrict? | Notes |
|---|---|---|
| **Security roles (privilege depth User / BU / BU+child / Org)** | GRANT. The depth *scopes* the grant; a narrow depth is the only way a role "restricts" — it never subtracts what another role/source grants. | Union across all roles (direct + team-inherited). |
| **Record sharing (POA)** | GRANT only. `GrantAccess`/`ModifyAccess`/`RevokeAccess` manipulate only the POA share row; revoke removes the share, never role/owner/team access. | "Should be an exception… less performant… tougher to troubleshoot" (`wp-security-cds`). |
| **Owner teams** | GRANT. Team ownership + team's role privileges reach members. Member's-privilege-inheritance mode ("team privileges" vs "direct user access") controls whether a User-level role acts on team-owned records. | The classic per-project grant seam. |
| **Access teams / templates** | GRANT. No roles, no ownership; access comes from the record share (POA) to the auto-created team. Template defines rights; members added manually/programmatically (`AddUserToRecordTeam`). | "More performant because they don't allow owning records by the team or having security roles assigned" (`wp-security-cds`). One POA row per record-team share. |
| **Entra group teams** | GRANT. Membership dynamic from Entra security group; otherwise like owner teams. Learn recommends mapping one Entra group per BU for admin at scale. | |
| **Business-unit scoping** | GRANT-scoping. A record in BU X is invisible to a user whose roles reach only BU Y — *provided no role grants Org depth on that table*. This is the closest thing to "restrict": move the record out of everyone's scope. | |
| **Matrix data access / modernized BUs** | GRANT-scoping (see Q4). Decouples `owningbusinessunit` from the owner's BU and lets one user hold roles from multiple BUs. Still additive. | [modernized-business-units-security](https://learn.microsoft.com/en-us/power-platform/admin/modernized-business-units-security) |
| **Hierarchy security (manager/position)** | GRANT only — gives managers access to subordinates' records. About the *user* hierarchy, not record parent/child. Irrelevant to restriction. | |
| **Column-level security (column security profiles + secured masking rules)** | The ONLY restrictive primitive — but at **column** granularity. Secured columns are withheld/masked even from users whose role grants table read. Explicitly NOT record-level: "column-level security has nothing to do with record-level security. A user must already have access to the record…" (`wp-security-cds`). | Cannot hide a row. |

**Conclusion**: to exclude a principal from a record you must remove/avoid the *source* of their access (role depth, BU placement, team membership, ownership, share). No mechanism subtracts.

---

## Q2 — Any NEW or preview record-level restrict/deny capability?

**No. Nothing GA, nothing in preview, as of 2026-08-20.** Checked:

- **Power Platform release plans**: [Dataverse 2026 wave 1 overview](https://learn.microsoft.com/en-us/power-platform/release-plan/2026wave1/data-platform/) (Apr–Sep 2026: Work IQ, agent programmability, storage management — no security/restriction features) and [2025 wave 2 overview](https://learn.microsoft.com/en-us/power-platform/release-plan/2025wave2/) (agentic capabilities, Dataverse MCP — nothing on row-level restriction).
- **The one genuine signal**: [Power Platform blog, 2025-08-07 "Strengthen Data Protection in Dataverse"](https://www.microsoft.com/en-us/power-platform/blog/2025/08/07/data-protection-in-dataverse/) describes a **"filtered view-based security model"** — admins define **predicates on column values, associated with security roles**, permitting CRUD only on rows matching the filter (example: user sees only rows where City ∈ {Redmond, Seattle}). The blog states it is "already in use by Dynamics 365 Finance and Operations and Power Pages and is **expanding to other workloads**." As of today there is **no Microsoft Learn doc, no PPAC switch, no release-plan item** exposing this to makers on custom Dataverse tables. Treat it as a WATCH item, not a design input. Note that even this is *filtered grant* semantics (role grants only matching rows), not a deny overlay — but if it ships for custom tables, "IsSecure = false OR user ∈ grantlist" style predicates could conceivably replace BU parking. Timeline unknown.
- Adjacent 2025 features that are NOT row deny: **secured masking rules** (column masking, GA), **app access control** (which apps reach an environment), **role-based view management** (UI visibility of views, not data security).

Explicitly: there is no feature named "private records", "confidential records", "record-level deny", or similar in Dataverse. Do not design against one.

---

## Q3 — Canonical Microsoft pattern for "confidential records visible only to named users"

Microsoft documents no single named "confidential records" recipe; the guidance across `wp-security-cds`, `how-record-access-determined`, and the POA doc composes into two sanctioned patterns:

**Pattern A — Narrow baseline + additive grants (share/team):**
- Standing roles give at most **User-level (Basic)** privileges on the confidential table → users see only what they own.
- Named users get access via **ownership, owner-team membership, or sharing** (prefer share-to-team / access-team over per-user shares — POA guidance, Q6).
- Trade-offs Microsoft names: sharing is "a less performant way of controlling access", "tougher to troubleshoot", "should be an exception"; access teams are the more performant sharing flavour; team ownership is preferred when the same user list recurs.

**Pattern B — BU compartmentalization:**
- Park confidential records in a BU whose roles are held only by the entitled users; everyone else's roles must be **BU-scoped, not Org-scoped**, on that table. "Business units define a security boundary" (`wp-security-cds`).
- With **matrix data access** (Q4) this no longer requires moving users or owners between BUs — you assign the entitled user a role *from* the secure BU. The doc's own use case: *"Users can have read/write access to records in one business unit while ensuring that records in another business unit remain private and inaccessible to unauthorized users."*
- Learn's admin-at-scale recipe: one **Entra security group per BU**, mapped to a **Dataverse group team** carrying that BU's role — membership add/remove in Entra = grant/revoke.

**The invariant both patterns share (and the trap):** any Org-depth privilege on the table, on ANY role held by ANY standing team of the user, defeats both patterns. "Very quickly a well-crafted security model starts looking like Swiss cheese" (`wp-security-cds`). A Secure Project design must therefore include an **audit that no role grants Org-level Read** on the secured tables (matter, document, todo, communication, …) to anyone but admin/app principals.

---

## Q4 — BU-per-record scaling; matrix data access

**Documented limits**: There is **no documented hard cap** on business-unit count. I could not find an authoritative "maximum business units" figure anywhere on Learn — flagging this as an ambiguity. The scaling concerns are indirect but real:

- **Role replication**: security roles created at the root BU are inherited into every child BU (a per-BU role instance exists so it can be assigned there). Roles + BUs multiply metadata; each BU also auto-creates a **default team** (system-managed). More BUs = more role instances, teams, and admin surface. (Behavior documented in `wp-security-cds` / `security-roles-privileges`; the multiplication *cost* is not quantified by Microsoft.)
- **Explicit Microsoft advice to minimize BUs**: the [POA storage doc](https://learn.microsoft.com/en-us/power-platform/admin/manage-principalobjectaccess-storage) — "If you have a complex business unit structure and frequent use of sharing: … **Minimize the number of business units** … Share to the team to allow users from different business units to access records."
- `wp-security-cds` steers away from 1:1 org modeling: BUs "lean more towards just defined security boundaries" — i.e., model boundaries, not entities. One BU per secure *project/matter* is a per-entity-instance boundary; at tens it's fine, at thousands it's the anti-pattern the "minimize" advice targets.
- Classic pain removed by matrix BUs: user reassignment between BUs (historically expensive — ownership cascade, role re-assignment) is no longer needed.

**Matrix data access / modernized BUs — what it changes** ([modernized-business-units-security](https://learn.microsoft.com/en-us/power-platform/admin/modernized-business-units-security), [wp-security-cds §Matrix data access](https://learn.microsoft.com/en-us/power-platform/admin/wp-security-cds#matrix-data-access-structure-modernized-business-units); **GA** — was preview circa 2021 wave 2, now standard PPAC switch):

- Enable per environment: PPAC → Settings → Product → Features → **"Record ownership across business units"** (= `EnableOwnershipAcrossBusinessUnits` OrgDBOrgSetting). Companion settings: `AlwaysMoveRecordToOwnerBusinessUnit=false` (record stays in its owning BU when reassigned), `RecomputeOwnershipAcrossBusinessUnits` (one-time recompute; locks system up to ~5 min).
- **`owningbusinessunit` becomes a settable column** (form/view/API/column-mapping), decoupled from the owner's BU. Setting it requires the caller's role to have **Append To (Local) on the Business Unit table**.
- **Users don't move BUs**: a user can be assigned security roles *from any BU*; access to a BU's records = holding a role from that BU with the table privilege. "The user's business unit is no longer relevant in determining the user's access to records."
- **Owner can be anywhere**: a user can own a record in any BU needing only *some* role with Read on the table — no role in the record's owning BU required.
- Cost note: it also removes the old need for team-ownership workarounds ("You no longer need to use Teams ownership to grant users from different business units…").

**Verdict for Spaarke**: BU-per-secure-project *works* and matrix access removes its worst classic costs (no user moves; `owningbusinessunit` is just a column you set at provisioning; grant = assign the secure BU's role, ideally via one Entra-group team per secure BU). But Microsoft's only directional guidance ("minimize BUs", BUs as boundaries not entities, role-instance multiplication, unquantified perf) argues for the **flatter alternative: ONE "Secure Projects" BU + one owner team per secure project** (team owns the project's records; team's role has User/Basic-depth privileges with team-privilege inheritance, so members reach only their team's records; contacts/externals granted via access team or share-to-team). Same exclusion guarantee, O(1) BUs, teams are cheap, membership is the grant seam. The BU-per-project design should be justified by something the single-BU+team model can't do (e.g., per-project role differentiation, per-BU admin delegation, or `owningbusinessunit`-based reporting) — otherwise simplify.

---

## Q5 — Impersonation (`MSCRMCallerID` / `CallerObjectId`)

Source: [Impersonate another user (Web API)](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/impersonate-another-user-web-api) (+ prior investigation 2026-07-16).

- **Yes — impersonated queries are row-filtered by the impersonated user's FULL effective access**: role privileges at their depth (incl. BU/matrix scoping), team memberships (owner, access, Entra-group), POA shares, hierarchy. There is no mechanism among these that impersonation bypasses or misses — the platform runs the same two-phase privilege+access check as a direct user query. This is exactly why it's the right lever for Secure Project list filtering.
- **The one real caveat — intersection semantics**: "When you impersonate another user, the effective privileges are the intersection of the privileges of the impersonating application user and the impersonated user." If Spaarke's application user's own role lacks a privilege (e.g., Read on a table, or has sub-Org depth), impersonated results can be NARROWER than what the user sees in the model-driven app. Mitigation: keep the app user's role Org-depth on all brokered tables (standard Spaarke setup) so the intersection ≡ the user's access.
- Header choice: `CallerObjectId` (Entra object id — preferred, no systemuserid lookup) vs `MSCRMCallerID` (systemuserid — legacy but fully supported). Functionally equivalent filtering. App user needs `prvActOnBehalfOfAnotherUser` (Delegate).
- Secondary caveats: the impersonated principal must exist as an **enabled SystemUser with a role** in the environment (relevant for the external-access account / contacts — contacts are NOT systemusers and cannot be impersonated; the Secure Project external story must broker contacts through the dedicated external-access systemuser or app-level filtering); records created under impersonation are owned by the impersonated user; audit captures both identities; column security also applies as the impersonated user.

---

## Q6 — POA (`principalobjectaccess`) scaling

Source: [Manage PrincipalObjectAccess storage](https://learn.microsoft.com/en-us/power-platform/admin/manage-principalobjectaccess-storage) (ms.date 2023-09-20; still the current doc, site-updated 2026-08-14).

- **Growth sources**: direct user shares; share-to-team (one row for the team; members covered indirectly); access-team membership (row per member add on system-managed teams); cascade/parental relationship inheritance (parent share/ownership fans out a POA row per child); the "share reassigned records with original owner" org setting.
- **No documented row-count limit** — the concern is storage, query cost on the access check, and cascade fan-out. Every access check on a non-owned record consults POA.
- **Microsoft's mitigation list (quoted)**: "Share with users … where the list of users isn't the same in the different records"; "**Use the team as the record owner if you frequently share records with the same list of users or share the record with the team**"; "Share only where needed"; "**Minimize the number of business units**"; "Share to the team to allow users from different business units to access records"; "Manage the lifecycle of your access team members. Remove users who are no longer needed"; "Remove all access team members when collaboration is over."
- **Direct POA deletion is unsupported** — clean up only by revoking through the security model. Changing a relationship's cascade to None triggers the async *inherited access rights cleanup* job.
- Design implication for Spaarke: per-record-per-user `GrantAccess` at scale is the POA-bloat path. **Team ownership (or share-to-one-team-per-project) is the sanctioned scale pattern** — one grant covers all the project's records; membership churn doesn't touch POA (owner team) or touches it once per member (access team), not once per member × record.

---

## Synthesis for the Secure Project design

1. **No deny exists; don't wait for one.** Exclusion must come from baseline scoping. The non-negotiable prerequisite is the **Org-depth audit**: no standing role held by regular users/teams may carry Org-level Read on any secured table.
2. **Current BU-per-secure-project mechanism is sound, not wrong** — and should adopt matrix-data-access ergonomics if it stays (set `owningbusinessunit` instead of moving records/users; grant via secure-BU role on an Entra-group team). But evaluate the **single-secure-BU + owner-team-per-project** variant: same guarantee, avoids BU proliferation Microsoft advises against, and makes team membership the single grant seam for users. Contacts/external principals still need the external-access systemuser brokering (contacts can't be impersonated or hold roles).
3. **Impersonated app-only reads will natively enforce whichever mechanism is chosen** — keep the app user Org-scoped so intersection semantics never narrow results.
4. **Watch item**: the filtered-view/predicate RLS model (blog 2025-08-07). If Microsoft ships it for custom tables, revisit — a role predicate like "row is not secure OR caller is granted" could eventually collapse the whole BU/team apparatus. Nothing to build on today.

## Sources (most authoritative first)

- https://learn.microsoft.com/en-us/power-platform/admin/wp-security-cds — ms.date 2025-06-03, updated 2026-08-14. Additivity quotes, BU/matrix sections, teams, sharing, column security.
- https://learn.microsoft.com/en-us/power-platform/admin/modernized-business-units-security — ms.date 2025-03-13, updated 2026-08-14. Matrix data access mechanics + rollout guidance.
- https://learn.microsoft.com/en-us/power-platform/admin/manage-principalobjectaccess-storage — ms.date 2023-09-20, updated 2026-08-14. POA growth + mitigations.
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/impersonate-another-user-web-api — intersection rule, headers, Delegate privilege.
- https://learn.microsoft.com/en-us/power-platform/release-plan/2026wave1/data-platform/ and https://learn.microsoft.com/en-us/power-platform/release-plan/2025wave2/ — confirm no record-restriction feature in current waves.
- https://www.microsoft.com/en-us/power-platform/blog/2025/08/07/data-protection-in-dataverse/ — filtered-view RLS signal (blog-only; no Learn doc as of 2026-08-20).
- https://learn.microsoft.com/en-us/power-platform/admin/how-record-access-determined — four access sources, union composition.
- Prior Spaarke researcher investigations 2026-07-16 (record-access union, POA APIs, impersonation) and 2026-08-18 (cascade/parental inheritance, POA bloat) — `.claude/agent-memory/researcher/`.

## Ambiguities / could-not-verify

- **No authoritative BU count limit exists** — "minimize" guidance is directional, unquantified.
- **Filtered-view RLS availability for custom Dataverse tables**: blog-only; no timeline, no preview switch found. Could be tenant-flighted; not verifiable from docs.
- **Role-instance-per-BU perf cost** is widely reported (community/field) but not quantified in official docs; treat as qualitative.
