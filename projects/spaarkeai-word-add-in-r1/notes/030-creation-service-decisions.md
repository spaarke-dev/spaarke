# Task 030: server-side creation service (Matter): decisions

> **Task**: `tasks/030-creation-service-matter.poml` (FR-13, Matter only)
> **Date**: 2026-09-11. **Revised the same day** after the owner's decisions that **numbering moves out of task 030**, and that a missing, empty or unknown matter type is **never a rejection** (§7).
> **Author**: task-execute sub-agent (opus / xhigh)
> **PR**: #960. The main session pastes §3, §4 and §11 into the PR description.

---

## 1. The delta: what the wizard sets that the server path did not (POML step 1)

I re-located every cited line by symbol on 2026-09-11:
- `OfficeService.QuickCreateAsync`: declared at `:1424` (doc comment from `:1407`); it was `:1410` before this task's edits.
- `matterService.ts` numbering: `:255-273`.
- `EntityCreationService.applyUserBuDefaults`: `:383`.
- `FieldMappingService.applyFieldMappings`: `:110`.
- `FieldMappingEndpoints.PushFieldMappingsAsync`: `:436`.

| Field / behaviour | `CreateMatterWizard` (`matterService.createMatter`) | `QuickCreateAsync` before 030 | After 030 |
|---|---|---|---|
| `sprk_mattername` / `sprk_matterdescription` | ✅ | ✅ | ✅ |
| `sprk_matternumber` | client-side random `{code}-{6 digits}`, only when a type is chosen, with no uniqueness probe | ❌ | ❌ **by design**: left to a planned separate on-create numbering component (§6, `030-numbering-handoff.md`) |
| `sprk_mattertype` (lookup) | ✅ when chosen | ❌ | ✅ when `matterTypeId` is supplied **and exists**. Missing, empty or unknown → created without it, plus a warning. |
| `sprk_practicearea`, attorneys, paralegal, law firm | ✅ when chosen in the form | ❌ | ❌ (not in the pane; "+More fields" deferred, design.md §4.2) |
| BU cascade: `sprk_searchindexname` (INV-5) + `sprk_ai_search_index` | ✅ | ❌ | ✅ |
| `ownerid` | implicit (the Xrm caller) | best-effort | ✅ **load-bearing** (403 when unresolved) |
| Field Mapping Framework `{parent → sprk_matter}` | ✅ with an `association` parent | ❌ | ✅ from the optional record context |
| `sprk_containerid` | ❌ deliberately (task 076 W1) | ❌ | ❌ |

## 2. Live facts (read-only Dataverse Web API GETs against `spaarkedev1`, 2026-09-11)

The Dataverse MCP server was down, so I used an `az account get-access-token` bearer token, **GET only**. Nothing was written. The numbering facts moved to `030-numbering-handoff.md` §3. The facts relevant to task 030's remaining scope:

- `sprk_mattertype` is RequiredLevel **None** (optional), so a matter without a type is valid in Dataverse.
- `sprk_matter.sprk_searchindexname` (String) and `sprk_matter.sprk_ai_search_index` (Lookup → `sprk_aisearchindex`) exist and are valid for create.
- `sprk_fieldmappingprofile` has **no active profile whose target is Matter**. Active profiles run Matter → {Work Assignment, Event, Invoice, Report Card}. In dev, the mapping step is a live **graceful no-op** today.
- 🔴 **`sprk_matter.PrimaryNameAttribute` is `sprk_matternumber`**, and `sprk_project`'s is `sprk_projectnumber`. Both were verified with `GET EntityDefinitions(LogicalName='…')?$select=LogicalName,PrimaryNameAttribute,PrimaryIdAttribute`. A pane-created matter therefore has a **blank primary name** until the numbering component exists. That is flagged in the hand-off; it is not a task 030 defect.

## 3. Placement justification (root CLAUDE.md §10, `.claude/constraints/bff-extensions.md` §A.1)

**Decision: `Sprk.Bff.Api/Services/Office/RecordCreationService.cs` (plus the internal static `CreateTimeFieldMapping.cs`), inside the BFF.**

