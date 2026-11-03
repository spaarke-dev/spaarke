# Step 6.5 — BFF Hygiene Check (CLAUDE.md §10)

Read by `code-review` Step 6.5 when any file in scope is under:
- `src/server/api/Sprk.Bff.Api/**`
- `src/server/shared/Spaarke.Core/**`
- `src/server/shared/Spaarke.Dataverse/**`

Run the binding §10 pre-merge checklist from `.claude/constraints/bff-extensions.md`:

```
LOAD .claude/constraints/bff-extensions.md (full file — it is the source of truth)

APPLY Section A (Pre-Merge Checklist — Binding):
  1. Placement Justification stated in PR description or design.md?
     (Even "obviously in BFF" requires a one-sentence justification per §10.)
  2. Relevant ADRs cited in PR/design? (ADR-001, ADR-007, ADR-008, ADR-010, ADR-013 most common)
  3. Publish-size delta reported? (EVERY BFF-touching task, not only package changes)
     - Rule: .claude/rules/bff-hygiene.md item 4 (delta vs a FRESH master build, both from
       short-path worktrees; thresholds +5 MB / 55 MB / 60 MB)
     - Procedure and current baseline: .claude/constraints/azure-deployment.md
       "BFF Publish-Size Per-Task Verification Rule" — do not use a remembered baseline
  4. NO new direct CRUD→AI dependency?
     - CHECK: grep diff for new injections of IOpenAiClient, IPlaybookService, or other Services/Ai/ internal types into code OUTSIDE Services/Ai/
     - If found in CRUD code (Finance/Workspace/Jobs handlers outside Services/Ai/) → Violation; must use Services/Ai/PublicContracts/ facade
  5. Feature-module DI used? (per ADR-010 — Add{Feature}Module() extension, not flat Program.cs registrations)

APPLY Section B (New Package References) — only if .csproj changed:
  - dotnet list package --vulnerable --include-transitive — flag any new HIGH-severity CVE
  - Pre-release packages (-beta, -rc, -preview) require inline csproj comment justifying

APPLY Section C (New Endpoints) — only if MapPost/MapPut/MapGet added:
  - Minimal API (not MVC controllers)?
  - Endpoint-filter authorization (.AddEndpointFilter<...>())?
  - Results.Problem(...) for errors (RFC 7807)?
  - Registered via Map{Feature}Endpoints extension, NOT directly in Program.cs?

APPLY Section D (New Background Work) — only if IHostedService/IJobHandler/IScheduledJob added:
  - Host decided under ADR-052 and stated in the Placement Justification?
  - Inside the BFF: queue → IJobHandler (ADR-004), schedule → IScheduledJob (ADR-036) — not a hand-rolled timer BackgroundService?
  - AI-coupled jobs in Services/Ai/Jobs/ (not Services/Jobs/Handlers/)?

FLAG SEVERITIES:
  - Missing Placement Justification → Critical (binding §10 imperative)
  - New direct CRUD→AI dep → Critical (per refined ADR-013)
  - New HIGH-severity CVE → Critical. If no fixed version exists upstream it stays Critical until the
    owner's sign-off appears in the PR; the implementer's part is the record in .claude/rules/bff-hygiene.md
    item 5 plus a root §6 escalation
  - Endpoint added directly in Program.cs → Warning (ADR-001/008)
  - Feature-module pattern not followed → Warning (ADR-010)
  - Publish-size delta not reported → Warning (binding rule; ask for the measurement)
```

Full rules: `.claude/constraints/bff-extensions.md`. Evidence base: `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`.
