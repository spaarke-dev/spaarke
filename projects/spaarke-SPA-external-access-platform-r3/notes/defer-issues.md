# Defer / Issue Tracking — spaarke-SPA-external-access-platform-r3

> **Source of truth** for deferred work and newly found issues in this project. Each entry has a paired GitHub Issue; see the `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label spaarke-SPA-external-access-platform-r3`.
> **CLAUDE.md §11 rule**: every entry names a concrete behaviour or contract that fails without it.

---

## Open (in priority order)

### ISS-001 — AuthorizationModule: Copilot audience merge and failure logging applied once per process, not per options instance

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-10 |
| **Source** | R3 task 010; confirmed by both adversarial review passes |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1623 |

**Description**

The default JwtBearer scheme's Copilot-audience merge and its `CopilotAuth` sign-in-failure logging run in a `PostConfigure` guarded by a static once-per-process flag (`_jwtPostConfigureApplied`), instead of once per options instance.

Any later `JwtBearerOptions("Bearer")` instance silently loses the Copilot audience (Copilot tokens then get 401) and the failure logging. Two ways to get a later instance:
- a second test host in the same process;
- an instance rebuilt after a configuration reload (`JwtBearerConfigureOptions` binds `IConfiguration` with a change token).

This is pre-existing and not caused by R3. R3's CIAM guard is a separate `PostConfigure` without the flag, so it is unaffected.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs:37` (the flag)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs:106-111` (the gated `PostConfigure`)
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/AuthorizationModuleCiamTests.cs` (the harness re-adds the Copilot audience to work around it)

**Suggested fix**

Make the merge idempotent per options instance, for example by checking whether the Copilot audience is already present. Then remove the test-harness workaround.

**Estimated effort**: 2–4 hours, including a test with two hosts in one process.
**Blockers**: none.
**Related**:
- `AuthorizationModule.cs` is shared with unified-access-control-r2.
- A candidate for the auth-system-of-record remediation phase.
- Not yet confirmed: whether a production reload actually rebuilds this instance.

---

### ISS-002 — ChatMessageStreamingContractTests mid-stream error test fails intermittently under full-suite load

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-10 |
| **Source** | R3 task 031 full unit-suite run (19512 passed, 54 skipped, 1 failed) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1633 |

**Description**

`SendMessage_ModelThrowsAfterTwoTokens_EmitsTypingEndThenSafeErrorAndNothingAfter` failed once in a full `dotnet test tests/unit/Sprk.Bff.Api.Tests/` run on the R3 branch. It took 2 min 25 s under load and passes 6/6 alone. Task 031 does not touch AI chat code. A load-dependent contract-test failure fails CI at random. The failure message was not captured, so the cause is unconfirmed.

Second occurrence 2026-10-11, full run after the task 034 merge (`ab4e91011`): 1 failed of 19,735, the same test, 2 min 47 s; it passes 6/6 alone and the merge touched no AI code. The failure message was again not captured (output filtered).

**Entry-points**

- `tests/integration/contract/Api/Ai/ChatMessageStreamingContractTests.cs:194`
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ChatMessageStreamingContractTests"` (passes alone)

**Suggested fix**

Capture the failure with `--logger trx` on a full run. Then check for a fixed SSE read timeout, or shared `_fixture.ChatClient` scripting across parallel tests.

**Estimated effort**: 2–4 hours, mostly reproduction.
**Blockers**: none.
**Related**: AI chat area; not R3 scope.

---

### ISS-003 — Shared sanitizeEmailHtml keeps style positioning and remote images

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-11 |
| **Source** | R3 task 051 adversarial review (F2 on the external surface, K2) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1636 |

**Description**

The shared `sanitizeEmailHtml` keeps the `style` attribute and removes only executable tokens. It also allows `img src` over http(s).
- An inbound email body can therefore use `position:fixed; inset:0; z-index:…` to paint over the whole host surface, for example a fake "sign in again" panel.
- Remote images and CSS `url(...)` load when someone opens the message, which tells the sender the reader's IP and read time.

Consumers: the internal CommunicationTimeline `MessageRow` and ConversationView `MessageBubble`; the Spaarke.Communication.Components email views; and the R3 external SPA Messages section, which external contacts read.

R3 confines HTML bodies in the external SPA with `contain: paint` (task 051 fix round). The other consumers are unchanged.

**Entry-points**

- `src/client/shared/Spaarke.UI.Components/src/utils/sanitizeEmailHtml.ts:131` (`img`), `:148` (`style`)
- `src/client/external-spa/src/components/detail/RecordMessages.tsx` (the R3 containment)

**Suggested fix**

Strip `position`, the `inset` family, `z-index` and `url(` from `style` in the shared util. Add a `blockRemoteImages` option for low-trust consumers.

**Estimated effort**: 4–8 hours.
**Blockers**: none.
**Related**: R3 task 051; ISS-003 is not R3 scope because the util is shared.

---

### ISS-004 — Internal document delete orphans the SPE file when the file delete fails

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-11 |
| **Source** | R3 task 032 implementation (delete-own) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1650 |

**Description**

`DocumentCheckoutService.DeleteAsync`, behind the internal `DELETE /api/documents/{documentId}`, deletes the SharePoint Embedded file first. If that call throws, it logs a warning and deletes the Dataverse row anyway.

A transient Graph failure (timeout, 429 or 5xx) therefore leaves the file in the container with no `sprk_document` row pointing to it. The user believes the content is gone, but it is still stored, and nothing in the app can find or delete it later.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs:794-820`: the catch-all around `_speFileStore.DeleteFileAsync`.
- The R3 external overload (task 032) keeps the row and returns 503 on a file-delete failure.

**Suggested fix**

- Use the R3 external behaviour on the internal route: keep the row and return 503 on a file-delete failure.
- Treat "file not found" as success.

Needs a decision from the owner of the internal document UX.

**Estimated effort**: 1–2 hours, including tests.
**Blockers**: none.
**Related**: R3 task 032 (FR-26). Out of R3 scope: the internal route is shared.

---

## Closed

(none)
