# Task 078 — Deviation Log

> **Task**: 078 — E2E verification of `POST /api/onboarding/consent-callback`
> **Executed**: 2026-08-18 (Wave 4 Batch 4E — serial after 055)
> **Rigor**: FULL (bff-api / auth / integration-test / testing — test-modifying override per root §8)
> **Deliverables**:
> - `tests/integration/Sprk.Bff.Api.IntegrationTests/Onboarding/ConsentCallbackE2ETests.cs`
> - `tests/integration/Sprk.Bff.Api.IntegrationTests/Onboarding/ConsentCallbackE2ETestFixture.cs`
> - `projects/customer-provisioning-orchestration-r1/notes/consent-callback-e2e-2026-08-18.md`

---

## Deviations from POML

### D-078-1 (Path C) — Test project folder is `Sprk.Bff.Api.IntegrationTests`, not `Sprk.Bff.Api.Tests`

**POML text**: `tests/integration/Sprk.Bff.Api.Tests/Onboarding/ConsentCallbackE2ETests.cs`

**Actual location**: `tests/integration/Sprk.Bff.Api.IntegrationTests/Onboarding/ConsentCallbackE2ETests.cs`

**Rationale**: The BFF integration test csproj is named `Sprk.Bff.Api.IntegrationTests` (see `.csproj` file at that path). The `.Tests` suffix in POML text is imprecise — POML likely conflates with the unit test project `tests/unit/Sprk.Bff.Api.Tests/`. The correct home for this WebApplicationFactory-based E2E test is the existing integration test project, which already has `Program` internals visibility (verified InternalsVisibleTo in `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` line 37) and follows the KEEP path `tests/integration/**` per ADR-038.

**Path chosen**: **C (comply via existing convention)** — the actual test project structure supersedes the POML literal.

**Blast radius**: Notes file references the actual path; TASK-INDEX.md row 078 updated accordingly.

---

### D-078-2 (Path C — documentation delta only) — Happy path returns 202 Accepted, not 200 OK

**POML text**: "Happy path: signed payload → **200** → sprk_dataverseenvironment.sprk_tenantid populated → Cosmos parameters.tenantId populated → L2 receives POST /api/runs."

**Actual endpoint behavior**: Returns **202 Accepted** per task 042 implementation (`ConsentCallbackEndpoint.HandleAsync` L261-266 uses `Results.Accepted(...)`).

**Rationale**: The endpoint is enqueue-only (drops a message onto Service Bus, work happens downstream in L2). 202 Accepted is the correct HTTP verb per RFC 7231 §6.3.3 for "accepted but not yet acted upon" — signalling to the caller that the request was received, HMAC-verified, and enqueued for processing. This is also documented in the endpoint's `.Produces<ConsentCallbackResponse>(StatusCodes.Status202Accepted)` metadata (line 78) and the endpoint's XML doc-comment (line 24). POML's "200" is a spec drafting imprecision; the tests assert 202 which is what the endpoint actually returns.

**Path chosen**: **C (comply with impl reality)** — POML's "200" was drafted before task 042 finalized the response verb. Test assertions use `HttpStatusCode.Accepted` (= 202).

**Blast radius**: None. Tests match production code. Notes file §4 documents the mismatch and the correct verb.

---

### D-078-3 (Path C — documentation delta only) — Endpoint enqueues via Service Bus, not L2 REST POST /api/runs

**POML text**: "…L2 receives POST /api/runs."

**Actual endpoint behavior**: Enqueues via `IProvisioningEnqueuer.EnqueueAsync` (a Service Bus sender wrapper). Message lands on `sprk-provisioning-jobs` queue; L2's `H05ConsentCaptureHandler` dequeues it via the reconciler (task 058) or direct SB consumer.

**Rationale**: ADR-036 (background job infrastructure) plus §10 BFF hygiene rule R20 (async-handler-in-HTTP-path required) forbids synchronous BFF → L2 REST calls in the HTTP thread. The endpoint's SB-enqueue pattern is architecturally correct. POML's "POST /api/runs" language predates the SB-based dispatch pattern. Task 057 implemented the L2 REST endpoints (`/api/runs`), but the consent-callback path uses SB (queue-driven) rather than REST (request-driven) — same pipeline reached, different transport.

**Path chosen**: **C (comply with impl reality + ADR-036)** — tests assert the SB enqueue behavior via `CapturingProvisioningEnqueuer` rather than looking for an outbound HTTP call to L2 REST.

**Blast radius**: None. Tests match the design-correct implementation. Notes file §5 documents the SB-vs-REST distinction.

---

