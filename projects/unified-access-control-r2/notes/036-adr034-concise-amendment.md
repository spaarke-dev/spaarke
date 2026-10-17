# Task 036 — ADR-034 concise amendment (draft for the main session)

> **For**: `.claude/adr/ADR-034-user-record-membership.md`. Sub-agents cannot write `.claude/` (root CLAUDE.md §3), so the
> main session applies the three edits below with, or before, the task 036 PR.
> **Full version** (already on the branch): `docs/adr/ADR-034-user-record-membership.md` § Amendment A2.
> **Approval**: owner round 75 (2026-10-06), CLAUDE.md §6.5 path B, reworded per owner D1. Cite it in the PR.

## Edit 1 — header banner (insert after the Amendment A1 banner, before the A4 banner)

```markdown
> ⚠️ **Amendment A2 (2026-10-06, `unified-access-control-r2` task 036, path B — ACCEPTED by the owner, round 75)**: for
> **systemuser ACCESS on the SPA/Teams plane** with `ExternalAccess:ImpersonatedRootSets:Enabled` on, Dataverse's own
> answer replaces the membership rules: the impersonated read decides which records an internal user reaches there (at
> Read), and every write is confirmed against the user's real Dataverse rights on the target record
> (`CallerRecordAccessProbe`). The membership walk — A1.1's ownership columns included — stays for scoping, for the
> contact plane, and for the systemuser plane while the flag is off. Full rules:
> [full ADR](../../docs/adr/ADR-034-user-record-membership.md#amendment-a2-2026-10-06-accepted-for-an-internal-users-access-on-the-spateams-plane-dataverses-own-answer-replaces-the-membership-rules).
```

And the **Last Updated** line becomes:

```markdown
> **Last Updated**: 2026-10-17 (Amendment A2, accepted 2026-10-06 — `unified-access-control-r2` task 036); 2026-10-03 (Amendment A4 accepted, `unified-access-control-r2` task 142); 2026-06-22 (post-implementation polish per R3 task 100)
```

## Edit 2 — replace the first line of the A1.1 MUST bullet

Replace:

```markdown
- **MUST** (**A1.1**, 2026-09-17, `unified-access-control-r2` task 043, owner-approved — CLAUDE.md §6.5 path B) treat the **Dataverse platform ownership columns** — `ownerid`, `owningteam`, `owningbusinessunit` — as **structurally conferring on the authorization surface, WITHOUT a registry entry**. A1 as originally written covered only the maker-authored contact/org axis, which made ownership **inexpressible**: the registry validator rejects any declared `IdentityType` outside `{Contact, Organization}` as malformed, so `ownerid` could not be added even deliberately.
```

with:

```markdown
- **MUST** (**A1.1**, 2026-09-17, `unified-access-control-r2` task 043, owner-approved — CLAUDE.md §6.5 path B; **scoped by A2**, 2026-10-06) treat the **Dataverse platform ownership columns** — `ownerid`, `owningteam`, `owningbusinessunit` — as **structurally conferring on the authorization surface, WITHOUT a registry entry**, wherever the membership walk is the access answer: the contact plane, and the systemuser plane while `ExternalAccess:ImpersonatedRootSets:Enabled` is off. **With the flag on, A2 replaces the walk for systemuser access on the SPA/Teams plane** — Dataverse's own answer decides, and these columns confer nothing there. A1 as originally written covered only the maker-authored contact/org axis, which made ownership **inexpressible**: the registry validator rejects any declared `IdentityType` outside `{Contact, Organization}` as malformed, so `ownerid` could not be added even deliberately.
```

The A1.1 sub-bullets (why ownership is not a registry concern; the MUST NOT on widening to every identity-typed lookup;
the evidence) stay as they are.

## Edit 3 — new bullets

Append to **✅ MUST** (after the A1.1 bullet and its sub-bullets):

```markdown
- **MUST** (**A2**, 2026-10-06, task 036, owner-accepted §6.5 path B) with `ExternalAccess:ImpersonatedRootSets:Enabled` on, take a systemuser's SPA/Teams record set from Dataverse's answer only — the impersonated read through `IImpersonatedRootSetSource`, at **Read** — and confirm every write that caller makes on `/api/v1/external` against Dataverse on the target record through the existing `CallerRecordAccessProbe` before writing (AppendTo admits a child, Write an edit, Delete a delete-own, the creator stamp still required). Keep the contact-grants union (B-17) and the deny / Restricted vetoes on both flag states. Enforced by: `ExternalProjectDataEndpoints.WithDataverseRightsAsync` at each write route, with per-route tests.
- **MUST** (A2) fail closed: a failed impersonated read (a fault, a missing or empty `SystemUserId`, no source) fails the composition; a failed or absent rights answer adds no rights.
```

Append to **❌ MUST NOT**:

```markdown
- **MUST NOT** (A2) fall back to the membership walk, an app-only read or a constant when Dataverse's answer cannot be read, or stamp `MembershipTermRights` (or any constant) on a record of a flag-on systemuser composition.
- **MUST NOT** (A2) narrow A1.1 or edit `PlatformOwnershipColumns` for the flag-off path (owner D1: flag-off never under-grants), and **MUST NOT** turn the flag on in an environment before tasks 142 and 143 are complete there and the task 034 canary passes there **strictly smaller** (equal sets are a failure). The checked-in value stays `false` (enforced by `ImpersonationNegativeCanaryTests`' config tripwire).
```
