# Secure Family: design brief

> **For:** the owner, and the session that runs `/design-to-spec` on this file.
> **Status:** brief, not a design. It collects what is decided and what is open, as of 2026-10-14.
> **Sources:** `notes/` in this folder. Owner rounds 87, 88, 94, 95, 99, 100 and 100b are quoted in `notes/owner-decisions-from-uac-r2.md`.

## 1. The problem

A secure record (a project, matter or work assignment flagged secure) hides itself and everything filed under it behind a wall: its to-dos, events, messages, documents, invoices, analyses, memos, Signals and Decision Records, and the secure records nested under it. Today:

- **Membership is computed, never stored.** Each time, the platform walks a row's typed lookups (`SecureChildLineage`) to decide whether it is behind a wall. Every part of the system that needs the answer repeats the walk: ownership, shares, file containers, external access, search.
- **Access is copied onto every row.** Every secure row is owned by one memberless team, Secure Record Owners (SRO). People get in through per-user shares that are created on the root and mirrored onto every child (`SecureChildShareSynchronizer`). That is N copies of each grant, which must be kept right on every add, re-file, flip and revoke.
- **Families are not isolated from each other by construction.** They all share one owner team, so the separation between two secure families rests entirely on the share mirror being correct.

The same root cause keeps producing defects:
- **#1506:** a secure work assignment under a secure matter got its own container, so uploads were refused.
- **#1857 / uac-r2 task 187:** external lists trusted a copied stamp, so rows moved behind a wall still listed. The fix is a parent walk on every read.
- **Share-mirror drift.**

## 2. The decided direction

