# Task 012 — Deviations from POML

> **Status**: Filed at task completion (2026-06-01)
> **Owner**: task-execute (012)
> **Scope**: Differences between authored implementation and the POML / task-010 design notes.

---

## D-012-01 — Cache key not version-pinned (deferred per FR-BFF-03 R1 simplification)

**Spec reference**: `010-authorization-filter-shape.md` §6 + POML step 3.

**Spec recommends**: `sdap:dv:entitymetadata:{entityLogicalName}:v{globalMetadataVersion}` with the global
metadata version stamp pinned via Dataverse `RetrieveAllEntities.ServerVersionStamp`. This gives instant
invalidation on solution import.

**Implementation actually uses**: `sdap:dv:entitymetadata:{entityLogicalName}` (no version suffix) with a
6-hour absolute TTL.

**Why deviated**: The task instructions explicitly permit this simplification —
"_for simplicity in R1, you can omit the version pin and use `sdap:dv:entitymetadata:{entityLogicalName}` with
6h absolute expiration_". Adding the version stamp requires either:

1. A second Dataverse round-trip per request (`RetrieveAllEntities` for `ServerVersionStamp`) — defeats the
   cache's purpose, or
2. A separate background refresher that publishes the current stamp to a shared cache key — out of scope for
   the task 012 budget (3h).

**Risk**: solution-import staleness — within the 6h window, an admin importing a new solution will not see
projected attribute changes via this endpoint until cache expiry. Acceptable for R1 (solution imports are
admin-coordinated change windows; mismatched metadata for &lt;6h is operationally tolerable).

**Follow-up**: file as R2 enhancement once `RetrieveAllEntities.ServerVersionStamp` polling cadence is
designed alongside other framework cache-invalidation paths.

---

## D-012-02 — `MetadataService` registered as `Scoped`, not `Singleton`

**Spec reference**: POML step 5, generic DI guidance.

**Spec implies**: ADR-010 "DI minimalism" + the `GraphMetadataCache` precedent suggest singleton is sensible
for a stateless cache facade.

**Implementation actually uses**: `services.AddScoped<MetadataService>()`.

**Why deviated**: `MetadataService` injects `IDataverseService` which is registered scoped (per-request
`ServiceClient` lifetime semantics on the existing `DataverseServiceClientImpl`). Registering
`MetadataService` as singleton would create a captive-dependency issue (singleton depending on scoped). The
service itself has no per-request state beyond its injected dependencies, so scoped is correct here.

**Risk**: none — scoped lifetime matches the upstream `IDataverseService` registration.

**Follow-up**: none.

---

## D-012-03 — Did NOT modify `Program.cs` (per sub-agent boundary)

**Spec reference**: POML step 5 says "Register `MetadataService` in `Program.cs` DI".

**Implementation actually does**: Provides `AddDataverseMetadataServices()` extension method on
`IServiceCollection` in `Services/Dataverse/Extensions/MetadataServiceExtensions.cs`. The main session
must call this from `Program.cs` after the B1 wave completes.

**Why deviated**: Sub-agent permission boundary per task instructions: "DO NOT modify `Program.cs`". The
extension method captures the intent without violating the boundary; main session wires it post-wave.

**Risk**: none — the extension method is the standard registration pattern; just needs one line in
`Program.cs` to invoke.

**Follow-up**: main session adds `builder.Services.AddDataverseMetadataServices();` to `Program.cs` and the
endpoint mapping `app.MapMetadataEndpoints();` once tasks 011-014 are all merged and the BFF compiles.

---

## Build status note

`dotnet build src/server/api/Sprk.Bff.Api/` returns 6 errors at task 012 completion time, **all in
`Services/Dataverse/Privileges/UserPrivilegeChecker.cs`** — owned by task 011 (sibling agent, still
in-flight). Zero errors in any of the 4 files authored by task 012. Per the task brief: "Sibling agents
(011, 013, 014) may have temporary errors during the wave; not your concern."

Verification command: `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj 2>&1 | grep error`
shows all 6 errors are line numbers in `UserPrivilegeChecker.cs`.
