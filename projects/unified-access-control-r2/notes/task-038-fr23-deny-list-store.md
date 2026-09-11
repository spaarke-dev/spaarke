# Task 038 — FR-23 deny-list store: schema + fail-closed BFF reader

> **Completed** 2026-09-04 · `sonnet` tier @ high · rigor FULL · `parallel-safe: true` (p1-independent)
> **Depends on** none · **Spec** FR-23 · **Design** §4.5 veto slot 1 · **Register** B-10, B-11
> Unit suite (ExternalAccess folder): **131 passed / 0 failed** (15 new) · publish delta **+0.02 MB**

---

## What was built

1. **Live Dataverse schema** — `sprk_noaccessentry` (see
   [`src/solutions/SpaarkeCore/entities/sprk_noaccessentry/entity-schema.md`](../../../src/solutions/SpaarkeCore/entities/sprk_noaccessentry/entity-schema.md)
   for the full field list, the four key-combination worked examples, and Business Rules). Deployed to
   `spaarkedev1.crm.dynamics.com`, verified live via `mcp__dataverse__describe` after creation.
2. **`INoAccessListReader` / `NoAccessListReader`** —
   `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/NoAccessListReader.cs`. Fail-closed reader:
   given a subject (contact id + active organization ids) and a candidate-record batch (each with its own
   referenced-organization set), returns the denied subset with provenance (matched entry ids), or a
   deny-all-queried result if the read faulted.
3. **DI registration** — append-only block in `ExternalAccessModule.AddExternalAccess()`, placed next to
   `ContactStandingGrantReader` (thematic neighbor): typed `HttpClient` + `INoAccessListReader` binding.
4. **Tests** — `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/NoAccessListReaderTests.cs`,
   15 tests covering exactly the task's closed acceptance-criteria set plus the structurally-necessary
   edges the reader's own contract implies (short-circuits, the defensive subject-size ceiling, malformed
   rows, multi-entry provenance).

**Not touched, deliberately**: `AccessibleRecordSetService.cs` (task 039 owns wiring the veto into
`ApplyVetoPipeline` Slot 1) and `TASK-INDEX.md` / `current-task.md` (main session owns these — five
agents are running concurrently in this worktree).

---

## Live-metadata verification (binding rule 4)

Before naming any column, live metadata was checked via `mcp__dataverse__describe`:

| Table | Status | Relevant fields confirmed |
|---|---|---|
| `sprk_noaccessentry` | Did **not** exist — confirmed via `describe`, error "Could not find an entity" | N/A — this task's deliverable |
| `sprk_recordtype_ref` | Exists | `sprk_recordtype_refid` (PK), collection `sprk_recordtype_refs`, `sprk_recordlogicalname` — the ADR-024 resolver target |
| `sprk_organization` | Exists | `sprk_organizationid` (PK), collection `sprk_organizations` |
| `contact` (OOB) | Exists | `contactid` (PK), collection `contacts` |

No task-brief schema claims needed correction this time (unlike task 037's `TrackingFieldTrio` citation
mismatch) — the task brief did not cite any specific option-set values for this entity, only structural
shape guidance, which was followed and then independently verified post-creation.

---

## 🔴 Tooling gap found: `mcp__dataverse__create_table` cannot target a publisher/solution

The first creation attempt used `mcp__dataverse__create_table` (the tool's schema has `tablename`,
`displayname`, `item`, `description` — **no solution/publisher parameter**). It silently created the table
under the environment's DEFAULT publisher instead of `Spaarke` (customization prefix `sprk`), producing a
stray **`cr140_noaccessentry`** (MetadataId `22929093-c35e-4573-9bc1-5df3deda6524`). It carries zero data,
zero relationships beyond its own, and zero code references.

**Fix applied**: abandoned that tool for this entity and used the raw Dataverse Web API with the
`MSCRM.SolutionUniqueName: SpaarkeCore` header — the exact pattern `scripts/Deploy-PrecedentEntity.ps1`
already uses in this repo — targeting the confirmed live `Spaarke` publisher (`customizationprefix: sprk`,
publisherid `6aeef721-ba73-f011-b4cb-6045bdd6a665`) and `SpaarkeCore` solution (`solutionid
fbfef485-e2a8-4b04-a795-7fa607402903`, `ismanaged: false`, per ADR-022). The correct `sprk_noaccessentry`
table was created this way, and every column was independently re-verified via `describe()` afterward.

**Outstanding**: the stray `cr140_noaccessentry` table still exists and needs deletion.
`mcp__dataverse__delete_table` requires `hasUserApproved: true`, settable only after the actual user's
affirmative consent — unavailable to a non-interactive subagent. This was surfaced to the project owner
directly (not routed through a peer agent, per the coordinator's correction mid-task — a peer agent cannot
grant an escalation it does not itself hold). **Action item for Ralph**: delete `cr140_noaccessentry` in
`spaarkedev1` when convenient; it is inert.

