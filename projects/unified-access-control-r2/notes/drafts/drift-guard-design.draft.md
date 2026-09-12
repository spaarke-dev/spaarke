# Draft — `WorkloadPlacementDocDriftTests` design (task 102, step 5)

> Adopted from the Fable-tier review's recommendation (2026-09-12), with the scope aligned to the ArchTests
> conventions in `tests/CLAUDE.md` (negative + positive controls, reasoned allowlist, maintenance procedure in
> the file).

## Purpose
A tripwire, not a parser: ADR-052 is the only full statement of the placement rule, so any *contradicting*
phrasing that reappears in a directive or document fails the build, pointing the author at ADR-052 and at the
allow-marker if the text is genuinely historical.

## Scanned roots
`.claude/**` · `docs/**` · `.github/**` · `.coderabbit.yaml` · root `CLAUDE.md` and `README.md` ·
`src/**` and `infra*/**` comments · `tests/**` (the guard file excluded) · `projects/<active>/CLAUDE.md`, where
"active" = rows in `projects/INDEX.md`.

**Path excludes** (history, not directives): `.claude/archive/**` · `.claude/agent-memory/**` · `knowledge/**` ·
`projects/**` except active projects' `CLAUDE.md`.

## Banned phrasings (case-insensitive regex)
```
\bno Azure Functions\b
\bnot Azure Functions\b
do(?:n'?t| not) use Azure Functions
Functions are (?:not permitted|prohibited|forbidden|banned|discouraged)
(?:avoid|prohibit\w*) (?:Azure )?Functions
Functions[^.\n]{0,80}out-of-band|out-of-band[^.\n]{0,80}Functions
(?:no|not|never|do not introduce|MUST NOT use) Durable Functions
Durable Functions \(always
IJobHandler<
event-driven \(timer, queue, webhook\)
(?:timer|queue|webhook)[^.\n]{0,40}(?:→|->) (?:Azure )?Functions
ADR-001 prefers in-process
in-process workers; no Azure Functions
default to BackgroundService when
\(use Service Bus \+ state machine
```

## Allowlist — by region markers, not by file
- Markdown: `<!-- adr052-drift:allow reason="…" -->` … `<!-- /adr052-drift:allow -->`
- C#: `// adr052-drift:allow reason="…"` on the line, or a `// adr052-drift:allow-begin` … `// adr052-drift:allow-end` block.
- **A marker without a `reason` is itself a failure.**

**Required marked regions:** ADR-052's Context table · ADR-001's retained original sections and A1's superseded
table · ADR-036 A1 §1 · ADR-004 A1's quoted rule · the `.claude/CHANGELOG.md` entry · the evaluation note (excluded
anyway as `projects/**`) · task 102's POML acceptance criteria (excluded anyway) · the guard's own banned-pattern
list (the guard file is excluded).

## Controls (per `tests/CLAUDE.md`)
- **Negative control**: scanning a synthetic in-memory document containing each banned phrase reports each one.
- **Negative control**: a marker with no `reason` is reported.
- **Positive control**: the same phrases inside a reasoned marker are not reported; the aligned repository passes.

## Known limits (stated in the test's doc comment)
- False negatives: paraphrases ("in-process only", "prefer BackgroundService"). Accepted — it is a tripwire.
- False positives: inventory statements ("zero Azure Functions today") — use a marker.

## Maintenance procedure (in the file)
When the guard fires: (1) if the text contradicts ADR-052, rewrite it as a one-line summary plus a link; (2) if it
is genuinely historical (a superseded rule quoted as history), wrap it in a reasoned marker; (3) never widen the
path excludes to make a directive pass.
