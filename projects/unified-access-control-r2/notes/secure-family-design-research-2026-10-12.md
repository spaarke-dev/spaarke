# Secure family: design options and research (2026-10-12)

**Status: direction approved in round 94; team type DECIDED in round 100b (2026-10-14): C, an owner team per family; not built. The §7 spikes are the build's verification phase (spike 1 is dropped: it only matters for B).**

**Research:** researcher memory `.claude/agent-memory/researcher/dataverse-secure-family-access-teams-2026-10-12.md` holds the full findings and every source URL.

**Related:** [ontology-standard-child-contract-2026-10-12.md](ontology-standard-child-contract-2026-10-12.md), which is the prerequisite for putting Signals and Decision Records into a family.

## 1. The problem

Today:
- every secure record is owned by ONE memberless team, Secure Record Owners (SRO), in the Secure Record business unit;
- access comes from per-user shares, created on the root and mirrored onto every child by `SecureChildShareSynchronizer`;
- membership in a family is inferred by walking typed lookups (`SecureChildLineage`).

As a result there is no canonical answer to "which family is this row in". There are N copies of each grant (one per row per sharee) that must be kept in sync on add, move, flip and revoke. Families are isolated from ordinary business units, but not from each other by construction: the isolation rests entirely on the share mirror being right.

## 2. The owner's direction (round 94)

- A `sprk_securefamily` table, with a lookup of the same name on every family table. This is the canonical **umbrella** relationship.
- A single lookup, so a row belongs to at most one family. A write that would place a row under two families is refused.
- The family table carries metadata, and a parent-family link for nested secure roots (task 158).
- **Rejected: one business unit per family.** It does not scale to thousands of families.
- External contacts are unaffected. They are still BFF-evaluated grants (FR-19); the family gives the evaluator a one-hop key.

## 3. Documented limits (Microsoft Learn; researcher note)

| Question | Finding |
|---|---|
| Cap on shares per team, teams per environment, teams per user, POA rows | **None documented.** Only soft guidance: share to teams rather than users; "use the team as the record owner if you frequently share records with the same list of users"; excessive sharing grows POA. Community reports show degradation above about 20M POA rows. |
| Access-team template limits (4 per entity, 100 entities) | Apply **only** to system-managed (template) teams. User-created teams are not limited. |
| POA cost of an access-team share | One row per (record, team) per community SQL analysis. Learn is ambiguous about extra rows per member, so this needs a spike. |
| Hierarchy security | **Leaks** through team shares: a manager gets rows shared to a direct report's team. Secure tables must stay off the hierarchy list. |
| BU depth | A literal reading of "record belongs to same BU as the team of which user is a member" means a role at BU depth may reach rows in the team's BU. This applies to **both** team designs and needs a spike. NFR-05 clause 1 already guards depth reaching the Secure Record BU. |
| `ShareToPreviousOwnerOnAssign` | Must stay **No**, or every Assign leaves a full-rights share for the previous owner. |
| `ReassignObjectsOwner` | Moves **all** records of a principal, so it can't target one family on a shared team. It **can** with a team per family. |
| Bulk re-own | Per-row Assign or `UpdateMultiple` (100–1,000 per request, all-or-nothing). Service protection: 6,000 requests / 5 min / user. |
| Elastic tables | No sharing, access teams or cascades. **Not viable.** |
| Entra group teams | Just-in-time membership sync, cached up to 8 hours. A poor fit for revocation. |
| Filtered record ownership | **Public preview** (2026-09/10). One filter per predicate value, so it doesn't scale to thousands of families, and it has no deny. Not viable now. |
| Revocation latency (security cache, Dataverse search, Copilot) | **Undocumented.** Spike. |

## 4. Options

| | A. BU per family | B. Access team per family | C. Owner team per family (**recommended**) |
|---|---|---|---|
| Owner of family rows | Family BU's team | SRO (unchanged) | The family's own team |
| Internal access | BU membership | Team membership + **one share per row** to the team | Team membership; **no share** |
| POA per family row | 0 | 1 (more if per-member rows exist, see spike) | 0 |
| Per-row artifact to keep in sync | ownerid | ownerid (SRO) **and** the team share | ownerid only |
| Drift check | ownerid | Per-row `RetrievePrincipalAccess` (expensive) | One query: `sprk_securefamily = F and ownerid ≠ F.team` |
| Isolation between families | Yes | By share correctness only | By construction (Basic-depth team role) |
| Unsecure (bulk re-own) | Per row | Per row + revoke the shares | `ReassignObjectsOwner` from the family team (moves exactly the family) |
| Scale | **Rejected** (owner) | Teams: no cap | Teams: no cap; each needs one role assignment |