**Recommendation for future schema-creation tasks**: verify the resulting entity's prefix with
`describe()` **immediately** after any `mcp__dataverse__create_table` call, before adding further columns.
If the prefix is wrong, stop and switch to the raw Web API + `MSCRM.SolutionUniqueName` path rather than
retrying the same tool call (retrying does not change which publisher it targets).

### A second, unrelated finding from the same deployment: transient metadata-propagation lag

The very first attribute-add immediately after entity creation failed with a generic
`"An unexpected error occurred"`; a second attempt (relying on a `Test-AttributeExists` GET-based
idempotency check) then tried to recreate the SAME attribute and got a "already exists" conflict — the
existence check had read stale metadata seconds after the write. Resolved by adding a `Start-Sleep -Seconds
5` between dependent metadata-definition calls and re-verifying state via an independent `describe()` read
before each further write, rather than trusting a GET-based existence check taken immediately after a
prior write. This matches the `dataverse-create-schema` skill's own documented failure mode ("Re-run
script (idempotent design)") but adds the specific mechanism (propagation lag, not a bad payload) as a
concrete data point for whoever next authors a multi-attribute entity-creation script.

---

## Design decisions

### Why the reader queries by SUBJECT first, not per-candidate

`NoAccessListReader` issues bounded, chunked OData queries filtered on
`(subject clauses) and (object clauses) and statecode eq 0` — never a per-record round trip (NFR-02). Two
independent loops:

- **Loop A** (ethical wall): chunks the *distinct* organization ids referenced across the WHOLE candidate
  batch (not per-candidate), matching rows on `sprk_objectorganization`.
- **Loop B** (per-child revocation): chunks the *distinct* candidate record ids themselves, matching rows
  on `sprk_objectrecordid`.

Matching against the fetched rows happens in-memory in C#, against the FULL candidate list, not just the
chunk that produced a given row — one small deny-list row can answer for many candidates in one round trip.

### Chunk sizes (`ObjectIdChunkSize = 50`, `MaxSubjectOrganizationIds = 25`)

`ObjectIdChunkSize` matches the proven `FlagQueryChunkSize` precedent in
`ExternalParticipationService.GetRootRecordFlagsAsync` (task 037) — the same class of query (a bounded
OR-filter of ids in a GET URL). Because my query embeds BOTH a subject fragment and an object fragment in
one `$filter` (unlike the single-dimension root-flag query), `MaxSubjectOrganizationIds` is set lower (25)
so the worst-case combined clause count in one request stays within the same order of magnitude as the
already-proven 50-clause precedent rather than doubling it. In practice a contact belongs to a small
handful of organizations (register C-5) — the ceiling exists to make an implausible case fail SAFE
(deny-all-queried, matching the NFR-01 fail-closed contract), not because it is expected to be hit.

### Fail-closed direction is the mirror image of `ContactStandingGrantReader`

