# Task 030: server-side creation service (Matter): decisions

> **Task**: `tasks/030-creation-service-matter.poml` (FR-13, Matter only)
> **Date**: 2026-09-11
> **Author**: task-execute sub-agent (opus / xhigh)
> **PR**: #960. The main session pastes §3 and §4 into the PR description.

---

## 1. The delta: what the wizard sets that the server path did not (POML step 1)

I re-located every cited line by symbol on 2026-09-11. All of them still hold. `OfficeService.QuickCreateAsync` is still at `:1410`. `matterService.ts` numbering is still at `:255-273`. `EntityCreationService.applyUserBuDefaults` is at `:383`, `FieldMappingService.applyFieldMappings` at `:110`, and `FieldMappingEndpoints.PushFieldMappingsAsync` at `:436`.

| Field / behaviour | `CreateMatterWizard` (`matterService.createMatter`) | `OfficeService.QuickCreateAsync` before 030 |
|---|---|---|
| `sprk_mattername` | ✅ trimmed | ✅ trimmed |
| `sprk_matterdescription` | ✅ if non-empty | ✅ if non-empty |
| `sprk_matternumber` | ✅ `{sprk_mattertypecode}-{random 100000–999999}`, **only when a matter type was chosen**, with **no uniqueness probe** | ❌ never |
| `sprk_mattertype` (lookup) | ✅ when chosen | ❌ (the request cannot carry it) |
| `sprk_practicearea`, `sprk_assignedattorney1`, `sprk_assignedparalegal1`, `sprk_assignedlawfirm1` | ✅ when chosen in the form | ❌ (not in the pane; "+More fields" deferred, design.md §4.2) |
| BU cascade: `sprk_searchindexname` (INV-5 guarded) + `sprk_ai_search_index` lookup from the acting user's business unit | ✅ | ❌ |
| `ownerid` | implicit (the Xrm caller creates the row) | best-effort (`ownerid` set only when the caller resolved; otherwise app-owned) |
| Field Mapping Framework `{parent → sprk_matter}` | ✅ `applyFieldMappings` when an `association` parent is supplied | ❌ |
| `sprk_containerid` | ❌ deliberately **not** written (task 076, W1) | ❌ (correct) |

**Task 030 scope** = number, type lookup, owner (load-bearing), BU cascade, field mapping. Practice area and attorneys stay out: they are form fields the pane does not collect, and design.md §4.2 defers them.

## 2. Live facts (read-only Dataverse Web API GETs against `spaarkedev1`, 2026-09-11)

The Dataverse MCP server was down. I used an `az account get-access-token` bearer token for **GET only**. Nothing was written.

- `sprk_mattertype_ref` has 5 active rows: `LITG`, `CMRCL`, `PAT`, `TMRK`, `EMPL`. Every code matches `^[A-Z]+$`.
- `sprk_matter` has 59 rows, 59 of them with a number and 0 null. There are **0 duplicate `sprk_matternumber` values**.
  - 53 of the 59 match `^[A-Z]+-\d{6}$`.
  - The other 6 are legacy/hand-entered values (`asasf`, `REAL-2026-123456.02`, `COPR-2026-123456.01`, `LIT-2025-0847`, `REAL-2026-123456.01`, `Form D - 2023`). None of them can collide with a generated `{CODE}-{6 digits}` value.
- `sprk_matternumber` is String, max length 100, RequiredLevel `Recommended`, and **not** an autonumber (`AutoNumberFormat` is empty).
- `EntityDefinitions('sprk_matter')/Keys` is empty, so **no alternate key** exists on `sprk_matter`.
- `sprk_mattertype` is RequiredLevel **None** (optional).
- `sprk_matter.sprk_searchindexname` (String) and `sprk_matter.sprk_ai_search_index` (Lookup → `sprk_aisearchindex`) both exist and are valid for create.
- `sprk_fieldmappingprofile` has **no active profile whose target is Matter**. Active profiles run Matter → {Work Assignment, Event, Invoice, Report Card}. In dev, the mapping step is therefore a live **graceful no-op** today.
- `sprk_matternumber` uniqueness could only be checked in **dev**. Production data was not reachable, so the escalation trigger's "not unique in production" clause is **unverified** (see §7).

## 3. Placement justification (root CLAUDE.md §10, `.claude/constraints/bff-extensions.md` §A.1)

