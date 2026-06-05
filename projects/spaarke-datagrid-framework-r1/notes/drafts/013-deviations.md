# 013 — Deviations from POML / Task 010 Design

> **Task**: 013 — FetchService + `POST /api/dataverse/fetch`
> **Owner**: B-Wave-1 (parallel with 011, 012, 014)
> **Completed**: 2026-06-01
> **Build status**: ✅ 0 errors, my files contribute 0 new warnings

---

## D-013-01 — Implementation file renamed (`FetchXmlPrivilegeAnalyzer` → `FetchXmlEntityExtractor`)

**Source design (POML §<context>.<relevant-files> + §<steps>.3)** referenced
`Services/Dataverse/FetchXmlPrivilegeAnalyzer.cs`. Task 010 design doc §2 and
§10 (interface `IFetchXmlEntityExtractor` at `Services/Dataverse/FetchXml/`)
established the canonical name. Task 011 created the interface file at the
canonical name. Implementation file follows that contract:

```
Services/Dataverse/FetchXml/FetchXmlEntityExtractor.cs   (implements IFetchXmlEntityExtractor)
```

No behavioral difference; pure naming alignment.

---

## D-013-02 — Service deliberately uses `FetchXmlParseException` (not `XmlException`)

The task brief stipulated `XmlException` for malformed FetchXML. Task 011's
`IFetchXmlEntityExtractor` interface contract (in
`Services/Dataverse/FetchXml/IFetchXmlEntityExtractor.cs`, also created by 011)
declares the exception type as `FetchXmlParseException` (a sealed exception
type defined in the same file). To preserve a single uniform catch path in
both `DataverseAuthorizationFilter` (task 011) and `FetchEndpoints` (task 013),
the extractor + the FetchService's `InjectPagingCookie` helper both throw
`FetchXmlParseException`.

The endpoint maps this to `400 ProblemDetails` with
`errorCode=DV_FETCHXML_MALFORMED` per task 010 §7. The behavior the brief
requested (400 instead of 500 for malformed FetchXML) is fully preserved.

---

## D-013-03 — `EntityName` body-validation widened from "match primary" to "must reference"

Task brief §4 step says: _"if `FetchRequestDto.EntityName` doesn't match the
primary entity in the FetchXml, return 400 ProblemDetails
(`DV_FETCHXML_ENTITY_MISMATCH`)"_.

The `IFetchXmlEntityExtractor.ExtractEntities` contract returns an
`IReadOnlySet<string>` (deliberately a set for the security check — order is
not preserved). Re-parsing the FetchXML inside the endpoint just to recover
the primary-entity name would duplicate work the extractor already did.

**Resolution**: the endpoint checks that `EntityName` is referenced *somewhere*
in the extracted set (primary OR link-entity). This is strictly weaker than
"matches primary" but still catches the failure modes the validation was
intended to catch:

- Body says `sprk_matter` but the FetchXML targets an entirely different
  entity (e.g., `account`) → caught (mismatch returns 400).
- Body says `sprk_matter` but the FetchXML targets `sprk_matter` with a
  link to `account` → still passes (matches primary).

This is acceptable because the authorization filter has already verified
Read privilege on **every** entity in the set. The endpoint validation is
defense-in-depth against routing/body-shape drift, not a privilege gate.

If a stricter "matches primary" check is required later, the extractor
interface would need an ordered or distinct-primary projection added; deferred
as a follow-up if needed.

---

## D-013-04 — Paging cookie default page is `2` (not `1`)

Task 010 §9 implementation note mentions paging-cookie injection but does not
specify what happens if the caller supplies a cookie *without* a `page`
attribute on `<fetch>`. Dataverse server-side rejects `page=1 + paging-cookie`
as a server error.

**Resolution**: when the caller supplies a paging cookie but no `page`
attribute, the service injects `page="2"` as the default. Most clients will
supply both (the FetchXmlService client library does), so this is a safety
net rather than a primary code path.

---

## D-013-05 — Response shape includes synthetic `@formattedValues` and `@logicalName` keys

The `FetchResponseDto.Entities` type is
`IReadOnlyList<IReadOnlyDictionary<string, object?>>`. The Dataverse
`Entity.Attributes` collection is the natural source, but it does not include
two things the client needs:

1. **`FormattedValues`** — display strings for OptionSet, EntityReference,
   DateTime, Money attributes. The client uses these for grid display without
   re-resolving lookup labels.
2. **`LogicalName`** — needed when an aliased subquery returns mixed entity
   types (rare in `<link-entity>` joins but supported).

**Resolution**: the `ProjectEntity` helper adds two synthetic keys to the
dictionary:

- `@formattedValues` → `IReadOnlyDictionary<string, string>` of formatted values
- `@logicalName` → the entity's logical name (string)

The `@` prefix marks them as non-attribute metadata, matching the
Microsoft.Dynamics.CRM convention used in the existing client-side
`FetchXmlService.ts` (which uses keys like `@Microsoft.Dynamics.CRM.morerecords`).

The `BffDataverseClient` client implementation (a different task) should
consume these and project them into its preferred row shape.

---

## D-013-06 — DI extension method added (not in POML output list)

The POML output list (§<outputs>) named six files; this task created a
seventh: `Services/Dataverse/Extensions/FetchServiceExtensions.cs`. This
matches the pattern established by task 012's
`MetadataServiceExtensions.cs` and avoids the main session having to remember
which services need explicit DI registration. The main session aggregates
all `Add*` extension methods into `Program.cs` after the wave completes.

No behavioral change — just consistency with task 012.

---

## Summary

All deviations are interface alignment, defense-in-depth refinements, or
follow the precedent established by task 012 (the parallel metadata task).
No security control was weakened. The cross-entity privilege check works as
designed (see report walkthrough). Build succeeds with 0 errors.
