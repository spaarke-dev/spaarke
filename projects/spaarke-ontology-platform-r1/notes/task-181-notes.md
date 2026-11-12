# Task 181 — Work Assignment wizard on the side pane

PR [#2752](https://github.com/spaarke-dev/spaarke/pull/2752) squash-merged to master as `b875772cec` (owner: merge on green, 2026-11-12).

- `AssignWorkStep`, `EnterInfoStep`, `CreateFollowOnEventStep` person/organization fields and Matter Type (`sprk_mattertype_ref`) / Practice Area (`sprk_practicearea_ref`) use `DataverseLookupField` (D-219 Q1).
- Law Firm Attorney: `buildLawFirmAttorneyFilters(firmId)` builds one `<filter type="and">` with the active-contact condition and `parentcustomerid eq <firm>` (GUID cleaned and XML-escaped). With no firm chosen the field keeps today's inline rule.
- Contacts are active only (`ACTIVE_CONTACT_LOOKUP_FILTERS`, D-219 Q6); `lookupFilters.ts` shared with task 179.
- Dev check runs with task 185's deploy verification.

rework: none