**Decision: `Sprk.Bff.Api/Services/Office/RecordCreationService.cs`, inside the BFF.**

| Criterion (bff-extensions.md) | Answer |
|---|---|
| Latency/TTFB budget against BFF state? | **Yes.** It runs inline in `POST /api/office/quickcreate/{entityType}`. The pane waits for the 201 and auto-selects the new record as its Related-to. |
| Writes BFF-managed session/audit/safety state in the same request? | **Yes, for the safety state.** Owner attribution and the membership event published by the same handler (`IMembershipEventPublisher`, R3 task 081) must describe the row this request created. |
| Retroactive annotation of a streaming response? | No, and not relevant. |
| Event-driven (timer/queue/webhook) with no synchronous user wait? | **No.** A user is waiting on it, so Azure Functions (ADR-001) is the wrong home. |
| Thin facade for EXTERNAL consumers? | No. |

**Why not `Spaarke.Core` or `Spaarke.Dataverse`**: those are base layers with no knowledge of Office request shapes, caller resolution (`ICallerSystemUserResolver`), or the Field Mapping Framework's BFF-side reader. The service composes existing BFF-side seams, `IGenericEntityService` and `IFieldMappingDataverseService`, and adds no new Dataverse client. Pushing it down would either drag BFF concepts into the base layer or force a second abstraction.

**Why not a new deployable**: it passes none of refined ADR-013's four extraction criteria. It is not AI, has no independent scaling profile, and shares the request's auth and transaction context.

**Why `Services/Office/` and not `Services/Dataverse/`**: its only caller in r1 is `OfficeService.QuickCreateAsync`, and design.md §7.1 defers wizard migration. If the post-r1 evaluation migrates the wizards, the file moves then, with the evidence.

**Boundary preservation**:
- No AI dependency of any kind (ADR-013), so `PublicContracts` is not needed.
- Exactly one new DI registration: `RecordCreationService` as a **concrete** type, following ADR-010.
- Authorization is an **endpoint filter** (ADR-008), not handler code.
- No new NuGet package.

**Config homes (§A.6 / §G)**: no new config field, column, or JSON property is added, so §G does not apply.

## 4. Component justification: CLAUDE.md §11 three questions

### `RecordCreationService` (new service; the one DI registration)

1. **Existing**: `Grep "sprk_matternumber" src/server` finds no server writer. `Grep "RandomNumberGenerator|matternumber" src/server/api/Sprk.Bff.Api/Services` finds no numbering service. The closest neighbours:
   - `OfficeService.QuickCreateAsync` creates the row but writes only name, description and a best-effort owner.
   - `FieldMappingEndpoints.PushFieldMappingsAsync` / `ApplyMappingRule` is a real server-side mapping engine, but it is parent → *existing* children, Copy only, and runs through `UpdateRecordFieldsAsync`.
   - `IFieldMappingDataverseService.GetFieldMappingProfileWithRulesAsync` is the server-side **profile reader**, and it is **reused**, not duplicated.
2. **Extension**: `QuickCreateAsync` stays the extension point, keeping its auth, idempotency, rate limit and membership event. The numbering, owner, BU and mapping logic cannot be added to `ApplyMappingRule`, because that helper is private to an endpoint class, update-shaped, and Copy-only. Growing it into Default/Concat/Template would change the push path's behaviour, which the Field Mapping architecture doc says stays unchanged. The logic is factored out of `OfficeService` rather than inlined, so that task 031 (Project) and the post-r1 wizard-migration evaluation call one implementation instead of forking a third.
3. **Cost of doing nothing**: a Matter created from the task pane is written with `sprk_matternumber` empty, no BU search-index routing, no mapped fields, and an owner that silently falls back to the application user. That is the UAT defect FR-13 exists to fix. The wizard's number cannot be reused because `Xrm` does not exist in an Office host (NFR-03).

### `QuickCreateSourceAccessFilter` (new endpoint filter; no DI registration)

