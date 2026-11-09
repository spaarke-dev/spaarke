# Task 054a — Direct message owner team: the lookup and its settings (part 1)

The first piece of 054a, committed alone so another project can merge it by SHA (code commit `6c1ad9cb16`). Rule: a Direct
1:1 message not filed under a record is owned by a dedicated team — a named, non-default, memberless Owner team in a
business unit that holds no users, is a direct child of the root, and is neither the root nor the Secure Record unit.
Rigor: FULL. Nothing here is wired into a create, send or thread path yet; that is the rest of 054a.

## What changed

- **One parameterised lookup (`SecureRecordOwnerTeam`).** `IdentifyAsync` / `ResolveAsync` are now the Secure Record names
  applied to one lookup parameterised by names (`NamedOwnerTeamLookup`):
  - `IdentifyNamedAsync` — the business unit by name, then its named, non-default Owner team (TOP 2 each);
  - `ProveUnoccupiedAsync` — the team has no member and the unit no user (an unreadable count refuses);
  - `ResolveNamedAsync` composes the two.
  The Secure entry points keep their signatures, reads and answers.
- **`DirectMessageOwnerTeam`** (concrete singleton, `Infrastructure/Dataverse`, registered in `CommunicationModule`):
  - `ResolveDirectMessageOwnerTeamAsync(ct)` → `DirectMessageOwnerTeamResolution(TeamId | Refusal, Fault)`;
  - `CannotBeDirectOwnerTeamAsync(teamId, ct)` — one team-row read, mirroring `CannotBeSecureOwnerTeamAsync`
    (another name, a default team or a non-Owner team → `true`; any doubt, an unset setting included → `false`).
- **Order of checks:** settings (no read) → Secure Record name → unit and named team (shared lookup; a not-found name
  that is the unit's DEFAULT team gets its own code) → placement (not the root; parent is the root) → no member, no user
  (shared lookup).
- **Refusal codes** (`DirectMessageOwnerTeamRefusal`, prefix `sdap.communication.direct_owner_team.`): `not_configured`,
  `bu_is_secure_record`, `bu_not_found`, `bu_ambiguous`, `bu_unreadable`, `bu_is_root`, `bu_not_root_child`,
  `team_not_found`, `team_is_default`, `team_ambiguous`, `team_unreadable`, `team_has_members`,
  `team_membership_unreadable`, `bu_has_users`, `bu_users_unreadable`.
- **Cache (ADR-009 metadata exception, documented in code):** the verdict (team or a non-fault refusal) is shared for
  2 minutes per instance, keyed by the configured names; one evaluation at a time (semaphore). A fault is never cached.
  `not_configured` and `bu_is_secure_record` are decided from settings alone and never cached.
- **Settings:** `DirectMessage:BusinessUnitName`, `DirectMessage:OwnerTeamName` — no defaults, no options type, no
  startup validation (unset refuses at use). Added to `appsettings.template.json` with `_DirectMessage_comment`.

## CLAUDE.md §11

**The generalised lookup** (`IdentifyNamedAsync` / `ProveUnoccupiedAsync` / `ResolveNamedAsync`)
1. *Existing:* `SecureRecordOwnerTeam.IdentifyAsync` / `ResolveAsync` (Web API), `SecureChildShareSynchronizer
   .ResolveSecureOwnerTeamAsync` (SDK), `RestrictedContentOwnerTeam.IdentifyAsync` (SDK, customer-unit team).
2. *Extension:* this IS the extension — the Secure pair's body, parameterised by names; no second copy of the reads.
3. *Cost of doing nothing:* the Direct team would need a second hand-written BU/team/member/user lookup that could
   drift from the Secure one (the POML forbids a new mechanism).

**`DirectMessageOwnerTeam` (class + DI registration)**
1. *Existing:* the lookup above; `CannotBeSecureOwnerTeamAsync`; `MemoizedHealthCheck` (process-local memo).
2. *Extension:* the reads are the shared lookup's; the class adds only the Direct-only checks (placement, default-team
   code, Secure-name collision) and the cache. A static helper (as `SecureRecordOwnerTeam`) has nowhere to hold the
   cache, so it is an instance.
3. *Cost of doing nothing:* every Direct send, join and create would run six Dataverse reads per message, or the
   checks would be skipped; the brief requires them once, cached.

**`DirectMessage:BusinessUnitName` / `DirectMessage:OwnerTeamName`**
1. *Existing:* `SecureRecord:*` and `RestrictedContent:OwnerTeamName` — the same "named lookup key" pattern.
2. *Extension:* the Secure keys cannot be reused; they name a different unit and team.
3. *Cost of doing nothing:* the Direct team could not be named per environment; a compiled default would select a
   team nobody set up on purpose (the rule is: no default).

## Placement (CLAUDE.md §10)

`Sprk.Bff.Api/Infrastructure/Dataverse`, beside `SecureRecordOwnerTeam`, on the existing `DataverseWebApiClient`
singleton. No new package, no interface (ADR-010), no options type, no background work. Registered in
`CommunicationModule` because its callers (send, join, Direct create) live there.

## Evidence