`ContactStandingGrantReader` answers an ADDITIVE yes/no question and fails closed toward `false` — an
unreadable term must contribute nothing, since it only ever widens access. `NoAccessListReader` answers a
VETO question, so its fail-closed direction is the opposite: an unreadable deny-list must contribute
DENIAL for every queried candidate, never "no denies" (spec NFR-01, this task's own binding rule 2).
`NoAccessListResult.FailedClosed` makes this distinction inspectable in code (not just in a log line) —
a caller or telemetry can tell "42 real denies today" apart from "3 fail-closed events."

### No caching, deliberately

Unlike `ExternalParticipationService`'s 60-second grant-set cache, this reader has no cache layer at all —
mirroring `GetRootRecordFlagsAsync` (task 037's Secure/Restricted veto-flag reader), which is also read
live per request. An ethical wall or a per-child revocation is exactly the kind of change that should take
effect on the very next request, not after a TTL window during which the walled-off party could still see
the record.

### Matching is by record id alone, not id + entity type

A candidate's `EntityLogicalName` is carried for provenance/logging but is NOT used to additionally confirm
the matched entry's `sprk_objectrecordtype`. Dataverse record ids are random v4 GUIDs assigned per row —
cross-table collision is not a realistic adversarial concern here, and a second join purely to re-confirm
entity type would add a round trip without closing a real gap.

### Malformed-row defense (Business Rule 1 in the schema doc)

No pre-create plugin enforces "exactly one subject" / "exactly one object" at write time (out of scope —
this task's declared outputs are the store + reader only). The reader defends against a row where the
object shape is ambiguous (neither or both of {object organization} / {object record type+id} populated)
by logging a WARNING and excluding that row from matching entirely — a malformed row must never silently
deny an unbounded set (the "exactly one" ambiguity means its intended scope is unknowable), and must never
be silently dropped without a trace either. This is a **data-quality guard**, deliberately distinct from
the NFR-01 fail-closed behavior for a faulted READ — pinned as a separate test
(`GetDeniedRecordsAsync_AmbiguousObjectShape_ExcludesRowFromMatching`) asserting `FailedClosed == false`
for that path.

---

## Testing approach: module-boundary substitute, not `Mock<HttpMessageHandler>`

`NoAccessListReader.QueryChunkAsync` is `internal virtual` (the ADR-010 testing seam,
`InternalsVisibleTo("Sprk.Bff.Api.Tests")` — the convention already used across this assembly). The test
file's `FakeNoAccessListReader` subclasses the reader and overrides ONLY that wire-level fetch, so the
REAL chunking, matching, malformed-row defense, and fail-closed orchestration in `GetDeniedRecordsAsync`
runs unmocked. This directly mirrors `FakeParticipationService` / `ThrowingFlagParticipationService` in
`AccessibleRecordSetServiceTests.cs` (task 037's own test doubles for the sibling `GetRootRecordFlagsAsync`
reader) — including passing `configuration: null!` / `credential: null!` to the base constructor, which is
why `NoAccessListReader`'s constructor has **no** `ArgumentNullException.ThrowIfNull` guards, matching
`ExternalParticipationService`'s and `ModuleEntitlementResolver`'s exact constructor shape (not
`ContactStandingGrantReader`'s, which validates because it never needs a null-dependency test double).

`statecode eq 0` is enforced by the emitted OData `$filter`, not client-side post-filtering — since the
query seam is overridden wholesale in tests, that literal clause was pulled into its own pure,
independently-testable method (`CombineFilter`) specifically so "a deactivated entry denies nothing" has a
real assertion rather than being an unverifiable implementation detail.

---

## Quality gates

**`code-review`** and **`adr-check`** — run at Step 9.5 (FULL rigor, mandatory). See task report for
findings.

**Placement Justification (CLAUDE.md §10 / §11)**:

- **Existing**: grep for `noaccess`/`denylist` under `src/server` returns zero access-control hits (spec
  Placement table row 2: "Deny-list store | none found | New" — verified again at implementation time, not
  just at authoring time).
- **Extension**: `sprk_externalrecordaccess` is an additive GRANT store; encoding deny rows there would
  make "No Access" level-shaped, which FR-23 explicitly forbids (`max()` would ignore a low value and the
  ethical wall would fail silently in exactly the case it exists for — root CLAUDE.md §5 fact 5).
- **Cost of doing nothing**: ethical walls and per-child revocation are unimplementable — a contact on the
  No Access List for organization X would retain Full Access wherever a grant exists, and FR-27's
  per-child revocation would have no mechanism.
- **DI**: appended to the ALREADY-registered `ExternalAccessModule.AddExternalAccess()` — no new module
  file, no new `Program.cs` wiring.

**Publish size (NFR-06)** — master `eb71df826` (confirmed unchanged since task 037's same-day measurement
today; re-verified via `git log origin/master -1` before citing it, rather than re-running an identical
build) = **45.46 MB**; branch (this task) = **45.48 MB**; delta **+0.02 MB**; ceiling 60 MB (14.52 MB
headroom). Zipped with PowerShell `Compress-Archive -CompressionLevel Optimal`, matching
`scripts/Deploy-BffApi.ps1`.

**CVE** — `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api.csproj`: no vulnerable
packages (no new NuGet packages were added — 2 new `.cs` files + 1 additive DI block only).

---

## For task 039

The deny-list slot (`AccessibleRecordSetService.ApplyVetoPipeline` Slot 1) is still a documented no-op and
runs FIRST by construction — a record removed there is gone before Restricted looks at it. Wire it as:

```csharp
var denyResult = await _noAccessListReader.GetDeniedRecordsAsync(contactId, organizationIds, candidates, ct);
foreach (var recordId in denyResult.DeniedRecordIds)
{
    composed.Remove(recordId); // NEVER composed[recordId] = AccessRights.None
}
```

Three inputs you need to supply that this reader deliberately does NOT resolve itself:

1. **`organizationIds`** (the caller's active org memberships) — `ExternalParticipationService` already
   has a private `QueryActiveOrgIdsAsync` doing exactly this resolution for the org-GRANT term; you may
   want the same membership set here, or a public seam onto it (currently private — evaluate whether to
   promote it or duplicate the tiny query, weighing §11 against a cross-cutting-concern coupling).
2. **`ReferencedOrganizationIds` per candidate record** — which organization-typed lookups on a
   project/matter/etc. count as "referenced" for the ethical wall's ANY-reference over-match. This is
   likely the SAME allow-list ADR-034's org-typed-lookup work establishes (register B-12) — confirm before
   assuming it's identical to the access-CONFERRING allow-list, since FR-23's over-match is deliberately
   broader ("even a non-conferring reference like opposing counsel").
3. **A note on `IsOperationPermittedAsync`**: it explicitly rejects `AccessRights.None` as a caller bug
   (task 033) — a `None` written by this veto would be refused as a MALFORMED REQUEST rather than honoured
   as a denial. `composed.Remove(recordId)` is the only correct representation; this task's own binding
   rule 1 says the same thing from the store/reader side.

For task 043 (org-expansion term, Phase 2): the deny-list reader is plane-agnostic (it takes contact id +
org ids, not a `WorkforcePrincipal`), so it should compose the same way regardless of which term produced
the candidate's referenced-org set.
