# Lessons Learned — spaarkeai-compose-r7

> Captured at wrap-up (task 090, 2026-08-17). Corrections + confirmed non-obvious approaches worth carrying to future Compose / BFF work.

## Corrections (things the plan/prior state got wrong)

1. **A green unit suite does NOT mean the whole solution compiles.** Task 013 added `IGenericEntityService.UpsertAsync` and updated 4 of 5 hand-rolled fakes; it missed `FakeGenericEntityService` in the *separate* `Sprk.Bff.Api.IntegrationTests` project. The unit suite (`Sprk.Bff.Api.Tests`) compiled green, so the CS0535 stayed hidden until the 090 holistic review built the integration project. **Lesson**: when adding a member to a shared interface, build EVERY test project that references it (`dotnet build <sln>` or all test csprojs), not just the folder under change. The holistic wrap-up review (whole-branch, cross-project) is what caught it — that pass earns its cost.

2. **A number-nonce prop must be baselined at mount, or it fires on first render.** Task 061's cross-pane focus seam seeded `focusInputSignal` via `useState(0)` in the host but guarded only `=== undefined` in the consumer, with `lastFocusSignalRef` starting `undefined`. On first commit `0 !== undefined` fired → focus-steal into the chat composer on load. **Lesson**: for a one-shot "act on a NEW value" nonce, seed the last-acted ref with the *mount-time value* (`useRef(signal)`), not `undefined`, unless the host guarantees it starts `undefined`. The `pendingOutboundMessage` idiom this mirrored happens to start undefined, which hid the asymmetry.

3. **Task 070's premise was stale.** D8 "blank page mounts non-editable" was already fixed by the born-in-editor rework (compose-r4). The honest move was a regression guard + a flag to the owner, NOT a fabricated fix. Don't invent a change to satisfy a task whose defect no longer reproduces.

## Confirmed non-obvious approaches (worked; reuse)

4. **`authenticatedFetch` throws a typed `ApiError` on any non-2xx (only returns on `.ok`).** So `if (!response.ok) { … }` after an `authenticatedFetch` await is DEAD code — the throw already happened. Handle status branches in `catch (err) { if (err instanceof ApiError && err.status === 404) … }`. Task 074 removed the dead idiom in the apply-template handler; the pattern was already shipped elsewhere in the same file.

5. **Client-only autosave keeps SPE version history clean (NFR-03) with zero BFF surface.** The draft store (`composeDraftStore.ts`) writes localStorage only; only an explicit Save appends a version. This is the whole reason UC-4 touched no BFF — and it structurally satisfies "no version-per-tick" without any server coordination.

6. **The stable logical identity (`sprkDocumentId ?? speDriveItemId ?? composeLogicalId`) unifies draft-recovery (FR-03) and dedup (FR-07).** One key, one accessor (`getComposeLogicalIdentity`). Don't derive a second key for drafts.

7. **Atomic upsert closes the duplicate-record hole that read-then-Create can't.** `PromoteIfEphemeralAsync` on `sprk_graphitemid_uk` — but changing a promote-write primitive ripples to EVERY test mocking it (12 BFF test files migrated `CreateAsync`→`UpsertAsync`). Budget for the test migration when touching a write primitive.

## Process notes

8. **§6.5 Path A works when the real fix exceeds task scope.** Task 074's If-Match hardening touched a shared ADR-007 facade (`SpeFileStore`) — well beyond the bounded task. Documenting the exception + deferring the hardening (DEF-001/#776) was the right call vs. silently expanding scope or shipping a no-op.

9. **Deploy coordination is a real gate, and the repo can answer most of it.** `/conflict-check` + the active-project registry (`projects/INDEX.md`) confirmed no open PR overlapped our files and no sibling had an in-flight dev deploy (dev was on the net10 baseline our branch targets). The one thing the repo *can't* answer — real-time deploy timing — is why the actual `spaarke-bff-dev` + `sprk_spaarkeai` deploy stays operator-run in a coordinated window (NFR-05).