### D-078-4 (Path A — documented exception) — Re-consent state-check semantics verified at BFF-contribution layer only

**POML criterion #2**: "Re-consent (existing Ready/Running/WaitingOnGate row + same tid): 200 with existing-run link in response; NO new pipeline kick."

**POML criterion #3**: "Re-consent (existing Failed/Cancelled row + same tid): 200 → new pipeline kick + restart from H0."

**Actual coverage**: Both criteria's BFF-layer CONTRIBUTIONS (byte-stable payloads + collapsible MessageId for #2; fresh RunId per callback + distinct MessageIds for #3) are verified end-to-end via `Idempotency_SameCustomerAndTid_YieldsCollapsibleServiceBusMessageId` and `Restart_FreshRunPerCallback_YieldsDistinctMessageIds`.

**NOT verified at BFF layer**: The Dataverse status-read + no-op-vs-restart decision. This is L2's `H05ConsentCaptureHandler` responsibility (owned by task 042 L2 side, tested in the L2 handler test suite).

**Rationale**: The BFF endpoint is enqueue-only (see D-078-2, D-078-3). It cannot verify "no new pipeline kick" or "restart from H0" without either (a) driving the L2 handler in-process (out of scope for BFF integration tests), or (b) simulating live Dataverse + Cosmos + SB queue in the WebApplicationFactory (out of scope + fragile).

The correct verification split:
- **BFF layer** (this task 078): HMAC verify + wire-payload correctness + fresh RunId per callback + no-default-tenant edge fail — verified end-to-end.
- **L2 layer** (task 042 L2 side): status-read + no-op-vs-restart decision + Dataverse + Cosmos writes — verified by L2 handler tests (existing).
- **Cross-layer** (task 089): end-to-end Model 1 acceptance including full re-consent + restart choreography against a live subscription — pending final integration.

**Path chosen**: **A (documented exception scoped to this task)** — the E2E test verifies what the BFF layer OWNS; L2-owned semantics remain verified where they are implemented and where their state (Dataverse row status) is authoritative.

**Blast radius**: None. Split matches ADR-036 + §10 BFF hygiene. Notes file §5 explicitly documents which parts of criteria #2/#3 are L2-owned.

---

### D-078-5 (Path C — documentation delta only) — Notes filename uses actual date, not `2026-XX` placeholder

**POML text**: `projects/customer-provisioning-orchestration-r1/notes/consent-callback-e2e-2026-XX.md`

**Actual filename**: `projects/customer-provisioning-orchestration-r1/notes/consent-callback-e2e-2026-08-18.md`

**Rationale**: POML uses `2026-XX` as a placeholder for the execution date. Environment `Today's date is now 2026-08-18` per session context. `2026-08-18` matches the actual execution date.

**Path chosen**: **C (fill in the placeholder correctly)**.

**Blast radius**: None.

---

## No Path B (ADR amendments) — none needed

None of the deviations require an ADR amendment. Deviations D-078-2 and D-078-3 are documentation reconciliations against the design-correct implementation (already-shipped task 042 impl). D-078-1 is a project-folder convention. D-078-4 is a scoping decision consistent with ADR-036 + §10 BFF hygiene. D-078-5 is a template placeholder fill.

---

## What this task deliberately did NOT do

Per POML `<constraints>` + prompt orchestrator escalation guidance:

1. **Did NOT drive a real Microsoft admin-consent redirect flow.** Chosen synthetic-signed payload path per POML alternative — see §2 of `consent-callback-e2e-2026-08-18.md` for rationale.
2. **Did NOT modify `ConsentCallbackEndpoint.cs`** (task 042 owns; this is a verification-only task).
3. **Did NOT modify L2 code** (task 057 owns).
4. **Did NOT modify other BFF endpoints.**
5. **Did NOT modify `.claude/` files** (sub-agent write boundary per root §3; also I am the executing subagent with the boundary applied).
6. **Did NOT create a live Dataverse or Cosmos row.** State-check semantics are L2 handler layer (see D-078-4).

---

## Gate results summary (§10 BFF hygiene)

| Gate | Result |
|---|---|
| BFF build (`dotnet build src/server/api/Sprk.Bff.Api/`) | **0/0** |
| Integration test project build | **0/0** |
| New E2E tests (7 methods) | **7/7 PASS** (~5s) |
| BFF unit baseline | **10,484 PASS / 0 FAIL / 97 SKIP** (preserved) |
| Publish size | **44.96 MB compressed**; Δ **+0.00 MB** attributable to task 078 (test-only additions) |
| CVE scan | **0 vulnerable packages** — 0 new HIGH |
| `/conflict-check` | Not needed — no BFF production code modified |
