# Task 092 — Prettier check was not developer-reproducible (#850)

> **Date**: 2026-08-28 · **Branch**: `ci/task-092-prettier-reproducible` · **Status**: complete

CI reported **1,907** unformatted files; a developer running the same command saw **46** (and on
this machine, **0**). A 40× disagreement means the check cannot be reproduced, therefore cannot be
fixed, therefore gets ignored — the "constant reds" failure mode north star #3 forbids.

## Root cause: `endOfLine: "crlf"` backed by nothing

`.prettierrc.json` declared `"endOfLine": "crlf"`. Nothing else in the repo backs that policy:

- `.editorconfig` scopes `end_of_line = crlf` to `[*.{cs,csx,vb,vbx}]` **only**.
- `.gitattributes` has `text eol=crlf` rules for those same C# types **only** — added 2026-08-19
  to fix *this exact bug class* for `dotnet format`, with a comment describing the identical
  symptom: *"the Format check was permanently red while nothing looked wrong on a developer machine."*
- TypeScript therefore had **no declared line-ending policy at all**.

So Prettier was unilaterally asserting a policy, satisfied on Windows only by the `core.autocrlf=true`
accident:

| | Working tree | Prettier expected | Result |
|---|---|---|---|
| Windows dev | CRLF | CRLF | ✅ 0 of 1,911 |
| Linux CI | LF (stored bytes) | CRLF | ❌ **1,907 of 1,911** |

1,911 tracked `.ts`/`.tsx` exist under `src/client/`. CI was flagging essentially **all of them**,
for line endings alone.

### How it was proven, not guessed

My first hypothesis (line endings) appeared **refuted** by an isolated two-line fixture — both a
CRLF and an LF file passed. That test was too trivial to be valid. The decisive test was inverting
the setting against the real corpus:

```
npx prettier --list-different --end-of-line lf "src/client/**/*.{ts,tsx}"   →  1911
```

The exact mirror image of CI's 1,907 with `crlf` against LF. Line endings are the sole
discriminator.

## Fix: `"endOfLine": "auto"`

Accepts whatever line ending a file already has, so line endings are governed where they belong —
by Git (`core.autocrlf` + `.gitattributes`) — instead of being asserted independently by the
formatter for a file type the repo never declared a policy for.

**Verified 0 differences on both platform conditions**: my CRLF working tree, and a real subtree
copy converted to LF to simulate a CI checkout.

### Why not the `.cs` precedent (`*.ts text eol=crlf` in `.gitattributes`)?

Because that precedent exists to **back an already-declared policy** — its own comment says
*"Scope deliberately matches .editorconfig."* For `.cs`, `.editorconfig` declares CRLF, so
`.gitattributes` enforces it. For TypeScript nothing declares anything, so adding a gitattributes
rule would be *inventing* a policy and forcing CRLF working trees on CI and on every non-Windows
contributor — a far larger blast radius than the defect warrants.

If TypeScript ever needs a real policy, the correct order is: declare it in `.editorconfig`, back
it with `.gitattributes`, *then* tighten Prettier. Not the reverse.

## Acceptance criteria

| Criterion | Status |
|---|---|
| CI and local report the same file count on the same commit | ✅ 0 both, verified on CRLF and simulated-LF trees |
| The job output names the exact local reproduction command | ⚠️ **Placed in `docs/procedures/testing-and-code-quality.md` instead** — printing it in the job output requires editing `ci-tier2-advisory.yml`, which the shadow-window freeze forbids. One-line workflow change, deferred to after cutover. |
| Remaining unformatted files are fixed or ignored with a written reason | ✅ zero remain |

## Freeze-safety

No workflow touched. The fix is `.prettierrc.json` plus a procedures doc — the CI command is
unchanged and picks up the config automatically.
