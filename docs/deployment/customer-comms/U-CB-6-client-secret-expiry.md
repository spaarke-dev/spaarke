# U-CB-6 — Client Secret Expiry (Customer Communication Template)

> **Purpose**: Plain-text operator-facing template to notify a customer that a Spaarke-owned client secret (e.g. `BFF-API-ClientSecret`, `Dataverse-ClientSecret`) is approaching its 24-month expiry, and to confirm the automated H4-rotate variant is scheduled to apply BEFORE the expiry date.
> **Applies when**: The 30-day-out alarm has fired on a Spaarke-owned client secret. This is the **proactive** notice — a separate incident-response notice applies if the alarm was missed and the secret has already expired (BFF authentication breaking silently).
> **Owner**: Spaarke Platform Operations (release manager / on-call for the automated rotation).
> **Delivery format**: Plain-text markdown — copy into the operator's chosen channel. No HTML, no branded styling.
> **Related**: `../version-compatibility-matrix.md` · `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` · `projects/customer-provisioning-orchestration-r1/design.md` §14A.4 U-CB-6 · Handler H4 rotate variant · U-CB-5 (KV cascading rotation — the applied rotation event this template previews) · CLAUDE.md MUST-NOT-DELETE list for `Dataverse-ClientSecret` and `BFF-API-ClientSecret`

---

## 1. Summary

Spaarke has detected that a client secret associated with your environment (`{customerName}` / `{environmentName}`) is approaching its 24-month expiry. Spaarke has scheduled the automated rotation to apply BEFORE the expiry date. This notice is your advance advisory; the applied rotation follows the U-CB-5 flow.

Secret nearing expiry:

- Secret name: `{secretName}` (e.g. `BFF-API-ClientSecret`, `Dataverse-ClientSecret`)
- Client / app-reg: `{appRegistrationDisplayName}` (`{appRegistrationClientId}`) in `{tenantContext}`
- Current expiry date: `{secretExpiryDate}`
- Days until expiry (at notice-sent time): `{daysToExpiry}`
- Last rotation applied: `{lastRotationDate}`
- Automated rotation scheduled: `{scheduledRotationDate}` (target: at least 7 days before expiry)

**This is a breaking change (U-CB-6)** in the operational-risk sense: if the scheduled rotation is missed AND the secret expires, BFF authentication to the downstream system fails silently — end users see cascading errors ranging from failed page loads to failed document uploads, and the failure is not always traceable to secret expiry without operator investigation.

## 2. Trigger conditions (why you are receiving this)

You are receiving this notice because ALL of the following are true:

- Handler H4's 30-day-out alarm has fired for `{secretName}`.
- The secret's remaining lifetime is `{daysToExpiry}` days (threshold: ≤30 days).
- Spaarke has scheduled the automated rotation for `{scheduledRotationDate}` (which is `{daysBeforeExpiry}` days before expiry).

If you are receiving this notice with `{daysToExpiry}` reported as `≤7`, escalate to `{operatorEmail}` immediately — the standard 30-day window has already been consumed and the risk window is narrowing.

## 3. Customer impact

- **Before scheduled rotation (proactive path)**: None. The secret is valid; all Spaarke features continue functioning normally.
- **During scheduled rotation**: Same impact as U-CB-5 — `{expectedDrainWindowSeconds}` seconds of BFF drain during slot-swap. See U-CB-5 notice for details.
- **After scheduled rotation completes**: None. New secret is active, old secret enters grace period (retained as disabled in KV for `{gracePeriodDays}` days), and BFF-to-downstream authentication resumes uninterrupted.
- **If rotation is missed and secret expires (incident path)**: BFF cannot acquire tokens against `{secretConsumerScope}`. User-visible symptoms include: `{listUserVisibleSymptoms}` (e.g. "document uploads fail with 401", "Dataverse writes return unauthorized"). Spaarke's `/health` endpoint on the BFF should surface the failure per r3 fail-fast requirement; if it does not, escalate.

