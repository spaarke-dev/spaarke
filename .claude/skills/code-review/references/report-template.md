# Code Review Report — template

Read by `code-review` Step 8. Fill in every section; write "None" rather than deleting a section. The ordering and clustering rules are in SKILL.md Step 8.

````markdown
## Code Review Report

**Files Reviewed:** {count} files
**Review Depth:** {quick|standard|thorough}
**Scope / BASE:** {how the scope was chosen (Step 1)} / {BASE used for quality direction}
**Date:** {timestamp}

### Quantitative Metrics

| File | Lines | Public Methods | Private Methods | Ctor Params | Complexity Est. | Interfaces |
|------|-------|----------------|-----------------|-------------|-----------------|------------|
| {file} | {n} | {n} | {n} | {n} | {n} | {n} |

{No severities in this table. For each file over a look-closer value (Step 2.5), one line:
 "cohesive: <reason>" or "not cohesive: <responsibilities that diverge>" — the second is also a finding below.}

### Quality Direction: {filename}

| Metric | Before | After | Delta | Signal |
|--------|--------|-------|-------|--------|
| Total Lines | {n} | {n} | {+/-n} | {Improved/Stable/Neutral/Warning/Degraded} |
| Public Methods | {n} | {n} | {+/-n} | {signal} |
| Private Methods | {n} | {n} | {+/-n} | {signal} |
| Ctor Params | {n} | {n} | {+/-n} | {signal} |
| Complexity Est. | {n} | {n} | {+/-n} | {signal} |

{Repeat for each changed file; a new file gets "New file -- no baseline for comparison".}

### Quality Direction Summary

| File | Overall Signal | Key Changes |
|------|----------------|-------------|
| {file} | {Improved / Neutral / Declining / Degraded / New file} | {e.g. +36% lines, +6 complexity} |

**Direction**: {1–2 sentences: does this changeset move quality in a positive or negative direction overall?}

### AI Code Smell Detection

| # | Smell | Files Affected | Count | Severity |
|---|-------|----------------|-------|----------|
| 1 | Interface w/ single impl | {files or --} | {n} | {severity} |
| 2 | Try/catch log-rethrow | {files or --} | {n} | {severity} |
| 3 | Null check on non-nullable | {files or --} | {n} | {severity} |
| 4 | Code-restating comment | {files or --} | {n} | {severity} |
| 5 | Method > 3 responsibilities | {files or --} | {n} | {severity} |

**AI Smell Score**: {count of warnings + suggestions} findings across {file count} files
**Verdict**: {Clean / Minor issues / Needs refactoring}

### Conditional steps

{Step 6.5 BFF hygiene / Step 6.55 enforcement / Step 6.6 component justification / Step 6.7 POML completeness /
 Step 7 technology checks: "applied" with findings listed below, or "N/A — <reason>".}

### Critical Issues (Block Merge)

{Each: file:line — description — confidence — suggested class (F1–F4 / K1–K4) — failure scenario for F1 —
 "found in passing" where it applies}

### Warnings (Should Address)

{Same fields as above}

### Suggestions (Consider)

{Same fields as above}

### What is Good

{Positive patterns observed}

### Summary by Category

| Category | Critical | Warning | Suggestion |
|----------|----------|---------|------------|
| Security | {n} | {n} | {n} |
| Performance | {n} | {n} | {n} |
| Style | {n} | {n} | {n} |
| ADR Compliance | {n} | {n} | {n} |
| AI Code Smells | {n} | {n} | {n} |

### Recommended Actions

1. {First priority fix}
2. {Second priority fix}
...

### Auto-Fixable Issues

Run these commands to fix some issues automatically:
- `dotnet format` - Fix C# formatting
- `npx eslint --fix {files}` - Fix TypeScript lint issues
- ADR check: `/adr-check` for full compliance scan
````