1. **Existing**: `EntityAccessFilter` (Office save; reads `SaveRequest.TargetEntity`; demands `entity.associate_document` = AppendTo). `RecordRouteAccessAuthorizationFilter` (record-keyed upload; reads **route values**). Both rest on `CallerRecordAccessProbe` + `EntityAccessFilter.TryResolveEntitySet` + `OperationAccessPolicy`, and so does this filter. It adds **no** entity-set map and **no** policy key.
2. **Extension**: neither existing filter fits.
   - `EntityAccessFilter` demands AppendTo with a "file documents against this record" message, but copying fields *from* a record needs Read. It also sits on the Office save hot path that `unified-access-control-r2` and task 023 are live on.
   - `RecordRouteAccessAuthorizationFilter` reads route values, and the quick-create source context is in the body.
   - Parameterising either would change a shipped filter's behaviour for a caller this task does not own. `RecordRouteAccessAuthorizationFilter` is itself the precedent for "a variant filter over the shared probe, not a fourth map".
3. **Cost of doing nothing**: the creation service reads the source record **app-only**, and without this check any authenticated caller could name *any* record id. The admin-configured mapped fields would then be copied into a new matter the caller **owns** and can therefore read: a cross-record data disclosure. The client engine never had this problem because it reads through `Xrm.WebApi` as the user.

### `CreateTimeFieldMapping` (internal static helper; no DI registration)

This is extracted from `RecordCreationService` at the Step 9.5 review (§11 W9, CLAUDE.md §11.5). It is not new behaviour.

1. **Existing**: the only other server-side rule applier is `FieldMappingEndpoints.ApplyMappingRule`. It is Copy-only and update-shaped (parent → *existing* children).
2. **Extension**: that applier cannot grow Default/Concat/Template without changing the push path, which the Field Mapping architecture doc says is unchanged.
3. **Cost of doing nothing**: the rule engine and the Matter invariants would share one ~940-line file, with two reasons to change. Task 031 would grow it further.

## 5. Service contract (POML step 2)

- **Input** (`RecordCreationRequest`):
  - `EntityType` (Matter only; any other value returns `InvalidInput`)
  - `Name`, `Description?`
  - `CallerUserId` (oid, for logs)
  - `OwnerSystemUserId?` (resolved by the endpoint through the existing `ICallerSystemUserResolver`)
  - `MatterTypeId?`
  - `SourceEntityLogicalName?` + `SourceRecordId?`: the record-context seam for task 012's resolver output. The pane does **not** send it yet; a client follow-up is needed.
- **Output** (`RecordCreationResult`): `RecordId`, `Number?`, `Name`, `Warnings[]`, **or** a `RecordCreationFailure { Kind, Code, Detail }`. The service never throws for a business outcome.
- **HTTP**: `QuickCreateRequest` gains three optional properties: `MatterTypeId`, `SourceEntityType`, `SourceRecordId`. `QuickCreateResponse` gains `Number` and `Warnings`. Both changes are **additive**. The shipped pane sends `{ name }` and reads `{ id, logicalName, name }`, so it is unaffected.
- **`IOfficeService.QuickCreateAsync` signature is UNCHANGED.** Changing it would break the shared `Phase2EndToEndFixture.cs:444` mock, which this task must not edit. A structured failure therefore crosses the `OfficeService` → endpoint boundary as the codebase's typed-problem type, `SdapProblemException`. The endpoint handler renders it in the Office ProblemDetails shape (`errorCode` + `correlationId`) rather than letting the generic `catch` turn it into a 500. `RecordCreationService` itself still returns a structured result, which is the contract task 031 consumes.

## 6. Numbering scheme (POML step 3)

The steps, in order:
1. Resolve the matter type code from `sprk_mattertype_ref.sprk_mattertypecode`. The type comes from `MatterTypeId`, or from a type that field mapping wrote. Trim the code. It must match `^[A-Z]+$`; otherwise the result is a structured failure (`matter_type_code_unusable`), and an off-format value is never written.
2. Build the candidate `{code}-{n}`, where `n = RandomNumberGenerator.GetInt32(100000, 1000000)`. That is the same 6-digit range as the client, drawn from a CSPRNG.
3. Probe with `IGenericEntityService.RetrieveMultipleAsync(QueryExpression sprk_matter, ColumnSet sprk_matterid, TopCount 1, sprk_matternumber Equal candidate)`. This is a typed condition with **no OData string interpolation** (ADR-044 / the POML constraint).
4. The probe is attempted **at most 5 times**. Five collisions give `matter_number_unavailable` (HTTP 409), and **no row is created**. A probe *error* gives `matter_number_probe_failed` (HTTP 503), and no row is created either: an unverified value is never written.
5. Order of operations: field mapping runs **before** numbering, so that the number's type code matches the `sprk_mattertype` actually written. `sprk_matternumber` and `ownerid` are then set **last**, and mapping may not overwrite them; a rule that targets either is skipped with a warning.