## 4. Timeline

| Milestone | Target date/time (all times {timezone}) |
|---|---|
| Secret last rotated | `{lastRotationDate}` |
| Current expiry date | `{secretExpiryDate}` |
| 30-day-out alarm fired (H4 detection) | `{alarmFiredDate}` |
| This notice sent | `{noticeSentDate}` |
| Scheduled automated rotation (U-CB-5 flow applies) | `{scheduledRotationDate}` |
| Post-rotation verification report | `{scheduledRotationDate}` + 1 hour |
| Old secret DELETED from KV (grace period end) | `{scheduledRotationDate}` + `{gracePeriodDays}` days |

**Absolute deadline**: rotation MUST complete by `{secretExpiryDate}` minus at least `{minLeadTimeHours}` hours. If for any reason the scheduled rotation on `{scheduledRotationDate}` is deferred, Spaarke has a hard fallback window ending `{fallbackWindowEnd}`.

## 5. Required customer action

For most rotations, no customer action is required. The automated rotation is Spaarke-owned end-to-end. However:

1. **Acknowledge this proactive notice** (see §6). If the customer's on-call or security team wants to be looped in for the applied rotation, indicate that in the acknowledgement.
2. **Coordinate on the rotation window** if `{scheduledRotationDate}` conflicts with a customer-critical operation. Rescheduling is possible **within the safety window** (before `{fallbackWindowEnd}`) — outside that window, Spaarke proceeds on schedule regardless of conflict.
3. **BINDING**: Spaarke MUST NOT delete `Dataverse-ClientSecret` or `BFF-API-ClientSecret` from KV per r3 handoff. Rotation is `set-new + retire-old`; if you see any operator communication that mentions deletion of either secret **before** the grace period completes, escalate immediately.

## 6. Confirmation of receipt (required)

Please reply to `{operatorEmail}` (or acknowledge in `{acknowledgementChannel}`) with:

> "`{customerName}` acknowledges receipt of U-CB-6 notice for environment `{environmentName}` dated `{noticeSentDate}`. We are informed that `{secretName}` expires on `{secretExpiryDate}` and Spaarke has scheduled automated rotation for `{scheduledRotationDate}` via the U-CB-5 flow. `{customerLoopInPreference}` (e.g. `"No customer involvement required"` or `"Loop in {customerOnCallContact} for the applied rotation"`)."

Spaarke records this reply in the ProvisioningRun record for the audit trail. For proactive U-CB-6 notices, **absence of reply does NOT block the scheduled rotation** — the security posture of running the secret to expiry vastly exceeds the risk of proceeding without acknowledgement.

## 7. Rollback semantics

The rotation itself follows the U-CB-5 slot-swap model — see U-CB-5 §7 for the full re-swap / secret-restore / full-rebuild rollback matrix.

**Additional U-CB-6-specific rollback consideration** — if the rotation is discovered to have introduced a defect AFTER the secret's original expiry date has already passed (i.e. rollback is not viable because the original secret is genuinely dead, not just replaced):

1. **Forward-only recovery**: Spaarke generates a new secret in the downstream identity provider, writes it to KV, and applies via a fresh U-CB-5 rotation event. The failed rotation's old secret is beyond salvage.
2. **Downstream identity provider outage recovery**: if the downstream identity provider (Entra ID, Dataverse app-reg endpoint, etc.) is itself unavailable at rotation time and the secret has already expired, Spaarke's rotation cannot complete. Recovery is contingent on the downstream provider's SLA; Spaarke escalates to Microsoft and communicates status until resolution.

**Incident-path counterpart**: if this U-CB-6 notice is missed and the secret expires before rotation applies, the operator uses the incident-response variant of this template (not authored in Phase A — see `notes/` for follow-on).

Full rotation lifecycle + fallback logic: `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` → H4 Secret Rotation Flow + U-CB-5 template.

---

*Template last reviewed: 2026-08-17 · Author when editing: Spaarke Platform Operations · Change record: track in git.*
