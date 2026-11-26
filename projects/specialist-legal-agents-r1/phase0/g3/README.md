# Phase 0 test G3: profile resolution from the caller's identity

> **Program:** specialist-legal-agents-r1 (design.md §8 G3; spec FR-02, FR-03, NFR-04, NFR-05).
> **Status:** spike branch built and tested (task 011). Not deployed, not run against Copilot. The header tables below are empty until the owner runs the surfaces after a DEV deploy of the branch.
> **Who does what:** the owner deploys the spike branch to DEV (on a go that names the SHA), sends the prompts and removes the branch afterwards; this session reads App Insights. Claude sessions never click tenant or admin actions.

## The spike branch

| Item | Value |
|---|---|
| Branch | `spike/specialist-agents-phase0` (never merged; no PR) |
| Cut from | `origin/master` at `6a2a569ac9` (2026-11-26) |
| Spike commit | `d307d67e36` (pushed to `origin/spike/specialist-agents-phase0`; first cut `ddccee88a6`, then master merged as `603e03e6ab` and the review fixes as `d307d67e36`). `origin/master` is an ancestor (checked 2026-11-26) |
| Worktree | `C:\wt-sla-spike` |
| Route | the existing `/mcp/legal-operations` (A10 connector token, `mcp` rate limit, kill switch); no new route, no package |

### Tools

| Tool | Argument | Returns | Fails closed |
|---|---|---|---|
| `spike_get_profile` | `function`: `nda` or `patent` (required) | the caller's own test profile as a data-guarded `<<<PROFILE … PROFILE>>>` block, then the profile's `returnedInstructions` (C1–C3) as instructions, plus `profileVersion` (text and `structuredContent`) | `CALLER_UNRESOLVED` (no signed-in caller, or no active systemuser); `NO_PROFILE` (no profile keyed to that systemuserid for that function). No default, nearest or other-user profile. |
| `spike_review_nda` | `documentText` (required, ≤ 60,000 characters) | the `agreement-review-spike` Action's structured review, run through `IActionRunner` with the caller's five NDA positions bound as the declared inputs (`term`, `residuals`, `nonSolicit`, `governingLaw`, `uncappedIndemnityFlag`) and the profile's tier (`Reasoning`) applied explicitly (#1934); plus `profileVersion`, Action code, applied tier and latency | as above, before any Action is read or run; a run that cannot complete is `REVIEW_FAILED` (no review against the generic standard) |

### Test profiles (spike branch only)

| Profile | Function | Keyed to (DEV systemuserid) | Who |
|---|---|---|---|
| `g1-a-v1` | nda | `1d02f31c-1872-f011-b4cb-7c1e52671ad0` | owner (WhoAmI) |
| `g1-b-v1` | nda | `849beec2-2b30-f111-88b5-7c1e520aa4df` | test user (`demo@…`, the only match) |
| `pat-a-v1` | patent | owner | |
| `pat-b-v1` | patent | test user | |

Read read-only from DEV on 2026-11-26. The profile files in this project worktree still carry `TO-BE-SET-BY-011`; only the copies on the spike branch carry the ids. Tester-only fields (`observableAs`, `scoringNote`, `checkInDraft`, `$comment`, `systemuserid`, `testerRole`, `testerNote`) are never served to the model.

## Header logging design (FR-03, NFR-05)

- One line per tool call: `MCP spike tool {ToolName} request header names ({HeaderCount}): {HeaderNames}; correlation {CorrelationId}.`
- **Names only.** No header value is read. `Authorization` and `Cookie` appear as names like any other header, never their values. Names are reduced to `[A-Za-z0-9-_.]`, cut to 80 characters, de-duplicated, sorted, and capped at 64 per line (the count still includes the rest).
- One telemetry line per call: tool, outcome (`ok` or the reason code), `profileVersion`, latency, correlation. No systemuserid, claim, token, profile text or document text is logged.
- Consequence for "can we key a run": the spike shows which headers exist, not their values. If a candidate header appears (for example a conversation or request id), deciding whether it is stable across a run needs its value. That is an owner decision: allow-list one named header for value logging in a follow-up spike commit.