**Documented residual (not closed here)**: probe-then-create has a TOCTOU window. Two concurrent creates of the same type would each need to draw the same 1-in-900,000 value inside the same few milliseconds. Closing it outright needs a Dataverse alternate key on `sprk_matternumber`, which is a schema change outside this task. It is also only possible because the 6 legacy values are unique today. Flagged for the owner.

**Interpretation note**: "retry up to 5 times" / "exhausts 5 retries" is implemented as **5 probed candidates in total**.

## 7. Open decision: escalated to main (🔔)

**Matter-type source for name-only requests.** The shipped pane POSTs `{ name }` only (`SaveFlow.tsx:567`). The only defined format, `{typeCode}-{6 digits}`, needs a type code, and the wizard itself numbers only `if (form.matterTypeId)`. AC1 ("a valid Matter name gives a number") therefore cannot be met from a name alone without either:
- (a) a pane matter-type picker (a client follow-up), or
- (b) a configured default type.

Inventing a prefix is forbidden by the escalation trigger. **Interim behaviour implemented** (status quo for this sub-case): a name-only Matter is created **without** a number, with owner, BU defaults and mapping applied, and a warning in `QuickCreateResponse.Warnings`. A request carrying `MatterTypeId` is fully numbered. The recommendation was sent to main: option 1, with AC1 re-read as "when a matter type is supplied".

## 8. Deliberate deviations from the POML's letter

| POML says | Implemented | Why |
|---|---|---|
| New file `IRecordCreationService.cs`; register `IRecordCreationService` | Concrete `RecordCreationService` registered as a concrete type; no interface | ADR-010: "MUST register concretes by default / MUST NOT create interfaces without genuine seam requirement". There is exactly one implementation, and task 031 adds a method, not an implementation. The contract test substitutes the Dataverse boundary (`IGenericEntityService`), not the service. §6.5 path C (comply). |
| Step 10: update TASK-INDEX | Not done | Main session owns TASK-INDEX / current-task / project CLAUDE.md (dispatch boundary). |
| Owner "best-effort" (old) vs "load-bearing" (task) | An unresolved caller now gives **403** `owner_unresolved` for **Matter**, with no row created | "Owner attribution here is load-bearing". Project and Invoice keep the old best-effort posture: they are out of scope, and task 031 decides Project. |
| Client field-mapping precedence (mapping overwrites everything it targets, including the number) | Mapping overwrites the same fields as the client **except** `sprk_matternumber` and `ownerid` | These two invariants are this task's reason for existing. A mapping rule must not bypass the uniqueness probe or the load-bearing owner. |

## 9. Known residuals (to report, not fixed here)

1. **Field-level security on the mapping source.** The source record is read **app-only** after the filter proves the caller holds **Read** on it (record-level). App-only reads do not apply column-level (FLS) masking, so a column secured from the caller but named in an admin-authored profile would be copied. The client engine reads as the user, where FLS applies. An OBO read would close this, but the only OBO Dataverse reader (`IDataverseUserClient`) is AI-gated (a CRUD→AI dependency plus asymmetric registration). Recommend a follow-up if any mapping source column is FLS-secured.
2. **Probe/create TOCTOU** (§6).
3. **The pane sends neither `MatterTypeId` nor the source context yet.** That needs a client follow-up (not in 030's scope: `src/client/office-addins/**` is task 013's lane in this wave).
4. **Default-rule typing server-side.** The SDK needs exact CLR types. `Number` defaults parse as `int` when integral, otherwise `decimal`. A currency (`Money`) or float target given a Default literal could be rejected by Dataverse and fail the create, the same outcome as a bad payload from the wizard. There are no Matter-target profiles in dev today.
5. **The caller's Create privilege is not checked** (pre-existing; task 030 inherits it). The create is app-only with `ownerid` = the caller, which is the same posture `QuickCreateAsync` already had for Matter, Project and Invoice. A user without Create on `sprk_matter` still gets a matter. Closing this needs an OBO create or a caller-privilege probe, a separate decision.
6. **Short entity aliases are inert, not a bypass.** `EntityAccessFilter.TryResolveEntitySet` accepts `matter`/`project`/… as well as logical names, so `sourceEntityType: "project"` passes the filter against the *same* collection. Downstream, though, no `sprk_recordtype_ref` row has that name, so no profile matches and nothing is read. The contract is "a logical name". Rejecting aliases explicitly would need a fourth name table (CLAUDE.md §11), so it was not done.
7. **The filter runs before request validation.** A half-supplied context passes the filter and is rejected with a 400 by `QuickCreateFieldRequirements.Validate` before the service runs. A Project or Invoice request that carries a source context is still probed, although those paths never read it. That is fail-safe and costs one extra OBO probe; clients do not send one.

