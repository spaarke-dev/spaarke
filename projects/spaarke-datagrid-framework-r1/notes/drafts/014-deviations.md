# Task 014 — Deviations from POML

> **Status**: Filed at task completion (2026-06-01)
> **Owner**: task-execute (014)
> **Scope**: Differences between authored implementation and the POML / task-010 design notes.

---

## D-014-01 — Default column projection requires a metadata round-trip

**Spec reference**: Task 014 brief — "If `selectFields == null` or empty, default to primary id + primary name only".

**Spec implies**: A simple sentinel that fetches only the two columns.

**Implementation actually does**: When `selectFields` is null/empty, `RecordService.ResolveDefaultColumnsAsync`
issues a `RetrieveEntityRequest` (with `EntityFilters.Entity`) to look up `PrimaryIdAttribute` +
`PrimaryNameAttribute` from Dataverse metadata, then projects only those two attributes.

**Why deviated**: The shared `IGenericEntityService.RetrieveAsync(entityLogicalName, id, columns, ct)`
contract requires explicit column names — there is no "primary attributes" sentinel. Resolving the
primary attribute names without the metadata round-trip would require either (a) caller-side knowledge
of every entity's primary attribute (brittle) or (b) hard-coded conventions like `{prefix}_name` (also
brittle — some entities use `name`, others use schema-specific patterns).

The metadata round-trip is a single, cheap call. Per FR-BFF-05 we do NOT cache the record itself, but
the metadata lookup is bounded and most callers will supply `$select` (so this path is the exception).
The pattern matches task 011's `UserPrivilegeChecker` precedent of casting `IDataverseService` to
`DataverseServiceClientImpl` for direct `ServiceClient.Execute` access.

**Risk**: ~50-100ms latency penalty when no `$select` is provided, vs. when it is. Acceptable for R1 —
chip "current value" lookups (the primary consumer) always know the field name they want.

**Follow-up**: If profiling shows the default-column path is hot, plumb the FR-BFF-03 metadata cache
through to this service (task 012 already established `MetadataService` with a 6h Redis cache).

---

## D-014-02 — Entity value unwrapping in the response dictionary

**Spec reference**: Task 014 brief — "Use `IReadOnlyDictionary<string, object?>` directly. Dataverse
Entity records are dynamic-keyed by nature."

**Spec implies**: Pass the raw `entity.Attributes` dictionary directly.

**Implementation actually does**: `RecordService.ProjectEntityToDictionary` walks the attributes and
unwraps the common boxed Dataverse value types into JSON-friendly shapes:

- `EntityReference` → `{ id, logicalName, name }` sub-dictionary
- `OptionSetValue` → underlying `int` value
- `Money` → underlying `decimal` value
- `AliasedValue` → recursively unwrap the inner value

It also adds an explicit `"id"` key with the record's `Guid` (Dataverse SDK does not put this in
`Attributes` by default — it lives on `Entity.Id`).

**Why deviated**: The raw `Attributes` dictionary serialises poorly with `System.Text.Json` —
`EntityReference` would serialise as `{"id": "...", "logicalName": "...", "rowVersion": null,
"keyAttributes": [...], ...}` (a dozen SDK-internal properties). The unwrap path produces a clean,
consumer-friendly shape suitable for chip "current value" rendering (the framework consumer needs the
id + display name for a lookup field; it does not need the SDK internals).

This is a pragmatic, low-risk projection — no semantic data is lost.

**Risk**: a consumer expecting raw SDK shapes (none known) would be surprised. None of the framework
chip code expects raw SDK shapes.

**Follow-up**: none — document the response shape in the OpenAPI annotations once integration test
task 016 lands.

---

## D-014-03 — Did NOT modify `Program.cs` (per sub-agent boundary)

**Spec reference**: POML step 5 says "Register `RecordService` in `Program.cs` DI".

**Implementation actually does**: Provides `AddDataverseRecordServices()` extension method on
`IServiceCollection` in `Services/Dataverse/Extensions/RecordServiceExtensions.cs`. The main session
must call this from `Program.cs` after the B1 wave completes.

**Why deviated**: Sub-agent permission boundary per task instructions: "DO NOT modify `Program.cs`".
The extension method captures the intent without violating the boundary; main session wires it
post-wave. Pattern matches task 012's deviation D-012-03.

**Risk**: none — the extension method is the standard registration pattern; just needs one line in
`Program.cs` to invoke and one line to call `app.MapRecordEndpoints()`.

**Follow-up**: main session adds:

```csharp
// in Program.cs DI section:
builder.Services.AddDataverseRecordServices();

// in Program.cs endpoint mapping section:
app.MapRecordEndpoints();
```

once tasks 011, 013 compile cleanly.

---

## D-014-04 — Conflate "row not found" + "row not readable" → 404

**Spec reference**: Task 014 brief acceptance criteria — "Given missing record ID, when called, then
404 ProblemDetails."

**Spec implies**: 404 is for record-not-found. Read-deny is handled by the `DataverseAuthorizationFilter`
which returns 403 before the handler runs.

**Implementation actually does**: Both genuine not-found AND Dataverse-server-side row-level access
denial are mapped to 404 by `RecordService`. The filter's 403 path only fires on entity-level Read
deny; row-level access (which is enforced server-side via the impersonated `CallerId`) shows up as a
not-found error from the SDK.

**Why deviated**: This is a security best practice, not a bug — surfacing 403 for unreadable rows
would leak the existence of records the caller has no business knowing about. This matches the design
explicitly called out in `010-authorization-filter-shape.md` §9: "**Does NOT validate per-record
access** for `/api/dataverse/record/{entity}/{id}` — the handler relies on OBO/CallerId returning 404
from Dataverse if the caller cannot read the specific row."

**Risk**: a debugging operator would see 404 for a row that genuinely exists; they would need to
inspect the audit log (per Q4 resolution — `AuditEnrichmentMiddleware` captures the trace). Acceptable
trade-off.

**Follow-up**: integration test task 016 should cover both genuine-404 and row-deny-404 paths to
document the design intent in tests.

---

## Build status note

`dotnet build src/server/api/Sprk.Bff.Api/` returns 8 errors at task 014 completion time, all in
sibling-agent territory:

- 6 errors in `Services/Dataverse/Privileges/UserPrivilegeChecker.cs` — owned by task 011
- 2 errors in `Services/Dataverse/FetchService.cs` (missing `FetchExpression` using) — owned by task 013

**Zero errors in any of the 3 files authored by task 014**:

- `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/Extensions/RecordServiceExtensions.cs`
- `src/server/api/Sprk.Bff.Api/Api/Dataverse/RecordEndpoints.cs`

Per the task brief: "Sibling agents may have temp errors". Build will succeed once tasks 011, 013
finalise.

Verification command:
```
dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj 2>&1 | grep "error CS" | grep -v UserPrivilegeChecker | grep -v FetchService
```
returns no output.

---

## Vulnerability scan note

`dotnet list package --vulnerable --include-transitive` reports:

- 1 HIGH-severity CVE (Microsoft.Kiota.Abstractions 1.21.2 — GHSA-7j59-v9qr-6fq9) — pre-existing,
  pulled in transitively via Microsoft.Graph 5.101.0; NOT introduced by task 014.
- 2 MODERATE CVEs (OpenMcdf 3.1.0, OpenTelemetry.Api 1.15.0) — pre-existing transitive deps.

Task 014 added zero new package references. No new HIGH-severity vulnerabilities introduced.
