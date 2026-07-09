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
