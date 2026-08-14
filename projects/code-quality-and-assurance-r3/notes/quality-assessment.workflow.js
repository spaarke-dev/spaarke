// ============================================================================
// quality-assessment.workflow.js
// ----------------------------------------------------------------------------
// The reusable Spaarke surface quality-assessment engine (r3 task 003, spec
// FR-02; design.md §6 "Engine (locked, owner Q1): a multi-agent Workflow").
//
// Encodes the method PROVEN on the BFF pass (2026-08-05/06: 6 parallel
// read-only investigations + 3-agent Fable verification — see
// projects/code-quality-and-assurance-r3/workstreams/bff-api/design.md),
// generalized to one finder per rubric dimension D1–D11
// (docs/standards/CODE-QUALITY-RUBRIC.md).
//
// THREE STAGES (POML 003 constraint #1):
//   1. fan-out      — 11 parallel READ-ONLY finder agents, one per rubric
//                     dimension, each returning structured findings with
//                     file:line evidence + an A–F dimension grade.
//   2. fable-verify — MANDATORY Fable adversarial verification (NFR-05,
//                     non-negotiable). Verifies each finding's file:line,
//                     checks test-only consumers (InternalsVisibleTo) and
//                     data-driven dispatch (Dataverse `sprk_*` rows, NFR-08),
//                     attempts refutation FIRST. Verdict per finding:
//                     CONFIRMED | REFUTED.
//   3. synthesize   — prioritized remediation design.md (severity, LOC,
//                     effort, risk, A/B tranche split) + the per-dimension
//                     SCORECARD row inputs. Consumes ONLY verified findings.
//
// HARD GATE (POML 003 constraint #3 / NFR-05): synthesis is STRUCTURALLY
// unable to run on unverified findings — the only collection handed to the
// synthesis agent is `verifiedFindings`, which is constructed exclusively by
// joining verdicts (verdict === 'CONFIRMED') against the deduped finding set.
// If ANY finding lacks a verdict the workflow throws; if the refutation rate
// exceeds 30% the workflow throws (escalation per tasks 010–015/017) — in
// both cases the synthesize phase never executes.
//
// READ-ONLY (POML 003 constraint #2 / NFR-03): finders and verifiers are
// spawned with agentType 'Explore' (no Edit/Write/NotebookEdit tools), so
// they CANNOT modify src/ — read-only is enforced structurally, not by
// prompt alone. The only write in the whole run is the synthesis agent
// writing the surface design.md under projects/.../workstreams/ (the
// sanctioned output; never src/, tests/, or .claude/).
//
// OPERATOR OPT-IN: the Workflow tool runs only on explicit per-run operator
// opt-in ("use a workflow"). See quality-assessment.workflow.README.md in
// this folder for invocation, args, and the manual fan-out fallback (an
// OPERATOR decision, never autonomous).
// ============================================================================

export const meta = {
  name: 'quality-assessment',
  description:
    'Spaarke surface quality assessment: 11 parallel read-only rubric-dimension finders (D1-D11) -> mandatory Fable adversarial verification (hard gate) -> synthesized prioritized remediation design.md + SCORECARD row inputs. Read-only over src/; per r3 spec FR-02 / NFR-03 / NFR-05.',
  phases: ['fan-out', 'dedup-gate', 'fable-verify', 'synthesize'],
};

// ---------------------------------------------------------------------------
// Constants — the rubric (D1–D11) and grading scale.
// Source of truth: docs/standards/CODE-QUALITY-RUBRIC.md (do not drift; if the
// rubric changes, update BOTH files). One finder per dimension — this is the
// documented generalization of the BFF pass's 6 investigation clusters
// (DI/services/endpoints/auth/dead-code/hygiene map onto D1–D11; the 11-way
// split keeps finder → SCORECARD-column mapping 1:1).
// ---------------------------------------------------------------------------

const GRADES = ['A+', 'A', 'A-', 'B+', 'B', 'B-', 'C+', 'C', 'C-', 'D+', 'D', 'D-', 'F'];
const SEVERITIES = ['CRITICAL', 'HIGH', 'MEDIUM', 'LOW', 'INFO'];

