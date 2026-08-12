# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-12 (task 073 live UAT round 2 — PCF header + Manage Access modal redesign)
> **Recovery**: read Quick Recovery, then `notes/task-073-uat-punchlist.md` + `notes/task-073-org-grant-design.md`.

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | **Task 073 UAT complete through v1.0.23; awaiting owner UAT of the v1.0.23 modal redesign.** Punch #1–#7 + the P2b access-write waves (028/070/071/072) MERGED to master via **PR #758**. PCF **TrackingFieldTrio v1.0.23** on SPAARKE DEV 1; BFF @ HEAD on spaarke-bff-dev (org grants + cache-version fix, 48.48 MB, 39/39 external-access tests); external-spa = dual-plane module-host shell on `swa-spaarke-external-spa-dev` (owner-triggered deploy). |
| **NEXT ACTION** | **Await owner UAT of PCF v1.0.23** — the Manage Access modal redesign: "+ Contact"/"+ Organization" open a **Fluent `OverlayDrawer` side pane** (overlays, never hides the modal); **per-row "Pick access level"** dropdown (no default); organizations render as first-class rows; description removed + top margin added. **KEY UNKNOWN to confirm:** does `OverlayDrawer` render cleanly in the React-16 PCF? If it looks wrong / doesn't open → **swap OverlayDrawer for a stacked side-panel** (the modal subtree is wrapped in `WidgetErrorBoundary`, so a fault degrades to an inline card, not a blank PCF). ALSO still open for owner: live UAT of #7 org grants (member sees / non-member doesn't / former-member loses it / revoke removes all; ~60s cache). |
| **After UAT settles** | The v1.0.20→v1.0.23 commits (6 ahead of master) need a **follow-up merge-to-master** (PR) once the owner signs off — PR #758 already carried #1–#7. Main repo master synced to `7529c387f` (PR #758 merge). |
| **Punch list / design** | `notes/task-073-uat-punchlist.md` (#1–#7 all RESOLVED, with root causes + version history). `notes/task-073-org-grant-design.md` (#7 org-grant design + build map; the `sprk_contactorganization` junction, BFF Term-3 union, cache-version fix). |
| **PCF version history (round 2)** | v1.0.17/18 = punch #1–#6; v1.0.19 = #7 org grants (modal + trio); v1.0.20 = always-on header row + packed-manifest `title` drift fix; v1.0.21 = header bottom padding + config property reorder (Header Title→Show Title→Show Version); v1.0.22 = wording "firm"→"organization contacts"; **v1.0.23 = Manage Access modal redesign (Fluent side-pane pickers + per-row levels + org rows)**. Rebuild recipe: bump 5 files (ControlManifest.Input.xml, index.ts versionText, Solution/pack.ps1, Solution/solution.xml, Solution/Controls/.../ControlManifest.xml) → `npm run build:prod` → copy out/controls/bundle.js to Solution/Controls/.../ → `cd Solution; ./pack.ps1` → `pac solution import --path bin/…zip --force-overwrite --publish-changes`. NOTE: packed ControlManifest.xml is hand-maintained — keep it in sync with the source. |
| **Branch/sync** | `work/spaarke-SPA-external-access-platform-r2` — clean, 0 unpushed (last commit **2423e9223**), **6 ahead / 1 behind** origin/master. |

## ✅ 070 / 071 / 072 — done (reference)
`notes/task-070-deviations.md` (polymorphic grant-write + repaired path + sprk_organization),
`notes/task-071-deviations.md` (polymorphic grant UI + side-pane lookup), `notes/task-072-deviations.md`
(Tier-1 entitlement Option-B: resolver + /me/entitlements + tab sets). 073 deploy record: `notes/task-073-deploy-and-uat.md`.

## Task 073 — deploy done; interactive UAT iterating
BFF + PCF + SWA deployed. The remaining work is the **live-UAT punch list** (grant modal + trio PCF polish)
in `notes/task-073-uat-punchlist.md`, worked one item per round with the owner eyeballing each (owner
can't be automated-verified; agent can't see the rendered UI). Do NOT mark 073 ✅ until the owner signs off
the both-plane UAT + the punch list is cleared.

## Notes index
`notes/`: `task-073-uat-punchlist.md` (⭐ active), `task-073-deploy-and-uat.md`, `task-072-deviations.md`,
`task-071-deviations.md`, `task-070-deviations.md`, `polymorphic-grant-authoring-enhancement.md`,
`module-entitlement-schema-decision.md`.