## 5. Recommendation: owner team per family (C)

Round 94 recorded an access team per family (B). The research changes my recommendation to C. Reasons:

1. **It matches Microsoft's guidance for exactly this case.** When the same list of users recurs across many records, Microsoft says to make the team the owner rather than share to it.
2. **It deletes the per-row grant.** The original problem is N copies of a grant that drift. B still keeps one share per row, created on add and moved on re-file and flip. C has no per-row grant: the family's access lives in one place, the team's membership.
3. **Drift becomes a one-line query.** Comparing `ownerid` with the family's team is a single FetchXML query. With B, verifying a share needs a principal-access read per row.
4. **Families are isolated from each other by construction.** The team role is Basic depth, so a member reads only rows the team owns. Under B every family row is owned by the same SRO team, and isolation depends on every share being right.
5. **Unsecure and bulk moves get simpler.** Because the team owns only its family, `ReassignObjectsOwner` moves exactly that family.
6. **The SRO pattern carries over.** Today's SRO role is already a Basic-depth team role guarded by the NFR-05 census. C reuses that guard, with one memberless SRO becoming one membered team per family.

### Costs and risks of C

- **Provisioning.** Creating a family creates one owner team and assigns it the family role. The BFF automates this at Make Secure. There are thousands of teams, with no documented cap.
- **All family teams sit in the single Secure Record BU** (not a BU per family). The BU-depth spike (spike 3) must show that a member's own BU-depth roles do not reach that BU through team membership. If they do, B has the same problem.
- **Member-create.** A member who creates a row through the UI could become its owner. In both designs, user roles hold no Create on family tables, and the BFF creates rows with `ownerid` set to the family team. Spike 5 confirms the privilege the BFF needs.
- **The Assign cascade** re-owns children and moves their BU. Spike 6 covers this behaviour at 10K rows.
- **Moving a row between families** is an Assign under C, or a revoke plus a grant under B. The cost is about the same.
- **Nested families** (a project under a secure matter becoming its own root) are unresolved in both designs. The owner needs to decide whether parent-family members reach child families: either through a parent link plus a membership union, or by making the parent's team a member.

**Decision needed from the owner:** C (owner team per family) or B (access team per family). In both cases it is conditional on the spikes in §7.

## 6. Contacts (external access)

There is no change in either design. Contacts never hold Dataverse or SPE permissions; the BFF evaluator decides (FR-19), and grants live in `sprk_externalrecordaccess`.

The family adds a one-hop key. A grant on the family root can be tested as `row.sprk_securefamily ∈ granted families`, the same shape as today's core-ancestor stamp test. The Secure veto (FR-22: direct grants only) still applies. G1–G5 (#1750–#1754) are unchanged.

## 7. Spikes (before building)

1. POA rows per team share and per `AddMembersTeam`. Matters for B only.
2. Latency from membership change to access in the Web API, Dataverse search and Copilot.
3. Does a member's own BU-depth role reach rows in the team's BU (the Secure Record BU)?
4. Does `AddMembersTeam` enforce per-table privileges on the member?
5. The privilege needed to create with `ownerid` set to the family team. Does the member-create hole close?
6. `UpdateMultiple` ownerid and Assign cascade at 10K rows; `ReassignObjectsOwner` from a family team.
7. An owner-team prototype: one family, three tables, a membership add and remove, and the drift query.

## 8. The umbrella for aggregation

**Secure records.** `sprk_securefamily = F` gives a deterministic set rather than a query result. For example, "update all related records" or "all decisions across related records" becomes one indexed filter, with no lineage walk.

**Ordinary records.** The family covers only secure records. Ordinary records already have a deterministic one-hop key: the core-ancestor stamps (`sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardingworkassignment`), kept fresh by `CoreAncestorRestamper` and the 5-minute job.

**Owner choice:**
- keep the family secure-only, and aggregate ordinary records by stamp; or
- generalise it to a "record family" for every core root, with a secure flag.

The second gives one aggregation key everywhere, at the cost of a family row per core record.
