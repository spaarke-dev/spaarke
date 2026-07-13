# VisualHost — Stale Shared-Library Dependency: Problem & Solution Assessment

> **Status**: Assessment (pre-decision) — evaluate full project vs mini project
> **Author**: Claude Code session, 2026-07-09
> **Trigger**: While deploying the `cleanGuid` GUID-normalization fix (PR #603, merged to master as `d2696b616`), the VisualHost PCF could not be built/deployed because it consumes a **frozen, version-pinned tarball** of `@spaarke/ui-components` rather than the live shared-library source. The fix therefore cannot reach VisualHost's "+" Create Wizard surface without a dependency change.
> **Related merged work**: `fix/wizard-guid-normalization` (PR #603) — shared-lib `cleanGuid` fix + `#7` adapter-boundary normalization. Deployed to dev for the 5 standalone wizard code pages. **VisualHost intentionally excluded** pending this assessment.

---

## 1. Executive Summary

VisualHost is the **only** PCF control in the repo that references `@spaarke/ui-components` as a **version-specific tarball** (`file:../../shared/Spaarke.UI.Components/spaarke-ui-components-2.0.0.tgz`, dated **May 27**). Every other PCF (8 of them) uses the **live directory dependency** (`file:../../shared/Spaarke.UI.Components`), which npm links to the current source/dist.

Consequences:
1. **VisualHost has silently missed every shared-lib change since May 27** — ~6 weeks of features and fixes, **including the `cleanGuid` fix just merged**. Its embedded "+" Create Wizard buttons still run the old braced-GUID code.
2. **The build currently fails** in this worktree because `@spaarke/auth` was not installed (a separate `file:` dep that the stale setup left unlinked).
3. The pinned tarball is **bloated** (it packs `coverage/`, `.storybook/`, etc. — no `files` allow-list in the shared-lib `package.json`).

**Recommendation: a MINI PROJECT.** The corrective config change is small and well-understood (align VisualHost to the standard directory dependency), but it forces a **6-week dependency jump (ui-components 2.0.0 → 2.3.0)** into a control last built against 2.0.0. The real work is **verifying VisualHost still builds and behaves correctly against current ui-components**, plus a PCF deploy and UAT of the "+" Create wizards. That is bigger than a one-liner but far short of a full project — no cross-cutting architecture change, and no other control is affected.

---

## 2. Problem Statement

The `cleanGuid` fix (brace-wrapped GUID normalization in `@odata.bind`) was merged into `@spaarke/ui-components` and deployed to the 5 standalone wizard **code pages** (which alias the live shared-lib source and rebuild cleanly). VisualHost's "+" Create Wizard button hosts those same wizards **inline** (via `React.lazy()` from a `wizardRegistry`), so it also needs the fix — but VisualHost **cannot pick it up** because it does not consume the live shared library. It consumes a frozen snapshot from May 27.

---

## 3. Evidence / Diagnosis

All verified this session (2026-07-09):

| Finding | Evidence |
|---|---|
| VisualHost pins a version-specific tarball | `package.json`: `"@spaarke/ui-components": "file:../../shared/Spaarke.UI.Components/spaarke-ui-components-2.0.0.tgz"` |
| The tarball is stale (May 27) | `ls -la` on `spaarke-ui-components-2.0.0.tgz` → `May 27 15:42` |
| Shared lib has moved on | `Spaarke.UI.Components/package.json` version is now **2.3.0** (tarball is 2.0.0) |
| Installed copy is a real extract, not a symlink | `node_modules/@spaarke/ui-components` is a directory copy (from the tarball), not a junction |
| Installed copy lacks the fix | `grep -c cleanGuid node_modules/@spaarke/ui-components/dist/services/PolymorphicResolverService.js` → **0** |
| `@spaarke/auth` was not installed | `node_modules/@spaarke/` contained only `ui-components`; build failed `TS2307: Cannot find module '@spaarke/auth'` (cascading `TS7006` implicit-any on `authenticatedFetch`). `npm install` restored it. |
| Repack can't overwrite the pinned file | `npm pack` produces `spaarke-ui-components-**2.3.0**.tgz` (from the current version); the referenced `**2.0.0**.tgz` filename is never updated |
| Tarball is bloated | `npm pack` tarball contents include `coverage/`, `.storybook/`, `storybook-static/` — no `files` allow-list in the shared-lib package.json |
| VisualHost is the sole outlier | 8 other ui-components-consuming PCFs (`DocumentRelationshipViewer`, `EmailProcessingMonitor`, `MatterHeader`, `RegardingResolver`, `RelatedDocumentCount`, `ScopeConfigEditor`, `SemanticSearchControl`) all use `file:../../shared/Spaarke.UI.Components` (directory) |
| The "+" button embeds wizards inline | `VisualHostRoot.tsx` imports the Xrm adapters + lazy-loads wizards via `wizardRegistry` (`React.lazy()`), rather than `navigateTo` to the standalone code pages |

---

## 4. Root Cause

**A single dependency-declaration mistake in VisualHost's `package.json`.** VisualHost was wired to a **packed tarball artifact** (`*.tgz`) instead of the **directory dependency** (`file:../../shared/Spaarke.UI.Components`) that every other PCF uses. Because the tarball filename embeds the version (`2.0.0`), and the shared lib has since advanced to `2.3.0`, VisualHost has been frozen at the May 27 snapshot. `npm install` / the `ensure-dist-fresh` prebuild hook rebuild the shared lib **source dist**, but they cannot help VisualHost because VisualHost never reads that dist — it reads the extracted tarball.

This also explains why the recent VisualHost version bumps (up to v1.4.34) never carried newer shared-lib code: every build has bundled the May 27 snapshot.

---

## 5. Impact

- **Functional (the reason this surfaced)**: the `cleanGuid` fix is absent from VisualHost. Creating a Matter/Project/etc. via VisualHost's "+" button with a native-picker-sourced (braced) GUID can still 400 with `Error in query syntax`. The 5 standalone wizard code pages are fixed; the VisualHost-embedded path is not.
- **Latent drift**: VisualHost is missing **all** `@spaarke/ui-components` changes from May 27 → present (features, bug fixes, behavior changes). This is a correctness and consistency risk beyond the GUID issue.
- **Build fragility**: the control does not build cleanly in a fresh worktree without a manual `@spaarke/auth` install.
- **Hygiene**: the bloated tarball (7.6 MB, includes coverage/storybook) is committed/referenced in the tree.

---

## 6. Solution Options

### Option A — Align VisualHost to the standard directory dependency (RECOMMENDED)
Change VisualHost's `package.json`:
```
"@spaarke/ui-components": "file:../../shared/Spaarke.UI.Components"   // was: .../spaarke-ui-components-2.0.0.tgz
```
Then `npm install`, `npm run build:prod`, fix any drift-induced build errors, test, deploy v1.4.35. Delete the stale `*.tgz` artifacts.

- **Pros**: makes VisualHost consistent with all other PCFs; live, always-current going forward; the `ensure-dist-fresh` prebuild hook becomes effective; drift can never silently recur.
- **Cons**: forces the full 2.0.0 → 2.3.0 jump at once; must verify/repair whatever drift broke.

### Option B — Update the pinned tarball only (NOT recommended)
Repack to `spaarke-ui-components-2.3.0.tgz`, update the reference to that filename, reinstall.
- **Pros**: smallest conceptual change.
- **Cons**: keeps the fragile, drift-prone tarball model; will go stale again on the next shared-lib version bump; retains the bloat. This just re-arms the same trap.

### Option C — Option A + shared-lib packaging hygiene (BEST long-term)
Do Option A, **and** add a `files` allow-list (or `.npmignore`) to `Spaarke.UI.Components/package.json` so any future `npm pack` excludes `coverage/`, `.storybook/`, `storybook-static/`; remove the committed `*.tgz` artifacts (`1.0.0`, `2.0.0`) from the tree.
- **Pros**: fixes the root cause and prevents a class of future issues.
- **Cons**: slightly larger surface; touches the shared-lib package (low risk, but shared).

---

## 7. Recommended Solution

**Option A (align to directory dependency), optionally folding in Option C's packaging hygiene.** This matches the proven pattern used by the other 8 PCFs and eliminates the drift trap permanently.

---

## 8. Risk Assessment

| Risk | Likelihood | Mitigation |
|---|---|---|
| 2.0.0 → 2.3.0 drift breaks the VisualHost build | Medium | VisualHost source is current (built from master), so it likely already expects ~2.3.0 APIs. Build errors are surfaced immediately by `build:prod`; fix incrementally. |
| Behavioral regression in VisualHost widgets/wizards after the jump | Medium | UAT the "+" Create wizards and the visualization widgets on dev before promoting. VisualHost has a Storybook + jest suite to lean on. |
| PCF bundle-size surprise | Low | Use `build:prod` (tree-shaking/minification) and verify bundle size per the pcf-deploy skill. |
| Shared-lib `files` allow-list accidentally excludes a needed path (Option C) | Low | Validate the packed contents include `dist/**` before adopting. |

---

## 9. Effort Estimate

| Task | Estimate |
|---|---|
| Repoint dependency + `npm install` | 15 min |
| `build:prod` + triage/repair 2.0.0→2.3.0 drift errors | 1–4 hrs (the main variable) |
| PCF version bump (5 locations) + pack + import | 30 min |
| UAT "+" Create wizards + core widgets on dev | 1–2 hrs |
| (Option C) shared-lib `files` allow-list + remove stray `.tgz` + verify pack | 30–60 min |
| **Total** | **~0.5–1.5 days** |

---

## 10. Full Project vs Mini Project

**Recommendation: MINI PROJECT.**

Reasons:
- **Contained blast radius**: one control (`VisualHost`); no other PCF or code page is affected (verified — only VisualHost uses the tarball).
- **Well-understood fix**: a dependency-declaration correction to match an existing, proven pattern.
- **The "project-ness" is the verification, not the design**: the effort is compatibility testing + PCF deploy + UAT, not architecture.
- Not a full project because there is **no cross-cutting change, no new architecture, no multi-surface coordination, and no spec ambiguity**.

Escalate to a **full project only if** the 2.0.0 → 2.3.0 build triage reveals that VisualHost's source has meaningful incompatibilities with current ui-components (i.e., the drift is a real migration, not a rebuild) — in which case the scope shifts from "fix the dependency" to "migrate VisualHost onto ui-components 2.3.0," which may warrant task decomposition.

---

## 11. Verification / Test Plan (for the mini project)

1. `build:prod` succeeds with expected bundle size (per pcf-deploy skill ranges).
2. `npm test` (VisualHost jest suite) green.
3. Deploy v1.4.35 to dev; hard-refresh; confirm version footer `v1.4.35`.
4. UAT: open a host record with VisualHost, click "+" → Create Matter (and Project) via the **native lookup picker** path (the braced-GUID case) → record creates with no `Error in query syntax`.
5. Smoke-test VisualHost's visualization widgets (the drift jump touches more than the wizards).

---

## 12. Appendix — Key Commands (evidence reproduction)

```bash
# The outlier dependency
node -e "console.log(require('src/client/pcf/VisualHost/package.json').dependencies['@spaarke/ui-components'])"
# → file:../../shared/Spaarke.UI.Components/spaarke-ui-components-2.0.0.tgz

# Every other PCF uses the directory dep
for d in src/client/pcf/*/; do node -e "try{console.log('$d', require('./$d/package.json').dependencies?.['@spaarke/ui-components'])}catch(e){}"; done

# Shared lib current version
node -e "console.log(require('src/client/shared/Spaarke.UI.Components/package.json').version)"   # → 2.3.0

# Installed copy is stale (no cleanGuid)
grep -c cleanGuid src/client/pcf/VisualHost/node_modules/@spaarke/ui-components/dist/services/PolymorphicResolverService.js  # → 0
```

---

*Decision owner: project owner. On approval as a mini project, scaffold under `projects/visual-host-version-update/` (spec.md + tasks) or execute directly given the small, well-defined scope.*
