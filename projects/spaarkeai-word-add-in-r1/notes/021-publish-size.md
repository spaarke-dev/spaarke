# Task 021 — BFF publish-size measurement

> Root CLAUDE.md §10 bullet 4 / ADR-029. Measured 2026-09-11 against a FRESH build of `origin/master`
> (not the recorded baseline number). `origin/master` was unchanged since task 030's measurement
> earlier today, so that figure is reused per the task's own instruction rather than re-publishing it.

| | Commit | Zip (incl. PDBs) |
|---|---|---|
| `origin/master` | `e0a6f87c4` (task 030's measurement today, reused — master unchanged) | **45.35 MB** |
| This branch | `8b5d58a2b` (task 013 merge, this worktree's base) + task 021 changes | **45.37 MB** |
| **Delta** | | **+0.02 MB** |

- **Method:** `dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o deploy/api-publish-021`,
  then PowerShell `Compress-Archive -Path "<publish>\*" -CompressionLevel Optimal -Force`. Same zip tool +
  compression level as `scripts/Deploy-BffApi.ps1:259` (its default `-CompressionLevel` is `Optimal`).
- **Why master wasn't re-published:** `git fetch origin master` confirmed `origin/master` is still at
  `e0a6f87c4` — the exact commit task 030 published + zipped with the same PowerShell method a few hours
  earlier at 45.35 MB. Re-measuring would reproduce the same number at the cost of a redundant publish;
  the constraint is "measure against a fresh build of master, not the recorded baseline," and 45.35 MB
  IS that fresh-build number, not a stale recorded one.
- **Scope of the delta:** task 021 adds NO new BFF route, NO new package reference, and NO new DI
  registration. It extends `GET /api/v1/documents/{id}`'s existing `ColumnSet` by 5 column names (string
  literals) and adds one `int?` property (`DocumentEntity.SummaryStatus`) plus five field assignments in
  `MapToDocumentEntity`. The entire server-side diff is a few dozen lines of IL inside two already-compiled
  methods — a +0.02 MB delta is consistent with that scope, not with new assemblies.
- **No new package references** — `dotnet list package --vulnerable --include-transitive` against
  `Sprk.Bff.Api.csproj` reports **no vulnerable packages**, consistent with zero package changes.
- **Thresholds:** +5 MB for a single task needs justification, 55 MB cumulative triggers an architecture
  review, 60 MB is a hard stop. +0.02 MB is far below all three.

## Re-measured after the `sprk_documenttype` bugfix (same day)

A coordinator review caught a runtime bug in the DocumentType mapping (see
`notes/021-profile-section-display.md` "Post-review bugfix" for the full writeup). The fix (a
corrected field-mapping expression + XML doc comments + a `private`→`public static` visibility
change on `MapToDocumentEntity`) was re-published and re-zipped the same way: **45.37 MB**, byte-for-
byte identical to the pre-fix measurement above — expected, since the change is a few lines of IL in
an already-compiled method plus comments (comments do not emit IL).
