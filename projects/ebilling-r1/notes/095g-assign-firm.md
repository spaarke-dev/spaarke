# 095g: Assign a firm from the Outlook pane (EB-D-224)

Status: design not started. Order: after 095e and 095f. A design note goes to uac-r2 before any code; uac-r2 reviews before merge; two adversarial passes.

## What

- An internal-only list of active firms, searchable.
- An assign route on matter, project and work assignment (e.g. `POST /api/finance/core-records/{entityType}/{entityId}/firms {firmId}`). It writes Assigned Law Firm 1/2. The answer is the record's firm list, as the §8 GET returns it, plus closed refusal codes: no right to change the record; both slots full; inactive firm; record unavailable (503).
- The slots are what the external access model reads, so a save gives that firm's external contacts access to the record. This makes it a sharing change.

## uac-r2 conditions (binding, 2026-11-13)

1. **G5:** check the caller as the user, then write as the app. The caller needs Write on the record, which is the C4 share right. Reuse `CallerRecordAccessProbe`; build no new check.
2. **One eligibility rule:** use `InternalShareEndpoints.ClassifyEligibility` / `IsBarredOnRestricted`, the rule every share writer uses.
   - A Restricted record refuses.
   - A Secure record refuses with a clear message, because the save would grant nothing.
   - A record filed under a secure or Restricted parent refuses, decided through the parent-derived value.
3. **NFR-01:**
   - An unknown record or firm id gets the same answer as a denied one.
   - Any read fault fails closed: 503, never a silent write.
   - The firm must be active, checked on the server. Never trust the pane's list.
4. **Write path:** one server-side writer (ADR-002), with the person recorded as on our other writes. Replacing or clearing a firm is a revoke: the old firm's external users lose access. The contract says so, and the pane confirms it.
5. **Route ledger:** declare a record check in `RouteAuthorizationGuardTests`. The tests need:
   - negative cases for Restricted, Secure, a secure parent, no Write, an external caller and an inactive firm;
   - a must-fire control.
6. **List route:** internal callers may see all active firms. Firm names are not access-sensitive, and the decision that matters is made when the firm is assigned.
   - It is internal-only. Every external or non-licensed caller gets the same answer as an unknown caller.
   - Add a stated waiver line in the route ledger, with that reason.

## Grant and revoke: one rule

| Action | What it does | Right needed |
|---|---|---|
| Add a firm to an empty slot | Grant | Write on the record, checked as the user |
| Replace a firm | Grant, plus a revoke of the old firm | Write on the record, checked as the user; the pane confirms the revoke |
| Clear a slot | Pure revoke | Write on the record, checked as the user |

(uac-r2, 2026-11-13)