- BFF build: 0 warnings, 0 errors.
- Fail-first: with the pre-change `SecureRecordOwnerTeam.cs` and without `DirectMessageOwnerTeam.cs`, the test project
  does not compile (CS0246/CS0103 on the new types). After: all pass.
- `tests/unit/domain/Dataverse/DirectMessageOwnerTeamTests.cs`: 42 passed — each refusal (15 codes), success, cache
  reuse and expiry, a non-fault refusal cached, a fault not cached (6 fault points), a changed setting read fresh,
  `CannotBe` (6), and the Secure Record lookup's reads and answers (4).
- Secure Record unchanged (existing suites, no edits): `SecureNamedOwnerTeamProvisioningTests` 22/22,
  `RecordAccessGateTests` 29/29, `SecureRecordOwnerTeam*` 4/4, `SecureChildShare*` + `SecureChildReown*` 132/132.
- Communication: `ThreadResolverTests`, `ThreadResolverSeamTests`, `DirectThreadAccessServiceTests`, the two module
  sweep-job tests: 218/218.
- ArchTests (`IOptionsDrift`, `Credential*`, `GateCombination*`, `AsymmetricRegistration`, `RecordOwnerAssignmentCensus`,
  `SecureRecordOwnerRoleSetParity`): 121/121.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.
- Publish-size delta: not measured (no package or asset added; not in this piece's gate list).

## Fix round 1 (Opus review of the lookup commit)

New code commit on top of `6c1ad9cb16` (descendant; nothing amended). Rule wording only.

1. **No name is compared in code.** Dataverse's `name eq` ignores case and accents; a code-side comparison does not, so it
   could disagree with what Dataverse selected.
   - Fix: once the configured unit is found (and nothing faulted), the Secure Record lookup (`SecureRecordOwnerTeam
     .IdentifyAsync`) runs and the Direct unit id and team id are compared with its ids → `bu_is_secure_record`. The
     settings-only name check is gone. An ambiguous Secure Record lookup refuses `secure_record_ambiguous`; an unreadable
     one `secure_record_unreadable` (fault carried, not cached). No Secure Record unit, or no Secure Record team in it, is
     an answer (compare what was found). The cache key now includes the Secure Record names.
   - Test fake: every name-bearing filter is matched case- AND accent-insensitively, as Dataverse matches.
2. **Identity apart from validity.** `IdentifyDirectMessageOwnerTeamAsync(ct)` → `DirectMessageOwnerTeamIdentity
   (BusinessUnitId?, TeamId?, Refusal?, Fault?)` with `IsIdentified` and `IsNotConfigured`: both ids whenever the unit and
   its named team exist, whatever their member, user or placement state; `IsNotConfigured` (no ids, no fault, no read)
   when a setting is unset; a fault only when a read was unreadable; otherwise a not-found or ambiguous code. Not cached.
   `DirectMessageOwnerTeamResolution.IsNotConfigured` added. Caller rule documented on both methods and the result: only
   not-configured means "no Direct team can exist"; any other refusal or fault means "could be the Direct team" — settle
   identity with Identify.
3. **`CannotBeDirectOwnerTeamAsync` never answers a wrong "cannot be".** It now decides by id against Identify: `true` only
   when the configured team is identified and the id differs; any doubt (empty id, unset, not found, ambiguous) is
   `false`; a fault propagates. The unused `TeamRow` DTO is removed.
4. **A default-team read fault** returns `team_unreadable` (was `team_not_found`), with the fault.

Tests (`DirectMessageOwnerTeamTests`, 42 → 60):
- Must-fire, fail-first against `6c1ad9cb16`'s implementation (new-API tests removed so the file compiles): 8 failed,
  40 passed. Failing for the defects: the accent variants of the Secure Record unit/team names (3 cases, the old code
  resolved the Secure team or answered `team_not_found`), `CannotBe` for an accent variant of the real team (old: `true`),
  the default-team fault code (old: `team_not_found`). Failing by design: the success read sequence (now includes the
  Secure Record reads), `CannotBe_ReadsOnlyTheConfiguredUnitAndTeam_ByName` (was `CannotBe_IsOneTeamRead`),
  `CannotBe_InDoubt_AnswersNo` (a configured-but-missing team now answers `false`).
- The Identify tests (unset → not configured, no ids, no fault; team with members → ids while Resolve refuses
  `team_has_members`; misplaced or occupied unit → ids; read fault → fault, no ids; not found / ambiguous → no ids, no
  fault) fail first by not compiling against the old code (CS1061 on the new members).
- Existing tests changed because they encoded the old design: `Resolve_WellFormedSetup_ReturnsTheNamedTeam` (read
  sequence), `Resolve_BusinessUnitIsTheSecureRecordUnit_Refuses` (no longer "no reads"; the configured-Secure-name case
  now creates its unit), `CannotBe_InDoubt_AnswersNo` (an unknown team id is "cannot be" once the configured team is
  identified), `CannotBe_IsOneTeamRead` → `CannotBe_ReadsOnlyTheConfiguredUnitAndTeam_ByName`.
- Not changed: `SecureRecordOwnerTeam`, `SecureChildShareSynchronizer` (its own name compare belongs to another project).

rework: none task 054a