const DIMENSIONS = [
  {
    id: 'D1',
    title: 'Architecture & boundaries',
    aPlus: 'Clean layering; ADR adherence; no cross-boundary coupling (e.g. facade rules); no God classes / captive deps',
    guidance:
      'Investigate: layering violations; ADR adherence (esp. ADR-013 AI facade, ADR-010 DI minimalism, ADR-032 null-object seams, ADR-022 PCF platform libs, ADR-028 auth architecture); cross-boundary coupling; God classes (>800 LOC or >12 ctor deps — report exact LOC); DI module health (captive dependencies: singleton capturing scoped; monolithic registration methods); folder/namespace migrations left half-finished.',
  },
  {
    id: 'D2',
    title: 'Correctness & reliability',
    aPlus: 'No latent broken paths; defensive edges; deterministic behavior',
    guidance:
      'Investigate: latent broken paths (e.g. always-failing casts like `IDataverseService as ServiceClient`, unreachable branches on live paths); conflicting/duplicate DI registrations where last-wins changes behavior; swallowed exceptions on live paths; race conditions / async misuse that changes results; null-deref edges on reachable inputs. Trace reachability: a broken path only counts as live if you can cite the call chain (file:line at each hop).',
  },
  {
    id: 'D3',
    title: 'Security',
    aPlus: 'Auth on every data path; secrets in KV; input validation; XSS/injection boundaries; least privilege',
    guidance:
      'Investigate: anonymous or auth-by-omission endpoints (check for `.AllowAnonymous()`, missing `.RequireAuthorization()`, absence of a fallback policy — grep `SetFallbackPolicy`); any WRITE path without auth (highest severity); secrets in code/config vs Key Vault; input validation gaps; exception-detail leakage (`ex.Message` echoed to callers); XSS/injection boundaries; token handling; CORS. For client surfaces: `@spaarke/auth` usage vs ad-hoc token code (ADR-028).',
  },
  {
    id: 'D4',
    title: 'Performance & scalability',
    aPlus: 'No N+1 where avoidable; bounded caches; publish/bundle-size budgets; async correctness',
    guidance:
      'Investigate: N+1 query patterns; unbounded caches/collections; publish/bundle-size vs budget (BFF ceiling 60 MB compressed; PCF/code-page bundle sizes); sync-over-async (`.Result`, `.Wait()`, `GetAwaiter().GetResult()` on hot paths); missing pagination on large reads; chatty remote calls in loops. You may run read-only measurement commands (du/size listings) but MUST NOT build-modify anything.',
  },
  {
    id: 'D5',
    title: 'DRY / dead code',
    aPlus: 'One place per concept; no copy-paste; no orphaned/superseded code',
    guidance:
      'Investigate: copy-pasted helpers (report every copy with file:line); provably-dead files/members (for EACH dead claim you MUST record: zero DI registration, zero endpoint mapping, zero src/ reference, AND zero tests/ reference — assemblies may expose internals to test projects via InternalsVisibleTo, so tests/ consumers keep code alive for deletion purposes); superseded-but-not-deleted services; `_archive/` and `.archived-*` files; unused exports (client libs). WARNING: dispatch driven by Dataverse rows (`sprk_analysistool.sprk_handlerclass`, class-name discovery endpoints) and reflection is NOT grep-provable — flag such candidates with claimType data-driven-dispatch caveat rather than asserting dead. Deliberate seams (ADR-032 null-objects, feature-flag factories, stub swap paths) are NOT dead code — check wiring before claiming.',
  },
  {
    id: 'D6',
    title: 'Consistency & conventions',
    aPlus: 'Uniform naming, structure, error handling, logging; matches surrounding code',
    guidance:
      'Investigate: naming drift; structural inconsistency between sibling modules; inconsistent error handling/logging; misleading or false comments (e.g. "follows X exactly" when it does not); duplicate/colliding class names; namespace drift; convention violations vs docs/standards/CODING-STANDARDS.md and ANTI-PATTERNS.md.',
  },
  {
    id: 'D7',
    title: 'Testability & test quality',
    aPlus: 'ADR-038 KEEP categories; behavior over mocks; no scaffolding tests; green + trustworthy suite',
    guidance:
      'Investigate against ADR-038 (docs/adr/ADR-038-testing-strategy.md): tests exercising dead/unwired code; scaffolding-class tests (banned patterns: Mock<HttpMessageHandler>, DI-registration tests, ctor null-check tests); untested LIVE critical paths (cite the live path); archived/skipped test files; fixture-config drift (non-contract values). Coverage % is an observation, never a gate — do not grade on coverage numbers alone.',
  },
  {
    id: 'D8',
    title: 'Dependency & supply-chain hygiene',
    aPlus: 'No HIGH CVEs; pinned/consistent versions; fresh lockfiles; no needless transitive bloat',
    guidance:
      'Investigate: CVEs (`dotnet list package --vulnerable --include-transitive` / `npm audit` — read-only commands are allowed); version-pin consistency across projects; lockfile freshness/breakage (`npm ci` viability); needless transitive bloat; duplicate major versions of the same package (e.g. React version drift across client packages). NOTE known repo context: some packages are deliberately HELD (licensing — FluentAssertions 6.x, QuestPDF) and some pins are deliberate (shared-lib System.Security.Cryptography.* 10.0.11) — do not report deliberate holds as findings without checking notes/deferred-package-upgrades.md.',
  },
  {
    id: 'D9',
    title: 'Observability',
    aPlus: 'Structured logs + correlation IDs; no PII in logs; telemetry on critical paths',
    guidance:
      'Investigate: structured logging vs string interpolation; correlation-ID propagation; PII/secrets in log statements; telemetry presence on critical paths; console.log residue in client code; exception detail in logs vs responses (response-side leaks belong to D3 — cross-reference, do not double-count).',
  },
  {
    id: 'D10',
    title: 'ALM / build hygiene',
    aPlus: 'Solution segmentation; PCF lifecycle correctness; reproducible builds; analyzers-as-errors; clean CI',
    guidance:
      'Investigate: build artifacts committed to git (zips/tarballs — check `git ls-files` read-only); .gitignore gaps; solution segmentation (Power Apps); PCF lifecycle correctness (init/updateView/destroy; version bump discipline); reproducible builds; compiler warnings in Release; analyzer configuration; package.json sprawl / broken `npm ci`.',
  },
  {
    id: 'D11',
    title: 'Knowledge/doc accuracy',
    aPlus: '`.claude/` + `docs/` match code (no drift)',
    guidance:
      'Investigate: `.claude/patterns|constraints|adr` and `docs/` statements about THIS surface that no longer match the code (stale file:line refs, retired components still documented as live, and — worst — docs that actively mandate an anti-pattern the code has moved past). Read .claude/ freely (read-only); NEVER write to it. Cite both the doc location AND the contradicting code location.',
  },
];

