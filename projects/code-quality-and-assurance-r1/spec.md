# Code Quality and Assurance R1 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-11
> **Source**: design.md

## Executive Summary

Establish a comprehensive code quality assurance system for Spaarke's 100% AI-generated codebase. This project combines an initial code quality audit (to baseline and remediate) with an ongoing automated quality system (to prevent regression). The system operates across four layers: PR-time checks (<5 min), nightly deep analysis, on-demand Claude Code skills, and quarterly comprehensive audits.

## Scope

### In Scope

**Domain A: Initial Code Quality Audit**
- Dead code elimination across C# (525 files), TypeScript (955 files), PowerShell (114 files)
- Abstraction audit (interfaces with single implementations per ADR-010)
- Pattern consistency analysis (endpoints, PCF controls, services)
- Error handling audit (defensive overkill, catch-log-rethrow patterns)
- Comment quality review (remove code-restating comments, resolve TODOs)
- File/method size analysis (Program.cs at 1,940 lines is primary target)
- Test quality assessment (behavior vs implementation testing)
- Dependency health audit (MimeKit 4.14.0 known issue)
- Quality scorecard generation (A-F per area)

**Domain B: PR-Time Quality Checks (Layer 1)**
- GitHub Actions PR workflow completing in <5 minutes
- `dotnet build -warnaserror` (blocking)
- `dotnet format --verify-no-changes` (blocking)
- `npm run lint` with strict ESLint rules (blocking)
- `dotnet test Spaarke.ArchTests` (blocking)
- CodeRabbit AI PR review (advisory)
- Claude Code Action architecture review (advisory)
- Trivy dependency vulnerability scan (existing, keep)

**Domain C: Nightly Quality Automation (Layer 2)**
- Scheduled GitHub Actions workflow (weeknights, 6 AM UTC / midnight MST)
- Full test suite with Coverlet code coverage and trend tracking
- SonarCloud deep analysis (quality gate, security, duplication, AI code detection)
- Claude Code headless review (dead code, pattern drift, ADR compliance, TODO aging)
- Dependency vulnerability audit (dotnet list --vulnerable + npm audit)
- Auto-creation of GitHub issues for findings

**Domain D: Claude Code Hooks and Skills**
- PostToolUse hook: lint edited files (`.cs` → dotnet format, `.ts/.tsx` → ESLint, `.ps1` → PSScriptAnalyzer)
- TaskCompleted hook: build verification + architecture test
- Enhanced `/code-review` skill with quantitative metrics and AI code smell detection

**Domain E: Multi-Model Quality Strategy**
- CodeRabbit for line-by-line review (every PR)
- Claude Code (Opus) for architecture review (every PR + nightly)
- SonarCloud for static analysis/SAST (nightly)
- Trivy + npm audit + dotnet list for dependency security (PR + nightly)

**Domain F: Quality Metrics and Reporting**
- Baseline metrics from audit (coverage %, TODO count, lint violations, dependency health)
- Nightly metric collection and trend tracking
- Weekly summary GitHub issue (auto-generated)
- README badges (coverage, SonarCloud quality gate)

**Domain G: Tooling Setup**
- CodeRabbit installation and configuration
- SonarCloud integration (sonar-project.properties, GitHub Action)
- Claude Code Action (anthropics/claude-code-action) workflow
- Prettier configuration and integration
- PSScriptAnalyzer for PowerShell scripts
- Husky + lint-staged for pre-commit hooks

**Domain H: Ongoing Enforcement (Graduation Plan)**
- Phase 1 (Weeks 1-4): Non-blocking quality checks
- Phase 2 (Weeks 5-8): Remediate audit findings, tighten rules
- Phase 3 (Weeks 9-12): Enforce blocking quality gates
- Phase 4 (Ongoing): Nightly automation, quarterly audits

### Out of Scope
- Custom AI model training or fine-tuning for code review
- Building a custom quality dashboard web application (use GitHub Issues + badges)
- Performance profiling or benchmarking (separate project: production-performance-improvement-r1)
- Security penetration testing (separate engagement)
- Refactoring functional code architecture (only quality/style improvements)
- Writing new unit tests for uncovered code (only auditing coverage gaps)
- Agent teams for code review (future consideration when feature matures)

