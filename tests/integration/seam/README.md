# `tests/integration/seam/**` — ADR-043 vertical-slice KEEP category

**Established by**: `spaarke-ai-architecture-redesign-r2` task AIR2-E10 (ADR-043 Move 1), 2026-07-09.

## What this category is

The **execution-spine vertical-slice** KEEP path (ADR-043 §Governance, added to the ADR-038 KEEP
paths). A seam test drives a real consumer input **all the way through** the execution spine:

```
consumer input → dispatch → ContextBinder input resolution → ActionRunner completion
              → OutputRouter → stored SessionOutput (ADR-040) → rendered terminal frame
```

It is the **definition-of-done for any dispatch/execution change**. A green *contract-shape* test
(shape of a request/response, a DI registration, a serialization round-trip) is **NOT sufficient** —
that is exactly how prior work shipped "done" while the wired path was 422-broken. A seam test MUST
fail if input resolution is stubbed / the operand never reaches the prompt / the output is not stored
before render.

## Sub-category: `seam/Auth/**` — credential-selection seams

**Established by**: `spaarke-auth-v4-dataverse-MI` task 090, 2026-08-24. Adjudicated deliberately so that
seven credential-seam files do not inherit deletion protection **by accident**.

The spine above is the AI execution spine. `seam/Auth/**` is a **second seam of the same kind**: the
credential-selection spine, driven end-to-end rather than shape-checked.

```
config (Graph:Credentials:Order) → OrderedCredentialClientProvider → IdentityConfigurationValidator
                                 → the confidential client actually constructed → the credential it is bound to
```

**Why here and not `tests/integration/auth/**`**: that directory contains only a `README.md` and is **not
compiled into any `.csproj`** — a test authored there would run *never*, which is strictly worse than a
misfiled test. `seam/**` is compiled (see below), so these files actually execute. Verified 2026-08-24.

Same bar applies: a green contract-shape test is not sufficient. An auth seam test MUST fail if the ordered
provider silently falls through to the next credential, or if a validator that should fail fast does not.
This is the category that caught the near-miss in which removing `ClientSecret` from the code-side canonical
default would have broken **every unconfigured environment** (`notes/decisions/033-secret-removal.md` §6).

The general lesson these encode: **a status code never establishes an outcome here** — the ordered provider
returns 200 whichever credential it used. Where a log line or byte-level artifact is unavailable, the seam
test removes the fallback so that success is the proof.

Files: `CredentialOrderingSeamTests`, `CredentialSelectionSeamTests`, `IdentityConflationSeamTests`,
`ConfidentialClientMigrationSeamTests`, `ConfidentialClientSharingSeamTests`, `ClientAssertionProviderSeamTests`,
`ServiceBusCredentialSeamTests`.

> Structural credential invariants (the FR-F1 ban and the FR-F2 census) are **not** here — they live in
> `tests/Spaarke.ArchTests/**`, a separate KEEP path since **ADR-038 Amendment A1** (2026-08-24). Behaviour
> here; structure there.

## Rules (per ADR-038 + ADR-043)

- **Real path, not mocked.** The binder, the completion engine, the output router, and the session
  ledger are the PRODUCTION types. Only the external LLM boundary (`IOpenAiClient`) and the catalog
  data boundaries (`IConsumerRoutingService` / `IScopeResolverService` / `ISessionFileTextSource`) are
  test doubles — mocking `ContextBinder`, `ActionRunner`, or `OutputRouter` defeats the category.
- **Observe stored state.** Assert against the stored `SessionOutput` (ledger) and the rendered frame,
  not against a pre-store local.
- **Deletion-protected.** Removing a file here requires a same-PR replacement covering the same slice.

## Compilation

Compiled INTO `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` via a `<Compile Include>` glob
(same pattern as `contract/**` and `regression/**`) so the production fixtures
(`TestableChatSessionManager`, `StubOpenAiClient`, `InMemoryTenantCache`) are reused without
duplication. CI picks these up automatically with the rest of the suite.