## 10. Server-side vs client rendering differences (same profile, two creation paths)

`CreateTimeFieldMapping` mirrors `FieldMappingService.applyFieldMappings` in structure (one profile read, one source read, ordering, never throws, no same-entity guard). Its **values** differ where the SDK needs typed data and the client sends JSON:

| Case | Client engine (`Xrm.WebApi`) | Server (`CreateTimeFieldMapping`) |
|---|---|---|
| Copy into a Text/Memo target | raw source value | text: option set → **label**, boolean → Yes/No, date → ISO-8601 (`o`), lookup → name (the push path's `TransformValue` conventions) |
| Copy of a null source value | copies `null` | skipped (the SDK omits nulls; on a create the outcome is the same) |
| Default literal | the string as written | converted to the target type (OptionSet → `OptionSetValue`, Number → `int`/`decimal`, DateTime, Boolean), or skipped with a warning |
| Rule targeting `sprk_matternumber` / `ownerid` / `sprk_containerid` | applied (can overwrite the number) | **skipped with a warning** (§8) |
| Mapping blanks `sprk_mattername`, or writes a non-matter-type value into `sprk_mattertype` | written; Dataverse rejects or accepts it | **reverted** with a warning; the requested value is kept |

Status codes chosen for refusals: 400 for bad input (`matter_type_not_found`). 403 for `owner_unresolved` (the caller cannot own records here). 409 for `matter_number_unavailable` and `matter_type_code_unusable`: the second is arguably a configuration error, and 409 keeps it out of the 5xx alerting a transient fault gets. 503 for `matter_type_lookup_failed` and `matter_number_probe_failed` (retryable).

## 11. Step 9.5 review disposition (independent reviewer + adr-check)

No Critical findings. What I changed after the review (path C, comply):

| Finding | Action |
|---|---|
| W1: a null probe result was read as "number is free" | Fixed. A missing result throws into the probe-failure path (503); a test pins it. |
| W2: fail-closed filter branches untested | Added tests: unmapped source type → 403 (and the value is not echoed); probe throws → 403; `matter_type_not_found` → 400; mapped-type numbering; non-matter-type mapping reverted; blanked name kept; null probe result → 503. |
| W6 / ADR-019: the filter's 403 lacked `errorCode`/`correlationId` | Fixed. The 403 now uses `EntityAccessFilter`'s Office shape: `errorCode OFFICE_009`, `reasonCode`, `correlationId`. |
| W7 / ADR-032: an optional constructor parameter masked a missing registration as a 403 | Fixed. `RecordCreationService` is a **required** `OfficeService` dependency; no test constructs `OfficeService` directly (grep). |
| W9 / §11.5: two responsibilities in one 940-line file | Extracted the pure rule engine into `CreateTimeFieldMapping` (internal static; **no DI registration**). The service keeps the I/O and the Matter invariants. |
| S1, S2, S5, S6, S9, S10, S11 | Mapped-type validation. `sprk_containerid` protected. Name blanking reverted. No echo of caller input in the 403. Client abort propagates. Stale comments and the ADR-024 mis-citation fixed. Nullability noise removed where the types guarantee non-null. Test assertions moved out of the mock callback. |

Documented, not changed: W3 (AC1 name-only, escalated, §7), W4 (Create privilege, §9.5), W5 (FLS, §9.1), W8 (`OfficeService` constructor growth, pre-existing; this change adds no constructor parameter beyond the one task 030 needs), W10 (the Placement Justification goes into PR #960 via the main session; this note is its source).
