# Task 013 — Envelope Contract + `kind` Taxonomy — Deviations & Design Notes

> Task: `013-envelope-contract-taxonomy.poml` — TYPES + TESTS ONLY (no producer/consumer wiring).

## Deviations from the POML's literal steps

None material. The implementation follows design.md §5A.3 / §5B.4 field-for-field and
design.md §10 decision #5 for the `kind` taxonomy. Two judgment calls worth recording:

1. **Test file location.** The POML's `<outputs>` names
   `tests/unit/Sprk.Bff.Api.Tests/Services/Notifications/EnvelopeSerializationTests.cs`. This
   path is NOT one of the 7 ADR-038 KEEP paths (`tests/integration/{auth,regression,data-mutation,
   tenant,contract,seam}/**` or `tests/unit/domain/**`) per `tests/CLAUDE.md`. Followed the POML's
   explicit path anyway because it matches the codebase's actual current convention — e.g. the
   nearest precedent cited by the task itself,
   `Services/Ai/PublicContracts/JobAwareOutcomeProjection.cs`, is tested at the parallel path
   `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PublicContracts/JobAwareOutcomeProjectionTests.cs` —
   also outside the strict KEEP-path list. `tests/CLAUDE.md` itself notes the KEEP-path
   reorganization (task 050) is not yet complete repo-wide. No action needed from this task; flagging
   for awareness in case a later repo-wide test-path migration touches this file.
2. **Kind-shape guard (`Validate()`).** Added a `Validate()` method to both envelope records
   that throws `InvalidOperationException` when `Kind` isn't a value the envelope's own shape
   permits (`CommunicationEnvelope` → `CommunicationArrived`/`CommunicationAssessed` only;
   `SuggestionEnvelope` → `Suggestion` only). Not explicitly requested by the POML, but mirrors
   the `GateDecisionV2.Validate()` precedent in the same `PublicContracts`-style folder and closes
   a gap the enum type alone doesn't cover (the enum permits all 6 taxonomy values on either
   envelope; `Validate()` narrows it per-shape). Producers in later tasks are expected to call it
   before writing to the outbox; this task does not wire that call anywhere (no producer exists yet).

## Design decisions (not deviations, recorded for downstream producer/consumer tasks)

- **`Channel`, `Direction`, `Source`, `ActionHint` are plain `string`, not enums.** Only `kind`
  carries the explicit "closed set, must fail to compile/deserialize on typo" constraint (POML
  step 2 / constraints). Keeping the other fixed-value-set fields as `string` avoids inventing
  additional taxonomy decisions the task didn't ask for (CLAUDE.md §11 — avoid scope creep).
  A future task MAY promote these to enums if a producer needs it; that is a new, separately
  justified decision.
- **`RegardingRecordId` is `string`, not `Guid`.** Matches the existing repo convention for
  regarding-family fields (ADR-024) — e.g. `Spaarke.Dataverse.Models.cs` `RegardingRecordId` is
  `string?` throughout the codebase (polymorphic target; not always a bare GUID string in every
  producer path).
- **`ExpiresAt` is `DateTimeOffset`**, matching the wire-date convention already used across
  `Services/Ai/PublicContracts/*` (e.g. `TraceEvent.Timestamp`, `GateDecisionV2`-adjacent types).
- **All wire property names are pinned with explicit `[JsonPropertyName]`** (camelCase, matching
  design.md's literal pseudocode) rather than relying on a caller-configured
  `JsonSerializerOptions.PropertyNamingPolicy`. This keeps the contract correct regardless of what
  naming policy a future producer/consumer's `JsonSerializerOptions` uses.
- **`kind` taxonomy implementation**: a single closed `NotificationKind` enum (not per-envelope
  enums) with a custom `JsonConverter<NotificationKind>` (`NotificationKindJsonConverter`) that
  explicitly throws `JsonException` on any unrecognized wire string — chosen over
  `[JsonConverter(typeof(JsonStringEnumConverter))]` because the wire values are kebab-case
  (`communication-arrived`) and don't match C# enum member naming; the custom converter also makes
  the "fails deserialization, not silently defaults" requirement explicit and testable rather than
  relying on `JsonStringEnumConverter`'s built-in (but less discoverable) unknown-value behavior.

## No BLOCKED condition encountered

No field was proposed that would carry a message body, privileged content beyond the gated
`Snippet`, or a pre-authorized action token — the NFR-02/NFR-03 escalation trigger did not fire.
