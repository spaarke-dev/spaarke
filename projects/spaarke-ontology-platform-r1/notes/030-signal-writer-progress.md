# Task 030 — Signal writer: progress, deviations, self-review findings

> Kept here per the task brief, NOT in `current-task.md` (main session's orchestrator checkpoint).
> Rigor FULL (POML-declared) · Sonnet @ effort high · steps mode directional.

> ## ⚠️ STALE — sections below this point through "Escalations (original implementation)" describe the
> ## FIRST DRAFT, superseded by the "Rework" sections further down (R11, second independent review).
>
> Specifically stale and **no longer true of the shipped code**: the writer was wrapped in an
> `IOntologyWriterDataverseClient` **interface** (deleted — ADR-010 Path C, F10); it used **`Upsert`** on
> `sprk_dedupekey` (replaced — create-first + reconcile-on-duplicate, F1/F4/F5/F6); ownership went through the
> existing **`IRecordOwnershipResolver`** TEAM resolver (removed — team ownership is CONFIRMED unworkable live,
> 403 `0x80040299`, for 59/62 matters; replaced by `owningbusinessunit`, F3/F22); and the "Owner privileges
> question" section's conclusion of **"no privilege change required"** is also superseded — the SECOND rework
> found a genuine privilege gap (F26: the writer lacks `AppendTo` on `sprk_communication`) that the first
> implementation's analysis did not surface, because it never actually ran live against a `sprk_communication`
> subject. Kept below (not deleted) as the audit trail of what changed and why; read the "Rework" section as
> the authoritative current state.

## Status (ORIGINAL — see STALE banner above)

Implementation complete. Not committed (main session commits). TASK-INDEX.md and the drift check are left
to the main session per the task brief; this POML's own `<status>`/`<completion>` are set by this task.

## Files

| File | Role |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/OntologyWriterCredentialFactory.cs` | New. Resolves the writer's dedicated UAMI credential from `Ontology:Writer:ManagedIdentityClientId`, fails closed. |
| `src/server/api/Sprk.Bff.Api/Services/Signals/IOntologyWriterDataverseClient.cs` | New. `IOntologyWriterDataverseClient` marker interface + `OntologyWriterDataverseClient` lazy adapter. |
| `src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs` | New. The writer itself. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SignalsModule.cs` | Modified. Added the two registrations above to the EXISTING `AddSignalsModule()` — no new DI module file. |
| `tests/unit/domain/Signals/SignalWriterTests.cs` | New. 19 unit tests, all passing. |
| `tests/integration/seam/Signals/SignalWriterSeamTests.cs` | New. Written, not run (see below). |

## Deviation 1 — test file locations differ from the POML's literal `<outputs>` paths

The POML names `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/SignalWriterTests.cs` and
`tests/integration/seam/SignalWriterSeamTests.cs`. Task 021 (the sibling predicate-compiler task) actually put
its tests at `tests/unit/domain/Signals/PredicateCompilerTests.cs` and
`tests/integration/seam/Signals/SignalPredicateTests.cs` — both compiled into the SAME `Sprk.Bff.Api.Tests.csproj`
via `Compile Include` globs (`..\domain\**\*.cs` / `..\..\integration\seam\**\*.cs`), per ADR-038 §2 KEEP-path
categories #6 (pure domain) and the ADR-043 seam category. I followed task 021's actual convention rather than
the POML's literal path, for consistency with the one existing sibling in this same domain. Verified both
globs exist in the csproj before writing; verified the test project builds and the new tests run.

## Deviation 2 — grouping-matter derivation is a closed, verified set of exactly 2 entries, not all 9

`SignalWriter.RegardingLookupByEntity` lists the 9 entity-specific regarding lookups that exist on `sprk_signal`
(verified live schema). `SignalWriter.VerifiedMatterDerivation` is DELIBERATELY smaller — `sprk_matter` (subject
IS the matter) and `sprk_communication` (`sprk_communication.sprk_regardingmatter`, the same lookup
`PredicateCompiler.VerifiedJoins` already relies on). The other 7 subject types (`sprk_project`, `sprk_invoice`,
`sprk_event`, `sprk_todo`, `sprk_workassignment`, `sprk_document`, `sprk_servicerequest`) ESCALATE
(`SignalWriterEscalationException`) rather than guessing an unverified lookup field name. This was not asked for
explicitly in those words, but follows directly from the POML's own escalation trigger and the
`PredicateCompiler` precedent (`EvaluatorGlobalReadableEntities`, `VerifiedJoins`) — a wrong-but-plausible field
name fails silently, which is exactly the failure mode both the escalation trigger and §0.3 exist to prevent.
Today's only PredicateCompiler-produced subject type is `sprk_matter` (Path B's subject), so this is not a live
gap — it is recorded so a future rule with a different subject type hits a loud escalation, not a silent wrong
write.

## Critical self-review finding, fixed before completion: a DI startup-crash hazard

**Found during the code-review/adr-check self-review pass, not by a test.** My first draft resolved
`OntologyWriterCredentialFactory.Create(configuration)` EAGERLY inside the `AddSingleton<IOntologyWriterDataverseClient>`
factory delegate. ASP.NET Core's `WebApplicationBuilder` defaults `ServiceProviderOptions.ValidateOnBuild = true`
in the `Development` environment, which resolves every registered singleton once at `Build()` time. Since the
factory's fail-closed throw fires whenever `Ontology:Writer:ManagedIdentityClientId` is unset — which is EVERY
environment today except a `spaarke-bff-dev` that has had the app setting added — this would have crashed the
whole BFF at startup for every local `dotnet run` in `Development`, and for `spaarke-bff-dev` itself until the
app setting is added post-deploy.