## Build and test results (spike branch)

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` | 0 warnings, 0 errors |
| Spike tests (`tests/integration/auth/Mcp/Spike/`) | 33 passed, 0 failed (on the final commit; with the two updated MCP host and caller-path classes, 58/58) |
| All MCP tests (filter `Mcp`) | 223 passed, 1 skipped, 0 failed |
| Full `Sprk.Bff.Api.Tests` | 24,082 tests: 23,953 passed, 20 skipped, 109 failed on a loaded machine (an ArchTests build and other sessions' dotnet runs in parallel). None of the 109 were MCP or spike tests. The 55 failing classes re-run: 1,306 passed, 7 skipped, 4 failed (different tests, all in two Office save classes). Those two classes in isolation: 72/72 passed. Read as load flakiness, not a spike regression. |
| `Spaarke.ArchTests` | 2,841 passed, 1 failed: ISS-039 (`ReferenceRowIdLiteralGuardTests`) fired on (1) the `agreement-review-spike` row id in a doc comment, removed, and (2) the two DEV systemuserids in the four embedded profiles, baselined on the spike branch only with four lines giving the reason (task 011 instruction; never on master). Guard re-run: 19/19 passed. |
| Publish size | no package added (0 `PackageReference` changes); the four embedded profile files total 24,769 bytes |

## Fail-closed results (FR-02)

Proven by tests on the spike branch against the production MCP host (real Kestrel, JwtBearer, A10 filter, production `AddMcpModule` and `MapMcpEndpoints`):

| Case | Expected | Result | Test |
|---|---|---|---|
| Owner asks for `nda` / `patent` | `g1-a-v1` / `pat-a-v1` only | pass | `GetProfile_ACallerWithAProfile_GetsThatProfile_ItsVersion_AndItsInstructionsAsInstructions` |
| Test user asks for `nda` / `patent` | `g1-b-v1` / `pat-b-v1` only | pass | same theory |
| Served profile | no tester-only field, no other profile's text | pass | `GetProfile_ServesNoTesterOnlyField_SoTheModelNeverSeesTheKey_TheScoringHints_OrTheOtherProfile` |
| Owner and test user calls in flight together | each gets only their own profile, never the other's text | pass | `Isolation_GetProfile_TheOwnerNeverReceivesTheTestUsersProfile_AndViceVersa_EvenWithBothCallsInFlight` |
| `spike_review_nda` for each caller | only that caller's five positions are bound; the result carries only that caller's version | pass | `Isolation_ReviewNda_BindsOnlyTheCallersOwnPositions_AndReturnsOnlyTheCallersVersion` |
| Resolved caller with no profile | `NO_PROFILE`, no profile, no review run | pass | `GetProfile_AResolvedCallerWithNoProfile_IsNoProfile_AndReceivesNoProfile`, `ReviewNda_FailsClosedTheSameWay_BeforeAnyReviewRuns` |
| Caller that does not resolve | `CALLER_UNRESOLVED`, no profile, no review run | pass | `GetProfile_AnUnresolvedCaller_IsCallerUnresolved_AndReceivesNoProfile`, `ReviewNda_FailsClosedTheSameWay_BeforeAnyReviewRuns` |
| Bad arguments | refused; never pick a profile | pass | `GetProfile_ArgumentsOtherThanOneKnownFunction_AreRefused_AndNeverPickAProfile` |
| Review run fails | `REVIEW_FAILED`, no other review | pass | `ReviewNda_AFailedRun_IsReviewFailed_WithNoReview` |
| Lookup is exact | no fallback, no default | pass | `Find_IsExactOnFunctionAndSystemUserId_AndNeverFallsBack` |
| Header line | names only; never a value, the bearer token or a cookie | pass | `HeaderLogging_RecordsHeaderNamesOnly_NeverAValue_TheBearerToken_OrACookie` |
| Tests catch a lookup bug | a lookup that falls back to the first profile, or ignores the user | caught by 4 and 6 tests respectively (mutation check, reverted) | |

## Headers seen (fill in after the DEV run)

Per surface and caller kind. Query App Insights `traces` for `MCP spike tool` on the correlation of each call.

| Surface | Caller kind (tenant connector / agent action) | Time (UTC) | Header names seen | Candidate run key? | Candidate agent identifier? |
|---|---|---|---|---|---|
| Copilot Chat | | | | | |
| Cowork | | | | | |
| Word | | | | | |
| Teams | | | | | |

## Answers (fill in after the DEV run)

| Question | Answer (yes / no) | Evidence |
|---|---|---|
| Can we key a run? | | |
| Can we identify the agent? (header, per-agent route, or nothing) | | |
| Every call in one run returns the same `profileVersion`, within the latency budget (NFR-04) | | |

## Recommendation for Phase 1

To be written after the DEV run.

## Review (2026-11-26)

- Two adversarial passes (opus) on `ddccee88a6`: no isolation or auth defect.
- Fix round `d307d67e36`, verified by a scoped re-check: 50/50 spike tests pass.
  - The review binds a neutral user context, so the tester's own profile and memory no longer reach it.
  - The result reports `modelDeployment`, and the review fails with `MODEL_TIER_UNAVAILABLE` rather than falling back.
  - New retryable codes `CALLER_LOOKUP_FAILED` and `AI_DISABLED`.
  - The `REVIEW_FAILED` text carries its reference.
  - The result reports `documentChars`.
- Error codes testers may see: `NO_PROFILE`, `CALLER_UNRESOLVED`, `CALLER_LOOKUP_FAILED`, `AI_DISABLED`, `MODEL_TIER_UNAVAILABLE`, `REVIEW_FAILED`, `INVALID_ARGUMENT`.

## Dev deploy (task 015)

- 2026-11-26: owner go in chat, "Go, deploy it", for `d307d67e36`. origin/master was an ancestor (0 behind) when checked.
- Deploy request sent to the uac-r2 session, which owns the dev BFF slot.
- uac-r2 replied that the go must be given in its own session as "go BFF deploy d307d67e36". Before that it needs (1) an access review of both tools, sent 2026-11-26 (neither tool reads a per-user record or an SPE document; the Action-catalogue read is app-only), and (2) its own test pass on the SHA (full solution build plus the BFF and Arch suites, about 1.5 h).
- At the time of the request the dev BFF was at `140c01219a` and master at `51ce01a116`, an ancestor of `d307d67e36`. The deploy also carries #2622, #3236, #3243 and #3247 to dev.
- 2026-11-26: uac-r2 cleared the access review; its spot-check of 51ce01a116..d307d67e36 (12 BFF files) matched. Its non-blocking note: the profiles are keyed by two DEV systemuserids on a public branch, so they must keep holding synthetic test positions only, never a real person's legal positions. Its full test pass started about 17:20Z in `C:/wtdep29` (about 1.5 h). The deploy needs that pass green AND the owner's "go BFF deploy d307d67e36" in uac-r2's session.
- **LIVE 2026-11-26 ~18:00Z** (uac-r2, hash-verified): the dev BFF runs `d307d67e36`, replacing `140c01219a` (the rollback target).
  - `tools/list` on /mcp/legal-operations returns 7 tools, including `spike_get_profile` and `spike_review_nda`. App Insights shows uac-r2's tools/list POSTs at 18:02Z returning 200.
  - healthz 200. `/healthz/catalog` returns 503, as it did before this deploy.
  - uac-r2's 30-min gate (18:31Z) and its full test pass are reported separately.
- **Latency:** not measured in task 015. Calling the tools needs a signed-in Copilot user (the connector token), so p95 per tool (at least 10 calls each) is measured from App Insights during the first owner run, task 021.
- COPILOT-PLATFORM-COORDINATION was told that the spike tools are visible to every dev connector user.
- **30-min gate PASS** (uac-r2, 18:00–18:31Z): 68 requests. The only 5xx was the pre-existing /healthz/catalog 503. No exceptions from the spike tools. uac-r2's full test pass on this SHA is still running.
- **Full test pass GREEN** (uac-r2, after triage): build 0/0 and ArchTests 2845/2845. There were 14 unit failures, all load timeouts. An isolated re-run passed 13 of them. The last one, EventStatusDeprecationTests, is the known demo-data issue #3175, not BFF code. No rollback: `d307d67e36` stays live.
