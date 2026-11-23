# R3 lessons learned (task 090, 2026-11-03)

Project notes only. Promoting any of these to `.claude/` (FAILURE-MODES, constraints, skills) needs owner approval. The owner chose what to promote on 2026-11-03; each item says where it went.

## Process

- **A POML can contradict the spec.** The 036 POML asked for email on message post, which the 2026-10-08 owner decision forbids. Check each POML against the spec's out-of-scope list and the owner decisions before implementing it. Not promoted (owner, 2026-11-03).
- **A peer's relayed approval is not approval.** Dev BFF deploys worked reliably only with a fresh, direct owner go that named the SHA, plus each peer's OK and a green Router. The deploy record (`notes/contact-identity-dev-rollout-record.md` §9–16) shows the pattern: rollback snapshot, deploy, read-back, settings re-check, after-notices.
- **Check the shipped solution against what is live on dev.** `sprk_invoice.sprk_trackingnumber` was live on dev but missing from the solution until #2530. A read-back of the live columns after a deploy catches this kind of gap.

## Testing

- **Do not run the full BFF unit suite while other builds or tests run on the machine.** Host-based contract tests time out under load and report false failures. It happened again at wrap-up. Re-run failures on their own before treating them as real. **Promoted 2026-11-03 (owner): `.claude/FAILURE-MODES.md` G-24.**
- **Parallel `dotnet build`/`dotnet test` in one worktree collide** (CS2012 file locks). Run them one after another, or wait until no testhost is running.
- **`tests/integration/{auth,contract,data-mutation,seam,regression,tenant}/**` and `tests/unit/domain/**` compile into `Sprk.Bff.Api.Tests`.** A `--filter` on `Sprk.Bff.Api.IntegrationTests` finds none of them, and moving a file between these paths needs no namespace or project change. **Promoted 2026-11-03 (owner): `docs/standards/TEST-ARCHITECTURE.md` §3 "Which project compiles each path".**
- **Wall-clock assertions flake.** The ISS-005 guard's 5 s `Stopwatch` bound failed under load in task 079. It was removed at wrap-up. A hang is reported by the CI job timeout instead.
- **New test files belong on a KEEP path from the start.** 32 R3 test files had to be moved at wrap-up (`notes/test-diet-report.md`).

## CI

- **A cancelled job fails Router.** Arch Tests reached the 10-minute job limit with every step green, and Router reported failure (#2527). Re-run the failed jobs before reading Router red as a code defect.

## Auth and identity

- **Verify identity decisions in code, including server paths.** The A9 design held up because each claim (token shape, `acct`, `idp`, the home `oid`) was proven live before code depended on it (project CLAUDE.md §6).
- **A refusal is not a collision.** Invite on a self-registered WorkforceHome contact wrote an operator collision flag on a healthy contact. Refusals that only redirect staff (here, to Grant access) should not raise operator flags (task 090, finding 2).