### Affected Areas

| Area | Path | Changes |
|------|------|---------|
| GitHub Actions workflows | `.github/workflows/` | New: nightly-quality.yml. Modified: sdap-ci.yml (add CodeRabbit, Claude Code Action) |
| Quality scripts | `scripts/quality/` | New: post-edit-lint.sh, task-quality-gate.sh, nightly review prompt |
| Claude Code hooks | `.claude/settings.json` | Add PostToolUse and TaskCompleted hooks |
| Claude Code skills | `.claude/skills/code-review/` | Enhanced with quantitative metrics |
| ESLint configuration | `src/client/pcf/`, `src/client/code-pages/` | Strictened rules, new custom rules |
| Prettier configuration | Root `.prettierrc.json` | New: TypeScript formatting standard |
| Husky hooks | `.husky/` | New: pre-commit hook with lint-staged |
| SonarCloud config | Root `sonar-project.properties` | New: SonarCloud project configuration |
| CodeRabbit config | Root `.coderabbit.yaml` | New: CodeRabbit rules and settings |
| Architecture tests | `tests/Spaarke.ArchTests/` | Potentially expand from 6 to more ADRs |
| Project documentation | `projects/code-quality-and-assurance-r1/` | Audit reports, scorecard, remediation tasks |
| Coverlet config | `config/coverlet.runsettings` | Verify coverage thresholds |

## Requirements

### Functional Requirements