// Escalation threshold — from tasks 010–015/017 <escalation>: if Fable refutes
// >30% of first-pass findings, STOP; do not synthesize (NFR-05).
const MAX_REFUTED_RATE = 0.3;
// Per-finder findings cap: prioritize by severity; keeps verification tractable.
const MAX_FINDINGS_PER_FINDER = 25;
// Verifier batch size (findings per Fable verification agent).
const VERIFY_BATCH_SIZE = 10;

// ---------------------------------------------------------------------------
// JSON Schemas (agent() validates returned objects against these).
// ---------------------------------------------------------------------------

const FINDER_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['dimension', 'grade', 'gradeRationale', 'findings'],
  properties: {
    dimension: { type: 'string', enum: DIMENSIONS.map((d) => d.id) },
    grade: { type: 'string', enum: GRADES },
    gradeRationale: {
      type: 'string',
      description: 'One paragraph: why this letter, anchored to the rubric §3 scale and the findings below.',
    },
    findings: {
      type: 'array',
      maxItems: MAX_FINDINGS_PER_FINDER,
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['title', 'severity', 'file', 'line', 'evidence', 'claimType'],
        properties: {
          title: { type: 'string', description: 'Short imperative-free description of the defect/debt.' },
          severity: { type: 'string', enum: SEVERITIES },
          file: { type: 'string', description: 'Repo-relative path (forward slashes).' },
          line: { type: 'integer', minimum: 1, description: 'Anchor line number. Use the primary anchor; extra sites go in evidence.' },
          evidence: {
            type: 'string',
            description:
              'Concrete evidence: what is at file:line, plus supporting refs (call chains, additional file:line sites, grep results). For dead-code claims MUST state the src/, tests/, DI-registration, and endpoint-mapping checks performed.',
          },
          claimType: {
            type: 'string',
            enum: [
              'dead-code',
              'duplication',
              'broken-path',
              'auth-gap',
              'boundary-violation',
              'perf',
              'convention',
              'test-quality',
              'dependency',
              'observability',
              'build-hygiene',
              'doc-drift',
              'data-driven-dispatch-caveat',
              'other',
            ],
          },
          locEstimate: { type: 'integer', minimum: 0, description: 'Approx LOC implicated (0 if n/a).' },
          suggestedRemediation: { type: 'string' },
        },
      },
    },
  },
};

const VERIFIER_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['verdicts'],
  properties: {
    verdicts: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['findingId', 'verdict', 'reason'],
        properties: {
          findingId: { type: 'string' },
          verdict: { type: 'string', enum: ['CONFIRMED', 'REFUTED'] },
          reason: {
            type: 'string',
            description:
              'For CONFIRMED: what you independently re-checked (file:line re-read, test/DI/dispatch checks). For REFUTED: the exact evidence that falsifies the claim (e.g. wired at file:line; consumed by test at file:line; ADR-032 seam).',
          },
          correctedFile: { type: 'string', description: 'If confirmed but the path drifted, the correct path.' },
          correctedLine: { type: 'integer', minimum: 1, description: 'If confirmed but the line drifted, the correct line.' },
          severityOverride: { type: 'string', enum: SEVERITIES, description: 'If the evidence supports a different severity.' },
          requiresDataverseCheck: {
            type: 'boolean',
            description:
              'true when the finding involves data-driven dispatch (sprk_* rows / class-name discovery) so remediation MUST run a live Dataverse pre-check before rename/delete (NFR-08). CONFIRMED verdicts with this flag are static-analysis-confirmed only.',
          },
        },
      },
    },
  },
};

