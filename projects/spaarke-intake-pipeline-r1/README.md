# Spaarke Intake Pipeline R1 (`spaarke-intake-pipeline-r1`)

> **Portfolio**: [Project #3128](https://github.com/spaarke-dev/spaarke/issues/3128) · Epic: [#421](https://github.com/spaarke-dev/spaarke/issues/421) · [Portfolio board view](https://github.com/users/spaarke-dev/projects/2)
> **Status:** Design draft (v0.1, 2026-11-25). No spec or tasks yet. Next: owner resolves `design.md` §13, then `/design-to-spec`.
> **Branch:** `work/spaarke-intake-pipeline-r1` · **Worktree:** `C:/code_files/spaarke-wt-spaarke-intake-pipeline-r1`
> **Origin:** split out of `specialist-legal-agents-r1` (owner, 2026-11-25).

One process model for **requests**: someone sends content to Legal (an NDA for review, an invoice, a Front Door request) and expects an outcome back.

Shape:
- **Doors:** email, Front Door, a Copilot agent (Teams via the agent), Console upload, the invoice routes.
- Each door feeds one orchestrator, which tracks every pass through the process as a **run** (`sprk_processingjob`) on the request (`sprk_servicerequest`).
- The orchestrator classifies the content and routes it to a **harness** for that kind of work. NDA review comes first, using the accelerator's redline engines, and produces a marked-up Word file plus a clause table.
- An attorney approves; the result returns through the door it came in by, and the decision goes into the Decision Record.

Content filing (profile and index) stays a separate, simpler level.

| File | Purpose |
|---|---|
| [design.md](design.md) | Design draft: architecture, records, stages, NDA harness, invoice migration, cleanup, ADR tensions, open questions |
| [notes/code-review-evidence-2026-11-24.md](notes/code-review-evidence-2026-11-24.md) | Evidence: three Fable code reviews of master `b005279c4d` (intake paths, invoice pipeline, routing, run records, outputs, return) |