1. **FR-01**: Run initial code quality audit across all code areas (C#, TypeScript, PowerShell, tests, config) and produce a scorecard per area — Acceptance: Audit report with A-F grades for each of the 6 areas defined in design Domain A
2. **FR-02**: Create PR-time GitHub Actions workflow that completes in <5 minutes with blocking gates (build, format, lint, arch tests) and advisory AI review — Acceptance: PR workflow runs on all PRs, completes <5 min, blocks on compilation/format/lint failures
3. **FR-03**: Install and configure CodeRabbit for automatic line-by-line AI PR review — Acceptance: CodeRabbit posts review comments on PRs with findings categorized by severity
4. **FR-04**: Configure Claude Code Action (anthropics/claude-code-action) for architecture-aware PR review using CLAUDE.md and ADRs — Acceptance: Claude reviews each PR for ADR compliance and posts findings as PR comment
5. **FR-05**: Create nightly GitHub Actions workflow that runs Claude Code headless review, SonarCloud analysis, test coverage, and dependency audit — Acceptance: Workflow runs weeknights, creates/updates GitHub issue with findings, completes <15 min
6. **FR-06**: Configure SonarCloud integration with quality gate, AI Code Assurance detection, and coverage tracking — Acceptance: SonarCloud dashboard shows project metrics, quality gate evaluates on nightly runs
7. **FR-07**: Implement Claude Code hooks (PostToolUse for lint, TaskCompleted for quality gate) — Acceptance: Hooks fire on file edits and task completions, run appropriate linter/checks
8. **FR-08**: Configure Prettier for TypeScript formatting and Husky + lint-staged for pre-commit hooks — Acceptance: Pre-commit hook runs Prettier + ESLint on staged .ts/.tsx files, dotnet format on staged .cs files
9. **FR-09**: Install and configure PSScriptAnalyzer for PowerShell script quality — Acceptance: PSScriptAnalyzer runs on .ps1 files in pre-commit hook and nightly workflow
10. **FR-10**: Enhance `/code-review` skill with quantitative metrics (file size, method count, complexity) and AI code smell detection — Acceptance: Code review skill outputs numeric metrics alongside qualitative findings
11. **FR-11**: Create nightly review prompt file stored in repository that evolves with codebase — Acceptance: Prompt file at `scripts/quality/nightly-review-prompt.md` used by headless Claude Code in nightly workflow
12. **FR-12**: Remediate critical audit findings (Program.cs refactor, ESLint rule strictening, TODO cleanup, dependency updates) — Acceptance: Quality scorecard improves from baseline, critical items resolved
13. **FR-13**: Implement graduation plan that transitions quality gates from advisory to blocking over 12 weeks — Acceptance: SonarCloud gate, test failures, and coverage thresholds become PR-blocking by Phase 3
14. **FR-14**: Track quality metrics over time with weekly auto-generated summary issues — Acceptance: Weekly GitHub issue shows metric trends (coverage, violations, TODOs, dependencies)

### Non-Functional Requirements

- **NFR-01**: PR-time quality pipeline MUST complete in <5 minutes total (current sdap-ci.yml is the baseline)
- **NFR-02**: Nightly quality pipeline MUST complete in <15 minutes total
- **NFR-03**: Pre-commit hooks MUST complete in <10 seconds for typical staged changes
- **NFR-04**: CodeRabbit/Claude Code Action MUST NOT block PR merges (advisory only, except during Phase 3+ graduation)
- **NFR-05**: Nightly findings MUST deduplicate across runs (don't re-report known issues)
- **NFR-06**: Quality tools MUST NOT introduce secrets into the repository (API keys in GitHub Secrets only)
- **NFR-07**: Monthly external tool cost MUST stay under $100/month (CodeRabbit + SonarCloud + Anthropic API)
- **NFR-08**: All quality configurations MUST be version-controlled in the repository (no external-only config)

## Technical Constraints

### Applicable ADRs

These ADRs are enforced by the quality system and inform what the system checks for:

| ADR | Relevance to Quality System |
|-----|----------------------------|
| **ADR-001** | NetArchTest validates no Azure Functions. Quality system preserves and extends this. |
| **ADR-010** | DI minimalism (≤15 registrations, concretes by default). Audit checks interface-to-impl ratio. |
| **ADR-019** | ProblemDetails for all errors. Code review skill validates error response patterns. |
| **ADR-020** | SemVer compliance. CI can detect breaking API/job contract changes. |
| **ADR-021** | Fluent UI v9 only, no hard-coded colors, dark mode required. ESLint rules enforce. |
| **ADR-022** | PCF: React 16 APIs only, no createRoot. ESLint rules detect React 18 imports in PCF. |
| **ADR-026** | Code Pages: Vite + singlefile, React 18, single HTML output. Build validation in CI. |

### MUST Rules

- ✅ MUST preserve existing NetArchTest suite (6 ADR test classes) — extend, don't replace
- ✅ MUST preserve existing `sdap-ci.yml` pipeline — add to it, don't restructure
- ✅ MUST keep ADR violation PR comments (existing `adr-pr-comment` job)
- ✅ MUST store all quality tool configurations in the repository
- ✅ MUST use GitHub Secrets for API keys (Anthropic, SonarCloud tokens)
- ✅ MUST support gradual enforcement (advisory → blocking transition)
- ❌ MUST NOT make CI pipeline longer than 5 minutes for PR checks
- ❌ MUST NOT introduce new UI frameworks or libraries for quality dashboards
- ❌ MUST NOT auto-fix code in CI (only report — fixes happen in tasks)
- ❌ MUST NOT remove `continue-on-error: true` from tests until Phase 3 graduation

### Existing Patterns to Follow

| Pattern | Location | Usage |
|---------|----------|-------|
| CI/CD primary pipeline | `.github/workflows/sdap-ci.yml` | Extend with new quality jobs |
| ADR audit workflow | `.github/workflows/adr-audit.yml` | Model for nightly scheduled workflow |
| Architecture tests | `tests/Spaarke.ArchTests/` | Extend for additional ADR coverage |
| Code review skill | `.claude/skills/code-review/SKILL.md` | Enhance with metrics |
| ADR check skill | `.claude/skills/adr-check/SKILL.md` | Reference for validation rules |
| Review checklist | `.claude/skills/code-review/references/review-checklist.md` | Extend with AI code smells |
| ADR validation rules | `.claude/skills/adr-check/references/adr-validation-rules.md` | Extend for new ADRs |
| Coverage config | `config/coverlet.runsettings` | Verify and potentially add thresholds |
| EditorConfig | `.editorconfig` | Already defines C# formatting rules |

## Success Criteria

1. [ ] Code quality audit completed with A-F scorecard for each area — Verify: Audit report published in project notes/
2. [ ] All critical audit findings remediated — Verify: No F-grade areas remain, Program.cs refactored below 500 lines
3. [ ] PR-time quality checks running in <5 minutes — Verify: GitHub Actions timing on 10 consecutive PRs
4. [ ] Nightly quality automation running weeknights — Verify: GitHub Actions scheduled runs visible, issues created
5. [ ] CodeRabbit or equivalent AI reviewer active on all PRs — Verify: Review comments appearing on test PR
6. [ ] SonarCloud quality gate configured and passing — Verify: SonarCloud dashboard shows green gate
7. [ ] Code coverage ≥70% on new code — Verify: Coverlet report on nightly run
8. [ ] Zero ADR violations in NetArchTest — Verify: Architecture test results in CI
9. [ ] TODO/FIXME count reduced to <20 — Verify: Nightly grep count in quality report
10. [ ] No critical/high dependency vulnerabilities — Verify: Trivy + npm audit + dotnet list in nightly
11. [ ] Quality metrics tracked and trending positive over 3 months — Verify: Weekly summary issues show improvement trends

## Dependencies

### Prerequisites
- GitHub repository admin access (for CodeRabbit installation, GitHub Secrets)
- Anthropic API key (for Claude Code Action in GitHub Actions)
- SonarCloud account and organization setup
- CodeRabbit account (evaluate free tier for private repos first)

### External Dependencies
- CodeRabbit GitHub App (external service, ~$15/user/month if free tier insufficient)
- SonarCloud (external service, free tier may suffice for single repo)
- Anthropic API (for claude-code-action, estimated ~$5-20/month)
- GitHub Actions runner minutes (existing allocation should suffice)

## Assumptions

*Proceeding with these assumptions (not explicitly specified in design):*

- **GitHub-hosted runners**: Assuming GitHub-hosted runners for nightly workflows (not self-hosted). If nightly Claude Code headless review requires longer execution, may need self-hosted runner.
- **CodeRabbit free tier**: Assuming we start with CodeRabbit free tier evaluation. If private repo requires paid plan, will flag for budget approval.
- **SonarCloud free tier**: Assuming SonarCloud free tier for single repository. Quality gate and AI Code Assurance may require paid tier.
- **Audit scope**: Assuming audit is automated scan + targeted manual review of outliers (not line-by-line review of all 1,594 source files). Claude Code headless mode will perform bulk analysis.
- **Program.cs refactoring**: Assuming Program.cs (1,940 lines) refactoring follows existing ADR-010 feature module pattern (`AddSpaarkeCore`, `AddDocumentsModule`, etc.) to break into multiple files.
- **ESLint strictening**: Assuming ESLint rules transition from WARN/OFF to ERROR gradually (not all at once) to avoid blocking all PRs immediately.
- **Pre-commit hook scope**: Assuming Husky pre-commit hooks run only on staged files (via lint-staged), not entire codebase, to keep <10 seconds.
- **Nightly review scope**: Assuming nightly Claude Code review analyzes last 24 hours of commits (not full codebase every night). Full codebase sweeps happen quarterly.
- **Tool budget**: Assuming combined external tool costs stay under $100/month. If exceeded, will prioritize: (1) Claude Code Action, (2) SonarCloud, (3) CodeRabbit.

## Unresolved Questions

- [ ] **CodeRabbit pricing**: Does the free tier support private repositories? Need to verify before committing to this tool. — Blocks: FR-03 (CodeRabbit setup)
- [ ] **SonarCloud AI Code Assurance**: Is this feature available on free tier or requires paid plan? — Blocks: FR-06 (SonarCloud configuration)
- [ ] **Claude Code Action runner requirements**: Does anthropics/claude-code-action require a specific runner type or can it run on GitHub-hosted Ubuntu? — Blocks: FR-04 (Claude Code Action setup)
- [ ] **Nightly workflow cost**: What is the expected GitHub Actions minutes consumption for nightly Claude Code headless review? — Blocks: NFR-07 (cost constraint)

---

*AI-optimized specification. Original design: design.md*