const SYNTHESIS_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['designWritten', 'designPath', 'scorecard'],
  properties: {
    designWritten: { type: 'boolean' },
    designPath: { type: 'string' },
    scorecard: {
      type: 'object',
      additionalProperties: false,
      required: ['dimensionGrades', 'surfaceGrade', 'gatingCapApplied', 'evidenceBullets'],
      properties: {
        dimensionGrades: {
          type: 'object',
          additionalProperties: false,
          required: DIMENSIONS.map((d) => d.id),
          properties: Object.fromEntries(DIMENSIONS.map((d) => [d.id, { type: 'string', enum: GRADES }])),
        },
        surfaceGrade: {
          type: 'string',
          enum: GRADES,
          description: 'min(weighted-mean grade, D2, D3) per rubric §4.2 — the gating cap is not waivable.',
        },
        gatingCapApplied: { type: 'boolean', description: 'true if D2 or D3 capped the composed grade.' },
        evidenceBullets: {
          type: 'array',
          items: { type: 'string' },
          description: 'One bullet per dimension ("**D1 B** — ...") for the SCORECARD evidence section, citing verified findings.',
        },
      },
    },
  },
};

// ---------------------------------------------------------------------------
// Args — parameterizes the target surface (POML 003 acceptance: tasks
// 010–015/017 each pass their surface name + root path). Tolerates either an
// object or a "key=value; key=value" string.
//   surface      (required) slug, e.g. "shared-client-libs" — names the
//                workstream folder + SCORECARD row.
//   rootPaths    (required) comma-separated repo-relative roots to assess,
//                e.g. "src/client/shared".
//   surfaceTitle (optional) human title for the design/scorecard row.
//   designPath   (optional) output path; default
//                projects/code-quality-and-assurance-r3/workstreams/{surface}/design.md
//   extraContext (optional) surface-specific notes: known KEEPs/seams, prior
//                findings, exclusions, things the finders should know.
//   excludePaths (optional) comma-separated paths to skip.
// ---------------------------------------------------------------------------

function parseArgs(raw) {
  let a = raw;
  if (typeof raw === 'string') {
    const trimmed = raw.trim();
    a = null;
    // The Workflow runtime may expose the `args` object as a JSON string — accept that first.
    if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
      try { a = JSON.parse(trimmed); } catch { a = null; }
    }
    // Fallback: "key=value; key=value" string form.
    if (!a || typeof a !== 'object') {
      a = {};
      for (const part of raw.split(/[;\n]/)) {
        const m = part.match(/^\s*([A-Za-z][A-Za-z0-9_]*)\s*=\s*(.+?)\s*$/);
        if (m) a[m[1]] = m[2];
      }
    }
  }
  a = a || {};
  if (!a.surface || !a.rootPaths) {
    throw new Error(
      'quality-assessment workflow requires args: surface=<slug>; rootPaths=<comma-separated repo-relative roots>. ' +
        'Optional: surfaceTitle, designPath, extraContext, excludePaths. ' +
        'Example: surface=shared-client-libs; rootPaths=src/client/shared; surfaceTitle=Shared client libs (Spaarke.*)',
    );
  }
  const rootPaths = Array.isArray(a.rootPaths) ? a.rootPaths : String(a.rootPaths).split(',').map((s) => s.trim()).filter(Boolean);
  const excludePaths = a.excludePaths
    ? (Array.isArray(a.excludePaths) ? a.excludePaths : String(a.excludePaths).split(',').map((s) => s.trim()).filter(Boolean))
    : [];
  return {
    surface: String(a.surface).trim(),
    surfaceTitle: a.surfaceTitle ? String(a.surfaceTitle).trim() : String(a.surface).trim(),
    rootPaths,
    excludePaths,
    designPath:
      a.designPath ||
      `projects/code-quality-and-assurance-r3/workstreams/${String(a.surface).trim()}/design.md`,
    extraContext: a.extraContext ? String(a.extraContext) : '',
  };
}

// ---------------------------------------------------------------------------
// Prompt builders.
// ---------------------------------------------------------------------------

