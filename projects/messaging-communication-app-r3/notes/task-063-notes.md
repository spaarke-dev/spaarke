# Task 063 — Redeploy all conversation surfaces (Phase 5 + UAT + 045) to spaarkedev1

**Status**: completed 2026-07-22 · target **spaarkedev1**. All surfaces now carry the current source (Phase 5 pin/attachments/privilege+privacy, #675 read-path fix, Phase 7 UAT PCF polish + Teams modal, 045 notification awareness).

## Deployed
| Surface | Artifact | Status |
|---|---|---|
| **030 PCF** | `CommunicationConversationPanelSolution_v1.1.0.zip` (bumped in all 4 locations: manifest, `CONTROL_VERSION` footer, solution.xml, pack.ps1) | **PACKED — operator uploads** |
| **032 code page** | `sprk_communicationconversationpage.html` (2055 KB) | ✅ PATCHed + published |
| **033 grid page** | `sprk_communicationspage.html` (1612 KB) | ✅ PATCHed + published |
| **031 SpaarkeAi** | `sprk_spaarkeai` (4827 KB, incl. 045 arrival badge/toast widget) | ✅ updated + published (publish retried past a transient 0x80071151 concurrent-customization collision) |

**PCF zip path**: `C:\code_files\spaarke-wt-messaging-communication-app-r3\src\client\pcf\CommunicationConversationPanel\Solution\bin\CommunicationConversationPanelSolution_v1.1.0.zip` — operator imports + hard-refreshes; footer should read **1.1.0**.

## SpaarkeAi build-config fixes required (from the master merges)
The SpaarkeAi build failed twice on transitive/config gaps introduced by the notification-spine merge; fixed:
1. `@spaarke/notifications` (notification-spine client lib) was a `file:` dep in SpaarkeAi's package.json but **not wired** into its vite `resolve.alias` **or** tsconfig `paths` — added both (source-aliased like the other `@spaarke/*` libs). SpaarkeAi's `src/services/notificationsBootstrap.ts` (from the merge) imports it.
2. `@microsoft/signalr@^8.0.0` — transitive dep of `Spaarke.Notifications/src/negotiate.ts`, not in SpaarkeAi's node_modules (SpaarkeAi bundles the lib from source). Installed.
(Earlier session also added `@tiptap/extension-unique-id` for the same class of Compose.Components gap.)

## Send-401 (#676) — FIXED (operator-authorized)
Granted the BFF UAMI `mi-bff-api-dev` the **"Communication and Email Service Owner"** role on `spaarke-acs-dev` (verified). Send should succeed after RBAC propagation — verify via a message send once propagated.

## Operator actions
1. Upload `CommunicationConversationPanelSolution_v1.1.0.zip` + hard-refresh (footer 1.1.0).
2. Re-verify the Matter form tab/PCF + UAT the redeployed surfaces (markers, attachments, pin, Teams modal, arrival badge/toast, and message send now that ACS RBAC is granted).
