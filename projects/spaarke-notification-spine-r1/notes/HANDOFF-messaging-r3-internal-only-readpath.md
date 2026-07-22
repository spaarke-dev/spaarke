# HAND-OFF → messaging-communication-app-r3: fix internal-only enforcement on the timeline read path

> **From**: spaarke-notification-spine-r1 (surfaced during the FR-08 fan-out security review, 2026-07-21)
> **To**: messaging-communication-app-r3 (PR #664 — owns/edits `CommunicationThreadReadService.cs`)
> **Severity**: **HIGH / security** — internal-only messages are currently readable by any caller.
> **Why hand-off, not a direct edit**: your PR #664 is live in `CommunicationThreadReadService.cs` + its tests; a parallel edit from the spine branch would hard-conflict. `/conflict-check` confirmed the overlap, so the spine did the shared resolver + its own fan-out fix, and hands you the 3-line read-path swap.

---

## The problem (confirmed with the owner, 2026-07-21)

`CommunicationAccessFilter` enforces internal-only via `!caller.IsInternalUser && isInternalOnly → deny`. But **`IsInternalUser` is never sourced from a real attribute** — the read path hardcodes it:

```csharp
// CommunicationThreadReadService.cs — THREE sites (approx lines 129, 185, 463 as of master)
var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: true);
```

Every caller is treated as internal ⇒ **the internal-only rule is unenforced on read today.** An external party **can be a licensed `systemuser`** (owner confirmation) — so an external-licensed user who can hit the timeline endpoint currently reads internal-only messages. The `systemuser ⇒ internal` assumption is false and must be replaced by an authoritative flag.

## The authoritative signal (already shipping)

- **Dataverse**: `systemuser.sprk_isexternal` (two-option boolean, default **No** = internal). Set `true` for external-licensed users. (10-user tenant is being backfilled manually; rollout/provisioning deferred.)
- **Shared resolver** (ships from notification-spine; on master once this branch merges):
  `ISystemUserIdentityResolver.IsExternalAsync(Guid systemUserId, CancellationToken)` → `Task<bool>`
  in `src/server/api/Sprk.Bff.Api/Services/Identity/SystemUserIdentityResolver.cs`.
  Cached (`IDistributedCache`, 10-min TTL). **Fail-closed**: empty id / missing row / unreadable flag ⇒ `true` (external). A present `false` ⇒ internal. Registered unconditionally in `NotificationsModule` (DI).

## The change you need to make (3 sites + DI)

1. **Inject** `ISystemUserIdentityResolver` into `CommunicationThreadReadService` (constructor). It's already registered in DI — no module change needed. Add `using Sprk.Bff.Api.Services.Identity;`.

2. **Replace each of the 3 hardcoded sites** (the methods are already `async` and already hold `callerSystemUserId`):

   ```csharp
   // BEFORE
   var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: true);

   // AFTER
   var isExternal = await _identityResolver.IsExternalAsync(callerSystemUserId, ct);
   var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: !isExternal);
   ```

   (Use the `CancellationToken` already in scope in each method.)

3. **No other file changes.** Do NOT touch `CommunicationAccessFilter` / `CommunicationAccessModels` (they already consume `IsInternalUser` correctly). `CallerContactId` stays as-is.

## Tests

- **Update** `CommunicationThreadReadServiceTests.cs`: the fixtures currently assume `IsInternalUser: true`. Provide a doubled `ISystemUserIdentityResolver` (Moq) whose `IsExternalAsync` returns `false` for the internal caller — keeps existing behaviors green. Follow the module-boundary-double convention (do NOT `Mock<HttpMessageHandler>`, per ADR-038).
- **Add the guard case** (the leak this closes): an **external-licensed** caller (`IsExternalAsync ⇒ true`) requesting a thread that contains an `sprk_isinternalonly=true` message → that message is **NOT** in the visible set (and internal callers still see it).
- **Add the CI guard test** (spine deferred it here because the pattern still exists until you land this): a test that greps `CommunicationThreadReadService.cs` (and ideally all of `Services/Communication/**`) and **fails on any literal `IsInternalUser: true`** — so the proxy can't silently return. It goes green the moment your 3-site swap lands. Suggested home: `tests/unit/domain/Communication/InternalOnlyEnforcementGuardTests.cs`.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api -c Release` → 0 errors.
- `dotnet test --filter FullyQualifiedName~CommunicationThreadReadService` → green, incl. the new external-caller guard case.

## Sequencing

1. **notification-spine merges first** → master has `IsExternalAsync`.
2. r3 rebases on master, makes the 3-site swap + tests above, lands with PR #664.
3. After both land, internal-only is enforced on **both** the timeline read (yours) and the notification fan-out (spine's `CommunicationFanOutTargetingService`, already done).

## Reference

- Spine-side implementation (the mirror of this change, already done): `CommunicationFanOutTargetingService.cs` now uses `!await _identityResolver.IsExternalAsync(...)` instead of `systemUserRef is not null`, with a seam test proving an external-licensed systemuser is excluded from an internal-only message.
- Full security discussion + audit: this project's chat log 2026-07-21; `notes/023-fanout-security-signoff.md`.