function finderPrompt(dim, cfg) {
  return `You are a READ-ONLY code-quality finder for the Spaarke surface "${cfg.surfaceTitle}" (roots: ${cfg.rootPaths.join(
    ', ',
  )}${cfg.excludePaths.length ? `; EXCLUDE: ${cfg.excludePaths.join(', ')}` : ''}).

You own exactly ONE rubric dimension:
  ${dim.id} — ${dim.title}
  "What A+ looks like": ${dim.aPlus}

Rubric: docs/standards/CODE-QUALITY-RUBRIC.md (read §2 for your dimension row and §3 for the A–F scale — grade against §3, not vibes).

INVESTIGATION GUIDANCE for ${dim.id}:
${dim.guidance}

${cfg.extraContext ? `SURFACE-SPECIFIC CONTEXT (from the invoking task — respect known KEEPs/seams):\n${cfg.extraContext}\n` : ''}RULES (binding):
- READ-ONLY (r3 spec NFR-03). You have no write tools; do not attempt to modify, build-mutate, or format anything. Read-only shell commands (grep/ls/git ls-files/du, dotnet list package --vulnerable, npm audit --dry-run style) are permitted.
- Stay inside your dimension. If you notice a finding belonging to another dimension, include it ONLY if material, marked in evidence as "cross-dimension: Dx".
- EVERY finding needs concrete file:line evidence. A claim without a citable location is not a finding — drop it.
- Dead-code discipline: check src/ AND tests/ (InternalsVisibleTo can make test projects the only consumer) AND DI registration AND endpoint mapping before claiming dead. State the checks performed in the evidence field.
- Data-driven dispatch (Dataverse sprk_* rows, class-name discovery, reflection) is NOT grep-provable — use claimType "data-driven-dispatch-caveat" for such candidates instead of asserting dead/renamable.
- Report at most ${MAX_FINDINGS_PER_FINDER} findings, prioritized by severity; if you hit the cap, say so in gradeRationale.
- Your findings will face an adversarial Fable verification pass that attempts to REFUTE each one. Only claims that survive count. Precision beats volume.

Return the structured object per the schema: your dimension id, an A–F grade for ${dim.id} on this surface (per rubric §3), a grade rationale, and the findings array.`;
}

function verifierPrompt(batch, cfg) {
  const findingsBlock = JSON.stringify(batch, null, 2);
  return `You are the MANDATORY Fable ADVERSARIAL VERIFICATION stage of the Spaarke quality-assessment workflow (r3 spec NFR-05 — non-negotiable; on the BFF pass this stage caught 2 real production bugs AND refuted 2 false-positive "dead code" claims that were load-bearing).

Surface: "${cfg.surfaceTitle}" (roots: ${cfg.rootPaths.join(', ')}).

Your stance is REFUTATION-FIRST: for each finding below, actively try to prove it WRONG before you confirm it. You are read-only.

For EACH finding:
1. Re-open the cited file at the cited line yourself. Confirm the evidence text/construct is actually there. If the file/line has drifted but the claim holds, CONFIRM with correctedFile/correctedLine.
2. For dead-code / deletion / unused claims: independently re-check (a) src/ references, (b) tests/ references — assemblies may expose internals to test projects via InternalsVisibleTo, making a test the live consumer; (c) DI registration (all *Module.cs / Program.cs paths); (d) endpoint mapping; (e) deliberate seams — ADR-032 null-object/kill-switch layers, feature-flag factories, stub swap paths are INTENTIONAL and wired (e.g. a stub registered in a DI module is NOT dead). A wired seam claim => REFUTED with the wiring file:line.
3. For rename/delete claims on anything dispatch-shaped: check for data-driven dispatch — Dataverse rows (e.g. sprk_analysistool.sprk_handlerclass), class-name-based discovery endpoints, reflection. Grep is NOT proof of safety there (NFR-08): if the static evidence holds but live-config could still reference it, CONFIRM with requiresDataverseCheck=true so remediation runs a live Dataverse pre-check first.
4. For broken-path / correctness claims: hand-trace the call chain; confirm the path is actually reachable in production wiring (cite the chain) — an unreachable "bug" is REFUTED (or severity-overridden to INFO if worth recording).
5. For auth/security claims: verify no filter/middleware/fallback-policy elsewhere covers the endpoint before confirming an exposure.
6. If the evidence supports a different severity, set severityOverride.

Verdicts: CONFIRMED (survived your refutation attempt; reason states what you independently re-checked) or REFUTED (reason states the falsifying evidence with file:line). Every finding gets exactly one verdict — no abstentions.

FINDINGS TO VERIFY:
${findingsBlock}

Return the structured object per the schema (verdicts array, one entry per findingId above).`;
}