| Criterion (bff-extensions.md) | Answer |
|---|---|
| Latency/TTFB budget against BFF state? | **Yes.** It runs inline in `POST /api/office/quickcreate/{entityType}`. The pane waits for the 201 and auto-selects the new record as its Related-to. |
| Writes BFF-managed session/audit/safety state in the same request? | **Yes, the safety state.** Owner attribution, and the membership event the same handler publishes (`IMembershipEventPublisher`, R3 task 081), must describe the row this request created. |
| Retroactive annotation of a streaming response? | No, and not relevant. |
| Event-driven (timer/queue/webhook) with no synchronous user wait? | **No.** A user is waiting, so Azure Functions (ADR-001) is the wrong home. *Contrast*: the numbering component **is** create-triggered and event-shaped, which is one reason it was split out of this request path (§7). |
| Thin facade for EXTERNAL consumers? | No. |

**Why not `Spaarke.Core` or `Spaarke.Dataverse`**: those are base layers with no knowledge of Office request shapes, caller resolution (`ICallerSystemUserResolver`), or the BFF-side Field Mapping reader. The service composes existing BFF seams, `IGenericEntityService` and `IFieldMappingDataverseService`, and adds no new Dataverse client.

**Why not a new deployable**: it passes none of refined ADR-013's four extraction criteria (not AI, no independent scaling profile, shares the request's auth context).

**Why `Services/Office/`**: its only r1 caller is `OfficeService.QuickCreateAsync`, and design.md §7.1 defers wizard migration.

**Boundary preservation**:
- No AI dependency (ADR-013).
- Exactly one new DI registration: concrete `RecordCreationService` (ADR-010). `CreateTimeFieldMapping` is internal static and not registered.
- Authorization is an **endpoint filter** (ADR-008).
- No new NuGet package, and no new config field (§G not applicable).

## 4. Component justification: CLAUDE.md §11 three questions (reduced scope)

### `RecordCreationService` (new service; the one DI registration)

1. **Existing**: `OfficeService.QuickCreateAsync` creates the row, but with name, description and a best-effort owner only. `IFieldMappingDataverseService.GetFieldMappingProfileWithRulesAsync` is the server-side profile reader, and it is **reused**. `FieldMappingEndpoints.ApplyMappingRule` is update-shaped and Copy-only. No server-side create path applies owner, BU defaults and field mapping together (grep over `Services/`).
2. **Extension**: `QuickCreateAsync` stays the extension point, keeping its auth, idempotency, rate limit and membership event. The owner, BU, type and mapping logic is factored out of `OfficeService` (2,500 lines, 15 constructor dependencies) rather than inlined, so that task 031 and the post-r1 wizard-migration evaluation call one implementation.
3. **Cost of doing nothing**: a pane-created Matter is:
   - **owned by the application user** when caller resolution fails, and silently so;
   - created with no BU search-index routing;
   - created with no mapped fields and no matter type;

   That is the UAT incompleteness FR-13 exists to fix, minus the number, which the numbering project now owns.

### `QuickCreateSourceAccessFilter` (new endpoint filter; no DI registration)

1. **Existing**: `EntityAccessFilter` (Office save; `SaveRequest.TargetEntity`; demands AppendTo) and `RecordRouteAccessAuthorizationFilter` (route values). This filter reuses the same `CallerRecordAccessProbe`, the same `EntityAccessFilter.TryResolveEntitySet` table, and the existing `read` policy key. It adds **no** map and **no** policy key.
2. **Extension**: neither existing filter fits. Copying fields *from* a record needs Read, not AppendTo, and the context lives in the body, not the route. Parameterising either filter would change a shipped filter on a hot path that `unified-access-control-r2` and task 023 are live on.
3. **Cost of doing nothing**: `RecordCreationService` reads the source **app-only**. Without this check, any authenticated caller could name any record id and read its mapped fields back through a new matter they own: a cross-record data disclosure.

### `CreateTimeFieldMapping` (internal static helper; no DI registration)

This is extracted from `RecordCreationService` at the round-1 Step 9.5 review (§12, W9 / CLAUDE.md §11.5). It is not new behaviour.

