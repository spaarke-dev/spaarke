# Task 073 — H14 Post-Deploy Integration Wiring Handler — Deviations

> Path references per root CLAUDE.md §6.5 (ADR Conflict Resolution Protocol). All three below are **Path C — pivot to comply** (documented reasoning, no ADR amendment needed).

## D-073-1: Single-writer DAG-parallel design (parent owns Cosmos I/O)

**POML literal wording**: "H14 is one IJobHandler that spawns 3 sub-handlers via internal DAG-parallel dispatch; each sub-handler has its own idempotency key." Read literally, this could imply each sub-handler independently reads + writes `ProvisioningRun` (the pattern every other handler in this codebase follows).

**What was built instead**: `H14aExchangePolicySubHandler` / `H14bGraphWebhookSubHandler` / `H14cDataverseWebhookSubHandler` do **not** touch `IProvisioningRunRepository` at all. `H14IntegrationWiringHandler` (parent) reads the run **once**, builds each sub-step's typed parameters as an opaque `ParametersJson` payload, dispatches all 3 via `Task.WhenAll` as pure external-system executors, aggregates their `HandlerResult` outcomes, and performs **one** `ReplaceRunAsync` call.

**Rationale**: 3 sub-handlers doing independent read-modify-write against the *same* Cosmos document under ETag optimistic concurrency, invoked via `Task.WhenAll` (true parallelism, not sequential), would race on the *same* ETag on **every single invocation** — not a rare edge case, a guaranteed one (2 of 3 concurrent writers would always observe `ReplaceRunResult.Conflict`). The single-writer design eliminates the race entirely while still satisfying "each sub-handler has its own idempotency key" — the key is a deterministic pure function of each sub-step's inputs (`BuildIdempotencyKey` on each sub-handler type), computed and checked by the parent *before* dispatch (an already-recorded key short-circuits that sub-step to a pre-completed `Success` task, still awaited via `Task.WhenAll` uniformly).

**Alternative considered and rejected**: retry-on-conflict loops inside each sub-handler (re-read + re-apply on `Conflict`). Rejected as unnecessary complexity — the single-writer design is simpler, has no retry-loop edge cases, and still achieves genuine parallelism for the actual *external* work (Exchange PS call / Graph REST calls / Dataverse REST call), which is where the wall-clock benefit of "DAG-parallel" actually matters.

**Full rationale**: `H14aExchangePolicySubHandler.cs` file header, section "PARENT-OWNS-COSMOS DESIGN"; `H14IntegrationWiringHandler.cs` file header, section "SINGLE-WRITER DAG-PARALLEL DESIGN".

## D-073-2: H14b Graph resource targets are run-parameter-supplied, not a hardcoded canonical catalog

**POML literal wording**: "Graph webhook subscriptions per Communication/Email module."

**What was built instead**: the exact Graph resource path per module (`communicationGraphResource` / `emailGraphResource`) is a **run parameter** (NonSecret), not a Spaarke-wide hardcoded constant (contrast with H2b's 7 canonical AI Search index names, which genuinely are identical across every customer).

**Rationale**: unlike AI Search index schema names, a Graph subscription resource for mail/communications typically targets a *specific per-tenant mailbox or scope* that is customer-tenant configuration, not a Spaarke platform constant. Hardcoding a plausible-but-unverified resource path (e.g. a specific mailbox GUID pattern) risks silently shipping incorrect Graph API assumptions. At least one of the two resources is required; H14b fails `Resumable` with a clear diagnostic if neither is configured.

**Follow-on**: once the Communication/Email module's exact subscription targets are finalized (a product-design decision, not an H14 implementation detail), a future task can promote these into a Dataverse-configured catalog. The seam (`IGraphSubscriptionCreator`) + idempotent create-or-renew mechanics do not change when that lands.

## D-073-3: Dataverse `serviceendpoint` option-set numeric values are Options-configurable, not hardcoded

**Context**: `serviceendpoint.contract` / `.messageformat` / `.authtype` are Dataverse option-set integers. The exact numeric values (Contract=8 for WebHook, MessageFormat=2 for JSON, AuthType=5 for None) are sourced from Microsoft Dataverse SDK documentation as last verified (2026-08) but are **not** exercised by the CI unit suite (real Dataverse Web API call — parity with every other H-series live-REST collaborator's "NOT under test in CI" posture).

**What was built**: these three values are `IntegrationWiringOptions` configuration knobs (with the documented defaults) rather than magic numbers baked into `DataverseWebApiServiceEndpointWebhookRegistrar.cs`. An operator/reviewer can override via app-setting without a code change if the target environment's live metadata differs, and the RECONFIRM caveat is called out explicitly in the options file's doc comments as an H0-preflight / operator-runbook item before a production customer stamp.

**Rationale**: honest engineering — rather than silently asserting a possibly-wrong constant, the value is surfaced as configurable + the uncertainty is documented at the point of decision (per CLAUDE.md §6.5 "the exception MUST be documented at the point of decision, not deferred").