function synthesisPrompt(cfg, verifiedFindings, refuted, dimensionGradeInputs) {
  return `You are the SYNTHESIS stage of the Spaarke quality-assessment workflow for surface "${cfg.surfaceTitle}".

INPUT DISCIPLINE (hard gate, r3 spec NFR-05): the findings below are the ONLY findings that exist for your purposes — every one has survived the mandatory Fable adversarial-verification stage. Refuted claims are listed separately for the record ONLY; they MUST NOT appear as findings, drive grades, or generate remediation items (you may list them in a short "Refuted by verification (do NOT act on)" appendix so future passes don't re-claim them — the BFF pass's KEEPs section is the model).

VERIFIED FINDINGS (verdict=CONFIRMED, with any corrected file:line and severity overrides already applied):
${JSON.stringify(verifiedFindings, null, 2)}

REFUTED CLAIMS (record-only appendix material):
${JSON.stringify(refuted.map((r) => ({ findingId: r.findingId, title: r.title, reason: r.reason })), null, 2)}

FIRST-PASS DIMENSION GRADES + rationales (from the finders — you must RE-ADJUDICATE these using only verified findings):
${JSON.stringify(dimensionGradeInputs, null, 2)}

YOUR TASKS:
1. Re-adjudicate each dimension grade D1–D11 against docs/standards/CODE-QUALITY-RUBRIC.md §3, using ONLY verified findings (a dimension whose worst findings were refuted moves UP; findings with requiresDataverseCheck stay findings). Compose the surface grade per rubric §4.2: equal-weight mean of the 11 grade-points, then the non-waivable gating cap min(mean, D2, D3).
2. WRITE the prioritized remediation design.md to exactly this path: ${cfg.designPath}
   Model it on projects/code-quality-and-assurance-r3/workstreams/bff-api/design.md. Required sections:
   - Header: surface, date, method ("quality-assessment workflow: 11-dimension fan-out + Fable adversarial verification"), read-only statement.
   - §0 Summary & verdict, with the per-area grade table.
   - Current-state inventory: every verified finding with severity, file:line, LOC estimate, effort (S/M/L), risk (low/med/high), and remediation.
   - Explicit KEEPs / refuted-claims appendix (record-only).
   - Data-driven-dispatch pre-check list: every finding flagged requiresDataverseCheck, with the exact Dataverse check remediation must run first (NFR-08).
   - Proposed workstreams → phases with an A/B tranche split (A = low-contention bugs/hygiene first; B = wide/contested edits for a quiet window) — per r3 NFR-04.
   - SCORECARD row inputs section (the 11 letters + composed surface grade + one evidence bullet per dimension).
   WRITE CONSTRAINT (r3 NFR-03): ${cfg.designPath} is the ONLY file you may write. You MUST NOT modify anything under src/, tests/, docs/, or .claude/ — this is a read-only assessment whose sole output is the design + scorecard inputs. Do NOT edit notes/SCORECARD.md yourself; the invoking task appends the row.
3. Return the structured object per the schema: designWritten, designPath, and the scorecard block (dimensionGrades, surfaceGrade with gating cap, gatingCapApplied, evidenceBullets — one per dimension, formatted like "**D1 B** — ...").`;
}

// ---------------------------------------------------------------------------
// Helpers.
// ---------------------------------------------------------------------------

function chunk(arr, size) {
  const out = [];
  for (let i = 0; i < arr.length; i += size) out.push(arr.slice(i, i + size));
  return out;
}

function gradePoints(g) {
  const map = { 'A+': 4.3, A: 4.0, 'A-': 3.7, 'B+': 3.3, B: 3.0, 'B-': 2.7, 'C+': 2.3, C: 2.0, 'C-': 1.7, 'D+': 1.3, D: 1.0, 'D-': 0.7, F: 0.0 };
  return map[g];
}

// ===========================================================================
// SCRIPT BODY
// ===========================================================================

const cfg = parseArgs(args);

log(
  `quality-assessment: surface="${cfg.surfaceTitle}" roots=[${cfg.rootPaths.join(', ')}] design→${cfg.designPath}. ` +
    `Method: 11-dimension read-only fan-out → Fable adversarial verify (hard gate) → synthesis. ` +
    `Reminder: this run exists because the operator opted in ("use a workflow") — per-run opt-in is required.`,
);

// ---------------------------------------------------------------------------
// PHASE 1 — fan-out. Parallel READ-ONLY finders, one per rubric dimension.
// parallel() is the right primitive here (not pipeline): the dedup step below
// is a genuine cross-item barrier — every finder must land before findings
// can be deduped and batched for verification.
// Maps to POML 003 constraint #1 stage (1) + constraint #2 (read-only:
// agentType 'Explore' has no write tools).
// ---------------------------------------------------------------------------

phase('fan-out');

const finderResults = await parallel(
  DIMENSIONS.map((dim) => () =>
    agent(finderPrompt(dim, cfg), {
      label: `finder-${dim.id}`,
      phase: 'fan-out',
      schema: FINDER_SCHEMA,
      agentType: 'Explore', // read-only enforcement (NFR-03): no Edit/Write tools
      effort: 'high',
      // model: inherit session default — finders don't need Fable (per task 003 spec);
      // the adversarial stage below is where Fable is mandatory.
    }),
  ),
);

