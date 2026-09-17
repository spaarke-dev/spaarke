# Task 014 — BFF publish-size measurement (FR-02 server-side identity stamp)

> Measured 2026-09-17 in the task-014 agent worktree
> `C:\code_files\spaarke\.claude\worktrees\agent-a510c79631dcccce2`.
> Rule: root CLAUDE.md §10 bullet 4 + POML constraint "measure the delta against a FRESH BUILD, not the
> recorded baseline".

---

## Numbers

| Build | Commit | Compressed | Bytes | Files |
|---|---|---|---|---|
| **Baseline** | `d21e54e98` (task 014's base; already contains 025 + 031 + 050) | **45.41 MB** | 47,620,167 | 214 |
| **Branch** | baseline + task 014 | **45.43 MB** | 47,631,797 | 214 |
| **Delta** | — | **+0.0111 MB (+11,630 bytes)** | +11,630 | **±0** |

- **Zip tool**: PowerShell `Compress-Archive -CompressionLevel Optimal` over the publish folder's contents —
  the method `scripts/Deploy-BffApi.ps1` uses, per `.claude/constraints/azure-deployment.md`. Both sides were
  zipped with the **same** tool in the same session, which is the point: the constraint records a ~1.3 MB
  swing on byte-identical content between `Compress-Archive` and Python `shutil.make_archive`.
- **PDB convention**: PDBs **included** (4 files in the publish).
- Publish command both sides: `dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o deploy/api-publish`
  (framework-dependent linux-x64, per the net10 baseline). Never published from `/tmp`.

**Verdict**: +0.0111 MB. Escalation threshold is ≥ +5 MB (none), architecture review ≥ 55 MB (no), hard stop
60 MB (no). The +11 KB is the new `OfficeDocumentStamp` type compiled into the existing assembly.

---

## Why the baseline is `d21e54e98` and NOT `origin/master`

The POML says "a fresh build of master". In this worktree that is not the right comparison, and taking it
would have reproduced the exact error root CLAUDE.md §10 was rewritten to prevent:

- `git worktree add … origin/master` in this repo resolves `origin/master` to **`e0a6f87c4`**, which is an
  **ancestor of this task's base**. Publishing it as the baseline would have attributed the size of tasks
  **025, 031 and 050** to task 014 — the stale-baseline overstatement (§10 records a 46× instance of exactly
  this).
- `origin/master` was **deliberately not refreshed**. `.git` is shared across ~40 worktrees in this repo, and
  `git fetch` mutates shared refs; doing so mid-flight while other agents and sessions are active is a
  side effect outside this task's worktree.
- **`d21e54e98` is the strictly better baseline for the question being asked.** It is this branch's actual
  parent, so the delta contains *only* task 014 and zero drift from any other project. §10's stated purpose —
  "re-measuring is the measurement; the recorded number is only a sanity check" — is satisfied more precisely
  by comparing against the real parent than against any master commit.

Sanity check against the recorded figure: §10 records `origin/master` @ `a826cf347` = 45.42 MB (2026-09-02).
This measurement's 45.41 MB base and 45.43 MB branch sit either side of that, i.e. the tree has not drifted
materially since — so the recorded number corroborates rather than contradicts.

---

## Why the delta is ~11 KB and not megabytes

The dominant risk this task carried was an **OOXML manipulation package**. None was added:

- `git diff --stat -- '*.csproj'` is **empty** — no `PackageReference` added, removed or moved on any project.
- `OfficeDocumentStamp` uses only **in-box** assemblies: `System.IO.Compression` (`ZipArchive`) and
  `System.Xml.Linq` / `System.Xml` (`XDocument`, `XmlReader`). Both ship in the shared framework, so they
  contribute **zero** publish bytes.
- **`DocumentFormat.OpenXml` is NOT referenced by production code.** The design note's probe used it only to
  *validate* its output; the shipped writer does not, and deliberately so — the SDK re-serializes
  `[Content_Types].xml` and the relationship parts it touches, normalising namespaces, attribute order and
  whitespace, which is the "library that normalizes XML on save" the task's own escalation trigger names.
- The file count is **identical on both sides (214)**, which independently confirms no new assembly landed.

---

## Other §10 gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 warnings / 0 errors** |
| `dotnet list package --vulnerable --include-transitive` | *"has no vulnerable packages given the current sources"* — no new HIGH/moderate CVE |
| New DI registrations | **none** — `OfficeDocumentStamp` is a static stateless helper (ADR-010) |
| New routes | **none** — the stamp hooks the existing `POST /api/office/save` |
| ArchTests | **191 / 191** (baseline held) |
