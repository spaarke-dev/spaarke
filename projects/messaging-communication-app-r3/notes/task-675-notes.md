# Task #675 / ISS-006 — Close the internal-only over-disclosure in the read path

> Security fix. FULL rigor. Completed 2026-07-21.

## Problem
`CommunicationThreadReadService` hardcoded `IsInternalUser: true` when building the
`CommunicationAccessContext` at 3 read sites. `CommunicationAccessFilter` therefore treated EVERY caller
as internal and never applied the internal-only (D-05) rule — so `sprk_isinternalonly` messages were
readable by external-licensed `systemuser`s (over-disclosure), on every read surface.

## Dependency (landed on master)
`ISystemUserIdentityResolver.IsExternalAsync(Guid systemUserId, CancellationToken)` — reads
`systemuser.sprk_isexternal` (fail-closed = external on empty id / missing row / unreadable flag),
cached 10 min. DI-registered **Singleton** in `Infrastructure/DI/NotificationsModule.cs`.

## Change
- Injected `ISystemUserIdentityResolver` into `CommunicationThreadReadService` (ctor now 5 deps).
- `callerSystemUserId` is a **`Guid`** (`ResolveCallerOrThrowAsync` returns `Task<Guid>`, parsing the
  impersonation systemuserid), so it is passed **directly** to `IsExternalAsync` — no parse needed.
- At each of the 3 sites, replaced the hardcoded context with:
  ```csharp
  var isExternal = await _identityResolver.IsExternalAsync(callerSystemUserId, ct);
  var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: !isExternal);
  ```

### The 3 read sites (all read paths covered)
1. `ReadThreadAsync` — thread-read (~line 143 before).
2. `GetUnreadCountAsync` — unread-scan (~line 191 before).
3. `QueryVisibleMessagesAsync` — the SHARED pipeline for BOTH `ReadByRegardingAsync` (by-regarding) and
   `QueryCommunicationsAsync` (filtered query) (~line 621 before).

`ListThreadsAsync`, `ReadThreadNameAsync`, `CanCallerSeeThreadAsync` do NOT apply the access filter
(impersonation-only thread-record reads) — no change needed there.

## DI-lifetime safety
Resolver is **Singleton**; `CommunicationThreadReadService` is **Scoped**. Injecting a Singleton into a
Scoped consumer is always safe (a captive dependency is the reverse — a shorter-lived dep captured by a
Singleton). BFF builds clean; no captive-dependency warning.

## Tests
- Updated all read-service/seam fixtures that construct the service to inject the resolver
  (`CommunicationThreadReadServiceTests`, `CommunicationByRegardingReadTests`,
  `CommunicationFilteredQueryTests`, `CommunicationListThreadsSeamTests`,
  `CommunicationWorkspaceReadSeamTests`, `CommunicationPrivilegePrivacySeamTests`). Default = internal
  (`IsExternalAsync ⇒ false`) so pre-fix behavior is preserved.
- **NEW negative tests** (`CommunicationPrivilegePrivacySeamTests`, section D), end-to-end through the REAL
  service + REAL filter for an EXTERNAL caller:
  - `ReadThreadAsync_ExternalCaller_InternalOnlyRowIsFilteredOut_ButRegularRowSurfaces` — impersonation
    returns BOTH an internal-only and a regular row; the internal-only row is DROPPED (no recipients /
    markers / id surface), the regular row still surfaces (no under-disclosure).
  - `GetUnreadCountAsync_ExternalCaller_DoesNotCountInternalOnlyMessages` — external caller's unread count
    excludes internal-only messages.
- Updated the two "no-membership-union" ctor-shape guards (`CommunicationListThreadsSeamTests`,
  `CommunicationWorkspaceReadSeamTests`) from freeze-at-4 to 5 params, explicitly allowing
  `ISystemUserIdentityResolver` while KEEPING the banned membership/grant-union seam-type guard intact.

Moq gotcha fixed: a blanket `IsExternalAsync(It.IsAny) ⇒ false` setup configured inside `Sut()` runs
after the per-caller `MarkExternal(...)` and (last-matching-setup-wins) clobbered it — removed it; the
loose mock returns `false` by default, and `MarkExternal` overrides the specific external caller.

## Verification
- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors** (22 pre-existing warnings).
- Communication tests: **670 passed, 0 failed, 8 skipped** (pre-existing skips).
- Publish (Release, incl PDBs): **47.45 MB compressed** — under the 60 MB ceiling, at baseline (no package change).
- CVE: no package change; the one HIGH (`System.Security.Cryptography.Xml 8.0.3`, transitive) is
  pre-existing baseline, NOT introduced here. Zero NEW HIGH CVE.

## Placement Justification (BFF hygiene §10/§11)
No new component. REUSED the sanctioned `ISystemUserIdentityResolver` (the single authoritative
internal/external source). No second access mechanism introduced; the shared `CommunicationAccessFilter`
remains the sole read-path enforcement point.