// ---------------------------------------------------------------------------
// PHASE 2 — dedup-gate (barrier). Collect all findings, assign stable IDs,
// merge exact file:line duplicates reported by multiple dimensions (near-dupes
// are left to the verifier/synthesis, which see full context). This is one of
// the two legitimate barriers (dedup-before-verify).
// ---------------------------------------------------------------------------

phase('dedup-gate');

const allFindings = [];
const dimensionGradeInputs = [];
for (const res of finderResults) {
  dimensionGradeInputs.push({ dimension: res.dimension, firstPassGrade: res.grade, gradeRationale: res.gradeRationale });
  res.findings.forEach((f, i) => {
    allFindings.push({
      findingId: `${res.dimension}-${String(i + 1).padStart(2, '0')}`,
      dimension: res.dimension,
      ...f,
    });
  });
}

const byLocation = new Map();
const deduped = [];
for (const f of allFindings) {
  const key = `${f.file.replace(/\\/g, '/').toLowerCase()}#${f.line}`;
  const existing = byLocation.get(key);
  if (existing && existing.claimType === f.claimType) {
    existing.alsoReportedBy = [...(existing.alsoReportedBy || []), f.dimension];
    existing.evidence += `\n[dup ${f.findingId} from ${f.dimension}]: ${f.evidence}`;
  } else {
    byLocation.set(key, f);
    deduped.push(f);
  }
}

log(`fan-out complete: ${allFindings.length} raw findings → ${deduped.length} after exact-location dedup, across ${finderResults.length} dimensions.`);

if (deduped.length === 0) {
  // Nothing to verify — still a legitimate outcome (a clean surface), but the
  // Fable stage must still adjudicate the grades, so we fall through with an
  // empty verification set rather than skipping to synthesis unverified.
  log('WARNING: zero findings from fan-out. Grades will be Fable-adjudicated at synthesis; treat an all-A surface with suspicion.');
}

// ---------------------------------------------------------------------------
// PHASE 3 — fable-verify. MANDATORY adversarial verification (NFR-05).
// pipeline() over batches: each batch is verified independently (no cross-batch
// dependency), so per-item flow is correct here — the barrier comes AFTER, when
// verdicts are joined and the gate is evaluated.
// Maps to POML 003 constraint #1 stage (2) + constraint #3 (hard gate).
// model:'fable' is REQUIRED here — do not downgrade this stage.
// ---------------------------------------------------------------------------

phase('fable-verify');

// Batch by dimension first (context locality), then split oversized groups.
const byDim = new Map();
for (const f of deduped) {
  if (!byDim.has(f.dimension)) byDim.set(f.dimension, []);
  byDim.get(f.dimension).push(f);
}
const verifyBatches = [];
for (const group of byDim.values()) verifyBatches.push(...chunk(group, VERIFY_BATCH_SIZE));

const verdictBatches = await pipeline(verifyBatches, (batch) =>
  agent(verifierPrompt(batch, cfg), {
    label: `fable-verify-${batch[0].dimension}`,
    phase: 'fable-verify',
    schema: VERIFIER_SCHEMA,
    agentType: 'Explore', // verification is also read-only
    model: 'fable', // MANDATORY (NFR-05) — the adversarial pass is Fable, non-negotiable
    effort: 'xhigh',
  }),
);

// ----- THE HARD GATE (NFR-05) ----------------------------------------------
// Synthesis is structurally unable to see unverified findings:
//   * verdict join: every finding must have exactly one verdict, else THROW —
//     the workflow dies before the synthesize phase can start.
//   * refutation-rate escalation (>30%): THROW — per tasks 010–015/017
//     <escalation>, a first pass this wrong must be reported, not synthesized.
//   * `verifiedFindings` (the ONLY collection handed to synthesis) is built
//     exclusively from verdict === 'CONFIRMED'.
// There is NO code path from `allFindings`/`deduped` into synthesisPrompt.
// ---------------------------------------------------------------------------

const verdictById = new Map();
for (const vb of verdictBatches) {
  for (const v of vb.verdicts) {
    if (verdictById.has(v.findingId)) {
      throw new Error(`VERIFY GATE VIOLATION: duplicate verdict for ${v.findingId}. Aborting before synthesis (NFR-05).`);
    }
    verdictById.set(v.findingId, v);
  }
}

const missing = deduped.filter((f) => !verdictById.has(f.findingId)).map((f) => f.findingId);
if (missing.length > 0) {
  throw new Error(
    `VERIFY GATE VIOLATION: ${missing.length} finding(s) received no Fable verdict [${missing.join(', ')}]. ` +
      'Synthesis MUST NOT run on unverified findings (NFR-05). Aborting — re-run the workflow.',
  );
}

