# Task 030 — notes

**Rigor:** STANDARD (as authored). Steps prescriptive. Run on Opus in the main session; the POML tier is sonnet @ high, so this is a higher tier than required.

**Result:** `notes/task-030-information-fields.md`. Of the 29 candidate columns, 18 exist, 0 are secured, and 11 are NOT FOUND and dropped. The owner's decisions are recorded there, along with the final per-type list for task 031.

## Deviations
- **Option B, not Option A.** The Dataverse MCP server failed to connect in this session (`CONNECTION_CLOSED`), so Option A (`describe`) was not available. Option B (Web API metadata GETs) was shown to the owner and approved, and it ran as shown.
- **Command format.** The commands ran from one script, `read-030-metadata.ps1` in the session scratchpad. It makes the 8 GETs listed in the notes, saves the bodies to `notes/task-030-raw/`, and prints one result line per candidate. The script itself is not committed because it lives in the session scratchpad. Its exact calls are in the command list in the notes.
- **Already-allow-listed columns re-read.** These columns (`sprk_mattertype`, `sprk_projecttype_ref`, `modifiedon`) were read again to record their type. This is a read only.

## Checks
- No write, publish, import, or role or setting change was made.
- A scan of the raw files found no token, email or bearer string.
- No 401 or 403 was returned, so no escalation trigger fired.

rework: none task 030