1. **Existing**: the only other server-side rule applier, `FieldMappingEndpoints.ApplyMappingRule`, is Copy-only and update-shaped.
2. **Extension**: that applier cannot grow Default/Concat/Template without changing the push path, which the Field Mapping architecture doc says is unchanged.
3. **Cost of doing nothing**: the rule engine and the Matter creation rules would share one file with two reasons to change. Task 031 would grow it further.

## 5. Service contract (POML step 2)

- **Input** (`RecordCreationRequest`):
  - `EntityType` (Matter only; anything else returns `InvalidInput`)
  - `Name`, `Description?`
  - `CallerUserId` (oid, for logs)
  - `OwnerSystemUserId?`, resolved by the endpoint through the existing `ICallerSystemUserResolver`
  - `MatterTypeId?`
  - `SourceEntityLogicalName?` + `SourceRecordId?`: the record-context seam for task 012's resolver output. The pane does **not** send it yet; a client follow-up is needed.
- **Output** (`RecordCreationResult`): `RecordId`, `Name`, `Warnings[]`, **or** a `RecordCreationFailure { Kind, Code, Detail }`. Kinds are `InvalidInput` (400: blank name, wrong entity type) and `OwnerUnresolved` (403). A business outcome is never an exception.
- **Matter type**: one existence read (`RetrieveAsync` on `sprk_mattertype_ref`).
  - Found → the `sprk_mattertype` lookup is set.
  - **Not found** (Dataverse `ObjectDoesNotExist`, classified by the shared `RecordContainerResolver.IsRecordNotFound`) → created **without** the lookup, plus the warning *"The selected matter type was not found…"*.
  - **Read could not answer** (any other Dataverse fault) → also created **without** the lookup, plus a *distinct* warning (*"The selected matter type could not be checked…"*). The optional type never fails the create: a kept-but-unverified lookup that turned out to dangle would fault it with a 500. Corrected 2026-09-12 per the main session; the first rework kept the lookup here.
  - **Missing or `Guid.Empty`** → no read, created without a type, plus the warning *"No matter type was supplied…"*.
- **HTTP**:
  - `QuickCreateRequest` gains the optional `MatterTypeId`, `SourceEntityType` and `SourceRecordId`. `matterTypeId` is **not** validated: no 400 for missing or empty.
  - `QuickCreateResponse` gains `Warnings`. The `Number` field added in `0d53d3146` was **removed**, because nothing on this path populates it.
  - All of this is **additive**. The shipped pane sends `{ name }` and reads `{ id, logicalName, name }`.
- **`IOfficeService.QuickCreateAsync` signature is UNCHANGED.** The shared `Phase2EndToEndFixture.cs:444` mocks it. A structured refusal crosses the `OfficeService` → endpoint boundary as `SdapProblemException`, and the endpoint renders it in the Office ProblemDetails shape (`errorCode` + `correlationId`). `RecordCreationService` itself returns the failure as data.

## 6. Numbering: moved out of task 030

Numbering was built and tested in commit `0d53d3146` (probe-based, CSPRNG, at most 5 candidates, structured failures), then **removed** by the follow-up commit per the owner's decision (§7). Everything the future numbering project needs is in **`notes/030-numbering-handoff.md`**: format, type-code source, live facts, every create path, the concurrency window and the alternate-key recommendation, and a pointer to the reusable generator at `0d53d3146`.

What task 030 still guarantees: **the quick-create path never sends `sprk_matternumber`**. A field-mapping rule targeting it is skipped with a warning, whether it is Copy, Default, Concat or Template, and whatever its casing or padding. A contract test pins this with a case-insensitive key assertion.

## 7. Owner decisions (2026-09-11)

