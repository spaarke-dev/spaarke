# Repository Cleanup Guide

This guide outlines a safe, repeatable process to trim unused or legacy files while preserving the material still needed for SDAP development and operations.

## Principles
- **Audit before deleting**: confirm a file is unreferenced (code, docs, CI, or Power Platform assets) before removal.
- **Prefer archiving over deletion**: if historical context might be valuable, move files to `archive/` with a short README explaining contents and date.
- **Keep the repo runnable**: ensure solution builds, tests, and packaging still succeed after cleanup.
- **Automate checks**: use scripted scans to avoid subjective decisions and reduce risk.

## Triage categories
- **Critical**: source in `src/`, tests in `tests/`, IaC in `infrastructure/`, Power Platform assets in `power-platform/`, CI in `.github/`, tooling in `tools/`, and configuration templates. Keep these unless superseded.
- **Generated artifacts**: `node_modules/`, `LogFiles/`, coverage results, tarballs (e.g., `deployment.tar.gz`), and binaries. Remove and add to `.gitignore` if needed.
- **Reference-only docs**: root-level analyses and historical reports (e.g., `*-ANALYSIS.md`, `*-GUIDE.md`). Keep the authoritative version in `docs/`; archive or delete duplicates after confirming they are not linked from README or ADRs.
- **One-off scripts/tests**: ad-hoc PoCs (`test-*.sh`, `.ps1`, `.js`, `.cs`) kept at root. Move into `tools/experimental/` with a README or delete if superseded.
- **Config stragglers**: local machine paths (e.g., `c:tempappsettings.json`) or environment-specific files should be removed and regenerated locally.

## Safe cleanup workflow
1. **Inventory tracked files**
   - `git ls-files > /tmp/tracked.txt`
   - `rg --files -g"*.sln"` to list solutions and ensure dependencies stay intact.
2. **Locate generated content**
   - `rg --files -g"node_modules/**" -0 | xargs -0 -I{} echo "Generated: {}"` (adjust per OS)
   - `find . -type f \( -name "*.zip" -o -name "*.tar.gz" -o -name "*.nupkg" -o -name "*.log" \)`
3. **Check references**
   - Search for mentions before deleting: `rg "<filename>"` in `README.md`, `docs/`, `.github/`, and `src/`.
   - For Power Platform assets, confirm `solution.xml` or plugin projects do not reference the file.
4. **Propose moves/deletions**
   - Group candidates (generated, outdated docs, redundant scripts) and open a PR summarizing each decision.
   - If archiving, create `archive/<YYYY-MM>/README.md` documenting why items moved.
5. **Verify builds and tests**
   - `dotnet build` and `dotnet test` for .NET assets.
   - `npm test` or `npm run lint` for any JavaScript/TypeScript utilities.
   - Re-run relevant GitHub Actions locally if workflows were touched.
6. **Update documentation**
   - Add or update `.gitignore` entries for removed generated folders (e.g., `node_modules/`, `LogFiles/`).
   - Note relocations in `README.md` or `docs/Repository_Structure.md` if structure changes.

## Quick wins to prioritize
- Remove tracked `node_modules/`, `LogFiles/`, coverage outputs, and packaged archives (store in releases instead).
- Consolidate root-level analysis markdown files into `docs/` or `archive/` to reduce clutter.
- Move ad-hoc test scripts (`test-*.sh`, `test-*.ps1`, `test-*.js`, `test-dataverse-connection.cs`) into `tools/experimental/` or delete after confirming replacements exist.
- Drop stray config artifacts like `c:tempappsettings.json` and ensure templates live in `docs/` or `src/api/Spe.Bff.Api/appsettings.*.json`.

## Definition of done
- `git status` shows only intentional files.
- Builds/tests pass after cleanup.
- README/structure docs accurately reflect where remaining assets live.
- Future generated outputs are ignored via `.gitignore`.