**Fix**: `OntologyWriterDataverseClient`'s constructor now takes a `Func<IGenericEntityService>`, wrapped in its
own `Lazy<IGenericEntityService>`. The credential resolution (and therefore the fail-closed throw) and the
`DataverseServiceClientImpl` construction are deferred until the FIRST actual Dataverse call — exactly the same
deferred-connect discipline `DataverseServiceClientImpl` itself already uses for its own `ServiceClient` (its own
XML doc cites the identical `ValidateOnBuild` SIGABRT hazard as the reason). Re-verified by rebuilding the BFF
(`dotnet build` — 0 errors) and re-running the 19 `SignalWriterTests` (all pass; the fakes implement the
interface directly, unaffected by the adapter's constructor signature change).

## Code-review / adr-check self-assessment (performed inline — see below for why)

I invoked the `code-review` and `adr-check` skills; both loaded their procedures into this turn rather than
running as separate agents (I am already the task-execute agent for this task, and the skills' own text says to
apply the checklist, not to re-delegate). I applied both procedures to the 6 files myself. Findings:

- **No Critical, no silent §6.5 violation.** The one startup-crash issue above was Critical-severity and is
  FIXED (see above), not left open.
- **ADR-010 (DI minimalism) — Path A documented exception.** `IOntologyWriterDataverseClient` has exactly one
  implementation, which Smell-1 / ADR-010 would normally flag. The seam is genuine and narrow: the container
  already holds ONE `IGenericEntityService` singleton (the shared sysadmin client, `GraphModule`); a second,
  separately-credentialed instance needs a DISTINCT type to avoid colliding with it. This is the same shape as
  `IDataverseService`'s existing narrow forwarding interfaces, except mine binds to a SEPARATE instance rather
  than forwarding to the same one. Documented in the interface's own XML doc (CLAUDE.md §11 three-question
  form) rather than left implicit.