- **A family record** (`sprk_securefamily`, working name), with **a single lookup of the same name on every family table**. A row belongs to at most one family. A write that would put it under two families is refused. *(Round 94.)*
- **One owner team per family** (option C), in the Secure Record business unit. The team owns every row in its family. Internal access is team membership, with no per-row shares. Isolation between families comes from a Basic-depth team role. *(Round 100b. The research recommended C over the owner's own earlier choice, an access team, on its merits.)*
- **One file container per family.** The topmost secure record's container serves the whole family. Making a family not secure MOVES its files to the business-unit container. *(Round 88; uac-r2 task 178, which moves into this project.)*
- **External contacts stay BFF-evaluated grants** (FR-19). The family gives the evaluator a one-hop key: a row is visible when the caller holds a grant on its family's root. The Secure veto (FR-22, direct grants only) still applies. *(Round 94.)*
- **Not one business unit per family:** rejected because it does not scale. *(Round 94.)*

## 3. The owner's model (2026-10-14)

> "We are creating a 'secure record' entity, it tracks if it is secure or not secure, and then has a relationship to all family members, so that if the access position changes from secure to not secure (or vice versa), all family members automatically follow."

This matches the direction. Two points in it are real design choices (D1, D2 below), not settled facts.

## 4. What it must deliver

1. Making a record secure creates (or joins) its family and its owner team. It also stamps the family lookup on every existing member, re-owns them to the family team, and moves new files to the family container.
2. Making a record not secure reverses all of that for the whole family at once: owners go back to their business-unit teams and files move to the business-unit container. Readable throughout.
3. A row created or re-filed under a family member joins the family in the same request, through the single write path. No Dataverse plugins (ADR-002).
4. Internal access is the family team's membership. "Manage Access" on the root manages that membership.
5. External access checks the family key in one hop. uac-r2 task 187's parent walk is replaced, or kept only as a fallback.
6. A one-query drift check finds every row whose owner or family lookup disagrees with its family.
7. Migration: existing secure records (SRO-owned with mirrored shares) move to families, with no access gap and no window where access is wider.

## 5. Open choices (owner)

- **D1. A family for secure records only, or a "record family" for every core record?**
  - **Secure-only** (the research note's default): a family exists only while its root is secure. Making a record secure means computing the membership once (a walk, at the flip), and making it not secure dissolves the family.
  - **Record family** (the owner's model in §3, research §8): every core record has a family row with a secure flag, and the membership is always maintained, so a flip in either direction is a flag change plus one re-own, with no walk at flip time. It also gives one aggregation key everywhere ("all decisions across related records").
  - **The catch:** ordinary rows can sit under several roots at once (a to-do under matter M and project P). A single family lookup holds exactly one. Options: the family follows one designated root, with the existing core-ancestor stamps carrying the rest; or the record family applies only to secure-capable roots; or a junction table. Secure-only avoids this because a row under two SECURE families is refused.
- **D2. How do members "automatically follow"?**
  - The BFF re-owns the family in one call (`ReassignObjectsOwner` from the family team: exactly the family, no walk); or
  - a Dataverse cascade on the family relationship (Assign: Cascade All), so re-owning the family row re-owns every member natively.
  - The cascade's behaviour at 10K+ rows, and its business-unit side effects, are spike 6.
- **D3. Nested families.** A secure project filed under a secure matter is either a member of the matter's family or a family of its own. If it is its own family, do the matter family's members reach it (parent link plus membership union, or the parent's team as a member of the child's team)? See uac-r2 task 158 and round 87 (the parent sets a floor; a child may be stricter).
- **D4. Moving a row between families** (a re-file from secure X to secure Y): re-own, plus a container move for its files. Confirm that the files move rather than staying in X's container, consistent with round 88's "un-secure MOVES files".
- **D5. Who may create rows in a family.** User roles hold no Create on family tables; the BFF creates with `ownerid` set to the family team (spike 5).

## 6. Platform checks (spikes, the first tasks)

From research §7. Spike 1 is dropped because it only matters for the access-team option.
- **2.** The latency from a membership change to effective access, in the Web API, Dataverse search and Copilot.
- **3.** Whether a member's own BU-depth roles reach rows in the team's business unit (the Secure Record BU). This affects any team design. NFR-05 clause 1 already guards depth into that BU.
- **4.** Whether `AddMembersTeam` enforces per-table privileges on the member.
- **5.** The privilege the BFF needs to create with `ownerid` = the family team, and whether the member-create hole closes.
- **6.** `UpdateMultiple` on `ownerid`, the Assign cascade at 10K rows, and `ReassignObjectsOwner` from a family team.
- **7.** A prototype: one family, three tables, a member add and remove, and the drift query.

Each spike needs go/no-go criteria written before it runs.

## 7. Constraints that carry over

- **ADR-002:** no Dataverse plugins. One server-side owner per invariant (`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`). Security fails closed.
- **ADR-028** is the single auth authority. Auth and security changes need owner sign-off.
- **D-123 (the secure wall)**, FR-19 (external is BFF-evaluated), FR-22 (the Secure veto: direct grants only).
- **NFR-05:** the Secure Record Owner role census. `config/secure-record-owner-role.json` is the codified privilege set, checked by `scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -Verify`. It extends to the per-family team role.
- `ShareToPreviousOwnerOnAssign` stays **No**. Secure tables stay off hierarchy security.
- **SPE container types are permanent** (at most 25 per tenant). Never purge or delete containers.
- **BFF hygiene** (`.claude/constraints/bff-extensions.md`): placement justification, publish-size delta, §11 component justification.
- **D-83:** no tenant-wide publish. No full SpaarkeMaster import on dev (#1618). The schema ships in a CI-built SpaarkeMaster release.

## 8. Scope

- **In scope:**
  - the family table and its lookups;
  - the per-family owner team and role;
  - the write path (create, re-file, flip);
  - internal access through membership, and Manage Access;
  - external access through the family key;
  - task 178's family container;
  - the drift check;
  - the migration of existing secure records;
  - retiring the share mirror for secure families.
- **Out of scope:** ordinary-record access (unless D1 chooses the record family); Signals and DRs beyond the standard child contract they already follow (round 95); Spaarke Basic User's Deep-depth reads (an owner item from uac-r2 task 046, unless spike 3 makes it part of this project).

## 9. Done when

- Making a record secure, or not secure, moves the whole family (owners, files, external visibility), proven live on dev at realistic volume.
- The drift query returns zero on dev after migration, and a guard keeps it there.
- Each of these is either a one-hop family check or a documented fallback: `SecureChildShareSynchronizer`'s per-row mirror; the lineage walks in `SecureChildReconciler`, `RecordContainerResolver` and task 187's `ParentLineage`.
- #1506 is closed, and task 187's follow-up (walk → family key) is closed.
