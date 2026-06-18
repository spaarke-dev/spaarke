# P3 E2E Test Plan — MDA Bell Deep-Link Verification

> **Purpose**: Manual post-deploy verification steps for task 062 (`062-p3-e2e-mda-bell-verification.poml`).
> **Scope**: Confirms that the P3 server-side validation of AI-returned `primaryEntityId` (FR-17) results in a clickable "Open" button in the MDA notification bell that routes to the correct record.
> **Author**: task 042 (`042-p3-publish-size-and-e2e-note.poml`).
> **Prerequisite**: Task 060 (BFF deploy to dev) must complete successfully and report a healthy `/healthz` ping.

---

## What P3 changed (recap)

`Services/Ai/Nodes/CreateNotificationNodeExecutor.cs` now:

1. Receives the supplied `regardingId` (the authoritative entity context the notification was triggered against).
2. Validates the AI-returned `primaryEntityId` (and entity logical name) against `regardingId`. If the AI response does not match, `primaryEntityId` is null-out'd before persistence to `appnotification`.
3. Per FR-12, per-item sub-row hyperlinks always use the supplied `regardingId`, not the AI output.

Net effect for end-users: the MDA bell's "Open" button must always be clickable and must always route to the correct in-context record. There is no "phantom Open button to nowhere" failure mode.

## Test environment

| Field | Value |
|---|---|
| MDA app | Spaarke model-driven app (whichever app exposes the notification bell in the dev environment) |
| User account | Any user with READ permission on the entity used to trigger the test notification |
| BFF endpoint | `/narrate` (or whichever endpoint invokes `CreateNotificationNodeExecutor` in the deployed pipeline) |
| Browser | Edge or Chrome, normal (non-incognito) profile |

## Test cases

### TC-1: Happy path — AI returns matching primaryEntityId

**Trigger**: Perform an action that fires a notification (e.g., upload a document to a known matter via the existing notification-producing flow, or invoke whatever endpoint exercises `CreateNotificationNodeExecutor` end-to-end).

**Steps**:
1. Note the `regardingId` of the entity the action targets (e.g., `sprk_matter` GUID).
2. Trigger the notification.
3. Wait for the bell badge to update (usually < 30 s; depends on `appnotification` polling cadence).
4. Click the bell icon in the MDA top ribbon.
5. Locate the new notification entry.
6. Confirm the **"Open" button is rendered, is enabled, and is clickable**.
7. Click "Open".

**Expected**:
- ✅ "Open" button is enabled (not greyed out).
- ✅ Clicking "Open" navigates to the record identified by `regardingId` (Step 1) — typically the matter/document/event form.
- ✅ Browser URL contains the correct `etn=` and `id=` parameters matching `regardingId`.
- ✅ No console errors in browser dev tools.

### TC-2: AI mismatch path — AI returns wrong primaryEntityId (or null)

This case verifies the P3 server-side null-out behavior. Reproducing it deterministically may require either:
- Temporarily injecting a mismatched primary entity in the AI response (development-only override), OR
- Triggering a notification flow where the AI is known to hallucinate a different entity (rare but observed during R2 design).

**Steps**:
1. Trigger a notification where the AI is expected to return a `primaryEntityId` that does NOT match the supplied `regardingId`.
2. Confirm via Dataverse Web API (or Advanced Find) that the persisted `appnotification` record has `primaryEntityId` set to NULL (P3 null-out applied).
3. Click the bell icon in the MDA top ribbon.
4. Locate the new notification entry.

**Expected**:
- ✅ The notification renders without a primary-entity-specific "Open" button at the header level (or the button degrades gracefully — exact UX depends on the notification template).
- ✅ Per-item sub-row hyperlinks (if present) still route to `regardingId` per FR-12. Click one and confirm correct routing.
- ✅ No console errors. No 4xx/5xx network calls fired when the user interacts with the notification.

### TC-3: Multi-item notification — per-item links use supplied regardingId (FR-12)

**Trigger**: A notification whose payload contains multiple sub-items (e.g., a digest covering several updates against the same matter).

**Steps**:
1. Note the parent `regardingId`.
2. Trigger the multi-item notification flow.
3. Open the bell, locate the new entry, expand it (if collapsible).
4. For each sub-item link, click and verify the destination.

**Expected**:
- ✅ Every sub-item hyperlink routes to a record under the supplied `regardingId` context (per FR-12).
- ✅ No sub-item hyperlink routes to an AI-hallucinated entity.

## Pass/fail criteria for task 062

Task 062 PASSES when ALL of the following are true:

- TC-1 passes on first run.
- TC-2 either passes on first run OR (if AI mismatch cannot be reproduced in dev) is recorded as "deferred — covered by unit test in task 041 `DailyBriefingEndpointsTests`". Unit test coverage of the null-out branch is acceptable substitute for live E2E reproduction when AI behavior is non-deterministic.
- TC-3 passes on first run.
- No JavaScript console errors in any of the three test cases.

Task 062 FAILS (and blocks production deploy in task 063) if any of:
- "Open" button is rendered as clickable but routes to a 404 / wrong record.
- Sub-item hyperlinks route to AI-hallucinated entity instead of supplied `regardingId`.
- Any browser console error originating from notification-rendering code.

## Evidence to capture for task 062

1. Screenshot of bell with new notification visible (TC-1).
2. Screenshot of destination record after clicking "Open" (TC-1) — URL bar visible.
3. Dataverse Web API query result showing `appnotification.primaryEntityId IS NULL` for TC-2 (if reproduced live) — or annotation that TC-2 was covered by unit test only.
4. Screenshot of multi-item notification expanded with at least 2 sub-items visible (TC-3).
5. Recording (or notes) of browser console state — confirm "no errors".

## Cross-reference

- Spec: [`../spec.md`](../spec.md) FR-12 + FR-17 + SC for P3 acceptance
- P3 implementation: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs` (task 040)
- P3 tests: `tests/unit/Sprk.Bff.Api.Tests/...` (task 041 — covers the null-out branch deterministically)
- Deploy: task 060 (`060-p3-deploy-bff-to-dev.poml`)
- Verification: task 062 (`062-p3-e2e-mda-bell-verification.poml`) — this plan is the source of truth for what 062 executes
- Sibling: [`bff-size-p2b-p3.md`](bff-size-p2b-p3.md) for size + CVE verification
