# Lessons Learned — spaarkeai-assistant-enhancements-r2

**Closed**: 2026-08-10

Non-obvious findings worth carrying forward (corrections + confirmed approaches + why). Mechanical history lives in `tasks/TASK-INDEX.md` and the phase commits.

## 1. Awareness without capability parity is a UX trap (drives R3)

Making the Assistant *aware* of the active tab (the `visibleToAssistant` active-tab-as-consent fix) was necessary but **not sufficient**. UAT showed the recurring failure shape: the prompt had the tab's **name** but no **tool** to act on its data, so the model hallucinated (`GETDATE()` for overdue tasks) or narrated stale counts. **Why it matters**: the fix is never "put more data in the prompt" — it's "give each surface a tool that queries the authoritative source." This finding is the entire thesis of R3 (the Assistant⇄Workspace Interaction Contract). See `projects/spaarkeai-assistant-enhancements-r3/design.md`.

## 2. JSON-round-tripped restore strips closures — persist stable IDs, not functions

The history-restore hang (UAT r1) was a `DocumentViewer` dispatched with a `fetchPreviewUrl` **closure** that a JSON persist/restore silently drops → infinite spinner. **How to apply**: anything that survives a persist boundary (Cosmos/sessionStorage/history) must carry **stable primitives** (`documentId`), and the consumer re-derives behavior (re-fetch `GET /api/documents/{id}/preview-url`) on restore. Treat `blob:`/`data:` URLs as absent-on-restore and resolve null on failure so a missing artifact never spins forever.

## 3. Active-tab-as-consent (ADR-015 Path A) needs the filter to actually include the active tab

Every user tab defaults `visibleToAssistant=false` and no UI flips it — so the active tab was being **filtered out before** the hoist, and the Assistant "couldn't see" the focused email/document. The fix makes the focus-stamped active tab content-visible **regardless** of the flag (background tabs stay metadata-only). **Why**: "the user is looking at it" is the consent signal; the flag is for *background* opt-in. Don't gate the active tab on a flag nothing sets.

## 4. Auth regressions: look for a project-local config flip before touching shared auth

The cold-start "Connecting to Dataverse…" hang was **not** a `@spaarke/auth` bug — it was a 3-day-old **SpaarkeAi-local** `requireSilentOnly: true` flag (added for a fully-silent cold start) that removed the **only** MSAL-cache-seeding path on a truly cold cache (`acquireTokenSilent` needs a cached account; first `ssoSilent` fails; popup was suppressed → `getAccessToken()` returned `''` forever). **How to apply**: when auth regresses, first diff the *consumer's* config passed into the shared factory; the shared library's popup fallback was correct all along. The revert is one line, **zero blast radius** beyond SpaarkeAi — do not conflate a local config revert with a shared-lib change (that conflation caused a false "big blast radius / impacts PCFs" alarm mid-investigation). Fully-silent cold start, if wanted, is a separate deliberate shared-lib effort.

## 5. SpaarkeAi bundles shared libs from `/src`, not `/dist` — no pre-build needed, but source drift ships immediately

`src/solutions/SpaarkeAi/vite.config.ts` resolves every `@spaarke/*` `file:` dep to its `../src` via a custom `resolveSharedLibDeps` plugin. **Consequence for deploys**: you do **not** rebuild shared libs before building SpaarkeAi (Vite compiles their source directly) — but it also means a merge that changes any shared-lib source (e.g. `ComposeWorkspace.tsx`) ships into the SpaarkeAi bundle the moment you rebuild. Clear the Vite cache (`rm -rf dist/ node_modules/.vite/`) every build. The auth fix (client-only) needed **only** a code-page redeploy — no BFF deploy — because R2's BFF changes were already live via 039/043.

## 6. Deploy playbook confirmed (carry into every SpaarkeAi/BFF deploy)

- **Re-sync master first** — master drifts every session; merge `origin/master` into the branch and build-verify before deploying (the D/C deploy did this from `@688e26582`, this one from `cb71cf3fc`).
- Verify a merge that touches a file you changed with the **three-dot** diff (`HEAD...origin/master`) — the two-dot `--name-only` shows *your* side too and looks like a phantom conflict.
- Husky pre-commit hook is **env-broken** in this worktree (`dotnet format` SIGKILL + bare `prettier` not on PATH) → `--no-verify` is owner-approved.
- Code-page publish occasionally returns transient `0x80071151` (concurrent publish from another worktree) → **retry**.

## 7. Test hygiene held — deleting a surface ≠ deleting its presentational component

Test-diet at close was clean (0 scaffolding, 0 orphans across 49 R2 test files). The one subtlety: Phase E deleted the `useSuggestionCards` **hook/lifecycle** surface but **retained** `SuggestionCard.tsx` because a *different* kept surface (`useRerunFullAnalysisCard`) reuses the presentational component. The test file correctly dropped the hook-lifecycle `describe` block and kept the component render tests. **How to apply**: when retiring a feature, separate the *surface/lifecycle* (delete) from *reusable presentational parts* (keep if another consumer exists) — and update the test to match, documenting the retention in the test's docblock. See `notes/test-diet-report.md`.

## 8. Follow-up carried out of the project

`useAuthProbe.ts` (App.tsx mount-gate) was an earlier attempted auth fix, superseded in *effect* by the flag revert but still wired live and shipped. It's not broken (post-revert the popup fallback resolves, so the probe's retry succeeds) but its continued necessity is a design question — flagged in `notes/test-diet-report.md` as a code-review/R3 follow-up rather than a risky wrap-up change.