1. **Matter type is never a rejection.** "The type is required so should always be present; do not reject." The pane must always send `matterTypeId`; a separate client task, created by the main session, adds a required matter-type field to the pane. The server does **not** 400 when it is missing. It creates the record with a warning.
2. **Numbering.** The number must come from a server-side record-numbering component that triggers on create; it need not show in the add-in. That component does not exist (verified by the main session in the repo, on `origin/master` `e0a6f87c4`, and in dev Dataverse), so the owner is setting up a **separate project** to build it. **Not a Dataverse plugin.**
3. **Unknown matter type** (decided by the main session under decision 1's "do not reject" rule, recorded in the project CLAUDE.md Decisions, 2026-09-11). A supplied `matterTypeId` that does not resolve must be neither a 500 nor a 400. It is treated like a missing type: one existence read, and if the type does not exist the matter is created **without** the type lookup, plus a warning. **If the existence read itself fails** (a Dataverse fault other than not-found), the matter is **also** created without the type, plus a distinct warning. The optional type never fails the create.

   Contract tests: `Post_Matter_WithUnknownMatterType_Returns201_WithoutTheType_AndWarns` and `Post_Matter_WhenTheTypeCheckFails_CreatesWithoutTheType_AndWarnsDistinctly`.

   This replaced two alternatives: the 500 (after numbering was removed there was no type read, so a dangling lookup faulted the create), and the 400 `matter_type_not_found` that round 2 had recommended restoring, which the main session rejected as contrary to "do not reject".

   Warnings in `QuickCreateResponse.Warnings` are plain user-facing sentences, like every other warning on this path; there are no machine codes.

This resolves the 🔔 escalation raised earlier in this task: the name-only request could not meet the original AC1.

## 8. Deliberate deviations from the POML's letter

| POML says | Implemented | Why |
|---|---|---|
| Step 3 and AC1–AC3: generate the number, probe it, retry up to 5 times | **Not in task 030** (built in `0d53d3146`, then removed) | Owner decision §7.2. The numbering project owns it. |
| New file `IRecordCreationService.cs`; register `IRecordCreationService` | Concrete `RecordCreationService` registered as a concrete type; no interface | ADR-010: "MUST register concretes by default / MUST NOT create interfaces without genuine seam requirement". There is one implementation. §6.5 path C (comply). |
| Step 10: update TASK-INDEX | Not done | The main session owns TASK-INDEX, current-task and the project CLAUDE.md (dispatch boundary). |
| Owner "best-effort" (old) vs "load-bearing" (task) | An unresolved caller gives **403** `owner_unresolved` for **Matter**; no row is created | "Owner attribution here is load-bearing". Project and Invoice keep the old best-effort posture (task 031 decides Project). |
| Client field-mapping precedence (mapping overwrites whatever it targets) | Same, **except** `sprk_matternumber`, `ownerid` and `sprk_containerid` are never written by a rule; a blanked name or a non-matter-type `sprk_mattertype` value is reverted | Number: left to the numbering component. Owner: load-bearing. Container: server-derived only (task 076). The reverts stop one bad rule from failing the whole create. |

## 9. Known residuals (to report, not fixed here)

1. **Field-level security on the mapping source.** The source record is read **app-only** after the filter proves the caller holds **Read** on it (record level). App-only reads do not apply column-level (FLS) masking, so a secured column named in an admin-authored profile would be copied. An OBO read would close this, but the only OBO Dataverse reader (`IDataverseUserClient`) is AI-gated (a CRUD→AI dependency and an asymmetric registration). Recommend a follow-up if any mapping source column is FLS-secured.
2. **A valid type can be dropped on a transient read failure.** Because the optional type must never fail the create, an existence read that cannot answer drops the lookup, even when the id is in fact valid. The matter is created untyped with the warning *"…could not be checked…"*, and the user sets the type on the record. That trade is deliberate: an unverified lookup that turned out to dangle would fault the whole create with a 500.
3. **The pane sends neither `matterTypeId` nor the source context yet.** That needs a client follow-up: the matter-type field is being created by the main session, and the record context needs its own task. The client must send `cleanGuid` output: System.Text.Json fails a brace-wrapped GUID closed with a 400.
4. **Default-rule typing server-side.** The SDK needs exact CLR types. `Number` defaults parse as `int` when integral, otherwise `decimal`. A currency (`Money`) or float target given a Default literal could be rejected by Dataverse and fail the create, the same outcome as a bad payload from the wizard. There are no Matter-target profiles in dev today.
5. **The caller's Create privilege is not checked** (pre-existing; inherited). The create is app-only with `ownerid` = the caller, the same posture `QuickCreateAsync` already had for Matter, Project and Invoice. Closing it needs an OBO create or a caller-privilege probe, a separate decision.
6. **Short entity aliases are inert, not a bypass.** `EntityAccessFilter.TryResolveEntitySet` accepts `matter`/`project`/… as well as logical names. No `sprk_recordtype_ref` row has an alias name, so no profile matches and nothing is read. The contract is "a logical name"; rejecting aliases explicitly would need a fourth name table (CLAUDE.md §11).
7. **The filter runs before request validation.** A half-supplied context passes the filter and is rejected with a 400 by `QuickCreateFieldRequirements.Validate` before the service runs. A Project or Invoice request carrying a context is still probed, which is fail-safe and costs one extra OBO probe.
8. **The type and name reverts are case-sensitive key lookups** (round 2, S7; predates the rework). The *protected* set is case-insensitive, but `ResolveFinalMatterType` and `KeepRequestedNameIfMappingBlankedIt` look up the exact lower-case logical names. A rule that targets `SPRK_MATTERTYPE` would skip type validation. The likely result is a Dataverse fault (500) caused by an admin-authored mis-cased target, not a security issue.

## 10. Server-side vs client rendering differences (same profile, two creation paths)

`CreateTimeFieldMapping` mirrors `FieldMappingService.applyFieldMappings` in structure: one profile read, one source read, ordering, never throws, no same-entity guard. Its **values** differ where the SDK needs typed data and the client sends JSON:

| Case | Client engine (`Xrm.WebApi`) | Server (`CreateTimeFieldMapping`) |
|---|---|---|
| Copy into a Text/Memo target | raw source value | text: option set → **label**, boolean → Yes/No, date → ISO-8601 (`o`), lookup → name (the push path's `TransformValue` conventions) |
| Copy of a null source value | copies `null` | skipped (the SDK omits nulls; on a create the outcome is the same) |
| Default literal | the string as written | converted to the target type (OptionSet → `OptionSetValue`, Number → `int`/`decimal`, DateTime, Boolean), or skipped with a warning |
| Rule targeting `sprk_matternumber` / `ownerid` / `sprk_containerid` | applied | **skipped with a warning** |
| Mapping blanks `sprk_mattername`, or writes a non-matter-type value into `sprk_mattertype` | written; Dataverse rejects or accepts it | **reverted** with a warning; the requested value is kept |

## 11. Acceptance-criteria re-scope (owner decisions 2026-09-11)

| # | Original | Re-scoped |
|---|---|---|
| AC1 | number `^[A-Z]+-\d{6}$`, owner, BU defaults, mapped fields | **Owner (non-null, the caller) + BU defaults + every field the applicable profile defines + `sprk_mattertype` set when `matterTypeId` is supplied and exists.** A missing, empty or unknown type creates the matter (201) with a warning, never a 400 or 500. `sprk_matternumber` is never sent. |
| AC2 | collision → probe → retry, no duplicate | **Withdrawn** → numbering project (`030-numbering-handoff.md`) |
| AC3 | 5 retries exhausted → structured failure, no row | **Withdrawn** → numbering project |
| AC4 | no profile → graceful no-op, record still created with number and owner | **Kept, minus "number"**: created with owner (and type) |
| AC5–AC11 | unauthorized 401/403 · blank name 400 · no `Create*Wizard` change · one DI registration · publish size · Placement Justification · tests and build green | **Kept unchanged** |

## 12. Step 9.5 review dispositions

### Round 1: on `0d53d3146`'s working tree (independent reviewer + adr-check; no Critical findings)

| Finding | Action |
|---|---|
| W1: a null probe result was read as "number is free" | Fixed in `0d53d3146`. **Moot now**: numbering was removed; the fix lives in the reusable generator at that commit. |
| W2: fail-closed filter branches untested | Added tests: unmapped source type → 403 (value not echoed); probe throws → 403. |
| W6 / ADR-019: the filter's 403 lacked `errorCode`/`correlationId` | Fixed. `EntityAccessFilter`'s Office shape: `errorCode OFFICE_009`, `reasonCode`, `correlationId`. |
| W7 / ADR-032: an optional constructor parameter masked a missing registration as a 403 | Fixed. `RecordCreationService` is a **required** `OfficeService` dependency; no code constructs `OfficeService` directly (grep). |
| W9 / §11.5: two responsibilities in one file | Extracted `CreateTimeFieldMapping` (internal static; no DI registration). |
| S1, S2, S5, S6, S9, S10, S11 | Mapped-type validation. `sprk_containerid` protected. Name blanking reverted. No echo of caller input. Client abort propagates. Stale comments and the ADR-024 mis-citation fixed. Nullability noise removed. Test assertions moved out of the mock callback. |

Documented, not changed: W4 (Create privilege, §9.5), W5 (FLS, §9.1), W8 (`OfficeService` constructor growth, pre-existing), W10 (the PR description is the main session's job).

### Round 2: on the numbering-removal delta (independent reviewer + adr-check; **no Critical findings**)

The reviewer confirmed that no quick-create path can send `sprk_matternumber`: the protected-set check runs before the mapping-type switch, and it is `OrdinalIgnoreCase` over a trimmed target. It also confirmed that a missing `matterTypeId` always creates.

| Finding | Action |
|---|---|
| W1 / ADR-038 KEEP path: the deleted type-not-found test and the "remove when no type was requested" branch lost coverage | Fixed. Added `WithUnknownMatterType_Returns201…`, `WhenMappingWritesANonMatterTypeValue_AndNoTypeWasRequested_RemovesIt`, `WithEmptyGuidMatterType_IsTreatedAsNoType…` and `WhenTheTypeCheckCannotAnswer_KeepsTheChosenType`. |
| W2: the API description asserted a numbering component that does not exist yet | Fixed. The endpoint, `OfficeService`, `RecordCreationService` and `OfficeModule` text now says "a planned separate component; until it exists, matters created here have no number". |
| W3 / ADR-019 tension: a nonexistent type gave a 500 | Fixed **per §7.3**: created without the type, plus a warning (not the 400 the reviewer recommended). |
| W4: stale "Matter numbering (uniqueness-probed)" comment in `OfficeModule.cs` | Fixed. |
| S1: `Guid.Empty` was a 400 with a misleading "required fields are missing" detail | Fixed. `Guid.Empty` is treated as "no type" (201 + warning); the `Validate` rule was removed. |
| S2: case-insensitive protection untested; the assertion was a case-sensitive `Contains` | Fixed. Added Template (a padded, mis-cased `  SPRK_MatterNumber `) and Concat rules targeting the number. `AssertNoMatterNumberSent` checks every payload key case-insensitively after trimming. |
| S3: `AssertNoNumberingQuery` over-claimed and locked in implementation shape | Removed. |
| S5: "the pane always sends it" written as current fact | Fixed. "will always send (client task pending)". |
| S6: the ADR-044 remark overstated `Guid.TryParse` coverage | Fixed. It now names the one string GUID and states that body GUIDs are bound by System.Text.Json ("D" form only; braces → 400). |
| S7: type and name reverts are case-sensitive | Documented, §9.8. It predates the rework. |
| S8: §12 placeholder; §1 line citation | Fixed (this section; §1 cites `:1424`). |
| S9: `030-creation-service-matter.poml` still carries the numbering ACs; `031-….poml:53` refers to a "Matter numbering scheme" | **Main session's files.** Amend them, or cite §11 here in the PR. |
| S4, S10: tests match English warning text; `new HttpClient()` in the test factory is never disposed | Accepted (low risk; test-only). |
| Hand-off accuracy: `Models.cs:995` is `GetReferenceNumberField`, not a primary-name reader; "Spaarke does not use plugins" is too broad; §7 omitted `matter_type_lookup_failed` (503); §8 listed only Default and Copy | All corrected in `030-numbering-handoff.md`. |
