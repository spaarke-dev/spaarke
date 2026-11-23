# Task 042a notes: characterize the #2483 window (tests only)

Rigor STANDARD plus code-review and adr-check (TEST-MODIFYING override, CLAUDE.md section 8). Branch `task/fm-042a`, cut from `origin/work/field-mapping-server-write-path-r1` (19245c9996) and merged with `origin/master` (961c6846af).

## What was added (no `src/` path changes; `git diff --stat` lists only the two test files plus this note)

Each test is marked "CHARACTERIZES #2483" in its assertion reasons and XML doc, and carries the `_DI009Window` suffix so 042b can find it with `--filter DI009Window`.

| ID | Test (file) | Pins today |
|---|---|---|
| A1 | `RouteCreate_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheEventsOrdinaryTeam_DI009Window` (`tests/integration/data-mutation/RecordOwnership/RouteCreatePreCreateStampTests.cs`, partial of `SecureChildOwnershipAiToolTests`) | 201; `ownerid@odata.bind` = `/teams(Directory.ChildTeam)` (ChildTeam, in ChildBu), not SecureNamedTeam; create body `sprk_RegardingMatter@odata.bind` = `/sprk_matters(SecureMatter)`; no sharee-mirror row for the new event (`_shareTable.MaskOf("sprk_event", id, Sharee)` is null) |
| A2 | `RouteCreate_TheReconcilersQuestionOverTheWrittenEvent_OwnsItToTheSecureTeam_WhereTheCreateDidNot_DI009Window` (same file) | Write row built from A1's create body; `RecordOwnershipContext.ForExistingRowAsync` -> `_world.Resolver().ResolveOwnerAsync` answers `Directory.SecureNamedTeam`, `IsSecureOwner` true, while the create's owner was ChildTeam |
| A3 | `CreateAsync_AsTheSystem_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheEventsOrdinaryTeam_DI009Window` (`tests/integration/seam/RecordCreation/RecordCreationServicePipelineSeamTests.PreCreateStamp.cs`) | System principal: owner `Directory.ChildTeam` (not SecureNamedTeam); create body has no `sprk_RegardingMatter@odata.bind`; `_stamps.Lookup("sprk_event", id, "sprk_regardingmatter")` = SecureMatter (post-create re-derivation) |

Setup (A1/A2): `_world.WithRecord("sprk_event", Event, ChildBu, owningTeam: ChildTeam, extra: sprk_regardingmatter = SecureMatter)`, share world `OrdinaryChild(... SecureMatter)`, `StampWorld` rows for the Secure matter and the event (-> `sprk_regardingmatter = SecureMatter`). A3 uses the same shape with `ParentEvent`.

Scope: exactly the three characterizations. Two private helpers in the route file (`TheTeamOwnedEventIsFiledUnderTheSecureMatter`, `TheWrittenRow`) support A1/A2; no other additions. `ChatCreatedChildStampTests.cs` (ordinary control, ~:106) is untouched and green; no existing test changed.

## Premise check (escalation trigger 1)
A1 reproduces the window on current src: the create is owned by ChildTeam, not SecureNamedTeam. Design section 15.1 premise holds. No escalation.

## AC5 (negative) scratch check, never committed
Local scratch edit in `RecordCreationService.ResolveOwnerOverAsync`: for table `sprk_event`, concatenated `sprk_regardingmatter -> RecordOwnershipParent("sprk_matter", <route harness SecureMatter id>)` into the lookups handed to `OwnerQuestionOf` (emulates handing step 7 the derived stamp). Result with `--filter DI009Window`: 3 failed / 0 passed.
- A1: `Expected fields["ownerid@odata.bind"] to be "/teams(cf15f587-...)" ... but found "/teams(d9ec0b6f-0000-4000-8000-000000000003)"` (SecureNamedTeam).
- A2: `Expected Owner(fields) to be {cf15f587-...} ... but found {d9ec0b6f-...0003}`.
- A3: failed too (its harness SecureMatter id differs from the hard-coded one, so the create is refused; it fails for a scratch reason, not a clean owner flip. The real fix inverts it cleanly in 042b).
Reverted with `git checkout -- src/.../RecordCreationService.cs`; `git status` showed only the two test files afterwards.

## Test counts
- `--filter FullyQualifiedName~DI009Window`: 3 passed, 0 failed.
- AC7 suites (`SecureChildOwnershipAiToolTests` incl. `ChatCreatedChildStampTests` and RouteCreate partials, `RecordCreationServicePipelineSeamTests`): 576 passed, 0 failed (Sprk.Bff.Api.Tests, filtered).
- Before the change the three tests do not exist (0 -> 3).

## Section 15 vs the code
Nothing contradicted. Observed in line with section 15.1: a team-owned intermediate is taken at its own BU, step 10 binds the stamp into the body only, the reconciler's question includes the stamp. One note for 042b: the route harness's `Bind` helper in `SecureChildOwnershipAiToolTests.cs` reads only `JsonElement` values; a server-added stamp bind is a plain string, so assert it by direct dictionary access (as A1 does and as the existing step-10 test does).

## Publish delta / CVE
n/a, tests only, git diff shows no src/ path.

## Gates (task-execute Step 9.5)

**adr-check** (scope: the two test files; no `src/`): 0 violations, 0 warnings.
- ADR-038: both files sit on KEEP paths (`tests/integration/data-mutation/**`, `tests/integration/seam/**`); no new file under `tests/unit/Sprk.Bff.Api.Tests/**`; no `Mock<HttpMessageHandler>` (grep of the diff: 0); `Mock.Of<IGenericEntityService>()` is a module-boundary double, the same as `UnfiledCommunicationDocumentOwnerTests`; names follow Method_Scenario_Result.
- ADR-002 / ADR-008 path A: no production check changes; the tests observe the existing owner decision. ADR-001/007/009/010/013/028 and BFF hygiene (section 10): not touched (no `src/`, no csproj).
- `dotnet test tests/Spaarke.ArchTests` (full): 1898 passed, 0 failed.

**code-review** (standard depth): 0 Critical, 0 Warning, 3 Suggestions (all K4, no stateable failure scenario).
- Metrics: route file +93 lines (2 tests, 2 private helpers); seam file +29 lines (1 test). Cohesive: both stay one-feature characterization blocks inside existing partial classes. Step 6.6 (new surface): no new `src` file, endpoint, DI, package or column; two private test helpers only. Step 6.7: no POML changed.
- Smells: none of the five (no interface, no try/catch, no null-check, comments state why and name #2483/042b).
- Mutation proof: the AC5 scratch edit flips A1 and A2 (and A3) red; reverted.
- S1 (K4): `TheWrittenRow` repeats the bind-to-column parse of `MaterializeInShareWorld` (the same file family). Not merged because that helper writes into the share world and 042b may reshape it; left separate.
- S2 (K4): A1 asserts `Owner(fields).Should().NotBe(SecureNamedTeam)` after the exact-owner assertion; it is redundant by design, so 042b's inversion reads as one flip.
- S3 (K4): `Bind(...)` in the route harness reads only `JsonElement` values (a server-added stamp is a string); A1 uses direct access. Noted for 042b (above).
- Environment note: after the machine crash `Sprk.Bff.Api.Tests.deps.json` was a truncated zero-byte file; deleting it and `dotnet build --no-incremental` restored the build. Nothing committed from `bin/` or `obj/`.
