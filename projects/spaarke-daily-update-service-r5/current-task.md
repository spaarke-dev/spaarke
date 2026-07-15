# Current Task State — spaarke-daily-update-service-r5

> **Last Updated**: 2026-07-10 (UAT round 2 complete, merged + operator-confirmed)
> **Status**: Core project + operator-UAT rounds SHIPPED to master and live on dev.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | Daily Briefing R5. Operator-UAT-driven fix mode on live SpaarkeAi Daily Briefing (spaarkedev1). |
| **Branch** | `work/spaarke-daily-update-service-r5`. **Merged to master via PR #611** (`bdf23f11e`, 2026-07-10). Branch == master content. |
| **State** | All UAT items live + operator-confirmed "looks good" (2026-07-10). Both tiers deployed from master. |
| **Next Action** | None required. Optional follow-ups below. If closing the project → `090-wrapup` (/test-diet + /defer). |

---

## What shipped (PR #607 earlier, then PR #611 this round)

**Merged + live on dev:**
- **Membership completeness**: assigned-attorney/paralegal matters surface via `systemuser.sprk_primarycontact` (+ earlier `distinct='true'` FetchXml fix). 0→49 matters.
- **Richer rows**: Matters/Documents/Projects/Tasks carry description (`sprk_*description`) + "Updated {date}" (ModifiedOn).
- **Matter/Project titles**: `"{number}   {name}"`.
- **Stat tiles**: Documents tile; "New matters" → "Matters & Projects" (summed).
- **File-preview modal**: Documents ⋮ "Open document" → shared `RichFilePreviewDialog` via `GET /api/documents/{id}/preview-url`.
- **Email-share** (PR #607): Email Briefing (server-send to internal colleague) + per-item email; wider dialog; Critical Today ⋮ menu.
- **Settings**: Due-soon/Recency windows wired end-to-end.
- **UI polish**: removed redundant doc icon; single-line description; dropped duplicate section-chrome "Daily Briefing" title.

---

## HARD-WON LESSONS (saved to agent memory — reuse)
1. **Stale incremental BFF build**: `dotnet publish` reuses `obj/bin`; source changes silently don't compile. Hash-verify (local==remote) won't catch it. FIX: `rm -rf src/server/api/Sprk.Bff.Api/{obj,bin,publish}` before deploy when a change "didn't take". → memory `bff-deploy-stale-incremental-build`.
2. **BFF dev auto-deploys from master**: `spaarke-bff-dev` is redeployed from master by frequent master merges. **A branch-only BFF deploy is TRANSIENT** — reverts within minutes. BFF changes only stably UAT'able on dev AFTER merging to master. → memory `bff-dev-continuous-deploy-from-master`.
3. **Direct-token /render verification**: `az account get-access-token --resource "api://<BFF-AzureAd-ClientId>"` (client id: `1e40baad-e065-4aea-a8d4-4b7ab273458c`) → POST `/api/ai/daily-briefing/render` renders as your az user. Invaluable for inspecting exact deployed JSON.
- App Insights for BFF: `spe-insights-dev-67e2xz` (appId `6a76b012-46d9-412f-b4ab-4905658a9559`).

---

## Deferred / optional follow-ups
- Email-share **typed-personal-note passthrough** for "Email Briefing" (Message field cosmetic; server owns HTML) — fast-follow.
- redesign-r2 **E-12 consumer reply** drafted at `notes/REPLY-to-redesign-r2-E12-consumer-response.md` — ready to send.
- Pre-existing unrelated test failures: `legalWorkspaceSectionRegistry`, `ActivityNotesSection.callbacks` (onKeep ttl) — 090 /defer candidates.
- **090 wrapup** (/test-diet + /defer) when the project closes.

## Notes files (this project)
- `notes/email-share-feature-plan.md`
- `notes/REPLY-to-redesign-r2-E12-consumer-response.md`
