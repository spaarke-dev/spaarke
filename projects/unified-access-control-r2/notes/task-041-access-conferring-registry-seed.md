# Task 041 — the access-conferring column registry: migration seed + findings

> **Completed** 2026-09-04. FR-24 / ADR-034 Amendment A1. Seed enumerated against **live Dataverse
> metadata** (`mcp__dataverse__describe`, this worktree's connected environment), not inherited from
> any prior doc. Code: `MembershipOptions.cs` (`AccessConferringRegistry` / `AccessConferringColumn` /
> `MembershipOptionsDefaults.CanonicalAccessConferringRegistry`), `MembershipResolverService.cs`
> (`FilterToAccessConferringRoles`), `IMembershipResolverService.cs`
> (`MembershipResolveOptions.AccessConferringOnly`).

---

## 1. What changed

The retired mechanism: `MembershipOptions.AccessConferringRoles` was a
`ConventionPrefix` ("sprk_assigned") + `ExcludedFields` pair. A descriptor qualified for the
CONTACT-anchored entry point (`ResolveByContactAsync`) iff it was Contact-typed, its field name
started with the prefix, and it wasn't excluded. The systemuser-anchored entry point (`ResolveAsync`)
applied **no filter at all** — every discovered descriptor conferred access.

The new mechanism: `MembershipOptions.AccessConferringRoles` is now an explicit
`AccessConferringRegistry` — `Dictionary<entityLogicalName, List<{Field, IdentityType}>>`. A column
confers access iff it is a literal registry entry for the entity being resolved, AND the entry's
declared `IdentityType` (`Contact` | `Organization`) matches what live metadata discovery actually
resolves for that field. No prefix, no convention — membership in the registry is the ONLY test.
`ResolveByContactAsync` still applies this filter unconditionally (as it always has); `ResolveAsync`
applies it only when the caller opts in via the new `MembershipResolveOptions.AccessConferringOnly`
(default `false` — the systemuser plane's scoping behavior is otherwise byte-identical to before this
task, pinned by `ResolveAsync_AccessConferringOnlyDefaultsFalse_ScopingOutputByteIdentical`).

**Why a rename can no longer silently change access**: under the retired convention, a column's own
name decided conferral with zero review — a maker naming a new field `sprk_assignedmonitor` (a
hypothetical illustrating the defect; no such live field exists — see §4) would have instantly started
conferring access, and a genuinely conferring field named without the prefix (e.g. `sprk_leadcontact`,
also hypothetical — no live field of that name exists either) would have been silently denied forever.
Under the registry, access depends ONLY on an explicit, reviewed entry — decoupled entirely from
whatever the column happens to be named. Renaming a column's naming pattern has zero effect on access;
only editing the registry does.

## 2. Call-site audit (independently re-verified, not inherited from task 040)

Grepped every reference to `AccessConferringRoles` / `FilterToAccessConferringContactRoles` in `src/`
before making any change. Confirmed: **only `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs`
consumes membership descriptors as an ACCESS ANSWER** (turns them into `AccessRights` via
`MembershipTermRights`). All other call sites use the resolver for scoping/self-query:

| Consumer | Surface | Access answer? |
|---|---|---|
| `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` (`ComposeForSystemUserAsync` / `ComposeForContactAsync`) | Authorization | **Yes** |
| `Api/Membership/MembershipEndpoints.cs` | Scoping (OBO, caller's own memberships) | No |
| `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` (playbook node) | AI scoping | No |

This task did **not** touch `Infrastructure/ExternalAccess/**` (another agent owns that surface
concurrently, and per the task notes the evaluator-side consumption of `AccessConferringOnly` is task
043's job). `AccessibleRecordSetService.ComposeForSystemUserAsync` still calls
`_membership.ResolveAsync(systemUserId, entityType, options, token)` with `AccessConferringOnly`
unset (defaults `false`) — so today's systemuser-plane behavior is **unchanged** by this task; task 043
is what flips the switch. Verified via a dedicated unit test run of
`AccessibleRecordSetServiceTests` (37/37 passed, unmodified) after the registry change landed.

## 3. The migration seed (live metadata, 2026-09-04)

Scope: the 10 entities named in this project's model (root CLAUDE.md / `projects/unified-access-control-r2/CLAUDE.md`
§ "The model" — Records row: **core** `sprk_project` / `sprk_matter` / `sprk_workassignment` /
`sprk_servicerequest`; **child** `sprk_invoice` / `sprk_communication` / `sprk_document` / `sprk_event`
/ `sprk_todo` / `sprk_analysis`). Each entity's FULL live attribute list was pulled via
`mcp__dataverse__describe('tables/{entity}')` and every `LOOKUP` column whose logical name starts with
`sprk_assigned` (case-insensitive — the retired convention prefix) was captured with its live target
table, translated to `Contact` / `Organization` per `IncludedIdentityTables`. This exactly reproduces
what `FilterToAccessConferringContactRoles` already admitted for the CONTACT path today (constraint:
"the cutover commit shows zero behavior change"), extended to also capture the org-typed columns
(binding rule 2).

| Entity | Contact-typed seed columns | Organization-typed seed columns |
|---|---|---|
| `sprk_matter` | `sprk_assignedattorney1`, `sprk_assignedattorney2`, `sprk_assignedparalegal1`, `sprk_assignedparalegal2`, `sprk_assignedtoexternal`, `sprk_assignedtointernal` | `sprk_assignedlawfirm1`, `sprk_assignedlawfirm2` |
| `sprk_project` | same 6 as `sprk_matter` | `sprk_assignedlawfirm1`, `sprk_assignedlawfirm2` |
| `sprk_workassignment` | `sprk_assignedattorney1`, `sprk_assignedattorney2`, `sprk_assignedlawfirmattorney1` ⚠️, `sprk_assignedparalegal1`, `sprk_assignedparalegal2`, `sprk_assignedto`, `sprk_assignedtoexternal`, `sprk_assignedtointernal` | `sprk_assignedlawfirm1`, `sprk_assignedlawfirm2` |
| `sprk_servicerequest` | **none** | **none** |
| `sprk_event` | `sprk_assignedattorney1`, `sprk_assignedattorney2`, `sprk_assignedparalegal1`, `sprk_assignedparalegal2`, `sprk_assignedto`, `sprk_assignedto1`, `sprk_assignedto2`, `sprk_assignedtoexternal`, `sprk_assignedtointernal` | `sprk_assignedlawfirm1`, `sprk_assignedlawfirm2` |
| `sprk_document` | **none** | **none** |
| `sprk_communication` | **none** | **none** |
| `sprk_invoice` | `sprk_assignedto1`, `sprk_assignedto2`, `sprk_assignedtoattorney1`, `sprk_assignedtoattorney2`, `sprk_assignedtoparalegal1`, `sprk_assignedtoparalegal2` | none |
| `sprk_todo` | `sprk_assignedto` | none |
| `sprk_analysis` | `sprk_assignedattorney1`, `sprk_assignedattorney2`, `sprk_assignedparalegal1`, `sprk_assignedparalegal2` | none |

**48 total entries across 7 entities.** `sprk_servicerequest`, `sprk_document`, `sprk_communication`
have zero seed entries — see §4.

⚠️ `sprk_workassignment.sprk_assignedlawfirmattorney1` has **no numbered "2" sibling** — a live naming
irregularity (confirmed against the raw `DESCRIBE TABLE` output, not a transcription slip), included
as-is because it is a real `sprk_assigned*`-prefixed Contact lookup today.

## 4. What's deliberately NOT in the seed — flagged for owner follow-up, not resolved here

The escalation trigger in task 041's POML fires on columns the OLD convention **wrongly admitted**
(e.g. a hypothetical `sprk_assignedmonitor`). Live metadata shows **no such field exists anywhere** in
this project's 10-entity scope — every `sprk_assigned*`-prefixed column found is a plausible
attorney/paralegal/law-firm/assignment role. So that half of the trigger does not fire.

The mirror-image case — a plausible conferring column the convention **wrongly denied** — DOES appear,
and is the live instance of the `sprk_leadcontact` example named in ADR-034 Amendment A1's own
rationale (that exact field does not exist either; it was illustrative). `sprk_servicerequest` carries
`sprk_requestedby` (Lookup → contact) and `sprk_regardingcontact` (Lookup → contact) — both read as
"the person this record is about," and neither ever matched the `sprk_assigned*` prefix, so today a
service request's requester gets **zero** membership-conferred access to their own request via this
mechanism. Per the seeding constraint ("the cutover MUST show zero behavior change"), these are **NOT**
added to the seed — doing so would be a behavior change smuggled into a mechanical migration.
Recorded here as a candidate registry edit for explicit owner review; FR-24's whole point is that such
an edit is now cheap and reviewable instead of requiring a rename.

`sprk_document` and `sprk_communication` are genuinely columnless under the `sprk_assigned*` convention
today (no candidates worth flagging — their closest fields, `sprk_relatedcontact` /
`sprk_regardingperson`, are informational/regarding fields, not assignment fields).

## 5. Test-suite migration note

Raw `new MembershipOptions()` has an EMPTY registry (same reasoning as `IncludedIdentityTables`
starting empty on raw construction — `IConfiguration.Bind` APPENDS to List-typed values, so seeding
can only happen via `IPostConfigureOptions`, never a property initializer, without risking double-up
under operator config). `MembershipResolverServiceTests.CreateSut`'s default now applies
`MembershipOptionsDefaults.PostConfigure` before wrapping in `Options.Create(...)` (`SeededOptions()`
helper) so the existing contact-path allowlist tests exercise the REAL migration-seeded registry, not
an empty one — this is why those tests pass **unchanged** (per AC6) even though the underlying
mechanism is completely different.

## 6. Verified unaffected by this task

- No `appsettings*.json`/`.template` anywhere in the repo sets `Membership:AccessConferringRoles`
  (grepped before deleting `ConventionPrefix`/`ExcludedFields`) — every deployed environment relies on
  the code-seeded default, so retiring those two properties carries zero config-drift risk.
- `AccessibleRecordSetServiceTests` (37/37) and the full `Services.Ai.Membership` namespace (155/155,
  includes discovery/identity-normalization/org-resolver/options/junction/reconciliation tests) pass
  unmodified.
- `MembershipResolveOptions.AccessConferringOnly` was added as the LAST positional-record parameter
  (default `false`); every existing call site (`MembershipEndpoints.cs`, `AccessibleRecordSetService.cs`
  ×2, `LookupUserMembershipNodeExecutor.cs`) uses named parameters, so this is source- and
  binary-compatible with zero call-site edits required.

## 7. Not verified / gaps

- The POML's `<relevant-files>` names `src/server/api/Sprk.Bff.Api/appsettings.json` as a file to
  modify. That literal file does not exist in this repo (gitignored local file); the tracked artifacts
  are `appsettings.template.json` / `appsettings.Development.json.template` /
  `appsettings.Production.json.template` / `appsettings.Testing.json`. None of them currently set
  `Membership:AccessConferringRoles`, so there is nothing to migrate in any of them — the code-seeded
  default covers every deployed environment today, matching the existing `IncludedIdentityTables`
  precedent. Not updated; flagged here rather than silently skipped.
- Live metadata was pulled for the 10 entities named in this project's explicit core+child model. If a
  future task widens the registry's scope to other `sprk_*` entities, those need their own live-metadata
  pass — not inherited from this seed.