const verifiedFindings = [];
const refuted = [];
for (const f of deduped) {
  const v = verdictById.get(f.findingId);
  if (v.verdict === 'CONFIRMED') {
    verifiedFindings.push({
      ...f,
      file: v.correctedFile || f.file,
      line: v.correctedLine || f.line,
      severity: v.severityOverride || f.severity,
      requiresDataverseCheck: v.requiresDataverseCheck === true,
      verification: v.reason,
    });
  } else {
    refuted.push({ ...f, reason: v.reason });
  }
}

const refutedRate = deduped.length === 0 ? 0 : refuted.length / deduped.length;
log(
  `fable-verify complete: ${verifiedFindings.length} CONFIRMED / ${refuted.length} REFUTED ` +
    `(refutation rate ${(refutedRate * 100).toFixed(1)}%; escalation threshold ${MAX_REFUTED_RATE * 100}%).`,
);

if (refutedRate > MAX_REFUTED_RATE) {
  throw new Error(
    `ESCALATION (NFR-05): Fable refuted ${(refutedRate * 100).toFixed(1)}% of first-pass findings (>${MAX_REFUTED_RATE * 100}%). ` +
      'Per the surface-task escalation trigger, the remediation design is NOT synthesized from a first pass this unreliable. ' +
      'STOP and report to the operator; inspect the refuted list in the transcript, fix the finder prompts/scope, and re-run.',
  );
}

// ---------------------------------------------------------------------------
// PHASE 4 — synthesize (barrier — needs ALL verified findings + all grades).
// One Fable agent: re-adjudicates D1–D11 grades on verified evidence only,
// applies the rubric §4.2 gating cap, writes the remediation design.md
// (severity/LOC/effort/risk + A/B tranche split), and returns the SCORECARD
// row inputs. Maps to POML 003 constraint #1 stage (3).
// The ONLY write of the entire workflow is cfg.designPath (never src/).
// ---------------------------------------------------------------------------

phase('synthesize');

const synth = await agent(synthesisPrompt(cfg, verifiedFindings, refuted, dimensionGradeInputs), {
  label: 'synthesize-design',
  phase: 'synthesize',
  schema: SYNTHESIS_SCHEMA,
  model: 'fable',
  effort: 'xhigh',
  // NOT 'Explore': this agent needs Write for the single sanctioned output
  // (cfg.designPath). Its prompt hard-constrains all other writes.
});

if (!synth.designWritten) {
  throw new Error(`Synthesis reported designWritten=false for ${synth.designPath} — assessment is incomplete.`);
}

// Sanity: recompute the composed grade in-script and warn on mismatch (the
// agent's letter stands, but a mismatch is surfaced for the operator).
const dg = synth.scorecard.dimensionGrades;
const mean = DIMENSIONS.reduce((s, d) => s + gradePoints(dg[d.id]), 0) / DIMENSIONS.length;
const cap = Math.min(mean, gradePoints(dg.D2), gradePoints(dg.D3));
log(
  `Grade check: equal-weight mean=${mean.toFixed(2)} pts; gating cap min(mean, D2=${dg.D2}, D3=${dg.D3})=${cap.toFixed(2)} pts; ` +
    `synthesis surfaceGrade=${synth.scorecard.surfaceGrade} (gatingCapApplied=${synth.scorecard.gatingCapApplied}). ` +
    'If the letter and points disagree, resolve manually before appending the SCORECARD row.',
);

// ---------------------------------------------------------------------------
// Final output — SCORECARD row inputs for the invoking task (which appends the
// row to notes/SCORECARD.md itself; this workflow does not touch SCORECARD.md).
// ---------------------------------------------------------------------------

const today = new Date().toISOString().slice(0, 10);
const row =
  `| **${cfg.surfaceTitle}** | ${DIMENSIONS.map((d) => dg[d.id]).join(' | ')} | ${today} (Fable-verified) | ` +
  `[\`${cfg.designPath}\`](${cfg.designPath}) |`;

log('=== quality-assessment COMPLETE ===');
log(`Design written: ${synth.designPath}`);
log(`Surface grade: ${synth.scorecard.surfaceGrade} (gating cap applied: ${synth.scorecard.gatingCapApplied})`);
log(`Verified findings: ${verifiedFindings.length}; refuted (record-only): ${refuted.length}; Dataverse pre-check flagged: ${verifiedFindings.filter((f) => f.requiresDataverseCheck).length}`);
log('SCORECARD row (append via the invoking task — one row per surface, no aggregate until all surfaces scored):');
log(row);
log('SCORECARD evidence bullets:');
for (const b of synth.scorecard.evidenceBullets) log(`  ${b}`);
