# Dev deploy and server gate: 3ff2c04bf5 (2026-10-27)

## Deploy

R3 deployed it with the owner's approval in R3's session.

- **Build:** DLL ProductVersion `1.0.0+3ff2c04bf5a516a314e4aecae41b4308148c5627`. OneDeploy ended 01:29:15Z; read-back at 01:30:10Z. It replaces bc99fc3e6b.
- **Read-back:** healthz 200, healthz/dataverse 200, anonymous /api/me 401; 4 file hashes and CORS verified (R3).
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261027T012731Z` (bc99fc3e6b), in the R3 worktree.
- **UAC content:**
  - #2322 (0b8acdf8dc): census accepted exceptions, Billing Administrator (EB-D-150) and AI Analysis Admin (round 127), direct assignment only.
  - #2323 (f183dd9c92): the access-record proof sees System Administrator and profiles held through a team, and matches System Administrator by template id (#2320).
  - #2324 (3ff2c04bf5): the proof joins team membership server-side, so no request grows with team count.

## Server gate: PASS

- **Window:** 01:29:00Z → 02:01Z.
- **Source:** App Insights `spe-insights-dev-67e2xz`, role `spaarke-bff-dev`. Read-only.
- **Independent check:** R3's read (01:29–02:01Z) agrees.

| Check | Result |
|---|---|
| Requests | 52; the 16 failures are 401s |
| 403 / 5xx | 0 / 0 |
| `access_record_hidden` / `access_record_not_secured` | 0 / 0 |
| `[FOLLOW-PARENT]`, `[SECURE-INHERIT]`, `[SECURE-CHILD-SHARES]`, `[ASSIGNED-ACCESS-RECON]`, `[NO-ACCESS-RECON]`, `[CORE-ANCESTOR-RECON]` | Heartbeats ok; no refusal, failure or incompleteness |
| Census | Child-table and project lines identical to bc99fc3e6b (cause: design §5.2, see the bc99fc3e6b note) |
| `[SECURE-CENSUS] AcceptedException` | None, as expected: on dev no user or team holds Spaarke AI Analysis Admin or Spaarke Billing Administrator (read 2026-10-27) |

**Coverage:** the proof change is exercised by the scheduled cascade jobs only. On dev the BFF holds System Administrator directly, so the team legs return through the one combined query; they ran clean.

**Exceptions, all pre-existing:**
- **#2292:** the `sprk_userentityassociation` alternate-key faults, ×1,862 in the 02:00Z hour. That is `MembershipReconciliationJob`'s daily run, and the 2026-10-26 02:00Z hour had the same 1,862.
- **New issue #2325:** one `TodoGenerationService` Rule 2 fault. It queries `sprk_matter.sprk_name`, but the column is `sprk_mattername`. It has occurred daily at 02:00Z since 2026-10-21, so it is not this deploy. Filed; it is not access control.
- **Unchanged:**
  - the `[ID-LINK-RECON] spaarke-acme` binding-column failure (#2163);
  - `RoutingConsumerTypeHealthCheck` catalog drift ×1;
  - reconciliation collision deny lines (R3: 18).