- **ADR-028 — Path A documented exception, not a violation.** `OntologyWriterCredentialFactory` deliberately
  does NOT go through `ManagedIdentityCredentialFactory` (the existing single shared credential factory) because
  that factory is pinned to the SYSADMIN identity's config keys (`Graph:ManagedIdentity:ClientId` /
  `ManagedIdentity:ClientId`) — reusing it would authenticate the writer as sysadmin, the exact defect task 006
  exists to remove. It also deliberately uses `ManagedIdentityCredential` directly rather than
  `DefaultAzureCredential` (every other BFF site's pattern) — this is NOT an ad hoc deviation; it is the EXACT
  recipe `notes/006-writer-identity-plan.md` specifies (task 006's own owner-approved plan for how task 030
  would authenticate), chosen so there is no credential-chain fallback that could silently land on a different
  identity. Both are documented inline in the factory's own XML doc.
- **ADR-024 — compliant, with one documented adaptation.** The dual-field write (typed lookup + denormalized
  resolver fields, together) follows `TodoRegardingBuilder`'s shape. One field differs from that precedent by
  necessity: `sprk_signal.sprk_regardingrecordtype` is `NVARCHAR(100)` (plain text) on the LIVE schema, not a
  lookup to `sprk_recordtype_ref` as it is on `sprk_todo`/`sprk_communication` (verified via
  `describe('tables/sprk_signal')`, 2026-10-04). The writer stores the subject's own logical name there
  directly — documented in `SignalWriter`'s class remarks as a deliberate, schema-driven adaptation, not a
  shortcut.
- **ADR-002 / ADR-013 — compliant.** No plugin code; no AI-internal type injected anywhere in this task's files.
- **BFF hygiene (§10) — Placement Justification**: in BFF (`Services/Signals/`), because it is a Dataverse
  write-path component with no AI concern and no external-consumer facade need. Feature-module DI used
  (`AddSignalsModule()`, not flat `Program.cs` registrations). No new package added (`dotnet list package
  --vulnerable --include-transitive` on `Sprk.Bff.Api` — zero vulnerable packages). No new endpoint, no new
  background work.
- **Suggestion-level only**: `ArgumentNullException.ThrowIfNull`/`?? throw` guards on non-nullable reference
  types appear in the new files (e.g. `OntologyWriterCredentialFactory.Create`, `SignalWriter`'s constructor).
  This matches the EXISTING, repo-wide convention (every ctor in `TodoRegardingBuilder`, `RecordOwnershipResolver`,
  etc. does the same) rather than being a file-local regression, so I did not change it.

## Owner privileges question (constraint 4 / task 006 finding (a)) — ⚠️ STALE, see R11 banner at top

**This section's "No privilege change is required" conclusion is SUPERSEDED.** It analyzed only the
`sprk_matter`-subject path; the second rework's live seam run found F26 (the writer lacks `AppendTo` on
`sprk_communication`) by actually exercising the `sprk_communication`-subject path, which this section never
did. Kept for the matter-subject analysis, which remains accurate for THAT path.

**Does SignalWriter need anything beyond the union `Spaarke Ontology Service` + the root BU default team already
gives? No.** Walking every Dataverse call `SignalWriter.WriteAsync` makes:

| Call | Privilege needed | Source today |
|---|---|---|
| `RetrieveAsync(sprk_communication\|sprk_matter, …)` — grouping-matter derivation | Read, Global | Default team (both tables listed Global in `notes/security-roles.md` §9's effective-read table) |
| `UpsertAsync(sprk_signal)` — first write | Create, Assign | **`Spaarke Ontology Service` directly** (`prvCreatesprk_Signal`, `prvAssignsprk_Signal` — confirmed §7.4/§8.1/§9) |
| `UpsertAsync(sprk_signal)` — re-evaluation (existing dedupekey) | Write | Default team only (§9: "`Spaarke Ontology Service` is Create-only by design") |
| `UpdateAsync(sprk_signal, …sprk_firstdetected)` | Write | Default team only (same as above) |
| `IRecordOwnershipResolver.ResolveOwningTeamAsync` (team/BU/systemuser reads) | Read (org-owned tables) | **The SHARED sysadmin client**, not the writer — deliberately routed there (see SignalWriter's class remarks); consumes none of the writer's own privileges |
| `sprk_policy` / `sprk_policyversion` lookup binds | AppendTo | `Spaarke Ontology Service` directly (§8.1: `R ApTo` on both) |

Every operation is covered by the EXISTING union. **No privilege change is required for 030 to work.**

**Recommendation to the owner** (not made here — role edits are outward-facing and out of this task's scope):
fold `prvWritesprk_Signal` (Global) directly into `Spaarke Ontology Service`. The writer's Create+Assign already
sit on the dedicated role; its Write-on-re-evaluation sits only on the default team, a role whose actual
purpose is ordinary human console users and which could be edited for unrelated reasons without anyone thinking
to check its effect on the writer (task 006's own words: "changing the root default team's roles silently
changes what the writer can do"). Folding the one privilege in removes that latent dependency and makes the
writer's privilege set fully self-contained and auditable in the one place `notes/security-roles.md` §1 already
argues dedicated roles exist for. This is a narrow, additive, one-privilege role edit — not a redesign.

## Publish size

| Side | Source | Size | Files |
|---|---|---|---|
| master `62277d50a` | `C:\wt111m` (pre-existing short-path worktree, re-verified at the SAME commit as `origin/master` after `git fetch`) | 45.66 MB (47,875,712 B) | 212 |
| branch (this task) | `C:\code_files\spaarke-wt-spaarke-ontology-platform-r1` → published to short path `C:\wt030b` | 45.69 MB (47,905,189 B) | 212 |

**Delta: +0.03 MB.** Zip tool: PowerShell `Compress-Archive -CompressionLevel Optimal` (both sides, per
`azure-deployment.md`'s pinned method). File counts match exactly (212 = 212) — the "shape of a trustworthy
measurement" per CLAUDE.md §10.

**One honest limitation**: the BRANCH side was NOT published from a separate fresh short-path worktree (Hazard
THREE's full discipline), because this task's changes are uncommitted per the "do not commit" constraint, and
`git worktree add` only checks out committed refs — it cannot capture an uncommitted diff. I published from the
existing long-path task worktree instead (after no bin/obj deletion — this worktree had already built
successfully multiple times this task with 0 errors, including the just-run full test suite, so there is no
independent evidence of corrupted intermediate state). The matching 212/212 file count is the corroborating
signal that the build was complete, not partial. A fully fresh-worktree branch measurement can only happen after
the main session commits.

## Full BFF unit test suite — uncontended, final code

```
Passed! - Failed: 0, Passed: 14376, Skipped: 54, Total: 14430, Duration: 18m 41s
```

Run alone in this worktree (no other dotnet test/build command issued concurrently by this task). No other
agent's build was known to be running against this SAME worktree during the run. Consistent with the documented
~15–25 minute expectation and with the 14,409-total baseline task 021 reported (14,430 - 14,409 = 21 additional
tests is consistent with the 19 new `SignalWriterTests` plus incidental growth from concurrent main-session
commits already in this worktree).

## Dataverse CVE check

`dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: **no vulnerable packages.** No new
package was added by this task.

## Live schema sanity check (not the full seam — see below for why the seam itself is unrun)

Used the Dataverse MCP tools (NOT the C# seam harness) to verify the exact field SHAPE `SignalWriter.BuildEntity`
produces is accepted by live `spaarkedev1` schema, independent of proving the writer's own managed-identity auth
path. Created one row:

| Table | Id | Name | Status |
|---|---|---|---|
| `sprk_signal` | `ae5d9784-f2bf-f111-a05c-0022482913fc` | "ONTOLOGY DEV TEST 030 - writer schema sanity check" | **Created, then deleted** — 0 rows remain |

Fields written matched `SignalWriter`'s exact shape: `sprk_dedupekey` (text alt-key), `sprk_matter` +
`sprk_regardingmatter` (both lookups to the same matter), `sprk_regardingrecordid`/`recordtype`/`recordname`/
`recordurl` (text trio), `sprk_policy`/`sprk_policyversion` lookups, `sprk_policycode`, `sprk_lane` (100000000),
`sprk_severity` (100000000), `sprk_sentence`, `sprk_factsnapshot`, `sprk_evidencerefs`, `sprk_signalstatus`
(100000000), `sprk_firstdetected`, `sprk_lastevaluated`. **Accepted without error** — confirms the live schema
matches what the code assumes. `ownerid` was deliberately left unset for this sanity check (not testing
ownership here; MCP's own identity owns the row by default) — FR-14's owner-from-matter behavior is covered by
the unit tests instead, which assert the exact `ownerid` EntityReference written.

## Why the seam test (`SignalWriterSeamTests.cs`) is written but not run

Same workstation-credential limitation `SignalPredicateTests` (task 021) already documents, COMPOUNDED by one
additional constraint specific to this task: `Azure.Identity.ManagedIdentityCredential` (what
`OntologyWriterCredentialFactory` uses) only resolves inside an Azure-hosted process with an `IDENTITY_ENDPOINT`
— no workstation or sandboxed task-execution environment has one (task 006's own finding: not even the Kudu SCM
container did). So even with a working `DefaultAzureCredential` token, THIS task's seam harness could never
exercise the writer's actual credential-acquisition path from here — it is written to prove `SignalWriter`'s
write-side LOGIC against real schema (via an operator/impersonated connection, same convention as
`SignalPredicateTests.LiveDataverse`), while explicitly documenting that it does NOT and CANNOT prove the
managed-identity token path. **That is first and only provable after `spaarke-bff-dev` is next deployed** with
`Ontology:Writer:ManagedIdentityClientId` set, when `SignalsModule`'s `IOntologyWriterDataverseClient`
registration resolves on the evaluator's first real write (task 031/032). Per task 006's own escalation: if that
first acquisition fails, STOP — do not fall back to the shared sysadmin client.

## Escalations (original implementation)

None fired. No subject type outside `VerifiedMatterDerivation`'s 2 verified entries was exercised against live
data (the MCP sanity check and the unit tests both used `sprk_matter` and `sprk_communication`, both verified).

---

## Rework (2026-10-04) — independent review returned FAIL; disposition per finding

The independent review ran `code-review` + `adr-check` against the implementation above and verified several
claims LIVE as the writer (MSCRMCallerID impersonation, test rows deleted, 0 remain). This section is the
finding-by-finding disposition. POML `<status>` was set back to `in-progress` for the duration and returned to
`completed` only once every item below was closed or explicitly rejected with reason.

### Ownership (F3, F22) — FIXED, design change

**Finding**: setting `ownerid` to the grouping matter's default owner team returns HTTP 403 `0x80040299`
("Read Privilege Check For Owner failed") for 59 of 62 live matters — team ownership is unworkable, not merely
undesirable. Setting `owningbusinessunit` directly (owner left as the writer, the default) returns 204 and IS
read by `Spaarke Console User` at Parent:Child BU depth because the org has
`EnableOwnershipAcrossBusinessUnits = true`.

**Fix**: dropped `IRecordOwnershipResolver` (team resolution) entirely from this writer. `SignalWriter` now
reads the grouping matter's `owningbusinessunit` directly via the SHARED sysadmin `IGenericEntityService`
(F25 — a metadata read, not a Signal write, so the writer's own identity is not needed for it) and sets
`sprk_signal.owningbusinessunit` to that BU on CREATE, leaving `ownerid` untouched (defaults to the writer).
After create, the row is read back and `owningbusinessunit` compared to the intended BU; a mismatch throws
`SignalWriterEscalationException` rather than leaving a Signal silently in the wrong business unit.
**Deploy/provisioning dependency recorded**: a new environment must have `EnableOwnershipAcrossBusinessUnits`
enabled, or every create escalates. Documented in `appsettings.template.json`'s new `Ontology.Writer` comment,
in `design.md` §3.2's new Signal-writer row, and in `SignalWriter`'s own class remarks.

**Live-verified**: `WriteAsync_MatterSubject_CreatesThenReconciles_AndSetsOwningBusinessUnit` (seam test) passed
against real `spaarkedev1` data as the writer principal — create, re-evaluation reconcile, and
`owningbusinessunit` read-back all exercised for real.

### Create-first restructure (F1, F4, F5, F6) — FIXED

**Finding**: Upsert cannot both satisfy NOT NULL constraints on first write and avoid re-sending detection-time
facts on re-evaluation. Required: CreateAsync with the full payload; on duplicate key (`0x80060892`), update by
alternate key with ONLY `sprk_lastevaluated`; never re-send status/firstdetected/owner-or-owningbu/
policyversion/factsnapshot/sentence.

**Fix**: `WriteAsync` now always attempts `OntologyWriterDataverseClient.CreateAsync` with the full entity. On
`DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex)` (the EXISTING classifier `CreateCommunicationRaceProofAsync`
already uses for the identical fault — reused, not reinvented), it resolves the existing row via
`RetrieveByAlternateKeyAsync` and updates ONLY `{ sprk_lastevaluated }`. `sprk_firstdetected` is in the SAME
Create payload as everything else — one round trip, no window for a half-written row.

**Tests added** (`tests/unit/domain/Signals/SignalWriterTests.cs`):
- `WriteAsync_FirstRun_Creates_SetsFirstDetectedInTheSameCreateCall` — asserts `CreateCallCount == 1`.
- `WriteAsync_ConcurrentDuplicateKeyCreate_BecomesAnUpdate_NotAThrow` — simulates a lost create race; asserts
  reconciliation, not a thrown exception.
- `WriteAsync_ReEvaluation_UpdatesOnlyLastEvaluated_NeverResendsDetectionTimeFields` — asserts the update
  payload's key set is EXACTLY `{ sprk_lastevaluated }`.
- `WriteAsync_ReEvaluation_AResolvedSignalStaysResolved` — seeds an existing row with
  `sprk_signalstatus = Resolved`, re-evaluates, asserts the status is UNCHANGED.

**Noted in the writer's own class remarks** (per the rework instruction): re-fire-after-resolution semantics and
`Superseded`-on-version-change are the EVALUATOR's decision (task 031), never this writer's — its only
duplicate-key behavior is "touch lastevaluated".

### Credential guard (F7, F13, F14, F15) — FIXED, larger restructure

**Finding**: the writer factory must refuse unless the managed-identity path is actually taken; an injected
credential must be authoritative; a future change must not be able to route the writer through the
`TENANT_ID`/`API_APP_ID` branch as `SDAP-BFF-SPE-API`. The inner connection should retry after a transient
failure rather than caching the failure until restart.

**Fix**: `OntologyWriterDataverseClient` no longer wraps `DataverseServiceClientImpl` at all. It builds its own
`ServiceClient` directly (`BuildClient`), with `OntologyWriterCredentialFactory.Create` as the ONLY credential
call — there is no `Graph:ManagedIdentity:Enabled` branch, no `TENANT_ID`/`API_APP_ID` fallback, in this class's
code at all, so there is no path for a future change to silently route through them.
`Lazy<IOrganizationServiceAsync2>` uses `LazyThreadSafetyMode.PublicationOnly` (F15) — a failed connect attempt
is never cached; the next call re-runs the factory from scratch.

**Tests added**:
- `OntologyWriterDataverseClient_EmptyCredentialKey_Throws` — empty `Ontology:Writer:ManagedIdentityClientId`
  throws `InvalidOperationException` on first use.
- `OntologyWriterDataverseClient_NeverTakesTheSharedSysadminClientAsADependency` — reflection-based structural
  guard: no constructor parameter of type `IGenericEntityService`/`IDataverseService`/`DataverseServiceClientImpl`
  exists on the class, so there is no way to hand it the shared client even by accident.

**Documented**: the `Lazy`/`PublicationOnly` caching behavior is in `OntologyWriterDataverseClient`'s own XML
doc remarks.

### ADR-010 (F10) — FIXED, Path C (comply)

**Finding**: `IOntologyWriterDataverseClient` (single implementation) is an ADR-010 smell.

**Fix**: deleted the interface. `OntologyWriterDataverseClient` is now `public sealed`, registered as the
CONCRETE type in `SignalsModule`, and injected directly into `SignalWriter`'s constructor. The typed seam tests
use instead is the Dataverse SDK's OWN `IOrganizationServiceAsync2` (already used this way elsewhere in this
codebase — `Services/Dataverse/FetchService.cs`) via the class's `internal Func<IOrganizationServiceAsync2>`
constructor overload (`InternalsVisibleTo("Sprk.Bff.Api.Tests")`, the same testing-seam convention used
repo-wide — see e.g. `NoAccessListReader.cs`, `ExternalParticipationService.cs`). No interface of this
project's own exists in the DI graph for this class.

### ADR-028 (F11, F12) — kept `ManagedIdentityCredential`; logged as a PROPOSED tension, owner's call

Per the rework instruction: kept the design (see F7/F13/F14 above for why). Added a row to `spec.md` §6 "ADR
tensions", Path A, explicitly marked "PROPOSED, pending owner approval" — the owner is being asked separately;
this is not a self-approved exception.

### Section 0.3 (F8, F9) — FIXED, reworded to what is actually enforced

**Finding**: the doc should state exactly what is enforced; throw on any leftover token after rendering; throw
on a null fact value (don't render empty).

**Fix**: `RenderSentence`'s XML doc now states the exact three checks (token must be a key; that key's value
must be non-null; the rendered result must contain no leftover placeholder) instead of a paraphrase.
Implementation: (1) unchanged — missing key throws; (2) a null fact value now throws
`SignalSentenceTemplateException` instead of rendering an empty string; (3) after substitution, the rendered
string is re-scanned with the SAME token regex and throws if anything still matches (guards the case where a
SUBSTITUTED VALUE itself contains placeholder-shaped text).

**Tests added**: `RenderSentence_NullFactValue_Throws`,
`RenderSentence_SubstitutedValueLooksLikeALeftoverToken_Throws`.

**Noted for 031/022** (in `SignalWriter`'s class remarks, per the rework instruction): `FactValues` must be
exactly the compiled predicate's read set, never a superset; task 022's save-time rule-body validation should
reject a message-template token naming a field outside the rule body's own read fields, so a mismatch is caught
at authoring time.

### Tests and seam (F16, F17, F18, F19) — FIXED, seam RUN live

- **F16**: deleted the lowercase/no-braces unit test — tautological (`Guid.ToString("D")` is always lowercase
  regardless of input; the test asserted nothing the implementation could get wrong).
- **F17**: seam cleanup (`DisposeAsync`) now deletes via `LiveDataverse.OperatorClient` — NOT impersonated. The
  writer holds no Delete privilege on `sprk_signal`; a cleanup delete issued as the writer would itself fail.
- **F18**: the ownership/BU read (`SysadminAdapter` in the seam test, mirroring `SignalsModule`'s own split) now
  goes through `LiveDataverse.OperatorClient` — NOT impersonated.
- **F19**: the seam was RUN once as the writer via `MSCRMCallerID` (`WriterClient.CallerId`). Operator
  connection uses `AzureCliCredential` (not `DefaultAzureCredential`, which task 021 found fails to obtain a
  token on this workstation) — `az` was logged in as `ralph.schroeder@spaarke.com`. Result below.

#### Seam run result (2026-10-04, live against spaarkedev1)

Env: `SIGNALS_LIVE_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`,
`SIGNALS_LIVE_CALLER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc` (the writer principal).

| Test | Result | Notes |
|---|---|---|
| `WriteAsync_MatterSubject_CreatesThenReconciles_AndSetsOwningBusinessUnit` | PASS | Real create, real re-evaluation reconcile (Created=false, same SignalId), real owningbusinessunit read-back verification — all against live spaarkedev1 data, as the writer principal |
| `WriteAsync_CommunicationSubject_DerivesTheRealGroupingMatter` | FAIL — genuine new live finding, F26 | See below |

**F26 (NEW — found by this run, not previously known).** Creating a Signal whose subject is a
`sprk_communication` fails: "does not have AppendToAccess right(s) for record ... of entity Communication"
(access-check fault; `BusinessUnitLevelMinimumPrivilegeDepthRequiredRights = AppendToAccess`). Setting the typed
`sprk_regardingcommunication` lookup on create requires `AppendTo` on the TARGET `sprk_communication` row, which
the writer does not hold (neither directly on `Spaarke Ontology Service` nor via the root-BU default team).
**The matter-subject path is unaffected** — `sprk_matter` lookups succeeded live (204). **Why** is explicitly
NOT "AppendTo at Basic is not a blocker" — matter access comes from a grant under investigation
(`security-roles.md` §9.1; a separate, owner-led investigation, R6 of the second independent review, into why
the writer holds full rights on matters despite the role matrix showing only Basic AppendTo/Assign/Share). So
today's only PredicateCompiler-produced subject type (`sprk_matter`, Path B) is NOT blocked, for a reason not
yet fully explained. But `VerifiedMatterDerivation`/`RegardingLookupByEntity`'s
inclusion of `sprk_communication` as a supported subject type is currently privilege-blocked, not merely
untested. **Not fixed here** — fixing it means adding `AppendTo` on `sprk_communication` to
`Spaarke Ontology Service` (or the default team), which is a role edit and out of this task's scope ("don't
touch roles"). **Escalated to the owner**: recommend granting `AppendTo` on `sprk_communication` to
`Spaarke Ontology Service`, mirroring the existing `AppendTo` grants on `sprk_policy`/`sprk_policyversion` for
the same principal. Until granted, any future rule whose subject is `sprk_communication` will fail at write
time with this exact fault — a loud failure, not a silent one, but worth fixing before task 031/032 needs it.

Test row created by the passing test (`sprk_signal`, name "ONTOLOGY DEV TEST 030 - seam sanity check") was
deleted by `DisposeAsync` (operator client). The failing test created NO row (the server-side fault means
nothing was written). Verified via direct query after the run — `SELECT sprk_signalid, sprk_name FROM
sprk_signal WHERE sprk_name LIKE 'ONTOLOGY DEV TEST%'` — 0 rows.

### F2 — REFUTED, no code change

**Finding**: `sprk_Matter` (capital M) nav-property casing matters for raw Web API calls; lowercase gives a 400.

**Disposition**: not applicable to this code path. `SignalWriter` writes lookups via the SDK's typed
`Microsoft.Xrm.Sdk.EntityReference` through `Entity["sprk_regardingmatter"] = new EntityReference(...)`, which
the SDK resolves to the correct navigation-property casing internally regardless of how the dictionary key is
cased. The casing hazard is specific to hand-built Web API JSON (`@odata.bind`), which this writer never
constructs. Documented in `SignalWriter`'s class remarks so a future reader doesn't re-raise it as a risk to
this file.

### Small items

- **F21 (length guards)**: added — `PolicyCode` <= 50, `ShortHeadline` <= 200 (schema: `sprk_name`), rendered
  sentence <= 2000 (schema: `sprk_sentence`). All three throw `ArgumentException` before any Dataverse I/O.
- **F23 (appsettings template)**: added an `Ontology.Writer.ManagedIdentityClientId: null` key to
  `appsettings.template.json`, with a deploy-note comment covering the config key itself, the additive
  UAMI-attach + Dataverse-application-user prerequisite, and the `EnableOwnershipAcrossBusinessUnits` org
  setting dependency (F1/F22).
- **F24 (Placement Justification)**: added a "Signal writer" row to `design.md` §3.2's planned-components
  table, covering `SignalWriter` / `OntologyWriterDataverseClient` / `OntologyWriterCredentialFactory` together.

---

## Owner decisions (2026-10-04, folded into the rework)

### 1. ADR-028 path A — APPROVED

The owner approved the ADR-028 project-scoped exception the same day it was raised. `spec.md` §6's table row is
updated from "PROPOSED, pending owner approval" to **"APPROVED by owner 2026-10-04"**. No design change — the
approval confirms the existing implementation (`OntologyWriterCredentialFactory` / `OntologyWriterDataverseClient`
building their own `ManagedIdentityCredential`, never routing through the shared `ManagedIdentityCredentialFactory`
or through `DataverseServiceClientImpl`'s `TENANT_ID`/`API_APP_ID` branch).

### 2. No silent failure — observability added

Owner directive: the writer fails closed by design, so a broken credential or a refused write must be
VISIBLE, not indistinguishable from "no conditions found".

**New files**:
- `src/server/api/Sprk.Bff.Api/Telemetry/OntologyWriterTelemetry.cs` — `Counter<long>` `ontology.writer.failures`
  on a new `Sprk.Bff.Api.Ontology` Meter (the repo's EXISTING Meter-per-feature pattern — `CacheMetrics`,
  `FinanceTelemetry`, `CircuitBreakerRegistry` — no new telemetry package), dimensioned by a bounded-cardinality
  `reason` tag. `OntologyWriterFailureReason` holds the shared reason-string constants used by BOTH the metric
  dimension and the matching log's structured property, so a log query and a metric query always agree.
- `src/server/api/Sprk.Bff.Api/Services/Signals/OntologyWriterEvents.cs` — one stable `EventId`
  (`WriteRefused`, id `50300`) covering every refusal path; `reason` differentiates which one.

**Registration**: `TelemetryModule.cs` — added `metrics.AddMeter(OntologyWriterTelemetry.MeterName)`. Without
this the new counter would be silently dropped from the App Insights export, exactly the trap this same file's
own comments record for two earlier meters (Event Rules, Compose-save) that each shipped once unregistered.

**Where each case is covered**:

| Case | Where | Reason tag |
|---|---|---|
| Credential missing/unresolvable | `OntologyWriterDataverseClient.BuildClient` (credential factory call + the ServiceClient `IsReady` check) | `credential_unresolvable` |
| Token acquisition failed | `OntologyWriterDataverseClient.BuildClient`'s `tokenProviderFunction` lambda | `token_acquisition_failed` |
| Dataverse 403 / `0x80040220` / `0x80040299` | `SignalWriter.WriteAsync`'s outer catch, classified by the new `IsDataverseAccessDenied` (mirrors `DataverseServiceClientImpl.IsAlternateKeyDuplicate`'s typed-first/message-fallback style) | `dataverse_access_denied` |
| Owning-BU read-back mismatch | `SignalWriter.WriteAsync` (the F1/F22 verify-after-create step) | `owning_business_unit_mismatch` |
| Any `SignalWriterEscalationException` (matter-derivation-unverified, matter-lookup-empty, owning-BU-not-found, plus the mismatch above) | `SignalWriter.WriteAsync`'s outer catch, ONE place for all escalations so the EventId+reason+rethrow discipline cannot drift per throw site | each exception's own `Reason` property |

**Rethrow**: every catch block logs, records the metric, then `throw;` (or constructs-and-throws the same
exception it logged) — never swallowed. The duplicate-key reconcile path (CM-9's normal re-evaluation) is
explicitly NOT treated as a failure — it is the one case in `WriteAsync`'s inner try/catch that is NOT wrapped
by the new outer failure-logging catch, logged only at Information.

**No fact values or sentence content.** Every log call's structured properties are limited to `reason` and
`policyCode` — never `FactValues`, never the rendered sentence, matching the owner's explicit instruction.

**Tests added**: `WriteAsync_RefusedWrite_LogsWriteRefusedEventId_IncrementsFailureMetric_AndRethrows` (escalation
path; asserts the EventId, the `reason`/`policyCode` structured properties, the metric increment, the rethrow,
and that NO Information-level entry exists — i.e. nothing is logged as success) and
`WriteAsync_DataverseAccessDenied_IsClassified_LoggedAndMetered_AndRethrown` (reproduces F26's exact live fault
shape as a pure classification test). Both use `CapturingLogger<T>`/`LogEntry` — REUSED from
`Sprk.Bff.Api.Tests.Services.Communication.RungTestSupport.cs` (internal, same test assembly) rather than a new
fake logger — and a `MeterListener` scoped by an `AsyncLocal<Guid>` correlation token, the SAME pattern
`TenantCacheMetricsTests` (Infrastructure/Cache) already established and documented for this exact
process-global-static-Meter hazard (a naive unscoped listener double-counts under parallel test execution).

### Re-verification after the rework

- Full BFF unit suite: `Failed: 1, Passed: 14386, Skipped: 54, Total: 14441, Duration: 21m 41s`. The one failure
  (`EmailAttachmentProcessorTests.ShouldFilterAttachment_LogoPattern_ReturnsTrue(fileName: "logoSmall.jpg")`) is
  unrelated to this task (email-attachment filename-pattern filtering, nothing to do with Dataverse/Signals) and
  re-ran clean in isolation (3/3, 17 ms). A PRIOR full run on this same rework (before the observability
  changes) also showed exactly one unrelated failure, in a DIFFERENT test (an SPE-container 409 test) — two
  different unrelated single-test failures across two runs is the signature of contention, not a regression.
  No master-baseline run was taken to prove it per CLAUDE.md §10's letter; reported transparently as
  circumstantial-but-strong evidence (isolated-rerun pass) rather than a claim of certainty.
- Publish size: re-measured the same way (master unchanged at `62277d50a`, 45.66 MB, 212 files from
  `C:\wt111m`; branch republished to `C:\wt030b`, 212 files, 45.69 MB, delta +0.03 MB). Same honest caveat as
  the first measurement: the branch side is the uncommitted task worktree, not a separately-fresh short-path
  worktree, because `git worktree add` cannot capture an uncommitted diff. Matching 212/212 file counts remain
  the corroborating signal.
- CVE check: `dotnet list package --vulnerable --include-transitive` — clean, no new package added by the rework.

---

## Second independent review (2026-10-04) — pass with findings; disposition per R-number

Returned "pass with findings" against the FIRST rework above. POML `<status>` set back to `in-progress` for the
duration. Not committed.

| # | Finding | Disposition |
|---|---|---|
| R1 (High) | Wrong-BU row never re-checked on reconcile; a read-back throw after create was unlogged | **Fixed.** `RetrieveByAlternateKeyAsync` on reconcile now ALSO selects `owningbusinessunit` (no extra round trip); `EnsureOwningBusinessUnitMatches` runs on BOTH the create-verify and the reconcile path, via a new shared helper. A read-back that THROWS (on either path) is wrapped as `SignalWriterEscalationException(reason: OwningBusinessUnitReadBackFailed)` so it funnels through the SAME logging/metering/rethrow point. New tests: `WriteAsync_ReconcileFindsWrongBusinessUnit_Escalates_NotSuccess` (wrong BU on reconcile → escalates, zero `UpdateAsync` calls, not a quiet success) plus the existing create-path mismatch test. |
| R2 | Rendered sentence content leaked into the leftover-placeholder exception message; no `sentence_template_invalid` reason/logging | **Fixed.** The rendered string is no longer interpolated into that message. New reason `sentence_template_invalid`; `RenderSentence` is now called INSIDE the outer try, and a new `catch (SignalSentenceTemplateException)` clause logs (EventId 50300) + meters + rethrows, same as every other refusal. |
| R3 | `Lazy<T>(PublicationOnly)` lets a burst of concurrent failures each run (and log) the factory independently; no `IDisposable` | **Fixed.** Replaced with a double-checked `lock`-guarded field, cache-ON-SUCCESS-only — concurrent callers serialize onto ONE real attempt instead of racing N parallel ones. `OntologyWriterDataverseClient` is now `IDisposable` (disposes the built `ServiceClient`; DI disposes the singleton at shutdown). `BuildClient` disposes a not-ready `ServiceClient` before throwing. |
| R4 | spec.md's ADR-028 "Rule challenged" cell misstated the actual rule | **Fixed.** Rewritten to the literal text supplied (Constraints MUS, A4 App-only row, the single-shared-provider-governs-confidential-clients-only clarification) and the (C) alternative corrected to name `ManagedIdentityCredentialFactory` as "a `DefaultAzureCredential` factory keyed to the sysadmin UAMI". "APPROVED by owner 2026-10-04" marker kept. |
| R5 | Wrong org-setting name (`RecordOwnershipAcrossBusinessUnits`) | **Fixed** — renamed to `EnableOwnershipAcrossBusinessUnits` everywhere: `appsettings.template.json`, `SignalWriter.cs`, `OntologyWriterTelemetry.cs`, this file, the task POML. |
| R6 | Progress notes explained matter-lookup success as "AppendTo at Basic is not a blocker" | **Fixed** — reworded to "matter access comes from a grant under investigation (`security-roles.md` §9.1)" per the exact instruction; the "why" is left to the owner's own separate investigation, not asserted here. |
| R7 | No `connect_failed` reason distinct from `credential_unresolvable` | **Fixed** — added `OntologyWriterFailureReason.ConnectFailed`; the ServiceClient-not-ready branch now uses it (unless the failure was a token-acquisition failure, which keeps its own more specific reason). |
| R8 | Possible double log/meter for one token-provider failure during connect | **Fixed.** The Dataverse Client SDK swallows a `tokenProviderFunction` exception and surfaces it only via `IsReady`/`LastError` — it does not rethrow out of the `ServiceClient` constructor. A local flag set inside the lambda (before it logs+meters) tells the `IsReady` check to skip ITS OWN generic log for the SAME attempt. Exactly one log/meter per connect attempt; later per-call token refreshes (after `BuildClient` has already returned) are unaffected and log normally each time, since they never re-enter the `IsReady` check. |
| R9 | Test fakes used message-text-only exceptions, not typed `FaultException<OrganizationServiceFault>`; access-denied fake used the wrong code | **Fixed.** Both fakes now throw a typed `FaultException<OrganizationServiceFault>` with an explicit `ErrorCode` — `0x80060892` (duplicate key) and `0x80040220` (generic access-check failure, the correct code for an `AppendToAccess` denial — `0x80040299` is the DIFFERENT, more specific "Read Privilege Check For Owner failed" fault and was mislabelled in the first rework). This exercises the TYPED classification branch in both `IsAlternateKeyDuplicate` and `IsDataverseAccessDenied`, not just their message-text fallback. |
| R10 (seam) | Cleanup tracked only known ids; silent operator fallback if `SIGNALS_LIVE_CALLER_ID` unset; BU assertion compared the writer's own value to itself | **Fixed**, all three. `DisposeAsync` now sweeps by `sprk_policycode == TestPolicyCode` via a live query (catches orphans from a crashed or concurrent run). `LiveDataverse.InitializeAsync` now THROWS if `SIGNALS_LIVE_CALLER_ID` is unset whenever the URL env var is set (fail fast — never silently runs as the operator). The matter-subject test now reads the MATTER's `owningbusinessunit` independently (via the operator client) and asserts the Signal's BU against THAT value, with a separate sanity-check against the literal `cb15f587-baa0-f111-aaac-000d3a99d1d7` (the BU1 matter's BU). |
| R11 | Stale first-draft sections; wrong test count | **Fixed.** Added the STALE banner at the top of this file naming the four specific superseded claims (interface, Upsert, team resolver, "no privilege change required"), plus an inline pointer on the "Owner privileges question" section itself. Corrected test count: see "Full BFF unit suite" below for the actual final `dotnet test` count of `SignalWriterTests` (do not trust an earlier number in an earlier completion record — count the LATEST run). |
| R12 | No invariant-registry row for Signal ownership | **Fixed** — added I-12 (renumbered I-14, I-15, then I-16 on 2026-10-07/08, then I-17 on 2026-10-09 after master merges) to `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5, explicitly noting it differs from I-6 (team pattern) and why. |
| R13 | Check whether the writer must set `sprk_regardingrecordnumber` / `sprk_privilegeflagged` | **Checked — neither is set; documented as a gap, not silently skipped.** See "R13 disposition" below. |
| R14 | Unused `_logger` field on `OntologyWriterDataverseClient` | **Fixed** — now used in `Dispose()` (logs at Debug when the underlying client is actually disposed). |
| R15 | Optional | **Skipped** — not specified beyond "optional; skip unless trivial"; no R15 content was given to act on. |

### R13 disposition — `sprk_regardingrecordnumber` and `sprk_privilegeflagged`

**`sprk_regardingrecordnumber`**: ADR-024 (concise) states "MUST populate ALL 5 resolver fields when an
association is made" (extended 4 → 5 per SRFR-071) — `sprk_signal`'s live schema has the column
(`NVARCHAR(100)`, optional). This writer does **not** set it. Neither does the canonical server-side reference
this task was explicitly told to mirror: `TodoRegardingBuilder`'s own doc says "4 denormalized resolver fields"
and its code writes exactly four — `sprk_regardingrecordnumber` is not one of them. So this writer's omission
is consistent with its mirrored reference, not a unique gap, but it IS a real gap against ADR-024's literal
5-field MUST. `schema-draft.md` §1 names the column but does not assign its population to a specific task.
Populating it correctly needs the target record's business-key number, which (per ADR-024's own field
description) is normally metadata-resolved via `sprk_recordtype_ref.sprk_regardingrecordnumberfield` — I-shaped
new work, not something `SignalWriteRequest` carries today. **Not fixed here** — recommend either (a) adding
an optional `SubjectRecordNumber` to `SignalWriteRequest` for the evaluator (task 031) to supply when it
already has the value (e.g. it may already resolve the subject's display name/number as part of evaluation),
or (b) a standalone ADR-024-compliance fix applied to `TodoRegardingBuilder` AND `SignalWriter` together, since
fixing the gap twice independently would drift. Citations: `.claude/adr/ADR-024-polymorphic-resolver-pattern.md`
("MUST populate ALL 5"); `notes/schema-draft.md` §1; `TodoRegardingBuilder.cs`'s own XML doc ("4 denormalized
resolver fields").

**`sprk_privilegeflagged`**: `spec.md`'s only mention (the ADR-table row for ADR-015) describes the column as
"copied forward; nothing branches on it" — it does not assign the COPY to a specific task, and
`schema-draft.md` §1 lists it under "Carried, never acted on" with the same description. `SignalWriteRequest`
carries no field for it, and populating it would require the writer (or its caller) to know the SUBJECT's own
privilege-flagged status — a value the evaluator (task 031) already reads as part of predicate evaluation, not
something `SignalWriter` has an independent way to obtain today. **Not fixed here** — the natural owner is
task 031, which should pass a `SubjectPrivilegeFlagged` value through to `SignalWriteRequest` if/when that
field is added. Citations: `spec.md` ADR-015 row; `notes/schema-draft.md` §1 "Carried, never acted on".

### Verification after the second rework

- `dotnet build` on `Sprk.Bff.Api`: 0 errors / 0 warnings (both Debug and Release).
- `SignalWriterTests` (the LAST run, R11 — this is the number to trust, not an earlier draft's count):
  `Passed! - Failed: 0, Passed: 31, Skipped: 0, Total: 31, Duration: 115 ms`.
- Full BFF unit suite, uncontended: `Passed! - Failed: 0, Passed: 14388, Skipped: 54, Total: 14442, Duration: 20m 57s`.
  Clean — zero failures. This is the THIRD full-suite run across this task (two earlier runs each showed
  exactly one failure, in two DIFFERENT unrelated tests, both re-ran clean in isolation); this run's zero
  failures is further confirmation those were contention, not a regression from this task's code.
- Publish size: master unchanged (`62277d50a`, 45.66 MB, 212 files, `C:\wt111m`) vs branch (R2 final code)
  45.69 MB, 212 files — **delta +0.03 MB**, consistent with every prior measurement (no new package; R2's
  changes restructure existing files only).
- CVE check: `dotnet list package --vulnerable --include-transitive` — clean.
