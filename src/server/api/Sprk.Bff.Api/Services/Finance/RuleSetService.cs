using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Who a rule set applies to (<c>sprk_ruleset.sprk_scope</c>, task 019). Values are the option values.</summary>
public enum RuleSetScope
{
    ClientDefault = 100000000,
    Firm = 100000001,
}

/// <summary>The review state of a drafted rule version (<c>sprk_policyversion.sprk_proposalstatus</c>, task 019).</summary>
public enum ProposalState
{
    Proposed = 100000000,
    Accepted = 100000001,
    Rejected = 100000002,
}

/// <summary>What happens when a billing rule fails (<c>sprk_policyversion.sprk_consequence</c>, task 019).</summary>
public enum BillingConsequence
{
    Reject = 100000000,
    Flag = 100000001,
    SuggestAdjustment = 100000002,
}

/// <summary>A rule set row as the admin sees it.</summary>
public sealed record RuleSetSummary(
    Guid Id, string Name, string Status, string Scope, Guid? FirmId, bool VisibleToFirms,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, int RuleCount)
{
    /// <summary>The Procedure whose rules the set holds (<c>sprk_ruleset.sprk_procedure</c>), or null for a legacy set. Internal and never
    /// serialized: the list route reads it to leave out the Procedure sets the caller cannot Read (uac-r2 re-check round 2, I-P5).</summary>
    internal Guid? ProcedureId { get; init; }
}

/// <summary>One drafted or published rule (a billing policy) as its WORKING version shows it.</summary>
/// <param name="InForce">The working version is the policy's current version.</param>
/// <param name="HasPublishedVersion">The policy has (or had) a version in force, so an edit creates a new version.</param>
/// <param name="RowVersion">
/// The working version row's version token (its Dataverse <c>versionnumber</c>, as a decimal string) as it was READ together with the content in
/// this view. The Rule Review page sends it back with Accept and Reject (<see cref="RuleSetService.DecideAsync"/>), so an admin never decides
/// content they were not shown. Null in the response to a write (the token after a write is not read back; reload the rule set to decide).
/// </param>
/// <param name="RejectionReason">
/// Why an admin rejected the draft (task 077, D-113): set only while <paramref name="State"/> is <c>Rejected</c>, otherwise null. <b>Admin-only</b>:
/// this view is the Rule Review read; no firm projection (<see cref="FirmClauseText"/>) carries it.
/// </param>
public sealed record ProposalView(
    Guid PolicyId, string PolicyCode, string Name, string State, bool InForce, bool HasPublishedVersion,
    Guid VersionId, int VersionNumber, string? RuleType, JsonNode? RuleBody, string? MessageTemplate, string? ShortHeadline,
    string? Consequence, string? DescriptionText, string? DescriptionFormal, string? DescriptionRefusal,
    string? ClauseQuote, string? ClauseLocation, IReadOnlyList<string> ValidationErrors,
    string? RowVersion = null, string? RejectionReason = null);

/// <summary>
/// A rule set with its rules (the Rule Review page's read). <see cref="NotConverted"/> is the not-converted list of the drafting run of the
/// set's Procedure's HIGHEST-numbered version (task 116: the marker lives on the Procedure Version), or null when that version has not been
/// drafted, or the set has no Procedure.
/// </summary>
public sealed record RuleSetDetail(
    RuleSetSummary RuleSet, JsonNode? NotConverted, IReadOnlyList<ProposalView> Proposals);

/// <summary>An edit to a rule's working version. A <c>null</c> member is left as it is.</summary>
public sealed record ProposalEdit(
    string? RuleType, string? RuleBody, string? MessageTemplate, string? ShortHeadline, string? Consequence,
    string? ClauseReference, string? ClauseLocation, Guid? CallerObjectId);

/// <summary>The result of an edit: the working version after it, and whether a new version was created.</summary>
public sealed record ProposalEdited(ProposalView Proposal, bool CreatedNewVersion);

/// <summary>A version a publish put in force and the version it superseded.</summary>
public sealed record PublishedRuleVersion(Guid PolicyId, Guid VersionId, int VersionNumber, Guid? SupersededVersionId);

/// <summary>A successful publish. <see cref="ClauseQuotesVisibleToFirms"/> mirrors the rule set's flag at publish (FR-28, D-66).</summary>
public sealed record RuleSetPublished(
    Guid RuleSetId, string Status, DateOnly EffectiveDate, IReadOnlyList<PublishedRuleVersion> Published,
    int NotPublishedProposed, int NotPublishedRejected, bool ClauseQuotesVisibleToFirms, string ClauseVisibilityNotice);

/// <summary>
/// What the rule drafting job reads (ebilling-r1 task 075, re-keyed per Procedure Version by task 116; model 7.2): a Draft version of a Billing
/// Procedure, the Procedure's rule set the drafts go into, and the version's ONE Servable Procedure-source Stored File, found server-side
/// (<see cref="FileId"/>; its bytes are read through the file store, never here). <see cref="AlreadyDrafted"/> is the idempotency marker: the
/// VERSION's Yes/No <c>sprk_notconverted</c> ("drafted", EB-D-211), written Yes last, after the not-converted list.
/// </summary>
public sealed record DraftSource(
    Guid ProcedureVersionId, Guid ProcedureId, Guid RuleSetId, Guid FileId, string FileName, bool AlreadyDrafted);

/// <summary>One rule the drafting job proposes: a flat rule body (type plus parameters) and the columns a version needs (task 075).</summary>
public sealed record DraftedRule(
    string RuleType, string RuleBody, string MessageTemplate, string ShortHeadline, string Consequence, string ClauseQuote, string? ClauseLocation);

/// <summary>Text of the source that did not become a rule, and why. Stored in the Procedure Version's <c>sprk_notconvertedlist</c> (EB-D-211).</summary>
public sealed record NotConvertedClause(string Text, string? Location, string Reason);

/// <summary>The result of adding drafted rules: how many were stored, how many were already there, and the full not-converted list.</summary>
public sealed record DraftsAdded(int Stored, int AlreadyPresent, IReadOnlyList<NotConvertedClause> NotConverted);

/// <summary>The result of the retry compensation (task 075): earlier untouched drafts removed, and rules of the rule set that were kept.</summary>
public sealed record DraftsCompensated(int Removed, int Kept);

/// <summary>
/// Task 116 fix round 4 (EB-D-216): one rule a redraft supersedes: its policy and every active version of it. The redraft deactivates the
/// policy and all these versions together, or none of them. Fix round 5 (EB-D-216a): <see cref="RuleSetId"/> is the rule set the policy row
/// names and <see cref="ProcedureVersionIds"/> the Procedure Version each of its versions names, as read, so the route can refuse a draft
/// outside the rule set and Procedure Version it checked the caller's rights on.
/// </summary>
public sealed record SupersededDraft(Guid PolicyId, IReadOnlyList<Guid> VersionIds, Guid RuleSetId, IReadOnlyList<Guid> ProcedureVersionIds);

/// <summary>
/// Task 116 fix round 4 (EB-D-216): the drafts a redraft of a Procedure Version will deactivate, decided before anything is written. Fix
/// round 5 (EB-D-216a): the route has already checked the caller's rights on the version's rule set and Procedure, and checks every draft
/// here belongs to them, before it hands the plan to <see cref="RuleSetService.DeactivateSupersededDraftsAsync"/>. Empty for a first draft.
/// </summary>
public sealed record RedraftPlan(IReadOnlyList<SupersededDraft> Drafts)
{
    /// <summary>Every row the redraft writes, versions first: at most <see cref="RuleSetService.MaxPerTransaction"/> (one transaction).</summary>
    public IEnumerable<(string Table, Guid Id)> Rows =>
        Drafts.SelectMany(d => d.VersionIds.Select(v => (RuleSetService.VersionEntity, v)))
            .Concat(Drafts.Select(d => (RuleSetService.PolicyEntity, d.PolicyId)));
}

/// <summary>One problem with one field of one rule (or of the request when <see cref="PolicyId"/> is null).</summary>
public sealed record RuleSetError(Guid? PolicyId, string? RuleName, string Field, string Message, string? Reason);

/// <summary>
/// What a firm may be shown of a rule's clause. Every member is null when nothing may be shown (D-66, D-79; ebilling-r1 task 083).
/// <paramref name="FirmFacingName"/> labels the clause: the Procedure's firm-facing name (its name when that is blank), or the neutral
/// "Billing instructions for this record" for a record rule; null for a legacy rule set, which has no Procedure.
/// </summary>
/// <remarks>
/// There is deliberately no member for the source document or any link to it (D-63), and none for the Procedure's internal name, its id, its
/// version id, an override reason or any other basis member (model 7.4): a firm never gets one.
/// </remarks>
public sealed record FirmClauseText(string? Quote, string? SectionReference, string? FirmFacingName = null)
{
    public static FirmClauseText None { get; } = new(null, null, null);
}

/// <summary>
/// THE single decision whether a rule's clause text may be shown to a firm (ebilling-r1 task 076, D-66 / D-79): the findings (130), the SPA
/// invoice detail (134) and the Billing Guidelines view (135) all call it and nothing else decides.
/// </summary>
public static class RuleSetClauseVisibility
{
    /// <summary>
    /// With the rule set's "visible to firms" flag on, the quote and the section reference (and <paramref name="firmFacingName"/> with them);
    /// with it off, nothing at all (the firm sees the rule name and reason only). Fails closed: a missing flag is off, blank text is returned
    /// as null, and a clause with neither a quote nor a section reference is no text (a label alone is not a clause).
    /// </summary>
    public static FirmClauseText ForFirm(bool? ruleSetVisibleToFirms, string? clauseQuote, string? clauseLocation, string? firmFacingName = null)
    {
        if (ruleSetVisibleToFirms != true)
        {
            return FirmClauseText.None;
        }

        var quote = NullIfBlank(clauseQuote);
        var location = NullIfBlank(clauseLocation);
        return quote is null && location is null ? FirmClauseText.None : new FirmClauseText(quote, location, NullIfBlank(firmFacingName));
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}

public enum RuleSetOutcomeKind
{
    Done,

    /// <summary>The input has problems (<see cref="RuleSetOutcome{T}.Errors"/>): HTTP 400, nothing written.</summary>
    Invalid,

    /// <summary>Refused for another stable reason (<see cref="RuleSetOutcome{T}.ErrorCode"/> with its status).</summary>
    Refused,

    /// <summary>No owner team could be resolved for the new rows: HTTP 409, nothing written.</summary>
    OwnerRefused,
}

/// <summary>The result of a rule set operation, mapped to HTTP by <c>RuleSetEndpoints</c>.</summary>
public sealed class RuleSetOutcome<T> where T : class
{
    private RuleSetOutcome() { }

    public RuleSetOutcomeKind Kind { get; private init; }
    public T? Value { get; private init; }
    public IReadOnlyList<RuleSetError> Errors { get; private init; } = Array.Empty<RuleSetError>();
    public string? ErrorCode { get; private init; }
    public int StatusCode { get; private init; }
    public string? Detail { get; private init; }
    public RecordOwnerResolution? OwnerRefusal { get; private init; }

    internal static RuleSetOutcome<T> Done(T value) => new() { Kind = RuleSetOutcomeKind.Done, Value = value };

    internal static RuleSetOutcome<T> Invalid(string errorCode, IReadOnlyList<RuleSetError> errors) =>
        new() { Kind = RuleSetOutcomeKind.Invalid, ErrorCode = errorCode, Errors = errors };

    internal static RuleSetOutcome<T> Refused(int statusCode, string errorCode, string detail) =>
        new() { Kind = RuleSetOutcomeKind.Refused, StatusCode = statusCode, ErrorCode = errorCode, Detail = detail };

    internal static RuleSetOutcome<T> OwnerRefused(RecordOwnerResolution resolution) =>
        new() { Kind = RuleSetOutcomeKind.OwnerRefused, OwnerRefusal = resolution };
}

/// <summary>
/// The rule set API's server-side owner (ebilling-r1 task 076, FR-28): create a rule set from an uploaded source document, read its rules
/// beside their clause quotes, edit / reject / accept each, publish, assign and set visibility.
/// </summary>
/// <remarks>
/// <para><b>Model.</b> A rule set (<c>sprk_ruleset</c>) groups billing policies (<c>sprk_policy</c>, <c>sprk_domain</c> = Billing, task 019); a
/// policy's versions (<c>sprk_policyversion</c>) hold the rule. A version is IN FORCE when it is the policy's <c>sprk_currentversion</c>;
/// nothing else is. A version's review state is <c>sprk_proposalstatus</c>. A policy's predecessor version is its sibling with the next
/// lower number (019: no link column). The WORKING version of a rule is its highest-numbered version.</para>
/// <para><b>Immutability.</b> A version that is current, or has ever been (it carries an <c>sprk_inforceto</c>), is never updated: an edit
/// creates a new version (next number, Proposed). A version that was never in force is a draft and is edited in place. Publish stamps the
/// predecessor's <c>sprk_inforceto</c> and moves the pointer; the only writes to an existing version are a draft's own columns (in place),
/// its <c>sprk_inforcefrom</c> at publish (the effective date) and a predecessor's <c>sprk_inforceto</c>.</para>
/// <para><b>Publish is all or nothing.</b> Every accepted rule that is not yet in force is validated
/// (<see cref="PolicyVersionValidator.ValidateForSave"/> with <see cref="RuleDomain.Billing"/>, consequence set, headline and template
/// set) before the first write; one failure refuses the whole publish with per-rule errors and writes nothing. The writes are three
/// transactions in an order that a retry completes: (1) the versions (the new versions' in-force-from, the predecessors' in-force-to), (2) the
/// policies (pointer and enabled), (3) the rule set (Published, effective-from). Nothing is in force before step 2. The stamp and the
/// pointer move together (D-14): the writes run with <see cref="CancellationToken.None"/> (a client that disconnects does not leave a half
/// state), and a failure in step 1 or 2 takes back the end dates written for policies whose pointer did not move, so the old versions
/// stay in force with no end date. A failure of step 3 leaves every rule in force and the set not Published; the next publish writes the set.
/// A retry re-stamps an old version whose end date differs from the new effective date, so it leaves no gap. Each step is atomic per
/// 1,000 rows (the Dataverse transaction cap), the whole is not. Steps 1 and 2, the take-back and an accept / reject are conditional on
/// the row versions read (review pass 2): a row another admin changed since is refused with 409 <c>ruleset.concurrent_edit</c>, never
/// overwritten.</para>
/// <para><b>Domain.</b> Every read of <c>sprk_policy</c> and <c>sprk_ruleset</c> here carries <c>sprk_domain = Billing</c> in the query
/// (<see cref="RuleDomains.AddBillingFilter"/>) AND is re-checked per row (<see cref="RuleDomains.IsBillingRow"/>), so an Ontology policy
/// (empty or Ontology domain) is the uniform "not found" for every operation and is never read into a response or written. This service
/// never writes <c>sprk_domain</c>. Versions are read only through a billing policy's id.</para>
/// <para><b>Identity.</b> The route filter has already established, as the caller, that they hold the Dataverse table privileges of a billing
/// admin (D-76). The writes run as the application (like the rate card import); the rows are owned by the owner team resolved for the caller.
/// The rule set, policy and version rows are hard-deny tables (EB-D-168 / EB-D-168a, UAC 194b R4b-2): their question is built by
/// <see cref="RecordOwnershipContext.ForRow"/> for the row's table (<see cref="ContentOwnerContext"/>), so an ordinary answer is the
/// customer's restricted-content owner team, never a business unit's default team. The source document is not hard-deny and keeps the
/// caller's business-unit team (<see cref="CallerOwnerContext"/>).</para>
/// <para>§11: <i>Existing</i> — RateCardImportService (the Draft-to-Published shape, SPE upload, ownership) and the Ontology authoring publish
/// order (stamp then move). <i>Extension</i> — neither can be extended: the Ontology service is caller-identity and Ontology-domain by
/// design, and the rate card service owns other tables. <i>Cost of doing nothing</i> — no billing rule can be reviewed or put in force.</para>
/// </remarks>
public class RuleSetService
{
    internal const string RuleSetEntity = "sprk_ruleset";
    internal const string PolicyEntity = "sprk_policy";
    internal const string VersionEntity = "sprk_policyversion";
    internal const string FirmEntity = "sprk_organization";

    internal const int RuleSetDraft = 1;
    internal const int RuleSetPublished = 659490000;
    internal const int RuleSetRetired = 2;

    // Stable error codes (ADR-019).
    internal const string CodeInvalid = "ruleset.invalid";
    internal const string CodePublishInvalid = "ruleset.publish_invalid";
    internal const string CodeNotFound = "ruleset.not_found";
    internal const string CodeProposalNotFound = "ruleset.proposal_not_found";
    internal const string CodeNothingToPublish = "ruleset.nothing_to_publish";
    internal const string CodeRetired = "ruleset.retired";
    internal const string CodeInForce = "ruleset.proposal_in_force";
    internal const string CodeResultTruncated = "ruleset.result_truncated";

    /// <summary>The detail of <see cref="CodeResultTruncated"/> when the list holds more rule sets than one pass reads (also the list route's, task 089e K2).</summary>
    internal const string TooManyRuleSetsMessage = "There are more rule sets than can be listed in one pass.";

    internal const string CodeWriteFailed = "ruleset.write_failed";
    internal const string CodeSourceMissing = "ruleset.source_missing";
    internal const string CodeAlreadyDrafted = "ruleset.already_drafted";

    /// <summary>Fix round 1: the version's source file was replaced while a run drafted from the earlier one (the job retries).</summary>
    internal const string CodeSourceChanged = "ruleset.source_changed";

    /// <summary>
    /// Fix round 4 (EB-D-216): a draft the redraft would deactivate is no longer a Draft (published, put in force, given a new version, moved)
    /// between the plan and the write. Nothing is deactivated (409).
    /// </summary>
    internal const string CodeDraftChanged = "ruleset.draft_changed";

    /// <summary>
    /// Fix round 4 (EB-D-216): the job found drafts of an earlier source file that a reviewer worked on and no <c>/draft</c> request has
    /// deactivated. The job does not deactivate them (only a request, after the caller's rights checks, does), so it drafts nothing (409); the
    /// administrator starts drafting again.
    /// </summary>
    internal const string CodeSupersededDraftsRemain = "ruleset.superseded_drafts_remain";

    /// <summary>Fix round 4 (EB-D-216): a rule of the set that a redraft deactivated, asked for by id (an edit, accept or reject): 409.</summary>
    internal const string CodeDraftInactive = "ruleset.draft_inactive";

    /// <summary>
    /// Fix round 5 (K2): a redraft would deactivate more rows than one transaction holds (<see cref="MaxPerTransaction"/>, the same cap the
    /// deactivation is sent under). Refused before anything is written (422).
    /// </summary>
    internal const string CodeRedraftTooLarge = "ruleset.redraft_too_large";

    /// <summary>
    /// Fix round 5 (F1): the deactivation transaction gave no answer (a timeout): it may have committed. Nothing more was done; a retry
    /// converges (a draft it deactivated is no longer planned) (503).
    /// </summary>
    internal const string CodeRedraftOutcomeUnknown = "ruleset.redraft_outcome_unknown";

    /// <summary>Fix round 4 (EB-D-216): <c>statecode</c> / <c>statuscode</c> of a deactivated policy or policy version (Inactive, its default status).</summary>
    internal const int StateInactive = 1;
    internal const int StatusInactive = 2;

    /// <summary>Task 116 (model 7.2, O-24): drafting runs only for a Draft Procedure Version (409).</summary>
    internal const string CodeVersionNotDraft = "procedure_version.not_draft";

    /// <summary>Task 116 (O-24): drafting runs only for a Procedure whose Area is Billing; Ontology Procedures are document-only in R1 (400).</summary>
    internal const string CodeNotBilling = "procedure.not_billing";

    /// <summary><c>sprk_procedureversion.sprk_notconverted</c> (080, Two Options): the per-version "drafted" marker (task 116, EB-D-211). The
    /// drafting run writes Yes LAST, after its rules and <see cref="VersionNotConvertedListColumn"/>; No or empty means not drafted yet.</summary>
    internal const string VersionDraftedColumn = "sprk_notconverted";

    /// <summary><c>sprk_procedureversion.sprk_notconvertedlist</c> (080, field-secured memo, EB-D-211): the JSON list of what the version's
    /// drafting run could not convert, written BEFORE <see cref="VersionDraftedColumn"/>. The rule set detail shows the list of the
    /// highest-numbered version.</summary>
    internal const string VersionNotConvertedListColumn = "sprk_notconvertedlist";

    /// <summary>
    /// Fix round 2 (F2): the server-internal shape of <see cref="VersionNotConvertedListColumn"/> a run writes when it knows the file it drafted
    /// from: <c>{"draftedFromFileId": "&lt;Stored File id&gt;", "notConverted": [ ... ]}</c>. It is written FIRST, so the drafted-from file is
    /// on the row before the marker is. The client contract (<see cref="RuleSetDetail.NotConverted"/>) is still the bare list: every reader
    /// unwraps it (<see cref="ParseStoredNotConverted"/>). A bare list (the earlier shape) carries no file id.
    /// </summary>
    internal const string EnvelopeFileIdProperty = "draftedFromFileId";

    /// <summary>The list inside the envelope (<see cref="EnvelopeFileIdProperty"/>).</summary>
    internal const string EnvelopeListProperty = "notConverted";

    internal const string CodeConcurrentEdit = "ruleset.concurrent_edit";
    internal const string CodeSystemRule = "ruleset.system_rule";
    internal const string CodePublishInProgress = "ruleset.publish_in_progress";

    /// <summary>Task 081: a Procedure's rule set is published by publishing a Procedure Version, never by the rule-set publish.</summary>
    internal const string CodeProcedurePublish = "ruleset.procedure_publish";

    /// <summary>Task 081: a Procedure's rules are edited and decided on its Draft version; there is none.</summary>
    internal const string CodeNoDraftVersion = "ruleset.no_draft_version";

    /// <summary>
    /// Task 081 (I-P4, review F): a Procedure's rule set mirrors the Procedure's scope and visibility (ProcedureService writes both, under the
    /// caller's Write on the Procedure and the scope lock), so the rule-set assignment and visibility routes refuse it.
    /// </summary>
    internal const string CodeProcedureOwned = "ruleset.procedure_owned";

    // ── Task 081 (EB-D-148, procedure model 7.2): the version chain re-keyed per (policy, Procedure Version). ──

    /// <summary><c>sprk_ruleset.sprk_procedure</c>: the Procedure whose rules the set holds (080). A set without it is a legacy set.</summary>
    internal const string ProcedureColumn = "sprk_procedure";

    /// <summary><c>sprk_policyversion.sprk_procedureversion</c>: the Procedure Version a billing policy version belongs to (080).</summary>
    internal const string ProcedureVersionColumn = "sprk_procedureversion";

    /// <summary><c>sprk_policyversion.sprk_publishstate</c> (global choice, 080): Draft or Published. Only Published versions are ever loaded
    /// for evaluation (082); <c>sprk_currentversion</c> means "latest published", for display only (ontology-r1 D-14 amendment).</summary>
    internal const string PublishStateColumn = "sprk_publishstate";

    internal const int PublishStateDraft = 100000000;
    internal const int PublishStatePublished = 100000001;

    /// <summary>
    /// How long the publish claim of a rule set lives if its holder dies without releasing it (the stale-claim timeout). A publish
    /// is a handful of Dataverse round trips (at most a few thousand rows, 1,000 per transaction), so ten minutes is far past any
    /// run; a claim that expires early lets a second publish in, which the conditional writes still catch except for the take-back
    /// race the claim exists to close.
    /// </summary>
    internal static readonly TimeSpan PublishClaimLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The lease id of the publish claim of one rule set: one publish of a set at a time, across every BFF instance.</summary>
    internal static string PublishClaimId(Guid ruleSetId) => $"ruleset-publish:{ruleSetId:D}";

    /// <summary>The prefix of a drafted rule's policy code: <c>BRD-{rule set id, 32 hex}-{n, 3 digits}</c> (50 characters at most is the column; this is 40).</summary>
    internal const string DraftCodePrefix = "BRD-";

    /// <summary>The most rules one drafting run stores; the rest are listed as not converted.</summary>
    internal const int MaxDraftedRules = 500;

    // sprk_notconvertedlist holds 100,000 characters; leave room for the overflow entry.
    private const int MaxNotConvertedJsonChars = 95_000;
    private const int MaxClauseQuoteChars = 4000;
    private const int MaxClauseLocationChars = 200;
    private const int DraftPolicyPriority = 500;

    /// <summary>The notice a publish carries when the flag is on (FR-28, D-66).</summary>
    internal const string VisibleNotice = "Clause quotes will be visible to firms.";

    /// <summary>The notice a publish carries when the flag is off.</summary>
    internal const string HiddenNotice = "Clause quotes will not be visible to firms.";

    // <= Dataverse's 1,000-request ExecuteTransaction cap; also keeps ConditionOperator.In under SQL Server's parameter limit.
    internal const int MaxPerTransaction = 1000;
    internal const int MaxInValues = 500;

    /// <summary>The row version Dataverse advances on every write; the publish and decide writes are conditional on it (review pass 2).</summary>
    internal const string RowVersionColumn = "versionnumber";

    /// <summary>The reason an admin gave when rejecting a draft (<c>sprk_policyversion</c>, task 077, D-113). Admin-only: never selected by a firm read.</summary>
    internal const string RejectionReasonColumn = "sprk_rejectionreason";

    /// <summary>The longest rejection reason (the column's max length).</summary>
    public const int MaxRejectionReasonChars = 2000;

    /// <summary>
    /// The namespace of the ids of versions an edit creates (RFC 4122 version 5). A constant of this service, generated once; never
    /// change it: the primary key of version N of a policy is <see cref="VersionIdFor"/> of it, which is what makes two concurrent edits
    /// of one in-force version collide instead of both creating version N.
    /// </summary>
    internal static readonly Guid EditVersionIdNamespace = new("3d7b9c1e-52a4-4f86-b0e3-9a6c2f41d875");

    // sprk_policy columns billing needs on a policy that is NOT created here (075 creates them): see the notes for the row shape.
    private static readonly string[] PolicyColumns =
        { "sprk_name", "sprk_policycode", "sprk_currentversion", "sprk_enabled", "sprk_ruleset", RuleDomains.PolicyDomainColumn, RowVersionColumn };

    private static readonly string[] VersionColumns =
    {
        "sprk_policy", "sprk_versionnumber", "sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate", "sprk_shortheadline",
        "sprk_consequence", "sprk_proposalstatus", "sprk_clausereference", "sprk_clauselocation", "sprk_inforcefrom", "sprk_inforceto",
        RowVersionColumn, RejectionReasonColumn, ProcedureVersionColumn, PublishStateColumn,
    };

    // Task 089c (089a K1): the core-record lookups are read too, so every read here can refuse a record-scoped set
    // (RecordRuleSet.IsRecordScoped: a record scope OR a core lookup).
    // Task 116: no source-document lookup (retired: a Procedure Version's file is its Stored File) and no set-level sprk_notconverted (the
    // drafting marker is the Procedure Version's).
    private static readonly string[] RuleSetColumns = new[]
    {
        "sprk_name", "sprk_scope", "sprk_firm", "sprk_visibletofirms", "sprk_effectivefrom", "sprk_effectiveto",
        "statuscode", RuleDomains.PolicyDomainColumn, ProcedureColumn,
    }.Concat(InvoiceFamilyCoreRecordGuard.FilingCoreTables).ToArray();

    private readonly IGenericEntityService _entities;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly PolicyVersionValidator _validator;
    private readonly RuleBodyDescriber _describer;
    private readonly IScheduledJobLease _lease;
    private readonly ILogger<RuleSetService> _logger;

    public RuleSetService(
        IGenericEntityService entities,
        IRecordOwnershipResolver ownership,
        PolicyVersionValidator validator,
        RuleBodyDescriber describer,
        IScheduledJobLease lease,
        ILogger<RuleSetService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _describer = describer ?? throw new ArgumentNullException(nameof(describer));
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reads
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The billing rule sets, newest first, each with its rule count.</summary>
    public virtual async Task<RuleSetOutcome<IReadOnlyList<RuleSetSummary>>> ListAsync(CancellationToken ct)
    {
        var query = new QueryExpression(RuleSetEntity) { ColumnSet = new ColumnSet(RuleSetColumns) };
        RuleDomains.AddBillingFilter(query);
        query.Orders.Add(new OrderExpression("createdon", OrderType.Descending));
        var sets = await _entities.RetrieveMultipleAsync(query, ct);
        if (sets.MoreRecords)
        {
            return RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, TooManyRuleSetsMessage);
        }

        // Task 089c: a record-scoped set is its record's secure child, changed only through the record's override routes; never listed here.
        var rows = sets.Entities.Where(r => RuleDomains.IsBillingRow(r) && !RecordRuleSet.IsRecordScoped(r)).ToList();
        var counts = new Dictionary<Guid, int>();
        foreach (var batch in rows.Select(r => (object)r.Id).Chunk(MaxInValues))
        {
            var policies = await _entities.RetrieveMultipleAsync(PolicyQuery("sprk_ruleset", ConditionOperator.In, batch), ct);
            if (policies.MoreRecords)
            {
                return RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Refused(
                    StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "There are more rules than can be counted in one pass.");
            }

            foreach (var policy in policies.Entities.Where(RuleDomains.IsBillingRow))
            {
                if (policy.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id is { } setId)
                {
                    counts[setId] = counts.GetValueOrDefault(setId) + 1;
                }
            }
        }

        return RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(
            rows.Select(r => Summarize(r, counts.GetValueOrDefault(r.Id))).ToList());
    }

    /// <summary>One rule set with every rule as its working version shows it, beside its clause quote.</summary>
    public virtual async Task<RuleSetOutcome<RuleSetDetail>> GetAsync(Guid ruleSetId, CancellationToken ct)
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return RuleSetOutcome<RuleSetDetail>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found.");
        }

        var loaded = await LoadRulesAsync(ruleSetId, ct);
        if (loaded.Truncated)
        {
            return RuleSetOutcome<RuleSetDetail>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "The rule set has more rules than can be read in one pass.");
        }

        var views = new List<ProposalView>();
        foreach (var rule in loaded.Rules.OrderBy(r => r.Policy.Code, StringComparer.Ordinal))
        {
            views.Add(await ViewAsync(rule, ct, carryRowVersion: true));
        }

        JsonNode? notConverted = null;
        if (set.GetAttributeValue<EntityReference>(ProcedureColumn)?.Id is { } procedureId
            && await LatestVersionNotConvertedAsync(procedureId, ct) is { Length: > 0 } text)
        {
            try
            {
                notConverted = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                notConverted = JsonValue.Create(text);
            }
        }

        return RuleSetOutcome<RuleSetDetail>.Done(new RuleSetDetail(Summarize(set, loaded.Rules.Count), notConverted, views));
    }

    /// <summary>
    /// Task 116: the not-converted list of the Procedure's HIGHEST-numbered version (the version under review), or null when it has none. The
    /// columns are pinned (the version's Procedure is re-checked on the row, so a query that lost its filter cannot answer for another
    /// Procedure). Fix round 1: the list is returned only under the drafted marker (Yes): a run writes the list first and the marker last, so a
    /// list without the marker is a run that has not finished (or a reset in progress), and the client contract says null until a run finishes.
    /// Fix round 2 (F2): "finished" is <see cref="IsDraftedAsync"/> (a Yes whose envelope names a file that is no longer the version's one
    /// Servable source file is a stale Yes, so null), and the list returned is the envelope's list, never the envelope.
    /// </summary>
    private async Task<string?> LatestVersionNotConvertedAsync(Guid procedureId, CancellationToken ct)
    {
        var query = new QueryExpression(ProcedureEntity.VersionEntity)
        {
            ColumnSet = new ColumnSet(
                ProcedureEntity.VersionProcedureColumn, ProcedureEntity.VersionNumberColumn, VersionNotConvertedListColumn, VersionDraftedColumn),
            TopCount = 1,
        };
        query.Criteria.AddCondition(ProcedureEntity.VersionProcedureColumn, ConditionOperator.Equal, procedureId);
        query.Orders.Add(new OrderExpression(ProcedureEntity.VersionNumberColumn, OrderType.Descending));
        var latest = (await _entities.RetrieveMultipleAsync(query, ct)).Entities
            .FirstOrDefault(v => v.GetAttributeValue<EntityReference>(ProcedureEntity.VersionProcedureColumn)?.Id == procedureId);
        return latest is not null && await IsDraftedAsync(latest, ct)
            ? ParseStoredNotConverted(latest.GetAttributeValue<string>(VersionNotConvertedListColumn)).ListText
            : null;
    }

    /// <summary>
    /// Fix round 2 (F2): what <see cref="VersionNotConvertedListColumn"/> holds. <see cref="StoredNotConverted.IsEnvelope"/> is true for the
    /// envelope (<see cref="EnvelopeFileIdProperty"/> present), whose <see cref="StoredNotConverted.DraftedFromFileId"/> is the file the run
    /// drafted from (null when it cannot be read as a Guid) and whose <see cref="StoredNotConverted.ListText"/> is its list. Anything else that is
    /// not blank is the earlier bare-list shape: no file id, the text itself is the list. Blank is no list at all.
    /// </summary>
    internal readonly record struct StoredNotConverted(bool IsEnvelope, Guid? DraftedFromFileId, string? ListText);

    /// <inheritdoc cref="StoredNotConverted"/>
    internal static StoredNotConverted ParseStoredNotConverted(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new StoredNotConverted(false, null, null);
        }

        try
        {
            if (JsonNode.Parse(text) is JsonObject envelope && envelope.ContainsKey(EnvelopeFileIdProperty))
            {
                Guid? fileId = envelope[EnvelopeFileIdProperty] is JsonValue value && value.TryGetValue<string>(out var raw)
                    && Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty
                        ? parsed
                        : null;
                return new StoredNotConverted(true, fileId, envelope[EnvelopeListProperty]?.ToJsonString());
            }
        }
        catch (JsonException)
        {
            // Not JSON: the earlier shape's text, returned as it is (the read shows it as a string).
        }

        return new StoredNotConverted(false, null, text);
    }

    /// <summary>
    /// Fix round 2 (F2): whether <paramref name="version"/> (read with <see cref="VersionDraftedColumn"/> and
    /// <see cref="VersionNotConvertedListColumn"/>) is drafted. Only an explicit Yes can be (No or empty is not). Under a Yes:
    /// <list type="bullet">
    /// <item>an envelope is drafted only while the file it names is still the version's ONE Servable source file. A Yes left behind for a file
    /// that has since been replaced (a take-back that failed, a host that stopped after the marker) is stale: not drafted, so the next
    /// <c>/draft</c> or retry drafts the current file;</item>
    /// <item>an envelope that names no file (<see cref="ClearedNotConverted"/>, what the version-file route's re-check writes) is not drafted;</item>
    /// <item>a list with no envelope (the earlier bare list, or no list at all) is drafted, as before.</item>
    /// </list>
    /// </summary>
    private async Task<bool> IsDraftedAsync(Entity version, CancellationToken ct)
    {
        if (version.GetAttributeValue<bool?>(VersionDraftedColumn) != true)
        {
            return false;
        }

        var stored = ParseStoredNotConverted(version.GetAttributeValue<string>(VersionNotConvertedListColumn));
        if (!stored.IsEnvelope)
        {
            return true;
        }

        return stored.DraftedFromFileId is { } fileId && await IsTheSourceFileAsync(version.Id, fileId, ct);
    }

    /// <summary>
    /// Fix round 2: the list value the version-file route's re-check writes to clear a version's list (<c>ProcedureService</c>): an envelope
    /// that names no file and holds no list. Unlike an empty column it can never read as drafted, so a drafting run that lands its marker
    /// after the clear (it wrote its list before the clear) leaves a version that is drafted again, not one stuck as drafted.
    /// </summary>
    internal static readonly string ClearedNotConverted = new JsonObject { [EnvelopeFileIdProperty] = null }.ToJsonString();

    /// <summary>
    /// Fix round 2 (F2): the value a run writes to <see cref="VersionNotConvertedListColumn"/>: the envelope naming
    /// <paramref name="draftedFromFileId"/> around <paramref name="listJson"/>, or the bare list when the run does not know its file
    /// (<see cref="Guid.Empty"/>, the overload without a file id).
    /// </summary>
    internal static string StoredNotConvertedOf(Guid draftedFromFileId, string listJson) =>
        draftedFromFileId == Guid.Empty
            ? listJson
            : new JsonObject
            {
                [EnvelopeFileIdProperty] = draftedFromFileId.ToString("D"),
                [EnvelopeListProperty] = JsonNode.Parse(listJson),
            }.ToJsonString();

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Drafting (task 075): the job reads the source, then adds Proposed rules through the same validation and row shape as an edit
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds what the drafting job and its trigger route work on (task 116, model 7.2 "Drafting", O-24): the Procedure Version
    /// <paramref name="procedureVersionId"/>, which must be a Draft (409 <c>procedure_version.not_draft</c>) of a live (409
    /// <c>procedure.retired</c>) Procedure whose Area is Billing (400 <c>procedure.not_billing</c>), that Procedure's rule set (409
    /// <c>procedure.ruleset_missing</c>; a retired set 409 <c>ruleset.retired</c>), and the version's ONE Servable Procedure-source Stored File
    /// (none, or more than one, 422 <c>ruleset.source_missing</c>). An unknown version is 404 <c>procedure.version_not_found</c>.
    /// <see cref="DraftSource.AlreadyDrafted"/> is the version's marker. The file's bytes are not read here: the job reads them through
    /// <c>StoredFileService.OpenForProcessingAsync</c>, which re-checks the row (Servable, own path, hash, scan verdict).
    /// </summary>
    public virtual Task<RuleSetOutcome<DraftSource>> GetDraftSourceAsync(Guid procedureVersionId, CancellationToken ct) =>
        GetDraftSourceAsync(null, procedureVersionId, ct);

    /// <summary>
    /// The trigger route's read (task 116): as <see cref="GetDraftSourceAsync(Guid, CancellationToken)"/>, and the version must be one of
    /// <paramref name="procedureId"/>'s; a version of another Procedure answers the unknown-id 404 <c>procedure.version_not_found</c> before any
    /// other check, so the route never says anything about another Procedure's version.
    /// </summary>
    public virtual async Task<RuleSetOutcome<DraftSource>> GetDraftSourceAsync(Guid? procedureId, Guid procedureVersionId, CancellationToken ct)
    {
        var target = await LoadDraftTargetAsync<DraftSource>(procedureVersionId, ct, procedureId);
        if (target.Refusal is { } refusal)
        {
            return refusal;
        }

        // Fix round 1 (K4): a finished run is decided before the source file is chosen, so a drafted version answers 409 already-drafted (and
        // the job a no-op). Fix round 2 (F2): "drafted" already includes "its run drafted the version's current file" for a list that names
        // its file, so a Yes left for a replaced file falls through to the source check below and is drafted again.
        if (target.AlreadyDrafted)
        {
            return RuleSetOutcome<DraftSource>.Done(new DraftSource(
                procedureVersionId, target.ProcedureId, target.RuleSet!.Id, Guid.Empty, "source", AlreadyDrafted: true));
        }

        var files = await LoadServableSourceFilesAsync(procedureVersionId, ct);
        if (files.Count != 1)
        {
            return RuleSetOutcome<DraftSource>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeSourceMissing,
                files.Count == 0
                    ? "The Procedure Version has no servable source file to draft rules from. Upload its file first."
                    : "The Procedure Version has more than one servable source file. Upload the file again to replace them.");
        }

        var file = files[0];
        return RuleSetOutcome<DraftSource>.Done(new DraftSource(
            procedureVersionId, target.ProcedureId, target.RuleSet!.Id, file.Id,
            file.GetAttributeValue<string>(StoredFileSchema.Name) is { Length: > 0 } name ? name : "source",
            AlreadyDrafted: false));
    }

    /// <summary>A drafting target (task 116): the version's Procedure, the Procedure's rule set, the version's marker, or the refusal.</summary>
    private sealed record DraftTarget<T>(Guid ProcedureId, Entity? RuleSet, bool AlreadyDrafted, RuleSetOutcome<T>? Refusal) where T : class;

    /// <summary>
    /// The columns of a Procedure Version a drafting read takes (task 116). Pinned: an absent marker reads as "not drafted", so the select must
    /// name it (a column that is not selected is absent too).
    /// </summary>
    internal static readonly string[] DraftVersionColumns =
    {
        ProcedureEntity.VersionProcedureColumn, ProcedureEntity.VersionNumberColumn, ProcedureEntity.VersionStatusColumn, VersionDraftedColumn,
        VersionNotConvertedListColumn, // fix round 2 (F2): the drafted-from file id is inside the list's envelope
    };

    /// <summary>The columns of a Procedure a drafting read takes (task 116): its Area and its Status.</summary>
    internal static readonly string[] DraftProcedureColumns = { ProcedureEntity.AreaColumn, ProcedureEntity.StatusColumn };

    /// <summary>
    /// The shared checks of <see cref="GetDraftSourceAsync"/>, <see cref="RemoveUntouchedDraftsAsync"/> and <see cref="AddDraftsAsync"/> (task 116),
    /// in this order: the version exists (404), its Procedure exists (404) and is Billing (400) and not Retired (409), the version is a Draft
    /// (409), the Procedure has a rule set (409) that is not Retired (409). Every read is app-only and re-checks the ids on the rows.
    /// </summary>
    private async Task<DraftTarget<T>> LoadDraftTargetAsync<T>(Guid procedureVersionId, CancellationToken ct, Guid? expectedProcedureId = null) where T : class
    {
        Entity? version = null;
        if (procedureVersionId != Guid.Empty)
        {
            var query = new QueryExpression(ProcedureEntity.VersionEntity) { ColumnSet = new ColumnSet(DraftVersionColumns), TopCount = 1 };
            query.Criteria.AddCondition("sprk_procedureversionid", ConditionOperator.Equal, procedureVersionId);
            version = (await _entities.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault(v => v.Id == procedureVersionId);
        }

        if (version?.GetAttributeValue<EntityReference>(ProcedureEntity.VersionProcedureColumn)?.Id is not { } procedureId || procedureId == Guid.Empty
            || (expectedProcedureId is { } expected && expected != procedureId))
        {
            return new DraftTarget<T>(Guid.Empty, null, false, RuleSetOutcome<T>.Refused(
                StatusCodes.Status404NotFound, ProcedureService.CodeVersionNotFound, "The Procedure Version was not found."));
        }

        var procedureQuery = new QueryExpression(ProcedureEntity.Entity) { ColumnSet = new ColumnSet(DraftProcedureColumns), TopCount = 1 };
        procedureQuery.Criteria.AddCondition("sprk_procedureid", ConditionOperator.Equal, procedureId);
        var procedure = (await _entities.RetrieveMultipleAsync(procedureQuery, ct)).Entities.FirstOrDefault(p => p.Id == procedureId);
        if (procedure is null)
        {
            return new DraftTarget<T>(procedureId, null, false, RuleSetOutcome<T>.Refused(
                StatusCodes.Status404NotFound, ProcedureService.CodeNotFound, "The Procedure was not found."));
        }

        // O-24: Ontology-area Procedures are document-only in R1. A missing Area is not Billing (fail closed).
        if (procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.AreaColumn)?.Value != RuleDomains.PolicyDomainBilling)
        {
            return new DraftTarget<T>(procedureId, null, false, RuleSetOutcome<T>.Refused(
                StatusCodes.Status400BadRequest, CodeNotBilling, "Rules are drafted only for a Procedure whose Area is Billing."));
        }

        if (procedure.GetAttributeValue<OptionSetValue>(ProcedureEntity.StatusColumn)?.Value == ProcedureEntity.StatusRetired)
        {
            return new DraftTarget<T>(procedureId, null, false, RuleSetOutcome<T>.Refused(
                StatusCodes.Status409Conflict, ProcedureService.CodeRetired, "A retired Procedure cannot be changed."));
        }

        if (version.GetAttributeValue<OptionSetValue>(ProcedureEntity.VersionStatusColumn)?.Value != ProcedureEntity.VersionStatusDraft)
        {
            return new DraftTarget<T>(procedureId, null, false, RuleSetOutcome<T>.Refused(
                StatusCodes.Status409Conflict, CodeVersionNotDraft, "Rules are drafted only for a Draft version."));
        }

        var set = await LoadProcedureRuleSetAsync(procedureId, ct);
        if (set is null)
        {
            return new DraftTarget<T>(procedureId, null, false, RuleSetOutcome<T>.Refused(
                StatusCodes.Status409Conflict, ProcedureService.CodeRuleSetMissing, "The Procedure has no rule set to draft rules into."));
        }

        if (set.GetAttributeValue<OptionSetValue>("statuscode")?.Value == RuleSetRetired)
        {
            return new DraftTarget<T>(procedureId, set, false, RuleSetOutcome<T>.Refused(
                StatusCodes.Status409Conflict, CodeRetired, "A retired rule set cannot be changed."));
        }

        // EB-D-211: only an explicit Yes is "drafted"; No or empty (never written) is not. Fix round 2 (F2): and a Yes counts only while the
        // file its run drafted from is still the version's one Servable source file (IsDraftedAsync); a stale Yes is redrafted.
        var drafted = await IsDraftedAsync(version, ct);
        return new DraftTarget<T>(procedureId, set, drafted, null);
    }

    /// <summary>
    /// The Servable Procedure-source Stored Files of a version (task 116), at most 2 (two already means "not one"). The same question as 081's
    /// publish check; the version, purpose and status are re-checked on each row, so a query that lost a filter cannot pass a wrong file.
    /// </summary>
    private async Task<IReadOnlyList<Entity>> LoadServableSourceFilesAsync(Guid procedureVersionId, CancellationToken ct)
    {
        var query = new QueryExpression(StoredFileSchema.Entity)
        {
            ColumnSet = new ColumnSet(StoredFileSchema.Name, StoredFileSchema.ProcedureVersionTable, StoredFileSchema.Purpose, StoredFileSchema.Status),
            TopCount = 2,
        };
        query.Criteria.AddCondition(StoredFileSchema.ProcedureVersionTable, ConditionOperator.Equal, procedureVersionId);
        query.Criteria.AddCondition(StoredFileSchema.Purpose, ConditionOperator.Equal, (int)StoredFilePurpose.ProcedureSource);
        query.Criteria.AddCondition(StoredFileSchema.Status, ConditionOperator.Equal, (int)StoredFileStatus.Servable);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        return (await _entities.RetrieveMultipleAsync(query, ct)).Entities
            .Where(f => f.GetAttributeValue<EntityReference>(StoredFileSchema.ProcedureVersionTable)?.Id == procedureVersionId
                && f.GetAttributeValue<OptionSetValue>(StoredFileSchema.Purpose)?.Value == (int)StoredFilePurpose.ProcedureSource
                && f.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status)?.Value == (int)StoredFileStatus.Servable)
            .ToList();
    }

    /// <summary>Fix round 1: whether <paramref name="fileId"/> is still the version's ONE Servable source file.</summary>
    private async Task<bool> IsTheSourceFileAsync(Guid procedureVersionId, Guid fileId, CancellationToken ct)
    {
        var files = await LoadServableSourceFilesAsync(procedureVersionId, ct);
        return files.Count == 1 && files[0].Id == fileId;
    }

    /// <summary>
    /// Fix round 1: the source file was replaced while a run drafted from the earlier one. A marker the run already wrote is taken back (No
    /// first, then the list, written as <see cref="ClearedNotConverted"/> since fix round 3), the run's untouched drafts are removed, and the run refuses 503 <c>ruleset.source_changed</c> (retried). Runs on
    /// <see cref="CancellationToken.None"/>; a fault in the clean-up is logged and the refusal stands (the retry compensates again).
    /// </summary>
    private async Task<RuleSetOutcome<DraftsAdded>> SourceChangedAsync(Guid ruleSetId, Guid procedureVersionId, bool markerWritten)
    {
        _logger.LogWarning("Procedure Version {ProcedureVersionId}: the source file changed while drafting; this run's drafts are withdrawn and the run retried", procedureVersionId);
        try
        {
            if (markerWritten)
            {
                await _entities.UpdateAsync(
                    ProcedureEntity.VersionEntity, procedureVersionId, new Dictionary<string, object> { [VersionDraftedColumn] = false }, CancellationToken.None);
                // Fix round 3: the cleared envelope, not an empty column, so an overlapping run whose marker lands after this write leaves a
                // Yes that reads as not drafted (IsDraftedAsync) rather than a Yes with no file id, which would read as drafted for any file.
                await _entities.UpdateAsync(
                    ProcedureEntity.VersionEntity, procedureVersionId, new Dictionary<string, object> { [VersionNotConvertedListColumn] = ClearedNotConverted }, CancellationToken.None);
            }

            var removed = await RemoveDraftsAsync(ruleSetId, procedureVersionId, supersededBefore: null, CancellationToken.None);
            if (removed.Kind != RuleSetOutcomeKind.Done)
            {
                _logger.LogWarning("Procedure Version {ProcedureVersionId}: this run's drafts could not all be removed ({Code}); the retry removes them", procedureVersionId, removed.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Procedure Version {ProcedureVersionId}: the clean-up after a changed source file failed; the retry compensates again", procedureVersionId);
        }

        return RuleSetOutcome<DraftsAdded>.Refused(
            StatusCodes.Status503ServiceUnavailable, CodeSourceChanged, "The source file was replaced while rules were drafted; drafting runs again.");
    }

    /// <summary>
    /// Retry compensation for the drafting job (ebilling-r1 task 075, review finding F1). A run that died midway left drafts but no
    /// completion marker; the model is asked again on the retry and its output is not deterministic, so the new set would sit beside the
    /// old one. Before drafting again the job removes the earlier run's UNTOUCHED drafts through this method. A rule is untouched only when
    /// ALL of these hold, and a row failing any one is never deleted (fail closed):
    /// <list type="bullet">
    /// <item>its policy was created by the identity this service writes as (<c>createdby</c> equals
    /// <see cref="IGenericEntityService.GetCurrentIdentityIdAsync"/>), and so was every version of it;</item>
    /// <item>it belongs to this rule set (the lookup, and the drafted code prefix <c>BRD-{rule set}-</c>);</item>
    /// <item>the policy is Off, every version is Proposed, and no version has been in force (no <c>sprk_inforceto</c>);</item>
    /// <item>the policy and each version have <c>modifiedon</c> equal to <c>createdon</c>: nobody has edited, accepted, rejected or
    /// published them (an administrator's edit of a draft is in place, so it moves <c>modifiedon</c>);</item>
    /// <item>the policy has no current version: it was never published (so 081's copy-forward drafts, whose policies have a current
    /// version, are never removed);</item>
    /// <item>task 116: EVERY version of the policy is a Draft (<c>sprk_publishstate</c>) under THIS Procedure Version
    /// (<c>sprk_procedureversion</c>): a draft of another version is never removed by this version's retry.</item>
    /// </list>
    /// A policy with no version at all (a run that died between the two creates and could not remove it) is untouched when the policy
    /// itself is: it is invisible, Off and in force nowhere, and a Procedure has at most one Draft version (081), so no other version's run
    /// can be creating it now. Versions are deleted before their policy, so a death between the two deletes leaves a version-less policy that
    /// the next pass removes. A row that is already gone counts as deleted (idempotent). Any other fault returns 500 <c>ruleset.write_failed</c>:
    /// the job fails without drafting, and its next delivery compensates again. A version with the completion marker is refused (409
    /// <c>ruleset.already_drafted</c>) and nothing is deleted: a finished run is never compensated. The version, Procedure and set checks are
    /// <see cref="GetDraftSourceAsync"/>'s.
    /// </summary>
    public virtual Task<RuleSetOutcome<DraftsCompensated>> RemoveUntouchedDraftsAsync(Guid procedureVersionId, CancellationToken ct) =>
        RemoveUntouchedDraftsAsync(procedureVersionId, Guid.Empty, ct);

    /// <summary>
    /// <see cref="RemoveUntouchedDraftsAsync(Guid, CancellationToken)"/>, for a run that drafts from the Stored File <paramref name="sourceFileId"/>.
    /// Fix round 4 (EB-D-216): the job removes only UNTOUCHED drafts (its own output, never modified; task 075's retry compensation). Drafts of
    /// this version made from an EARLIER source file that a reviewer worked on are superseded by a redraft, and a redraft deactivates them in
    /// the <c>/draft</c> request (the caller's rights, <see cref="PlanRedraftAsync"/>, <see cref="DeactivateSupersededDraftsAsync"/>),
    /// never here: the job has no user to check. If such an active draft is still there (no request deactivated it: a retry that drafts a file
    /// the administrator never asked to draft), the run refuses 409 <see cref="CodeSupersededDraftsRemain"/> and removes nothing. A deactivated
    /// row is never read here (both reads name <c>statecode</c>), so it is never deleted. When the source file's creation time cannot be read,
    /// only untouched drafts are considered.
    /// </summary>
    public virtual async Task<RuleSetOutcome<DraftsCompensated>> RemoveUntouchedDraftsAsync(Guid procedureVersionId, Guid sourceFileId, CancellationToken ct)
    {
        var target = await LoadDraftTargetAsync<DraftsCompensated>(procedureVersionId, ct);
        if (target.Refusal is { } refusal)
        {
            return refusal;
        }

        if (target.AlreadyDrafted)
        {
            return RuleSetOutcome<DraftsCompensated>.Refused(
                StatusCodes.Status409Conflict, CodeAlreadyDrafted, "Rules have already been drafted for this Procedure Version.");
        }

        var created = await SourceCreatedOnOrRefusalAsync<DraftsCompensated>(procedureVersionId, sourceFileId, ct);
        if (created.Refusal is { } unread)
        {
            return unread;
        }

        return await RemoveDraftsAsync(target.RuleSet!.Id, procedureVersionId, created.CreatedOn, ct);
    }

    /// <summary>The current source file's <c>createdon</c> (null for <see cref="Guid.Empty"/> or a file not found), or the 500 a read fault answers.</summary>
    private async Task<(DateTime? CreatedOn, RuleSetOutcome<T>? Refusal)> SourceCreatedOnOrRefusalAsync<T>(Guid procedureVersionId, Guid sourceFileId, CancellationToken ct)
        where T : class
    {
        if (sourceFileId == Guid.Empty)
        {
            return (null, null);
        }

        try
        {
            return (await SourceFileCreatedOnAsync(sourceFileId, ct), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Procedure Version {ProcedureVersionId}: the source file's creation time could not be read; nothing was cleared", procedureVersionId);
            return (null, RuleSetOutcome<T>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The earlier drafts could not be cleared. Try again; if it keeps failing, contact support."));
        }
    }

    /// <summary>
    /// Fix round 4 (EB-D-216): the drafts a redraft of <paramref name="procedureVersionId"/> from the Stored File <paramref name="sourceFileId"/>
    /// supersedes, decided before anything is written: the version's UNTOUCHED drafts (<see cref="IsUntouchedDraft"/>) and its drafts made from
    /// a source file created before the current one (<see cref="IsSupersededDraft"/>), each as a whole rule (the policy and every active version
    /// of it). Only active rows are read. A first draft plans nothing. The version, Procedure and set checks are <see cref="GetDraftSourceAsync"/>'s;
    /// a drafted version is 409 already-drafted; a read fault is 500.
    /// <para>Fix round 5 (F2, fix a): ONLY rows created before the current source file's <c>createdon</c>, untouched ones too. A rule a run of
    /// the CURRENT file stored (a run still storing, or one that died before its marker) is never planned: it stays with the job's own
    /// compensation, which runs under the job's per-version lock. So is a policy whose version is not created yet (a run between its two
    /// creates): a policy is never deactivated before its version exists. When the file's <c>createdon</c> cannot be found, nothing is planned.
    /// A plan of more than <see cref="MaxPerTransaction"/> rows is refused 422 <see cref="CodeRedraftTooLarge"/> (K2).</para>
    /// </summary>
    public virtual async Task<RuleSetOutcome<RedraftPlan>> PlanRedraftAsync(Guid procedureVersionId, Guid sourceFileId, CancellationToken ct)
    {
        var target = await LoadDraftTargetAsync<RedraftPlan>(procedureVersionId, ct);
        if (target.Refusal is { } refusal)
        {
            return refusal;
        }

        if (target.AlreadyDrafted)
        {
            return RuleSetOutcome<RedraftPlan>.Refused(
                StatusCodes.Status409Conflict, CodeAlreadyDrafted, "Rules have already been drafted for this Procedure Version.");
        }

        var created = await SourceCreatedOnOrRefusalAsync<RedraftPlan>(procedureVersionId, sourceFileId, ct);
        if (created.Refusal is { } unread)
        {
            return unread;
        }

        if (created.CreatedOn is not { } sourceCreatedOn)
        {
            return RuleSetOutcome<RedraftPlan>.Done(new RedraftPlan(Array.Empty<SupersededDraft>()));
        }

        try
        {
            var found = await FindDraftsAsync(target.RuleSet!.Id, procedureVersionId, sourceCreatedOn, ct);
            if (found.Truncated)
            {
                return RuleSetOutcome<RedraftPlan>.Refused(
                    StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "The rule set has more rules than can be read in one pass.");
            }

            // Fix round 5 (F2, fix a): an untouched draft of the CURRENT file (created at or after it) belongs to a run of that file; the
            // job's compensation, under its lock, owns it. Superseded drafts are created before the file by definition (IsSupersededDraft).
            var plan = new RedraftPlan(found.Untouched
                .Where(d => CreatedBefore(d.Policy, sourceCreatedOn) && d.Versions.Count > 0 && d.Versions.All(v => CreatedBefore(v, sourceCreatedOn)))
                .Concat(found.Superseded)
                .Select(d => new SupersededDraft(
                    d.Policy.Id,
                    d.Versions.Select(v => v.Id).ToList(),
                    d.Policy.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id ?? Guid.Empty,
                    d.Versions.Select(v => v.GetAttributeValue<EntityReference>(ProcedureVersionColumn)?.Id ?? Guid.Empty).ToList()))
                .ToList());

            var rows = plan.Rows.Count();
            if (rows > MaxPerTransaction)
            {
                _logger.LogWarning(
                    "Procedure Version {ProcedureVersionId}: a redraft would deactivate {Rows} rows, more than one transaction holds ({Cap}); nothing was changed",
                    procedureVersionId, rows, MaxPerTransaction);
                return RuleSetOutcome<RedraftPlan>.Refused(
                    StatusCodes.Status422UnprocessableEntity, CodeRedraftTooLarge,
                    $"Drafting again would replace {plan.Drafts.Count} earlier drafts of this version ({rows} records), more than one change can "
                    + $"hold ({MaxPerTransaction}). Nothing was changed; contact support.");
            }

            return RuleSetOutcome<RedraftPlan>.Done(plan);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Procedure Version {ProcedureVersionId}: the drafts a redraft supersedes could not be read; nothing was changed", procedureVersionId);
            return RuleSetOutcome<RedraftPlan>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The earlier drafts could not be read. Try again; if it keeps failing, contact support.");
        }
    }

    /// <summary>
    /// Fix round 4 (EB-D-216): deactivates (<c>statecode</c> Inactive, <c>statuscode</c> its default) every row of <paramref name="plan"/>, as the
    /// application, after the route has checked the caller's rights (fix round 5, EB-D-216a: the rights a reject of these drafts needs, as the
    /// caller). All or nothing, in this order:
    /// <list type="number">
    /// <item>every planned row is read again (no <c>statecode</c> filter, so a row already deactivated is seen, and skipped);</item>
    /// <item>every rule must still be what the plan saw: the policy has no current version, every ACTIVE version is a planned one and every
    /// planned one is still there (fix round 5, K2: an inactive version the plan never read does not count), and each active one is still a
    /// Draft (<c>sprk_publishstate</c>) under THIS Procedure Version and never in force. Any other answer (a version published, put in force,
    /// moved, added or removed in between) refuses 409 <see cref="CodeDraftChanged"/> and NOTHING is written: no row is written until every
    /// row is re-checked;</item>
    /// <item>one transaction deactivates every version, then its policy, each conditioned on the row version just read, so a publish that
    /// lands between the re-read and the write rolls the whole transaction back (also 409, nothing written). It is sent with
    /// <see cref="CancellationToken.None"/> (fix round 5): a client that disconnects never stops it halfway.</item>
    /// </list>
    /// A published version, a copy-forward draft and a rule outside the plan are therefore never deactivated. A deactivated row is never
    /// deleted; a finding, Signal or Decision Record that cites it still resolves it by id. A plan of more than <see cref="MaxPerTransaction"/>
    /// rows is 422 <see cref="CodeRedraftTooLarge"/>, nothing sent. A transaction Dataverse refused answers 500 <c>ruleset.write_failed</c>
    /// with nothing applied (it rolled back); one that gave NO answer (fix round 5, F1: <see cref="DataverseTransactionException.OutcomeUnknown"/>,
    /// or a timeout) may have committed and answers 503 <see cref="CodeRedraftOutcomeUnknown"/>.
    /// </summary>
    public virtual async Task<RuleSetOutcome<DraftsCompensated>> DeactivateSupersededDraftsAsync(Guid procedureVersionId, RedraftPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Drafts.Count == 0)
        {
            return RuleSetOutcome<DraftsCompensated>.Done(new DraftsCompensated(0, 0));
        }

        if (plan.Rows.Count() > MaxPerTransaction)
        {
            return RuleSetOutcome<DraftsCompensated>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeRedraftTooLarge,
                $"Drafting again would replace more earlier drafts than one change can hold ({MaxPerTransaction}). Nothing was changed; contact support.");
        }

        try
        {
            var policyIds = plan.Drafts.Select(d => (object)d.PolicyId).Distinct().ToArray();
            var policies = new Dictionary<Guid, Entity>();
            var versionsByPolicy = new Dictionary<Guid, List<Entity>>();
            foreach (var batch in policyIds.Chunk(MaxInValues))
            {
                // By id: a deactivated policy must be seen here (skipped), so no statecode condition.
                var policyQuery = new QueryExpression(PolicyEntity) { ColumnSet = new ColumnSet(PolicyColumns) };
                policyQuery.ColumnSet.AddColumns("statecode");
                policyQuery.Criteria.AddCondition("sprk_policyid", ConditionOperator.In, batch);
                foreach (var p in (await _entities.RetrieveMultipleAsync(policyQuery, ct)).Entities)
                {
                    policies[p.Id] = p;
                }

                // all versions: every version of a planned rule, active or not, so a version added since the plan is seen (409).
                var versionQuery = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
                versionQuery.ColumnSet.AddColumns("statecode");
                versionQuery.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
                var versions = await _entities.RetrieveMultipleAsync(versionQuery, ct);
                if (versions.MoreRecords)
                {
                    return RuleSetOutcome<DraftsCompensated>.Refused(
                        StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "The rule set has more rules than can be read in one pass.");
                }

                foreach (var v in versions.Entities)
                {
                    if (v.GetAttributeValue<EntityReference>("sprk_policy")?.Id is { } pid)
                    {
                        if (!versionsByPolicy.TryGetValue(pid, out var list))
                        {
                            versionsByPolicy[pid] = list = new List<Entity>();
                        }

                        list.Add(v);
                    }
                }
            }

            var requests = new List<OrganizationRequest>();
            var deactivated = 0;
            foreach (var draft in plan.Drafts)
            {
                if (!policies.TryGetValue(draft.PolicyId, out var policy))
                {
                    return DraftChanged(procedureVersionId, draft.PolicyId, "the rule is gone");
                }

                var versions = versionsByPolicy.GetValueOrDefault(draft.PolicyId) ?? new List<Entity>();
                // Fix round 5 (K2): the plan read active versions only, so only active versions are compared: every active version must be a
                // planned one (a version added since is a 409), and every planned one must still be there (an inactive one is skipped below).
                if (policy.GetAttributeValue<EntityReference>("sprk_currentversion") is not null
                    || !versions.Where(IsActive).Select(v => v.Id).ToHashSet().IsSubsetOf(draft.VersionIds)
                    || !versions.Select(v => v.Id).ToHashSet().IsSupersetOf(draft.VersionIds))
                {
                    return DraftChanged(procedureVersionId, draft.PolicyId, "the rule was published or its versions changed");
                }

                foreach (var version in versions.Where(IsActive))
                {
                    if (version.GetAttributeValue<OptionSetValue>(PublishStateColumn)?.Value != PublishStateDraft
                        || version.GetAttributeValue<DateTime?>("sprk_inforceto") is not null
                        || version.GetAttributeValue<EntityReference>(ProcedureVersionColumn)?.Id != procedureVersionId)
                    {
                        return DraftChanged(procedureVersionId, draft.PolicyId, "a version is no longer a Draft of this Procedure Version");
                    }

                    requests.Add(DeactivateRequest(VersionEntity, version));
                }

                if (IsActive(policy))
                {
                    requests.Add(DeactivateRequest(PolicyEntity, policy));
                    deactivated++;
                }
            }

            if (requests.Count > 0)
            {
                // Fix round 5: once sent, the transaction is not cancelled by the caller (a disconnect must not leave its outcome to chance).
                try
                {
                    await _entities.ExecuteTransactionAsync(requests, CancellationToken.None);
                }
                catch (OperationCanceledException ex)
                {
                    // Sent with CancellationToken.None, so this is the client's own timeout: the transaction may have committed.
                    throw new DataverseTransactionException("The deactivation transaction timed out; it may have committed.", null, true, ex);
                }
            }

            _logger.LogInformation(
                "Procedure Version {ProcedureVersionId}: a redraft deactivated {Deactivated} superseded draft(s) ({Rows} row(s))",
                procedureVersionId, deactivated, requests.Count);
            return RuleSetOutcome<DraftsCompensated>.Done(new DraftsCompensated(deactivated, 0));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.Data.DBConcurrencyException || DataverseServiceClientImpl.IsRowVersionMismatch(ex))
        {
            return DraftChanged(procedureVersionId, Guid.Empty, "a row changed between the re-read and the write");
        }
        catch (DataverseTransactionException ex) when (ex.OutcomeUnknown)
        {
            // Fix round 5 (F1): no answer came back, so the drafts may already be inactive. A retry converges: a deactivated draft is not
            // planned again, and one still active is planned and deactivated then.
            _logger.LogError(ex, "Procedure Version {ProcedureVersionId}: the deactivation transaction returned no answer; it may have committed", procedureVersionId);
            return RuleSetOutcome<DraftsCompensated>.Refused(
                StatusCodes.Status503ServiceUnavailable, CodeRedraftOutcomeUnknown,
                "The earlier drafts may already have been replaced, but no answer came back. Try again; drafting again finishes the change.");
        }
        catch (Exception ex)
        {
            // The transaction rolls back: nothing was deactivated.
            _logger.LogError(ex, "Procedure Version {ProcedureVersionId}: the superseded drafts could not be deactivated; nothing was changed", procedureVersionId);
            return RuleSetOutcome<DraftsCompensated>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The earlier drafts could not be deactivated. Try again; if it keeps failing, contact support.");
        }
    }

    private RuleSetOutcome<DraftsCompensated> DraftChanged(Guid procedureVersionId, Guid policyId, string why)
    {
        _logger.LogWarning("Procedure Version {ProcedureVersionId}: a superseded draft changed before the redraft ({Why}, policy {PolicyId}); nothing was deactivated",
            procedureVersionId, why, policyId);
        return RuleSetOutcome<DraftsCompensated>.Refused(
            StatusCodes.Status409Conflict, CodeDraftChanged,
            "An earlier draft of this version changed (it may have been published) while the redraft started. Nothing was changed; reload and try again.");
    }

    private static bool IsActive(Entity row) => (row.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0) == 0;

    private static UpdateRequest DeactivateRequest(string table, Entity read) => new()
    {
        Target = new Entity(table, read.Id)
        {
            RowVersion = read.GetAttributeValue<long?>(RowVersionColumn)?.ToString(CultureInfo.InvariantCulture),
            ["statecode"] = new OptionSetValue(StateInactive),
            ["statuscode"] = new OptionSetValue(StatusInactive),
        },
        ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
    };

    /// <summary>When the Stored File <paramref name="fileId"/> was created (Dataverse's own <c>createdon</c>), or null when it cannot be found.</summary>
    private async Task<DateTime?> SourceFileCreatedOnAsync(Guid fileId, CancellationToken ct)
    {
        var query = new QueryExpression(StoredFileSchema.Entity) { ColumnSet = new ColumnSet("createdon"), TopCount = 1 };
        query.Criteria.AddCondition(StoredFileSchema.IdColumn, ConditionOperator.Equal, fileId);
        return (await _entities.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault(f => f.Id == fileId)?.GetAttributeValue<DateTime?>("createdon");
    }

    /// <summary>One active rule (policy and its active versions) the compensation or a redraft plan considered.</summary>
    private sealed record FoundDraft(Entity Policy, IReadOnlyList<Entity> Versions);

    /// <summary>The active rules of the set: the version's untouched drafts, its superseded (earlier-file, reviewed) drafts, and the total count.</summary>
    private sealed record FoundDrafts(IReadOnlyList<FoundDraft> Untouched, IReadOnlyList<FoundDraft> Superseded, int Total, bool Truncated);

    /// <summary>
    /// Fix round 4: the one read and decision behind both the job's compensation and a redraft's plan. ACTIVE rows only (the policy read is
    /// <see cref="PolicyQuery"/>, the version read names <c>statecode</c>), so a deactivated draft is never planned, removed or counted. Untouched
    /// drafts are <see cref="IsUntouchedDraft"/>; when <paramref name="supersededBefore"/> is given, the other drafts made from a source file
    /// created before that time are <see cref="IsSupersededDraft"/>. A fault throws.
    /// </summary>
    private async Task<FoundDrafts> FindDraftsAsync(Guid ruleSetId, Guid procedureVersionId, DateTime? supersededBefore, CancellationToken ct)
    {
        var identity = await _entities.GetCurrentIdentityIdAsync(ct);
        if (identity == Guid.Empty)
        {
            throw new InvalidOperationException("The identity the drafted rules were written as is unknown.");
        }

        var none = new FoundDrafts(Array.Empty<FoundDraft>(), Array.Empty<FoundDraft>(), 0, true);
        var policyQuery = PolicyQuery("sprk_ruleset", ConditionOperator.Equal, new object[] { ruleSetId });
        policyQuery.ColumnSet.AddColumns(AuditColumns);
        var policies = await _entities.RetrieveMultipleAsync(policyQuery, ct);
        if (policies.MoreRecords)
        {
            return none;
        }

        var rows = policies.Entities
            .Where(p => RuleDomains.IsBillingRow(p) && p.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id == ruleSetId)
            .ToList();

        var versionsByPolicy = new Dictionary<Guid, List<Entity>>();
        foreach (var batch in rows.Select(p => (object)p.Id).Chunk(MaxInValues))
        {
            var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
            query.ColumnSet.AddColumns(AuditColumns);
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // fix round 4: a deactivated draft is not a draft
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            if (versions.MoreRecords)
            {
                return none;
            }

            foreach (var v in versions.Entities)
            {
                if (v.GetAttributeValue<EntityReference>("sprk_policy")?.Id is { } policyId)
                {
                    if (!versionsByPolicy.TryGetValue(policyId, out var list))
                    {
                        versionsByPolicy[policyId] = list = new List<Entity>();
                    }

                    list.Add(v);
                }
            }
        }

        var codePrefix = $"{DraftCodePrefix}{ruleSetId:N}-";
        var untouched = new List<FoundDraft>();
        var superseded = new List<FoundDraft>();
        foreach (var p in rows)
        {
            var versions = versionsByPolicy.GetValueOrDefault(p.Id) ?? new List<Entity>();
            if (IsUntouchedDraft(p, versions, identity, codePrefix, procedureVersionId))
            {
                untouched.Add(new FoundDraft(p, versions));
            }
            else if (supersededBefore is { } before && IsSupersededDraft(p, versions, identity, codePrefix, procedureVersionId, before))
            {
                superseded.Add(new FoundDraft(p, versions));
            }
        }

        return new FoundDrafts(untouched, superseded, rows.Count, false);
    }

    /// <summary>
    /// The job's compensation (task 075 F1; task 116; fix round 4): removes this version's untouched drafts (the job's own output, never
    /// modified). When <paramref name="supersededBefore"/> is given and a superseded draft a reviewer worked on is still active, nothing is
    /// removed and the run refuses 409 <see cref="CodeSupersededDraftsRemain"/>: only a <c>/draft</c> request deactivates those, after the
    /// caller's rights checks (EB-D-216, EB-D-216a). Faults answer 500.
    /// </summary>
    private async Task<RuleSetOutcome<DraftsCompensated>> RemoveDraftsAsync(
        Guid ruleSetId, Guid procedureVersionId, DateTime? supersededBefore, CancellationToken ct)
    {
        try
        {
            var found = await FindDraftsAsync(ruleSetId, procedureVersionId, supersededBefore, ct);
            if (found.Truncated)
            {
                return RuleSetOutcome<DraftsCompensated>.Refused(
                    StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "The rule set has more rules than can be read in one pass.");
            }

            if (found.Superseded.Count > 0)
            {
                _logger.LogWarning(
                    "Procedure Version {ProcedureVersionId}: {Count} reviewed draft(s) of an earlier source file are still active and no request deactivated them; nothing drafted",
                    procedureVersionId, found.Superseded.Count);
                return RuleSetOutcome<DraftsCompensated>.Refused(
                    StatusCodes.Status409Conflict, CodeSupersededDraftsRemain,
                    "Drafts of an earlier source file are still active. Start drafting again from the Procedure Version.");
            }

            var removed = 0;
            foreach (var draft in found.Untouched)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var version in draft.Versions)
                {
                    await DeleteIfPresentAsync(VersionEntity, version.Id, ct);
                }

                await DeleteIfPresentAsync(PolicyEntity, draft.Policy.Id, ct);
                removed++;
            }

            _logger.LogInformation(
                "Rule set {RuleSetId}, Procedure Version {ProcedureVersionId}: retry compensation removed {Removed} untouched draft(s) of an earlier run and kept {Kept} other rule(s)",
                ruleSetId, procedureVersionId, removed, found.Total - removed);
            return RuleSetOutcome<DraftsCompensated>.Done(new DraftsCompensated(removed, found.Total - removed));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Rows already deleted stay deleted and nothing is drafted; the next delivery compensates again.
            _logger.LogError(ex, "Rule set {RuleSetId}: the earlier run's drafts could not be cleared; nothing was drafted", ruleSetId);
            return RuleSetOutcome<DraftsCompensated>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The earlier drafts could not be cleared. Try again; if it keeps failing, contact support.");
        }
    }

    // Who created a row and when it was last changed: what "untouched" is decided on. Read only by the compensation.
    private static readonly string[] AuditColumns = { "createdby", "createdon", "modifiedon" };

    private static bool IsUntouchedDraft(Entity policy, IReadOnlyList<Entity> versions, Guid identity, string codePrefix, Guid procedureVersionId) =>
        policy.GetAttributeValue<string>("sprk_policycode") is { } code
        && code.StartsWith(codePrefix, StringComparison.Ordinal)
        && policy.GetAttributeValue<bool?>("sprk_enabled") == false
        && policy.GetAttributeValue<EntityReference>("sprk_currentversion") is null
        && CreatedByAndNeverModified(policy, identity)
        && versions.All(v =>
            CreatedByAndNeverModified(v, identity)
            && v.GetAttributeValue<OptionSetValue>("sprk_proposalstatus")?.Value == (int)ProposalState.Proposed
            && v.GetAttributeValue<DateTime?>("sprk_inforceto") is null
            // Task 116: only this Procedure Version's drafts (a version under another one, or none, keeps the rule).
            && v.GetAttributeValue<EntityReference>(ProcedureVersionColumn)?.Id == procedureVersionId
            && v.GetAttributeValue<OptionSetValue>(PublishStateColumn)?.Value == PublishStateDraft);

    /// <summary>
    /// Fix round 1 (owner ruling 2026-11-10): a draft of THIS version made from a source file that has since been replaced, whatever an
    /// administrator did to it while it stayed a Draft. Never a published policy (no current version), never a copy-forward draft (those have a
    /// current version), never a version in force, never a row another identity created, never a draft of another version. A version with no
    /// policy versions is decided by the policy alone.
    /// </summary>
    private static bool IsSupersededDraft(
        Entity policy, IReadOnlyList<Entity> versions, Guid identity, string codePrefix, Guid procedureVersionId, DateTime supersededBefore) =>
        policy.GetAttributeValue<string>("sprk_policycode") is { } code
        && code.StartsWith(codePrefix, StringComparison.Ordinal)
        && policy.GetAttributeValue<EntityReference>("sprk_currentversion") is null
        && CreatedByBefore(policy, identity, supersededBefore)
        && versions.All(v =>
            CreatedByBefore(v, identity, supersededBefore)
            && v.GetAttributeValue<DateTime?>("sprk_inforceto") is null
            && v.GetAttributeValue<EntityReference>(ProcedureVersionColumn)?.Id == procedureVersionId
            && v.GetAttributeValue<OptionSetValue>(PublishStateColumn)?.Value == PublishStateDraft);

    private static bool CreatedBefore(Entity row, DateTime before) =>
        row.GetAttributeValue<DateTime?>("createdon") is { } created && created < before;

    private static bool CreatedByBefore(Entity row, Guid identity, DateTime before) =>
        row.GetAttributeValue<EntityReference>("createdby")?.Id == identity
        && row.GetAttributeValue<DateTime?>("createdon") is { } created
        && created < before;

    private static bool CreatedByAndNeverModified(Entity row, Guid identity) =>
        row.GetAttributeValue<EntityReference>("createdby")?.Id == identity
        && row.GetAttributeValue<DateTime?>("createdon") is { } created
        && row.GetAttributeValue<DateTime?>("modifiedon") is { } modified
        && created == modified;

    /// <summary>Deletes a row; a row that is already gone counts as deleted (a repeated compensation, or an administrator removed it).</summary>
    private async Task DeleteIfPresentAsync(string entity, Guid id, CancellationToken ct)
    {
        try
        {
            await _entities.DeleteAsync(entity, id, ct);
        }
        catch (Exception ex) when (IsRowGone(ex))
        {
            // Idempotent: nothing left to delete.
        }
    }

    private static bool IsRowGone(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Stores the drafted rules of a Procedure Version (task 116) in its Procedure's rule set as Proposed rules (a Billing-domain policy,
    /// Off, with no current version, and its version 1 under <paramref name="procedureVersionId"/> with <c>sprk_publishstate</c> Draft), then
    /// writes the VERSION's not-converted list (<c>sprk_notconvertedlist</c>) and, LAST, its drafted marker <c>sprk_notconverted</c> = Yes, which
    /// marks the run complete (EB-D-211). Model output is a proposal: every rule passes the one
    /// validation of this service (<see cref="PolicyVersionValidator.ValidateForSave"/> with <see cref="RuleDomain.Billing"/>, plus the
    /// required columns) before anything is written, and a rule that fails it is not dropped: it is added to the not-converted list with
    /// the reason. Re-running is safe: a rule whose type, body and clause equal one already under this version is skipped, and a run after
    /// the marker is written is refused (409 <c>ruleset.already_drafted</c>). Codes stay <c>BRD-{rule set}-{n}</c>, numbered per set.
    /// </summary>
    public virtual Task<RuleSetOutcome<DraftsAdded>> AddDraftsAsync(
        Guid procedureVersionId, IReadOnlyList<DraftedRule> rules, IReadOnlyList<NotConvertedClause> notConverted, Guid? callerObjectId, CancellationToken ct) =>
        AddDraftsAsync(procedureVersionId, Guid.Empty, rules, notConverted, callerObjectId, ct);

    /// <summary>
    /// <see cref="AddDraftsAsync(Guid, IReadOnlyList{DraftedRule}, IReadOnlyList{NotConvertedClause}, Guid?, CancellationToken)"/>, for a run that
    /// drafted from the Stored File <paramref name="draftedFromFileId"/> (fix round 1, owner ruling 2026-11-10). Before the list and the marker
    /// are written, the version's one Servable source file is read again; after the marker, once more. If it is not that file any more (it was
    /// replaced while the run worked), the run removes its own untouched drafts, writes no list or marker (or takes back the ones it just
    /// wrote: the marker No first, then the list), and refuses 503 <c>ruleset.source_changed</c>, which the job retries, so the retry drafts
    /// the new file. <see cref="Guid.Empty"/> skips the check.
    /// </summary>
    public virtual async Task<RuleSetOutcome<DraftsAdded>> AddDraftsAsync(
        Guid procedureVersionId, Guid draftedFromFileId, IReadOnlyList<DraftedRule> rules, IReadOnlyList<NotConvertedClause> notConverted,
        Guid? callerObjectId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(notConverted);

        var target = await LoadDraftTargetAsync<DraftsAdded>(procedureVersionId, ct);
        if (target.Refusal is { } refusal)
        {
            return refusal;
        }

        if (target.AlreadyDrafted)
        {
            return RuleSetOutcome<DraftsAdded>.Refused(
                StatusCodes.Status409Conflict, CodeAlreadyDrafted, "Rules have already been drafted for this Procedure Version.");
        }

        var ruleSetId = target.RuleSet!.Id;

        // 1. Validate everything before the first write. A refusal is listed, never dropped.
        var listed = new List<NotConvertedClause>(notConverted);
        var valid = new List<(DraftedRule Rule, int RuleType, int Consequence, string? Note)>();
        foreach (var (rule, index) in rules.Select((r, i) => (r, i)))
        {
            if (index >= MaxDraftedRules)
            {
                listed.Add(new NotConvertedClause(
                    rule.ClauseQuote, rule.ClauseLocation, $"Not stored: a drafting run stores at most {MaxDraftedRules} rules."));
                continue;
            }

            var problem = CheckDraft(rule, out var ruleType, out var consequence, out var note);
            if (problem is not null)
            {
                listed.Add(new NotConvertedClause(rule.ClauseQuote, rule.ClauseLocation, problem));
                continue;
            }

            valid.Add((rule, ruleType, consequence, note));
        }

        try
        {
            var stored = 0;
            var present = 0;
            if (valid.Count > 0)
            {
                // A drafted rule's policy is a hard-deny row; its first version is filed under that policy alone, so it takes the
                // policy's answer (the version's question over the policy names the same team: EB-D-168a).
                var owner = await _ownership.ResolveOwnerAsync(ContentOwnerContext(PolicyEntity, callerObjectId), ct);
                if (!owner.IsOwned)
                {
                    return RuleSetOutcome<DraftsAdded>.OwnerRefused(owner);
                }

                var existing = await LoadDraftedFingerprintsAsync(ruleSetId, procedureVersionId, ct);
                if (existing is null)
                {
                    return RuleSetOutcome<DraftsAdded>.Refused(
                        StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "The rule set has more rules than can be read in one pass.");
                }

                var next = existing.NextIndex;
                foreach (var (rule, ruleType, consequence, note) in valid)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!existing.Fingerprints.Add(Fingerprint(ruleType, rule.RuleBody, rule.ClauseQuote)))
                    {
                        present++;
                        continue;
                    }

                    // A rule that survived the retry compensation was worked on by an administrator (edited, accepted, rejected or
                    // published). The model's second wording of the same clause is not stored beside it; it is listed instead.
                    if (existing.CoveredClauses.Contains(BillingRuleDraftTranslator.NormaliseForQuoteCheck(rule.ClauseQuote)))
                    {
                        listed.Add(new NotConvertedClause(
                            rule.ClauseQuote, rule.ClauseLocation,
                            "Not stored: a rule already under this Procedure Version, from an earlier drafting run, covers this clause."));
                        continue;
                    }

                    var code = $"{DraftCodePrefix}{ruleSetId:N}-{next++.ToString("D3", CultureInfo.InvariantCulture)}";
                    await CreateDraftRuleAsync(ruleSetId, procedureVersionId, code, rule, ruleType, consequence, note, owner, ct);
                    stored++;
                }
            }

            // 2. The not-converted list, then the marker LAST, on the VERSION (task 116, EB-D-211): two writes, so a run that dies between them
            //    has its list but no marker and is compensated and drafted again (the list is overwritten). After the marker the job is a no-op
            //    and the trigger answers 409; another version of the same Procedure has its own marker, so it can still be drafted.
            //    Fix round 1: only while the file this run drafted from is still the version's one Servable source file.
            if (draftedFromFileId != Guid.Empty && !await IsTheSourceFileAsync(procedureVersionId, draftedFromFileId, ct))
            {
                return await SourceChangedAsync(ruleSetId, procedureVersionId, markerWritten: false);
            }

            // Fix round 2 (F2): the list carries the file this run drafted from (the envelope), so a marker that outlives a replace of that file
            // reads as not drafted (IsDraftedAsync) even when the take-back below never lands.
            var json = StoredNotConvertedOf(draftedFromFileId, BuildNotConvertedJson(listed));
            await _entities.UpdateAsync(
                ProcedureEntity.VersionEntity, procedureVersionId, new Dictionary<string, object> { [VersionNotConvertedListColumn] = json }, ct);

            // Fix round 2: the marker and everything after it run on CancellationToken.None, so a cancelled run never stops between a written
            // marker and the check that may have to take it back.
            await _entities.UpdateAsync(
                ProcedureEntity.VersionEntity, procedureVersionId, new Dictionary<string, object> { [VersionDraftedColumn] = true }, CancellationToken.None);

            // A replace that began after the check above made its new file Servable after it reset the marker; this re-read catches it. A fault
            // in the re-read counts as "changed" (fix round 2): the marker is taken back and the run is retried.
            if (draftedFromFileId != Guid.Empty)
            {
                bool unchanged;
                try
                {
                    unchanged = await IsTheSourceFileAsync(procedureVersionId, draftedFromFileId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Procedure Version {ProcedureVersionId}: the source file could not be re-read after the marker; handled as changed", procedureVersionId);
                    unchanged = false;
                }

                if (!unchanged)
                {
                    return await SourceChangedAsync(ruleSetId, procedureVersionId, markerWritten: true);
                }
            }

            _logger.LogInformation(
                "Procedure Version {ProcedureVersionId} (rule set {RuleSetId}): drafting stored {Stored} rule(s), skipped {Present} already present, listed {NotConverted} as not converted",
                procedureVersionId, ruleSetId, stored, present, listed.Count);
            return RuleSetOutcome<DraftsAdded>.Done(new DraftsAdded(stored, present, listed));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Rows already written stay (Proposed, Off, in force nowhere); the marker is not written, so a re-run adds the rest.
            _logger.LogError(ex, "Procedure Version {ProcedureVersionId}: drafting could not store its rules; the run can be repeated", procedureVersionId);
            return RuleSetOutcome<DraftsAdded>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The drafted rules could not be saved. Try again; if it keeps failing, contact support.");
        }
    }

    /// <summary>
    /// The one validation of a drafted rule. Returns a plain-sentence reason, or null when it is valid. When the ONLY problem is that the
    /// rule's type may not carry the drafted consequence (Reject on a TimeIncrement rule, say), the rule is kept as a Flag instead of being
    /// lost (073 review F1-b: there is no route to create a rule by hand): <paramref name="consequenceValue"/> is Flag and
    /// <paramref name="loweredNote"/> says what the model drafted and why it was lowered. Any other problem still lists the clause as not
    /// converted.
    /// </summary>
    private string? CheckDraft(DraftedRule rule, out int ruleTypeValue, out int consequenceValue, out string? loweredNote)
    {
        ruleTypeValue = 0;
        consequenceValue = 0;
        loweredNote = null;
        if (!TryParseConsequence(rule.Consequence, out var consequence))
        {
            return "Not stored: the consequence was not Reject, Flag or SuggestAdjustment.";
        }

        consequenceValue = (int)consequence;
        if (string.IsNullOrWhiteSpace(rule.ClauseQuote) || rule.ClauseQuote.Length > MaxClauseQuoteChars)
        {
            return $"Not stored: the clause quote is missing or longer than {MaxClauseQuoteChars} characters.";
        }

        List<RuleSetError> Validate(int consequenceToCheck) => ValidateRule(
            new PolicyRow(Guid.Empty, "draft", rule.ShortHeadline, null),
            rule.RuleType,
            new VersionRow(Guid.Empty, 1, null, rule.RuleBody, rule.MessageTemplate, rule.ShortHeadline, consequenceToCheck, null, null, null, null, null));

        var errors = Validate(consequenceValue);
        if (errors.Count > 0
            && consequenceValue != (int)BillingConsequence.Flag
            && errors.All(e => e.Reason == OntologyWriterFailureReason.ConsequenceNotAllowed))
        {
            var asFlag = Validate((int)BillingConsequence.Flag);
            if (asFlag.Count == 0)
            {
                loweredNote = LoweredConsequenceNote(consequence, errors[0].Message);
                consequenceValue = (int)BillingConsequence.Flag;
                errors = asFlag;
            }
        }

        if (errors.Count > 0)
        {
            return "Not stored: the drafted rule was not valid (" + string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")) + ").";
        }

        // Valid: the type parses and is a billing type (ValidateForSave with the Billing domain), so this cannot fail.
        RuleBodySchemaValidator.TryParseRuleType(rule.RuleType, out var ruleType);
        ruleTypeValue = (int)ruleType;
        return null;
    }

    /// <summary>
    /// The <c>sprk_proposedaction</c> of a drafted rule whose consequence was lowered to Flag (073 review F1-b): the usual code first, so a
    /// reader that matches the code still can, then what the model drafted and the refusal sentence. Capped at the column's 400 characters.
    /// </summary>
    internal static string LoweredConsequenceNote(BillingConsequence drafted, string refusal)
    {
        var note = $"{ProposedActionText((int)BillingConsequence.Flag)}: drafted as {drafted}, lowered to Flag. {refusal}";
        return note.Length <= MaxProposedActionChars ? note : note[..(MaxProposedActionChars - 3)] + "...";
    }

    /// <summary>The length of <c>sprk_policyversion.sprk_proposedaction</c> (text 400, task 019).</summary>
    private const int MaxProposedActionChars = 400;

    private sealed record DraftedFingerprints(HashSet<string> Fingerprints, int NextIndex, HashSet<string> CoveredClauses);

    private static string Fingerprint(int ruleType, string ruleBody, string clauseQuote) =>
        string.Create(CultureInfo.InvariantCulture, $"{ruleType}\n{ruleBody}\n{clauseQuote}");

    /// <summary>
    /// The rules already under <paramref name="procedureVersionId"/> (for skipping repeats: task 116 scopes them to the version, so a clause an
    /// earlier version drafted, or rejected, can be proposed again for this one; the copy-forward drafts of this version count) and the next
    /// free index of the drafted-code sequence, which stays per SET. Null when truncated.
    /// </summary>
    private async Task<DraftedFingerprints?> LoadDraftedFingerprintsAsync(Guid ruleSetId, Guid procedureVersionId, CancellationToken ct)
    {
        var policies = await _entities.RetrieveMultipleAsync(PolicyQuery("sprk_ruleset", ConditionOperator.Equal, new object[] { ruleSetId }), ct);
        if (policies.MoreRecords)
        {
            return null;
        }

        var rows = policies.Entities
            .Where(p => RuleDomains.IsBillingRow(p) && p.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id == ruleSetId)
            .ToList();

        var prefix = $"{DraftCodePrefix}{ruleSetId:N}-";
        var next = 1;
        foreach (var code in rows.Select(p => p.GetAttributeValue<string>("sprk_policycode")))
        {
            if (code is not null && code.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                next = Math.Max(next, n + 1);
            }
        }

        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var batch in rows.Select(p => (object)p.Id).Chunk(MaxInValues))
        {
            var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // fix round 4: a deactivated draft is not a repeat
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            if (versions.MoreRecords)
            {
                return null;
            }

            foreach (var v in versions.Entities.Where(v => v.GetAttributeValue<EntityReference>(ProcedureVersionColumn)?.Id == procedureVersionId))
            {
                var row = ToVersion(v);
                if (row.RuleType is { } type && row.RuleBody is not null && row.ClauseReference is not null)
                {
                    fingerprints.Add(Fingerprint(type, row.RuleBody, row.ClauseReference));
                }

                if (!string.IsNullOrWhiteSpace(v.GetAttributeValue<string>("sprk_clausereference")))
                {
                    covered.Add(BillingRuleDraftTranslator.NormaliseForQuoteCheck(v.GetAttributeValue<string>("sprk_clausereference")!));
                }
            }
        }

        return new DraftedFingerprints(fingerprints, next, covered);
    }

    private async Task CreateDraftRuleAsync(
        Guid ruleSetId, Guid procedureVersionId, string code, DraftedRule rule, int ruleType, int consequence, string? note,
        RecordOwnerResolution owner, CancellationToken ct)
    {
        var policy = new Entity(PolicyEntity)
        {
            ["sprk_name"] = rule.ShortHeadline,
            ["sprk_policycode"] = code,
            ["sprk_enabled"] = false, // a drafted rule is Off until an administrator publishes it
            ["sprk_priority"] = DraftPolicyPriority,
            ["sprk_lane"] = new OptionSetValue((int)DecisionLane.Decide),
            [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
            ["sprk_ruleset"] = new EntityReference(RuleSetEntity, ruleSetId),
        };
        owner.ApplyTo(policy);
        var policyId = await _entities.CreateAsync(policy, ct);

        try
        {
            var version = new Entity(VersionEntity)
            {
                ["sprk_name"] = $"{code} v1",
                ["sprk_policy"] = new EntityReference(PolicyEntity, policyId),
                ["sprk_versionnumber"] = 1,
                ["sprk_ruletype"] = new OptionSetValue(ruleType),
                ["sprk_rulebody"] = rule.RuleBody,
                ["sprk_messagetemplate"] = rule.MessageTemplate,
                ["sprk_shortheadline"] = rule.ShortHeadline,
                ["sprk_consequence"] = new OptionSetValue(consequence),
                // A consequence lowered to Flag at drafting carries its note here (073 review F1-b); an edit rewrites the column.
                ["sprk_proposedaction"] = note ?? ProposedActionText(consequence),
                ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Proposed),
                ["sprk_clausereference"] = rule.ClauseQuote,
                // Required column; provisional until publish sets the effective date. No reader treats a non-current version as in force.
                ["sprk_inforcefrom"] = DateTime.UtcNow,
                // Task 116 (model 7.2): the draft belongs to the Procedure Version it was drafted from, Draft until that version publishes.
                [ProcedureVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, procedureVersionId),
                [PublishStateColumn] = new OptionSetValue(PublishStateDraft),
            };
            if (!string.IsNullOrWhiteSpace(rule.ClauseLocation))
            {
                version["sprk_clauselocation"] = rule.ClauseLocation.Length > MaxClauseLocationChars
                    ? rule.ClauseLocation[..MaxClauseLocationChars]
                    : rule.ClauseLocation;
            }

            owner.ApplyTo(version);
            await _entities.CreateAsync(version, ct);
        }
        catch
        {
            // A policy with no version is invisible to the review and counts as a rule: remove it, then let the run fail and be repeated.
            try
            {
                await _entities.DeleteAsync(PolicyEntity, policyId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Drafted policy {PolicyId} has no version and could not be removed; it is Off and in force nowhere", policyId);
            }

            throw;
        }
    }

    /// <summary>The not-converted list as the JSON stored on the rule set. Never empty text: an empty list is <c>[]</c>. Capped under the column size.</summary>
    internal static string BuildNotConvertedJson(IReadOnlyList<NotConvertedClause> clauses)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var kept = new List<NotConvertedClause>();
        var size = 2;
        foreach (var clause in clauses)
        {
            var length = JsonSerializer.Serialize(clause, options).Length + 1;
            if (size + length > MaxNotConvertedJsonChars)
            {
                break;
            }

            kept.Add(clause);
            size += length;
        }

        if (kept.Count < clauses.Count)
        {
            kept.Add(new NotConvertedClause(
                string.Empty, null, $"{clauses.Count - kept.Count} more clause(s) were not listed because the list reached its size limit."));
        }

        return JsonSerializer.Serialize(kept, options);
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Edit / accept / reject
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edits a rule's working version. A draft (never in force) is edited in place; a version that is, or has been, in force is left
    /// untouched and the edit becomes a NEW Proposed version. The result is validated first: an invalid result writes nothing. An in-place
    /// edit that changes the type, body, template, consequence, clause text or clause location returns the draft to Proposed (D-109), so
    /// it is accepted again before a publish can put it in force. The new version's id is derived from (policy, number)
    /// (<see cref="VersionIdFor"/>), so two edits of one in-force version that both read number N cannot both create version N+1: the
    /// second create collides on the primary key and is answered 409 <c>ruleset.concurrent_edit</c> instead of being silently lost.
    /// </summary>
    public virtual async Task<RuleSetOutcome<ProposalEdited>> EditAsync(Guid ruleSetId, Guid policyId, ProposalEdit edit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var found = await FindRuleAsync<ProposalEdited>(ruleSetId, policyId, ct);
        if (found.Refusal is not null)
        {
            return found.Refusal;
        }

        var rule = found.Rule!;
        var work = rule.Working;
        var errors = new List<RuleSetError>();

        if (edit is { RuleType: null, RuleBody: null, MessageTemplate: null, ShortHeadline: null, Consequence: null, ClauseReference: null, ClauseLocation: null })
        {
            errors.Add(new RuleSetError(policyId, rule.Policy.Name, "edit", "Nothing to change: name at least one member.", null));
        }

        int? consequence = work.Consequence;
        if (edit.Consequence is not null)
        {
            if (TryParseConsequence(edit.Consequence, out var parsed))
            {
                consequence = (int)parsed;
            }
            else
            {
                errors.Add(new RuleSetError(policyId, rule.Policy.Name, "consequence", "consequence must be Reject, Flag or SuggestAdjustment.", null));
            }
        }

        var ruleTypeRaw = edit.RuleType ?? work.RuleType?.ToString(CultureInfo.InvariantCulture);
        var merged = work with
        {
            RuleBody = edit.RuleBody ?? work.RuleBody,
            Template = edit.MessageTemplate ?? work.Template,
            Headline = edit.ShortHeadline ?? work.Headline,
            Consequence = consequence,
            ClauseReference = edit.ClauseReference ?? work.ClauseReference,
            ClauseLocation = edit.ClauseLocation ?? work.ClauseLocation,
        };
        errors.AddRange(ValidateRule(rule.Policy, ruleTypeRaw, merged));
        if (errors.Count > 0)
        {
            return RuleSetOutcome<ProposalEdited>.Invalid(CodeInvalid, errors);
        }

        // Valid: the type parses and is a billing type (ValidateForSave with the Billing domain), so this cannot fail.
        RuleBodySchemaValidator.TryParseRuleType(ruleTypeRaw, out var ruleType);
        merged = merged with { RuleType = (int)ruleType };

        try
        {
            if (work.IsDraft(rule.Policy))
            {
                var fields = new Dictionary<string, object>
                {
                    ["sprk_ruletype"] = new OptionSetValue(merged.RuleType!.Value),
                    ["sprk_rulebody"] = merged.RuleBody!,
                    ["sprk_messagetemplate"] = merged.Template!,
                    ["sprk_shortheadline"] = merged.Headline!,
                    ["sprk_consequence"] = new OptionSetValue(merged.Consequence!.Value),
                    ["sprk_proposedaction"] = ProposedActionText(merged.Consequence),
                };
                if (merged.ClauseReference is not null)
                {
                    fields["sprk_clausereference"] = merged.ClauseReference;
                }

                if (merged.ClauseLocation is not null)
                {
                    fields["sprk_clauselocation"] = merged.ClauseLocation;
                }

                // What the rule does, or what the firm is shown of it, changed: it is back in review (D-109: the clause text and its
                // location count, as the type, body, template and consequence do). The status is written with EVERY content change,
                // even when this read saw Proposed (a no-op then), so the content and "not yet reviewed" always travel in one write. The
                // races are closed by row versions: an Accept that lands between this read and this write makes this write refused
                // (below), and one that read the old content before this write landed is refused itself (DecideAsync). A headline edit,
                // or one that sets the same values, keeps the decision.
                if (merged.RuleType != work.RuleType || merged.RuleBody != work.RuleBody
                    || merged.Template != work.Template || merged.Consequence != work.Consequence
                    || merged.ClauseReference != work.ClauseReference || merged.ClauseLocation != work.ClauseLocation)
                {
                    fields["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Proposed);

                    // The decision is void, and so is the reason that went with it (D-113): the same write clears both, so a Proposed
                    // draft never carries a rejection reason. Written only when there is a reason to clear.
                    if (work.RejectionReason is not null)
                    {
                        fields[RejectionReasonColumn] = DBNull.Value;
                    }

                    merged = merged with { State = (int)ProposalState.Proposed, RejectionReason = null };
                }

                // Conditional on the row as read (review round 2, Step 9.5): a publish that has already written this draft's start date
                // (its step 1) is about to put THIS row in force, and an unconditional write here would put content nobody accepted
                // in force with it. Another admin's edit or decision since the read is refused the same way, never overwritten.
                try
                {
                    await _entities.BulkUpdateIfRowVersionsMatchAsync(VersionEntity, new List<(Guid id, Dictionary<string, object> fields, long rowVersion)>
                    {
                        (work.Id, fields, RequireRowVersion(work.RowVersion, VersionEntity, work.Id)),
                    }, ct);
                }
                catch (System.Data.DBConcurrencyException)
                {
                    _logger.LogWarning("Rule {PolicyId}: the edit of draft version {VersionId} was refused; it changed after it was read", policyId, work.Id);
                    return RuleSetOutcome<ProposalEdited>.Refused(
                        StatusCodes.Status409Conflict, CodeConcurrentEdit,
                        "The rule changed after you opened it (another edit, a decision or a publish), so your edit was not saved. Reload the rule and make your change again.");
                }

                rule = rule with { Working = merged };
                return RuleSetOutcome<ProposalEdited>.Done(new ProposalEdited(await ViewAsync(rule, ct), CreatedNewVersion: false));
            }

            // The working version is, or has been, in force: it is never updated. The edit is a new version.
            var owner = await _ownership.ResolveOwnerAsync(ContentOwnerContext(VersionEntity, edit.CallerObjectId), ct);
            if (!owner.IsOwned)
            {
                return RuleSetOutcome<ProposalEdited>.OwnerRefused(owner);
            }

            // Task 081: a rule loaded for one Procedure Version sees only that version's rows, so its next number is the POLICY's (numbers and
            // the deterministic ids are per policy, across Procedure Versions).
            var number = work.ProcedureVersionId is null
                ? rule.Versions.Max(v => v.Number) + 1
                : await NextVersionNumberAsync(policyId, ct);
            var created = new Entity(VersionEntity, VersionIdFor(policyId, number))
            {
                ["sprk_name"] = $"{rule.Policy.Code} v{number.ToString(CultureInfo.InvariantCulture)}",
                ["sprk_policy"] = new EntityReference(PolicyEntity, policyId),
                ["sprk_versionnumber"] = number,
                ["sprk_ruletype"] = new OptionSetValue(merged.RuleType!.Value),
                ["sprk_rulebody"] = merged.RuleBody!,
                ["sprk_messagetemplate"] = merged.Template!,
                ["sprk_shortheadline"] = merged.Headline!,
                ["sprk_consequence"] = new OptionSetValue(merged.Consequence!.Value),
                ["sprk_proposedaction"] = ProposedActionText(merged.Consequence),
                ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Proposed),
                // Required column; provisional until publish sets the effective date. No reader treats a non-current version as in force.
                ["sprk_inforcefrom"] = DateTime.UtcNow,
            };
            if (merged.ClauseReference is not null)
            {
                created["sprk_clausereference"] = merged.ClauseReference;
            }

            if (merged.ClauseLocation is not null)
            {
                created["sprk_clauselocation"] = merged.ClauseLocation;
            }

            if (work.ProcedureVersionId is { } procedureVersionId)
            {
                if (PolicyVersionValidator.ValidateProcedureVersionLink(rule.Policy.Domain, procedureVersionId) is { IsValid: false } link)
                {
                    return RuleSetOutcome<ProposalEdited>.Invalid(CodeInvalid, new[]
                    {
                        new RuleSetError(policyId, rule.Policy.Name, ProcedureVersionColumn, link.Errors[0], link.Reason),
                    });
                }

                // Task 081: the new version stays in its Procedure Version's chain, as a draft until that version (or a correction) publishes.
                created[ProcedureVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, procedureVersionId);
                created[PublishStateColumn] = new OptionSetValue(PublishStateDraft);
            }

            owner.ApplyTo(created);
            Guid newId;
            try
            {
                newId = await _entities.CreateAsync(created, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Whatever the fault, look again: a version with this number now existing means another edit created it first (the id
                // is the same, so this create collided with it). Refused as a conflict, never reported as a save, so no edit is lost
                // silently. Otherwise the create failed for another reason (500 below).
                if (await VersionNumberExistsAsync(policyId, number))
                {
                    _logger.LogWarning(ex, "Rule {PolicyId} of rule set {RuleSetId}: version {Number} was created by another edit first; this edit is refused as a conflict",
                        policyId, ruleSetId, number);
                    return RuleSetOutcome<ProposalEdited>.Refused(
                        StatusCodes.Status409Conflict, CodeConcurrentEdit,
                        "The rule changed while your edit was being saved: another edit created its next version first. Reload the rule and, if your change is not there, make it again.");
                }

                throw;
            }

            var newWorking = merged with
            {
                Id = newId,
                Number = number,
                State = (int)ProposalState.Proposed,
                InForceFrom = null,
                InForceTo = null,
            };
            var versions = rule.Versions.Append(newWorking).ToList();
            _logger.LogInformation("Rule {PolicyId} of rule set {RuleSetId}: new draft version {Number} {VersionId} (predecessor {Predecessor} untouched)",
                policyId, ruleSetId, number, newId, work.Id);
            return RuleSetOutcome<ProposalEdited>.Done(new ProposalEdited(
                await ViewAsync(rule with { Working = newWorking, Versions = versions }, ct), CreatedNewVersion: true));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule {PolicyId} edit failed; the working version {VersionId} is unchanged or a never-in-force draft", policyId, work.Id);
            return RuleSetOutcome<ProposalEdited>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The rule could not be saved. Try again; if it keeps failing, contact support.");
        }
    }

    /// <summary>Accept (validated) or reject a rule's working version. A version in force or ever in force cannot be decided (409).</summary>
    /// <param name="expectedRowVersion">
    /// The row version token of the working version the admin was SHOWN (<see cref="ProposalView.RowVersion"/>, task 077). A decision is
    /// never taken on content the admin did not see: when the row has changed since (an edit, another decision, a publish), the decision is
    /// refused 409 <see cref="CodeConcurrentEdit"/> before it is validated or written, whatever the change was (a headline edit included,
    /// because the admin saw the old headline). The write below is also conditional on this same token.
    /// </param>
    /// <param name="rejectionReason">
    /// Required for a reject (task 077, D-113): trimmed, 1 to <see cref="MaxRejectionReasonChars"/> characters, else 400 <see cref="CodeInvalid"/>
    /// with a <c>reason</c> field error and nothing read or written. Stored in the same conditional write as the Rejected status. Ignored by an
    /// accept, which clears any reason an earlier reject left. Optional only so that the accept call sites stay as they were.
    /// </param>
    public virtual async Task<RuleSetOutcome<ProposalView>> DecideAsync(
        Guid ruleSetId, Guid policyId, bool accept, long expectedRowVersion, CancellationToken ct, string? rejectionReason = null)
    {
        var reason = string.Empty;
        if (!accept && !TryNormalizeRejectionReason(rejectionReason, out reason))
        {
            return RuleSetOutcome<ProposalView>.Invalid(CodeInvalid, new[] { RejectionReasonError(policyId) });
        }

        var found = await FindRuleAsync<ProposalView>(ruleSetId, policyId, ct);
        if (found.Refusal is not null)
        {
            return found.Refusal;
        }

        var rule = found.Rule!;
        var work = rule.Working;
        if (!work.IsDraft(rule.Policy))
        {
            return RuleSetOutcome<ProposalView>.Refused(
                StatusCodes.Status409Conflict, CodeInForce,
                "This version is, or has been, in force and cannot be accepted or rejected. Edit the rule to draft a new version.");
        }

        // What the admin was shown is not what is stored now: refuse before validating (the errors would be about content they never saw)
        // and before writing. A row with no version token cannot be compared, so it is refused the same way (fail closed).
        if (work.RowVersion is not { } current || current != expectedRowVersion)
        {
            _logger.LogWarning("Rule {PolicyId}: the {Decision} was refused; the admin saw version token {Expected} of {VersionId} but it is {Current}",
                policyId, accept ? "accept" : "reject", expectedRowVersion, work.Id, work.RowVersion);
            return RuleSetOutcome<ProposalView>.Refused(
                StatusCodes.Status409Conflict, CodeConcurrentEdit,
                "The rule changed after you opened it, so your decision was not saved. Reload the rule, review the change and decide again.");
        }

        if (accept)
        {
            var errors = ValidateRule(rule.Policy, work.RuleType?.ToString(CultureInfo.InvariantCulture), work);
            if (errors.Count > 0)
            {
                return RuleSetOutcome<ProposalView>.Invalid(CodeInvalid, errors);
            }
        }

        var state = accept ? ProposalState.Accepted : ProposalState.Rejected;
        try
        {
            // Conditional on the row as read: the decision is about the content this read validated. An edit that landed since then
            // (it changed the content and wrote Proposed) advanced the row version, so this decision is refused instead of putting
            // content nobody reviewed into the Accepted state (review pass 2).
            // The reason travels with the status in this one write (D-113): a reject stores it, an accept clears an earlier one (only
            // when there is one, so an accept of a never-rejected draft writes what it always did).
            var decision = new Dictionary<string, object> { ["sprk_proposalstatus"] = new OptionSetValue((int)state) };
            if (!accept)
            {
                decision[RejectionReasonColumn] = reason;
            }
            else if (work.RejectionReason is not null)
            {
                decision[RejectionReasonColumn] = DBNull.Value;
            }

            await _entities.BulkUpdateIfRowVersionsMatchAsync(VersionEntity, new List<(Guid id, Dictionary<string, object> fields, long rowVersion)>
            {
                (work.Id, decision, RequireRowVersion(work.RowVersion, VersionEntity, work.Id)),
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (System.Data.DBConcurrencyException)
        {
            _logger.LogWarning("Rule {PolicyId}: the {State} decision was refused; version {VersionId} changed after it was read", policyId, state, work.Id);
            return RuleSetOutcome<ProposalView>.Refused(
                StatusCodes.Status409Conflict, CodeConcurrentEdit,
                "The rule changed after you opened it, so your decision was not saved. Reload the rule, review it and decide again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule {PolicyId} could not be set {State}", policyId, state);
            return RuleSetOutcome<ProposalView>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The decision could not be saved. Try again; if it keeps failing, contact support.");
        }

        return RuleSetOutcome<ProposalView>.Done(await ViewAsync(
            rule with { Working = work with { State = (int)state, RejectionReason = accept ? null : reason } }, ct));
    }

    /// <summary>
    /// The one rule for a rejection reason (D-113): trimmed, 1 to <see cref="MaxRejectionReasonChars"/> characters. The reject route applies it
    /// before the service is called and <see cref="DecideAsync"/> applies it again, so no caller can store a blank or an over-long reason.
    /// </summary>
    public static bool TryNormalizeRejectionReason(string? raw, out string reason)
    {
        reason = raw?.Trim() ?? string.Empty;
        return reason.Length is >= 1 and <= MaxRejectionReasonChars;
    }

    /// <summary>The field error for a missing, blank or over-long rejection reason.</summary>
    public static RuleSetError RejectionReasonError(Guid policyId) => new(
        policyId, null, "reason",
        $"reason is required to reject a rule: 1 to {MaxRejectionReasonChars.ToString(CultureInfo.InvariantCulture)} characters.", null);

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Publish
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts every Accepted version that is not yet in force into force on <paramref name="effectiveDate"/>, all or nothing. Proposed and
    /// Rejected versions are left as they are and counted in the result.
    /// </summary>
    /// <remarks>
    /// <b>One publish of a rule set at a time (review round 3).</b> The publish takes the set's claim (<see cref="PublishClaimId"/>, an
    /// <see cref="IScheduledJobLease"/>: distributed in the BFF, with an expiry) before its first read and releases it when it ends,
    /// however it ends. A second publish finds the claim held and is refused 409 <see cref="CodePublishInProgress"/> without reading or
    /// writing. Without it, one publish's take-back could clear the end date a second publish had just stamped on the same old version
    /// before the second moved its pointer, leaving a superseded version with no end date, which the in-force rule (072) never evaluates.
    /// The claim store being unreachable fails closed (500, nothing written).
    /// </remarks>
    public virtual async Task<RuleSetOutcome<RuleSetPublished>> PublishAsync(Guid ruleSetId, DateOnly effectiveDate, CancellationToken ct)
    {
        var claimId = PublishClaimId(ruleSetId);
        ScheduledJobLeaseGrant grant;
        try
        {
            grant = await _lease.TryAcquireAsync(claimId, occurrenceUtc: null, PublishClaimLifetime, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Rule set {RuleSetId}: the publish claim could not be taken (the lease store is unreachable); nothing was read or written", ruleSetId);
            return RuleSetOutcome<RuleSetPublished>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed,
                "The rule set could not be published. Publish again; if it keeps failing, contact support.");
        }

        if (grant.Status != ScheduledJobLeaseStatus.Granted || grant.Token is not { } token)
        {
            return RuleSetOutcome<RuleSetPublished>.Refused(
                StatusCodes.Status409Conflict, CodePublishInProgress,
                "Another publish of this rule set is in progress. Wait for it to finish, reload the rule set and publish again.");
        }

        try
        {
            return await PublishClaimedAsync(ruleSetId, effectiveDate, ct);
        }
        finally
        {
            try
            {
                await _lease.ReleaseAsync(claimId, token, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The claim expires on its own (PublishClaimLifetime); until then a publish of this set is told one is in progress.
                _logger.LogWarning(ex, "Rule set {RuleSetId}: the publish claim could not be released; it expires in {Minutes} minutes", ruleSetId, PublishClaimLifetime.TotalMinutes);
            }
        }
    }

    private async Task<RuleSetOutcome<RuleSetPublished>> PublishClaimedAsync(Guid ruleSetId, DateOnly effectiveDate, CancellationToken ct)
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return RuleSetOutcome<RuleSetPublished>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found.");
        }

        var status = set.GetAttributeValue<OptionSetValue>("statuscode")?.Value;
        if (status == RuleSetRetired)
        {
            return RuleSetOutcome<RuleSetPublished>.Refused(StatusCodes.Status409Conflict, CodeRetired, "A retired rule set cannot be published.");
        }

        // Task 081 (model 7.2): a Procedure's rules go into force by publishing a Procedure Version (ProcedureService), which marks them Published
        // under that version. This publish would stamp end dates and move pointers on the legacy chain, which the re-keyed chain does not use.
        if (set.GetAttributeValue<EntityReference>(ProcedureColumn) is not null)
        {
            return RuleSetOutcome<RuleSetPublished>.Refused(
                StatusCodes.Status409Conflict, CodeProcedurePublish,
                "This rule set belongs to a Procedure. Publish the Procedure's Draft version instead; that puts its rules in force.");
        }

        var setErrors = new List<RuleSetError>();
        var scope = set.GetAttributeValue<OptionSetValue>("sprk_scope")?.Value;
        var hasFirm = set.GetAttributeValue<EntityReference>("sprk_firm") is not null;
        if (scope == (int)RuleSetScope.Firm && !hasFirm)
        {
            setErrors.Add(new RuleSetError(null, null, "scope", "A rule set assigned to a firm needs its firm before it is published.", null));
        }
        else if (scope != (int)RuleSetScope.Firm && scope != (int)RuleSetScope.ClientDefault)
        {
            setErrors.Add(new RuleSetError(null, null, "scope", "The rule set has no scope: assign it to the client default or a firm before it is published.", null));
        }
        else if (scope == (int)RuleSetScope.ClientDefault && hasFirm)
        {
            setErrors.Add(new RuleSetError(null, null, "scope", "A client default rule set takes no firm.", null));
        }

        var loaded = await LoadRulesAsync(ruleSetId, ct);
        if (loaded.Truncated)
        {
            return RuleSetOutcome<RuleSetPublished>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "The rule set has more rules than can be published in one pass.");
        }

        var candidates = new List<RuleSetRule>();
        int proposed = 0, rejected = 0;
        foreach (var rule in loaded.Rules)
        {
            var work = rule.Working;
            if (!work.IsDraft(rule.Policy))
            {
                continue; // already in force (or its end is stamped): nothing to publish
            }

            switch (work.StateOrProposed)
            {
                case ProposalState.Accepted:
                    candidates.Add(rule);
                    break;
                case ProposalState.Rejected:
                    rejected++;
                    break;
                default:
                    proposed++;
                    break;
            }
        }

        if (candidates.Count == 0 && setErrors.Count == 0)
        {
            // A publish whose last write failed left every rule in force and the set not Published, with nothing left to put in force.
            // The retry writes the set: refusing it as "nothing to publish" would leave the set Draft for good.
            var inForce = status == RuleSetPublished
                ? new List<RuleSetRule>()
                : loaded.Rules.Where(r => r.Policy.CurrentVersionId == r.Working.Id).ToList();
            if (inForce.Count == 0)
            {
                return RuleSetOutcome<RuleSetPublished>.Refused(
                    StatusCodes.Status400BadRequest, CodeNothingToPublish, "No accepted rule is waiting to be published. Accept at least one rule first.");
            }

            // The rules came into force on the date the earlier attempt stamped; the set shows that date, not the one in this request.
            var startedOn = inForce.Where(r => r.Working.InForceFrom is not null).Select(r => r.Working.InForceFrom!.Value).DefaultIfEmpty(ToUtcDate(effectiveDate)).Min();
            var startedDate = DateOnly.FromDateTime(startedOn);
            if (!await MarkPublishedAsync(ruleSetId, set, status, ToUtcDate(startedDate)))
            {
                return RuleSetOutcome<RuleSetPublished>.Refused(
                    StatusCodes.Status500InternalServerError, CodeWriteFailed,
                    "The rule set could not be published. Publish again; if it keeps failing, contact support.");
            }

            _logger.LogInformation("Rule set {RuleSetId} marked Published: {Count} rule version(s) were already in force from {Effective}", ruleSetId, inForce.Count, startedDate);
            // The superseded version is the one that carries an end date (the earlier attempt stamped it); a lower-numbered version
            // without one was never in force (a rejected draft, say) and superseded nothing.
            return RuleSetOutcome<RuleSetPublished>.Done(PublishedResult(
                set, ruleSetId, startedDate, inForce.Select(r => new PublishedRuleVersion(
                    r.Policy.Id, r.Working.Id, r.Working.Number,
                    r.Versions.Where(v => v.Id != r.Working.Id && v.InForceTo is not null).MaxBy(v => v.Number)?.Id)).ToList(),
                proposed, rejected));
        }

        // Every candidate is validated BEFORE the first write; one failure refuses the whole publish.
        var errors = new List<RuleSetError>(setErrors);
        var effectiveStart = ToUtcDate(effectiveDate);
        foreach (var rule in candidates)
        {
            var work = rule.Working;
            errors.AddRange(ValidateRule(rule.Policy, work.RuleType?.ToString(CultureInfo.InvariantCulture), work));
            if (rule.Predecessor is { } predecessor && predecessor.InForceFrom is { } from && effectiveStart < from.Date)
            {
                errors.Add(new RuleSetError(rule.Policy.Id, rule.Policy.Name, "effectiveDate",
                    $"The effective date is before the date the version it replaces came into force ({DateOnly.FromDateTime(from):yyyy-MM-dd}).", null));
            }
        }

        if (errors.Count > 0)
        {
            return RuleSetOutcome<RuleSetPublished>.Invalid(CodePublishInvalid, errors);
        }

        // The last point at which the caller going away stops the publish. The writes below are not abandoned half way (D-14).
        ct.ThrowIfCancellationRequested();

        switch (await WritePublishAsync(ruleSetId, set, status, candidates, effectiveStart))
        {
            case PublishWrite.Conflict:
                return RuleSetOutcome<RuleSetPublished>.Refused(
                    StatusCodes.Status409Conflict, CodeConcurrentEdit,
                    "A rule in this set changed while it was being published (another publish, edit or decision), so the publish stopped. " +
                    "Reload the rule set and publish again.");
            case PublishWrite.Failed:
                return RuleSetOutcome<RuleSetPublished>.Refused(
                    StatusCodes.Status500InternalServerError, CodeWriteFailed,
                    "The rule set could not be published. Publish again; if it keeps failing, contact support.");
        }

        _logger.LogInformation("Rule set {RuleSetId} published: {Count} rule version(s) in force from {Effective}", ruleSetId, candidates.Count, effectiveDate);
        return RuleSetOutcome<RuleSetPublished>.Done(PublishedResult(
            set, ruleSetId, effectiveDate,
            candidates.Select(c => new PublishedRuleVersion(c.Policy.Id, c.Working.Id, c.Working.Number, c.Predecessor?.Id)).ToList(),
            proposed, rejected));
    }

    private static RuleSetPublished PublishedResult(
        Entity set, Guid ruleSetId, DateOnly effectiveDate, IReadOnlyList<PublishedRuleVersion> published, int proposed, int rejected)
    {
        var visible = set.GetAttributeValue<bool>("sprk_visibletofirms");
        return new RuleSetPublished(ruleSetId, "Published", effectiveDate, published, proposed, rejected, visible, visible ? VisibleNotice : HiddenNotice);
    }

    /// <summary>How the publish writes ended.</summary>
    private enum PublishWrite
    {
        Done,

        /// <summary>A write failed (500). The end dates this publish may have written are taken back where the pointer did not move.</summary>
        Failed,

        /// <summary>A row changed after the publish read it (409). The refused chunk wrote nothing and is not taken back.</summary>
        Conflict,
    }

    /// <summary>
    /// The three publish writes, in order, with <see cref="CancellationToken.None"/>: a client that disconnects between them must not leave
    /// an end date on a version whose pointer never moved (D-14: the stamp and the pointer move together). A failure before the pointer
    /// move takes back the end dates this publish may have written (<see cref="TakeBackEndDatesAsync"/>); a failure of the last write leaves
    /// every rule in force and the set not Published, which the next publish finishes.
    /// </summary>
    /// <remarks>
    /// <para><b>Claimed (review round 3).</b> Called only with the rule set's publish claim held (<see cref="PublishAsync"/>), so no other
    /// publish of the set stamps, takes back or moves pointers meanwhile; the take-back can therefore not clear a stamp that belongs to
    /// another publish. An edit, a decision or an Ontology publish still can run: the conditional writes below catch those.</para>
    /// <para><b>Conditional (review pass 2).</b> Steps 1 and 2 are conditional on the row versions <see cref="LoadRulesAsync"/> read: a row
    /// another admin changed since (a concurrent publish that stamped the same old version or moved the same pointer, an edit, a decision)
    /// makes Dataverse refuse that chunk, which then wrote NOTHING, and the publish stops with <see cref="PublishWrite.Conflict"/>.</para>
    /// <para><b>Take-back only for what landed.</b> The chunks of step 1 that landed (or whose outcome is unknown) are taken back for the
    /// policies whose pointer did not move. A refused step 1 chunk is not: whatever end date its old versions carry is not this
    /// publish's, and clearing it could clear the end of a version a concurrent publish really superseded. A refused step 2 chunk is taken
    /// back like any other failure (round 3): the take-back decides from a fresh read, so a policy whose pointer another writer moved
    /// keeps its stamp (that version was superseded) and a policy that changed WITHOUT its pointer moving (a rename, a toggle) has its
    /// stamp cleared: its old version is still current and must not end up carrying an end date.</para>
    /// </remarks>
    private async Task<PublishWrite> WritePublishAsync(
        Guid ruleSetId, Entity set, int? status, IReadOnlyList<RuleSetRule> candidates, DateTime effectiveStart)
    {
        var ct = CancellationToken.None;

        // The old versions this publish stamps. An old version that already ends on the effective date is not stamped again; one that
        // ends on another date (an earlier attempt) is, so a retry with a different date leaves no gap between the two versions.
        var stamped = candidates.Where(r => r.Predecessor is { } p && p.InForceTo != effectiveStart).ToList();
        var stampedPredecessors = stamped.Select(r => r.Predecessor!.Id).ToHashSet();

        // Every update and the row version it is conditional on, built before the first write: a row read without its row version stops
        // the publish here, with nothing written.
        var versionUpdates = new List<(Guid id, Dictionary<string, object> fields, long rowVersion)>();
        var policyUpdates = new List<(Guid id, Dictionary<string, object> fields, long rowVersion)>();
        try
        {
            foreach (var rule in candidates)
            {
                versionUpdates.Add((rule.Working.Id, new Dictionary<string, object> { ["sprk_inforcefrom"] = effectiveStart },
                    RequireRowVersion(rule.Working.RowVersion, VersionEntity, rule.Working.Id)));
                if (rule.Predecessor is { } predecessor && stampedPredecessors.Contains(predecessor.Id))
                {
                    versionUpdates.Add((predecessor.Id, new Dictionary<string, object> { ["sprk_inforceto"] = effectiveStart },
                        RequireRowVersion(predecessor.RowVersion, VersionEntity, predecessor.Id)));
                }

                policyUpdates.Add((rule.Policy.Id, new Dictionary<string, object>
                {
                    ["sprk_currentversion"] = new EntityReference(VersionEntity, rule.Working.Id),
                    ["sprk_enabled"] = true,
                }, RequireRowVersion(rule.Policy.RowVersion, PolicyEntity, rule.Policy.Id)));
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Rule set {RuleSetId} publish stopped before any write: a row was read without its row version", ruleSetId);
            return PublishWrite.Failed;
        }

        var mayHaveStamped = new HashSet<Guid>();   // old versions whose end date a landed (or unknown) step 1 chunk wrote
        try
        {
            // (1) the versions: the new versions' in-force-from, the old versions' in-force-to (one table, one transaction per chunk).
            foreach (var chunk in versionUpdates.Chunk(MaxPerTransaction))
            {
                var stampsInChunk = chunk.Where(u => stampedPredecessors.Contains(u.id)).Select(u => u.id).ToList();
                mayHaveStamped.UnionWith(stampsInChunk); // before the write: a write whose outcome is unknown counts as landed
                try
                {
                    await _entities.BulkUpdateIfRowVersionsMatchAsync(VersionEntity, chunk.ToList(), ct);
                }
                catch (System.Data.DBConcurrencyException)
                {
                    mayHaveStamped.ExceptWith(stampsInChunk); // refused: this chunk wrote nothing
                    throw;
                }
            }

            // (2) the policies: the pointer move is what puts a version in force.
            foreach (var chunk in policyUpdates.Chunk(MaxPerTransaction))
            {
                await _entities.BulkUpdateIfRowVersionsMatchAsync(PolicyEntity, chunk.ToList(), ct);
            }
        }
        catch (System.Data.DBConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Rule set {RuleSetId} publish stopped: a rule changed after the publish read it. The refused chunk wrote nothing; the end dates of the chunks that landed are taken back where the pointer did not move", ruleSetId);
            await TakeBackEndDatesAsync(ruleSetId, stamped.Where(r => mayHaveStamped.Contains(r.Predecessor!.Id)).ToList());
            return PublishWrite.Conflict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule set {RuleSetId} publish failed before every rule was in force; the old versions stay in force and the end dates this publish wrote are taken back", ruleSetId);
            await TakeBackEndDatesAsync(ruleSetId, stamped.Where(r => mayHaveStamped.Contains(r.Predecessor!.Id)).ToList());
            return PublishWrite.Failed;
        }

        // (3) the rule set. Last, so a failure before it leaves a state a second publish finishes.
        return await MarkPublishedAsync(ruleSetId, set, status, effectiveStart) ? PublishWrite.Done : PublishWrite.Failed;
    }

    /// <summary>Writes the rule set Published (and its effective-from when it has none or was not yet Published). False on failure, logged.</summary>
    private async Task<bool> MarkPublishedAsync(Guid ruleSetId, Entity set, int? status, DateTime effectiveStart)
    {
        var setFields = new Dictionary<string, object>
        {
            ["statecode"] = new OptionSetValue(0),
            ["statuscode"] = new OptionSetValue(RuleSetPublished),
        };
        if (set.GetAttributeValue<DateTime?>("sprk_effectivefrom") is null || status != RuleSetPublished)
        {
            setFields["sprk_effectivefrom"] = effectiveStart;
        }

        try
        {
            await _entities.UpdateAsync(RuleSetEntity, ruleSetId, setFields, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule set {RuleSetId}: the rules are in force but the set could not be marked Published; publishing again finishes it", ruleSetId);
            return false;
        }
    }

    /// <summary>
    /// After a failed or refused publish: clears the end date of every old version this publish may have stamped whose policy's pointer
    /// still names it (it is in force, so it has no end; a version that was replaced keeps its end date). Decided from a fresh read, not
    /// from which write failed, so a partly applied chunk or a write whose outcome is unknown is handled the same way. The value written is
    /// always "no end date", never the value read before the publish: that value may itself be a stale stamp left by an earlier attempt
    /// (review pass 2). Each clear is conditional on the row version of the fresh read, so a version another publish (or a late write)
    /// changed after that read is left as it is. A version not cleared is logged as an Error with its id: until it is cleared, or the set
    /// is published again, a version in force carries an end date.
    /// </summary>
    private async Task TakeBackEndDatesAsync(Guid ruleSetId, IReadOnlyList<RuleSetRule> stamped)
    {
        if (stamped.Count == 0)
        {
            return;
        }

        var restore = new List<(Guid id, Dictionary<string, object> fields, long rowVersion)>();
        try
        {
            var now = await LoadRulesAsync(ruleSetId, CancellationToken.None);
            if (now.Truncated)
            {
                throw new InvalidOperationException("The rule set has more rules than can be read in one pass.");
            }

            var byPolicy = now.Rules.ToDictionary(r => r.Policy.Id);
            foreach (var rule in stamped)
            {
                var old = rule.Predecessor!;
                if (byPolicy.TryGetValue(rule.Policy.Id, out var current)
                    && current.Policy.CurrentVersionId == old.Id
                    && current.Versions.FirstOrDefault(v => v.Id == old.Id) is { InForceTo: not null } row)
                {
                    restore.Add((old.Id, new Dictionary<string, object> { ["sprk_inforceto"] = DBNull.Value }, RequireRowVersion(row.RowVersion, VersionEntity, old.Id)));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule set {RuleSetId}: could not read the rules to take back the end dates of a failed publish. Versions that may carry an end date with no pointer move: {VersionIds}",
                ruleSetId, string.Join(", ", stamped.Select(r => r.Predecessor!.Id)));
            return;
        }

        foreach (var chunk in restore.Chunk(MaxPerTransaction))
        {
            try
            {
                await _entities.BulkUpdateIfRowVersionsMatchAsync(VersionEntity, chunk.ToList(), CancellationToken.None);
            }
            catch (System.Data.DBConcurrencyException ex)
            {
                _logger.LogError(ex, "Rule set {RuleSetId}: the end date of a failed publish was not taken back from version(s) {VersionIds}: one of them changed after the take-back read it (another publish or a late write). Each keeps its end date; one still in force keeps it until it is cleared or the rule set is published again",
                    ruleSetId, string.Join(", ", chunk.Select(c => c.id)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rule set {RuleSetId}: the end date of a failed publish could not be taken back from version(s) {VersionIds}; each is in force with an end date until it is cleared or the rule set is published again",
                    ruleSetId, string.Join(", ", chunk.Select(c => c.id)));
            }
        }
    }

    /// <summary>The row version a conditional write needs. Dataverse always returns <c>versionnumber</c> when it is selected; a row read
    /// without one is not written unconditionally instead (that would defeat the condition): the write fails (500, logged).</summary>
    private static long RequireRowVersion(long? rowVersion, string entity, Guid id) =>
        rowVersion ?? throw new InvalidOperationException($"{entity} {id} was read without its row version; it is not written unconditionally.");

    /// <summary>
    /// The id of version <paramref name="number"/> of <paramref name="policyId"/> when an edit creates it: a version-5 UUID (SHA-1) of the
    /// name <c>"sprk_policyversion:{policyId:D}:{number}"</c> in <see cref="EditVersionIdNamespace"/>. Two edits that both create the same
    /// number therefore create the same primary key, and Dataverse refuses the second (the same derivation as
    /// <c>InquiryDispositionService.TodoIdFor</c> and <c>SignalSuppressionService.ProbeId</c>, which are private to their namespaces).
    /// </summary>
    internal static Guid VersionIdFor(Guid policyId, int number)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"sprk_policyversion:{policyId:D}:{number}"));
        var input = new byte[16 + name.Length];
        EditVersionIdNamespace.ToByteArray(bigEndian: true).CopyTo(input, 0);
        name.CopyTo(input, 16);

        var bytes = System.Security.Cryptography.SHA1.HashData(input)[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Task 081: the next version number of <paramref name="policyId"/> across every Procedure Version (highest existing + 1).</summary>
    private async Task<int> NextVersionNumberAsync(Guid policyId, CancellationToken ct)
    {
        // all versions: numbering counts every version, active or not (the number is a key).
        var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet("sprk_versionnumber"), TopCount = 1 };
        query.Criteria.AddCondition("sprk_policy", ConditionOperator.Equal, policyId);
        query.Orders.Add(new OrderExpression("sprk_versionnumber", OrderType.Descending));
        var top = (await _entities.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault();
        return (top?.GetAttributeValue<int?>("sprk_versionnumber") ?? 0) + 1;
    }

    /// <summary>Task 081: the Procedure's Draft version (the highest-numbered when, against the one-draft rule, there are several); null when none.</summary>
    private async Task<Guid?> FindDraftProcedureVersionAsync(Guid procedureId, CancellationToken ct)
    {
        var query = new QueryExpression(ProcedureEntity.VersionEntity) { ColumnSet = new ColumnSet(ProcedureEntity.VersionNumberColumn), TopCount = 1 };
        query.Criteria.AddCondition(ProcedureEntity.VersionProcedureColumn, ConditionOperator.Equal, procedureId);
        query.Criteria.AddCondition(ProcedureEntity.VersionStatusColumn, ConditionOperator.Equal, ProcedureEntity.VersionStatusDraft);
        query.Orders.Add(new OrderExpression(ProcedureEntity.VersionNumberColumn, OrderType.Descending));
        return (await _entities.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault()?.Id;
    }

    /// <summary>Whether <paramref name="policyId"/> has a version numbered <paramref name="number"/> (read after a failed create).</summary>
    private async Task<bool> VersionNumberExistsAsync(Guid policyId, int number)
    {
        // all versions: the (policy, number) key holds for inactive versions too.
        var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet("sprk_versionnumber"), TopCount = 1 };
        query.Criteria.AddCondition("sprk_policy", ConditionOperator.Equal, policyId);
        query.Criteria.AddCondition("sprk_versionnumber", ConditionOperator.Equal, number);
        return (await _entities.RetrieveMultipleAsync(query, CancellationToken.None)).Entities.Count > 0;
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Assignment and visibility
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Assigns the rule set to the client default or to one firm.</summary>
    public virtual async Task<RuleSetOutcome<RuleSetSummary>> AssignAsync(Guid ruleSetId, RuleSetScope scope, Guid? firmId, CancellationToken ct)
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return RuleSetOutcome<RuleSetSummary>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found.");
        }

        if (set.GetAttributeValue<OptionSetValue>("statuscode")?.Value == RuleSetRetired)
        {
            return RuleSetOutcome<RuleSetSummary>.Refused(StatusCodes.Status409Conflict, CodeRetired, "A retired rule set cannot be changed.");
        }

        if (ProcedureOwned<RuleSetSummary>(set) is { } owned)
        {
            return owned;
        }

        var errors = new List<RuleSetError>();
        if (scope == RuleSetScope.Firm && (firmId is null || firmId == Guid.Empty))
        {
            errors.Add(new RuleSetError(null, null, "firmId", "Assigning to a firm needs firmId.", null));
        }
        else if (scope == RuleSetScope.ClientDefault && firmId is not null)
        {
            errors.Add(new RuleSetError(null, null, "firmId", "The client default takes no firmId.", null));
        }
        else if (firmId is { } firm && !await ExistsAsync(FirmEntity, "sprk_organizationid", firm, ct))
        {
            errors.Add(new RuleSetError(null, null, "firmId", "The firm was not found.", null));
        }

        if (errors.Count > 0)
        {
            return RuleSetOutcome<RuleSetSummary>.Invalid(CodeInvalid, errors);
        }

        var fields = new Dictionary<string, object>
        {
            ["sprk_scope"] = new OptionSetValue((int)scope),
            // DBNull clears the lookup (the generic seam's convention), so moving a firm set to the client default drops the firm.
            ["sprk_firm"] = firmId is { } id ? new EntityReference(FirmEntity, id) : DBNull.Value,
        };
        return await UpdateSetAsync(ruleSetId, set, fields, ct);
    }

    /// <summary>Turns "visible to firms" on or off. Takes effect for every firm-facing projection at once (it is read when the finding is shown).</summary>
    public virtual async Task<RuleSetOutcome<RuleSetSummary>> SetVisibilityAsync(Guid ruleSetId, bool visibleToFirms, CancellationToken ct)
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return RuleSetOutcome<RuleSetSummary>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found.");
        }

        if (set.GetAttributeValue<OptionSetValue>("statuscode")?.Value == RuleSetRetired)
        {
            return RuleSetOutcome<RuleSetSummary>.Refused(StatusCodes.Status409Conflict, CodeRetired, "A retired rule set cannot be changed.");
        }

        if (ProcedureOwned<RuleSetSummary>(set) is { } owned)
        {
            return owned;
        }

        return await UpdateSetAsync(ruleSetId, set, new Dictionary<string, object> { ["sprk_visibletofirms"] = visibleToFirms }, ct);
    }

    /// <summary>Task 081: the 409 for a rule set that belongs to a Procedure (<see cref="CodeProcedureOwned"/>); null for any other set.</summary>
    private static RuleSetOutcome<T>? ProcedureOwned<T>(Entity set) where T : class =>
        set.GetAttributeValue<EntityReference>(ProcedureColumn) is null
            ? null
            : RuleSetOutcome<T>.Refused(StatusCodes.Status409Conflict, CodeProcedureOwned,
                "This rule set belongs to a Procedure. Change who it applies to, or its type, on the Procedure.");

    private async Task<RuleSetOutcome<RuleSetSummary>> UpdateSetAsync(Guid ruleSetId, Entity set, Dictionary<string, object> fields, CancellationToken ct)
    {
        try
        {
            await _entities.UpdateAsync(RuleSetEntity, ruleSetId, fields, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule set {RuleSetId} could not be updated", ruleSetId);
            return RuleSetOutcome<RuleSetSummary>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The rule set could not be saved. Try again; if it keeps failing, contact support.");
        }

        foreach (var (key, value) in fields)
        {
            set[key] = value is DBNull ? null : value;
        }

        var loaded = await LoadRulesAsync(ruleSetId, ct);
        return RuleSetOutcome<RuleSetSummary>.Done(Summarize(set, loaded.Rules.Count));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // A Procedure's rules (task 081, EB-D-148, procedure model 7.2)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The rule set row of a new Procedure (task 081, model 3.3: "a Procedure is the document; the rule set is the rules drawn from it"):
    /// Billing domain, Draft, linked to the Procedure, its scope and firm mirroring the Procedure's (<see cref="ProcedureScope"/>), and the
    /// initial clause-visibility flag of its type (D-66). The caller applies the owner and places the create in its own transaction.
    /// </summary>
    internal static Entity NewProcedureRuleSet(Guid ruleSetId, string name, Guid procedureId, Guid? firmId, bool visibleToFirms)
    {
        var row = new Entity(RuleSetEntity, ruleSetId)
        {
            ["sprk_name"] = name,
            [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
            [ProcedureColumn] = new EntityReference(ProcedureEntity.Entity, procedureId),
            ["sprk_visibletofirms"] = visibleToFirms,
            ["statecode"] = new OptionSetValue(0),
            ["statuscode"] = new OptionSetValue(RuleSetDraft),
        };
        foreach (var (column, value) in ProcedureScope(firmId))
        {
            if (value is not DBNull) row[column] = value;
        }

        RecordRuleSet.RequireOneHome(row); // task 089c (089a K1): a Procedure's set never carries a core-record lookup or a record scope
        return row;
    }

    /// <summary>
    /// A Procedure rule set's <c>sprk_scope</c> and <c>sprk_firm</c> for the Procedure's firm (task 081). Applicability is the Procedure's
    /// (Applies to, Firm; read by 082's resolver), so these exist only for the legacy readers that still read the set (the D-79 firm clause):
    /// a Procedure naming a firm is a Firm set of that firm, otherwise a client default set with no firm. <see cref="DBNull"/> clears.
    /// </summary>
    internal static IReadOnlyList<(string Column, object Value)> ProcedureScope(Guid? firmId) => firmId is { } firm
        ? new (string, object)[] { ("sprk_scope", new OptionSetValue((int)RuleSetScope.Firm)), ("sprk_firm", new EntityReference(FirmEntity, firm)) }
        : new (string, object)[] { ("sprk_scope", new OptionSetValue((int)RuleSetScope.ClientDefault)), ("sprk_firm", DBNull.Value) };

    /// <summary>The Billing rule set of a Procedure (task 081): null when it has none. Read through the billing filter, as every read here.</summary>
    internal async Task<Entity?> LoadProcedureRuleSetAsync(Guid procedureId, CancellationToken ct)
    {
        var query = new QueryExpression(RuleSetEntity) { ColumnSet = new ColumnSet(RuleSetColumns), TopCount = 2 };
        query.Criteria.AddCondition(ProcedureColumn, ConditionOperator.Equal, procedureId);
        RuleDomains.AddBillingFilter(query);
        query.Orders.Add(new OrderExpression("createdon", OrderType.Ascending));
        var rows = (await _entities.RetrieveMultipleAsync(query, ct)).Entities
            .Where(r => RuleDomains.IsBillingRow(r) && r.GetAttributeValue<EntityReference>(ProcedureColumn)?.Id == procedureId)
            .ToList();
        if (rows.Count > 1)
        {
            // One rule set per Procedure (the service creates exactly one, with the Procedure, in one transaction). The first is used.
            _logger.LogError("Procedure {ProcedureId} has more than one billing rule set; the earliest, {RuleSetId}, is used", procedureId, rows[0].Id);
        }

        return rows.FirstOrDefault();
    }

    /// <summary>The live (statecode Active) Billing policies of a rule set, with their enabled flag and row version (task 081: the retire).</summary>
    internal async Task<(IReadOnlyList<PolicyRow> Policies, bool Truncated)> LoadPoliciesAsync(Guid ruleSetId, CancellationToken ct)
    {
        var policies = await _entities.RetrieveMultipleAsync(PolicyQuery("sprk_ruleset", ConditionOperator.Equal, new object[] { ruleSetId }), ct);
        var rows = policies.Entities
            .Where(p => RuleDomains.IsBillingRow(p) && p.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id == ruleSetId)
            .Select(p => new PolicyRow(
                p.Id,
                p.GetAttributeValue<string>("sprk_policycode") ?? p.Id.ToString(),
                p.GetAttributeValue<string>("sprk_name") ?? p.Id.ToString(),
                p.GetAttributeValue<EntityReference>("sprk_currentversion")?.Id,
                p.GetAttributeValue<long?>(RowVersionColumn),
                p.GetAttributeValue<OptionSetValue>(RuleDomains.PolicyDomainColumn)?.Value,
                p.GetAttributeValue<bool?>("sprk_enabled")))
            .ToList();
        return (rows, policies.MoreRecords);
    }

    /// <summary>What a copy-forward creates (task 081): the creates, in order, and how many rules they copy.</summary>
    internal sealed record CopyForwardPlan(IReadOnlyList<OrganizationRequest> Creates, int Copied, bool Truncated, IReadOnlyList<RuleSetError> Errors);

    /// <summary>
    /// Copy-forward (task 081, model 7.2 "v2 copies v1", O-12): for every enabled Billing policy of the set with a version PUBLISHED under
    /// <paramref name="fromProcedureVersionId"/>, a Draft copy of the latest such version under <paramref name="toProcedureVersionId"/> — the SAME
    /// policy, the policy's next number, its deterministic id (<see cref="VersionIdFor"/>), Accepted (it is v(n)'s content, already accepted;
    /// an edit sends it back to Proposed as any edit does), owned by <paramref name="owner"/> with the caller as creator person. Nothing is
    /// written here: the caller places the creates in the same transaction as the new Procedure Version. A policy with no published version
    /// under the source is not copied, and a system rule (D-125) never is. A rule the new version then rejects is "dropped": it stays enabled
    /// with no version published under the new Procedure Version (model 7.2).
    /// </summary>
    internal async Task<CopyForwardPlan> PlanCopyForwardAsync(
        Guid ruleSetId, Guid fromProcedureVersionId, Guid toProcedureVersionId, RecordOwnerResolution owner, CancellationToken ct)
    {
        // Every ACTIVE version of every active policy (fix round 4: LoadRulesAsync names statecode 0); the next number is per policy. Only a
        // rule with a published version is copied, and a redraft never deactivates one (it deactivates whole rules that were never published).
        var loaded = await LoadRulesAsync(ruleSetId, ct);
        if (loaded.Truncated)
        {
            return new CopyForwardPlan(Array.Empty<OrganizationRequest>(), 0, true, Array.Empty<RuleSetError>());
        }

        var creates = new List<OrganizationRequest>();
        var errors = new List<RuleSetError>();
        foreach (var rule in loaded.Rules)
        {
            if (rule.Policy.Enabled == false || rule.Versions.Any(v => v.RuleType == (int)RuleType.SubmissionIntegrity))
            {
                continue;
            }

            var source = rule.Versions
                .Where(v => v.ProcedureVersionId == fromProcedureVersionId && v.IsPublishedUnderProcedure)
                .MaxBy(v => v.Number);
            if (source is null)
            {
                continue;
            }

            if (PolicyVersionValidator.ValidateProcedureVersionLink(rule.Policy.Domain, toProcedureVersionId) is { IsValid: false } link)
            {
                errors.Add(new RuleSetError(rule.Policy.Id, rule.Policy.Name, ProcedureVersionColumn, link.Errors[0], link.Reason));
                continue;
            }

            var number = rule.Versions.Max(v => v.Number) + 1;
            var copy = new Entity(VersionEntity, VersionIdFor(rule.Policy.Id, number))
            {
                ["sprk_name"] = $"{rule.Policy.Code} v{number.ToString(CultureInfo.InvariantCulture)}",
                ["sprk_policy"] = new EntityReference(PolicyEntity, rule.Policy.Id),
                ["sprk_versionnumber"] = number,
                ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Accepted),
                [ProcedureVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, toProcedureVersionId),
                [PublishStateColumn] = new OptionSetValue(PublishStateDraft),
                // Required column; provisional until the Procedure Version publishes and sets the effective date.
                ["sprk_inforcefrom"] = DateTime.UtcNow,
            };
            if (source.RuleType is { } type) copy["sprk_ruletype"] = new OptionSetValue(type);
            if (source.RuleBody is not null) copy["sprk_rulebody"] = source.RuleBody;
            if (source.Template is not null) copy["sprk_messagetemplate"] = source.Template;
            if (source.Headline is not null) copy["sprk_shortheadline"] = source.Headline;
            if (source.Consequence is { } consequence)
            {
                copy["sprk_consequence"] = new OptionSetValue(consequence);
                copy["sprk_proposedaction"] = ProposedActionText(consequence);
            }

            if (source.ClauseReference is not null) copy["sprk_clausereference"] = source.ClauseReference;
            if (source.ClauseLocation is not null) copy["sprk_clauselocation"] = source.ClauseLocation;
            owner.ApplyTo(copy);
            creates.Add(new CreateRequest { Target = copy });
        }

        return new CopyForwardPlan(creates, creates.Count, false, errors);
    }

    /// <summary>The rule-set side of a Procedure Version publish (task 081): the conditional updates, what they put in force, or the errors.</summary>
    internal sealed record ProcedureRulesPublishPlan(
        IReadOnlyList<OrganizationRequest> Updates, IReadOnlyList<PublishedRuleVersion> Published, int Rejected,
        IReadOnlyList<RuleSetError> Errors, bool Truncated);

    /// <summary>
    /// Plans the publish of a Procedure Version's rules (task 081, model 7.2): every Accepted Draft policy version under
    /// <paramref name="procedureVersionId"/> is marked Published with <c>sprk_inforcefrom</c> = <paramref name="effectiveFrom"/>, and its policy's
    /// <c>sprk_currentversion</c> ("latest published", display only) moves to it with <c>sprk_enabled</c> = Yes. Rejected versions stay Draft (the
    /// version dropped them); a Proposed one refuses the whole publish (every rule is decided before a version goes into force). Draft policy
    /// versions under OTHER Procedure Versions are never touched (the rows are loaded for this one only). The set is marked Published.
    /// Every candidate is validated first (<see cref="PolicyVersionValidator.ValidateForSave"/>, Billing, consequence; and
    /// <see cref="PolicyVersionValidator.ValidateProcedureVersionLink"/>); one failure plans nothing. Version and policy updates are
    /// conditional on the row versions read. Nothing is written here: the caller sends the updates in ONE transaction with the Procedure
    /// Version's own writes, so the rules and the version go into force together or not at all (I-P1). A version already marked Published
    /// (a retry) is not planned again.
    /// </summary>
    internal async Task<ProcedureRulesPublishPlan> PlanProcedureVersionPublishAsync(
        Guid ruleSetId, Guid procedureVersionId, DateOnly effectiveFrom, CancellationToken ct)
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return new ProcedureRulesPublishPlan(Array.Empty<OrganizationRequest>(), Array.Empty<PublishedRuleVersion>(), 0,
                new[] { new RuleSetError(null, null, "ruleSet", "The Procedure's rule set was not found.", null) }, false);
        }

        var loaded = await LoadRulesAsync(ruleSetId, ct, procedureVersionId);
        if (loaded.Truncated)
        {
            return new ProcedureRulesPublishPlan(Array.Empty<OrganizationRequest>(), Array.Empty<PublishedRuleVersion>(), 0, Array.Empty<RuleSetError>(), true);
        }

        var errors = new List<RuleSetError>();
        var candidates = new List<RuleSetRule>();
        var rejected = 0;
        foreach (var rule in loaded.Rules)
        {
            var work = rule.Working;
            if (!work.IsDraft(rule.Policy))
            {
                continue; // already Published under this version (a retry of a publish whose last write landed)
            }

            switch (work.StateOrProposed)
            {
                case ProposalState.Accepted:
                    candidates.Add(rule);
                    errors.AddRange(ValidateRule(rule.Policy, work.RuleType?.ToString(CultureInfo.InvariantCulture), work));
                    if (PolicyVersionValidator.ValidateProcedureVersionLink(rule.Policy.Domain, procedureVersionId) is { IsValid: false } link)
                    {
                        errors.Add(new RuleSetError(rule.Policy.Id, rule.Policy.Name, ProcedureVersionColumn, link.Errors[0], link.Reason));
                    }

                    break;
                case ProposalState.Rejected:
                    rejected++;
                    break;
                default:
                    errors.Add(new RuleSetError(rule.Policy.Id, rule.Policy.Name, "state",
                        "This rule is still Proposed. Accept or reject every rule of the version before publishing it.", null));
                    break;
            }
        }

        if (errors.Count > 0)
        {
            return new ProcedureRulesPublishPlan(Array.Empty<OrganizationRequest>(), Array.Empty<PublishedRuleVersion>(), rejected, errors, false);
        }

        var effectiveStart = ToUtcDate(effectiveFrom);
        var updates = new List<OrganizationRequest>();
        try
        {
            foreach (var rule in candidates)
            {
                var version = new Entity(VersionEntity, rule.Working.Id)
                {
                    [PublishStateColumn] = new OptionSetValue(PublishStatePublished),
                    ["sprk_inforcefrom"] = effectiveStart,
                    RowVersion = RequireRowVersion(rule.Working.RowVersion, VersionEntity, rule.Working.Id).ToString(CultureInfo.InvariantCulture),
                };
                updates.Add(new UpdateRequest { Target = version, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });

                var policy = new Entity(PolicyEntity, rule.Policy.Id)
                {
                    ["sprk_currentversion"] = new EntityReference(VersionEntity, rule.Working.Id),
                    ["sprk_enabled"] = true,
                    RowVersion = RequireRowVersion(rule.Policy.RowVersion, PolicyEntity, rule.Policy.Id).ToString(CultureInfo.InvariantCulture),
                };
                updates.Add(new UpdateRequest { Target = policy, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Procedure Version {ProcedureVersionId} publish stopped before any write: a row was read without its row version", procedureVersionId);
            return new ProcedureRulesPublishPlan(Array.Empty<OrganizationRequest>(), Array.Empty<PublishedRuleVersion>(), rejected,
                new[] { new RuleSetError(null, null, "rules", "A rule could not be read completely. Reload and publish again.", null) }, false);
        }

        // The set: Published (the legacy readers, e.g. the D-79 firm clause, require it), with its first effective date.
        var setRow = new Entity(RuleSetEntity, ruleSetId)
        {
            ["statecode"] = new OptionSetValue(0),
            ["statuscode"] = new OptionSetValue(RuleSetPublished),
        };
        if (set.GetAttributeValue<DateTime?>("sprk_effectivefrom") is null)
        {
            setRow["sprk_effectivefrom"] = effectiveStart;
        }

        updates.Add(new UpdateRequest { Target = setRow });
        return new ProcedureRulesPublishPlan(
            updates,
            candidates.Select(c => new PublishedRuleVersion(c.Policy.Id, c.Working.Id, c.Working.Number, c.Predecessor?.Id)).ToList(),
            rejected, Array.Empty<RuleSetError>(), false);
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Firm-facing clause text (130 / 134 / 135)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ebilling-r1 task 083 (model 3.2, 7.4, 8.1; D-66, D-79): what the invoice's law firm may be shown of one rule version's clause.
    /// <b>A firm sees a clause only for a policy version its invoice's evaluation snapshot applied</b>: the snapshot is the
    /// <c>procedureSnapshot</c> member of the invoice's own submission findings, read app-only; it is never re-resolved, so a Procedure
    /// published, changed or retired after the invoice was judged does not change the answer for that invoice. There is no scope switch and no
    /// "in force" rule here: the snapshot already says which versions judged the invoice, for this invoice's firm.
    /// <para>Fails closed, with no throw: no text when the invoice or its firm cannot be read, when the invoice has no snapshot (a row judged
    /// before the snapshot existed, a snapshot a patch dropped, an unknown snapshot version, a damaged one), when the snapshot does not list
    /// the version as applied (displaced, excluded or absent), when the version, its billing policy or its rule set cannot be read, when the rule
    /// set is not Published or Retired, when a Procedure rule set's Procedure cannot be named, when the rule set's visible-to-firms flag is off
    /// (the last step, <see cref="RuleSetClauseVisibility.ForFirm"/>), and on any read fault.</para>
    /// <para>The result names the clause with <see cref="FirmClauseText.FirmFacingName"/> only: the Procedure's firm-facing name (its name when
    /// that is blank), or the neutral record label for a record rule. Never the Procedure's name when a firm-facing name is set, an id or an
    /// override reason; nothing here logs one.</para>
    /// </summary>
    /// <param name="policyVersionId">The rule version the finding was evaluated against.</param>
    /// <param name="invoiceId">
    /// <b>Caller contract (130 / 131 / 133 / 134 / 135).</b> The invoice the finding belongs to, passed only after the route's own
    /// firm-membership check has established that the caller may see that invoice, and read server-side. Never a value from the request (body,
    /// query or route) and never derived from the SPA identity's firm: a CIAM caller may belong to several firms (130), and internal callers
    /// (131, 133) have no SPA identity at all. The invoice's firm (<c>sprk_vendororg</c>) is read here; <see cref="Guid.Empty"/> or an invoice
    /// without a firm returns no text.
    /// </param>
    public virtual async Task<FirmClauseText> GetFirmClauseAsync(Guid policyVersionId, Guid invoiceId, CancellationToken ct)
    {
        if (policyVersionId == Guid.Empty || invoiceId == Guid.Empty)
        {
            return FirmClauseText.None;
        }

        try
        {
            return await ReadFirmClauseAsync(policyVersionId, invoiceId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Information and above carry the exception type only (a Dataverse fault message can name ids and records), plus the invoice and
            // policy version ids as structured parameters so a fault can be traced to its read. No other id, and never the exception itself.
            _logger.LogWarning(
                "A firm clause read faulted ({ExceptionType}) for invoice {InvoiceId}, policy version {PolicyVersionId}; no clause is shown",
                ex.GetType().Name, invoiceId, policyVersionId);
            _logger.LogDebug(ex, "Firm clause read fault detail");
            return FirmClauseText.None;
        }
    }

    private async Task<FirmClauseText> ReadFirmClauseAsync(Guid policyVersionId, Guid invoiceId, CancellationToken ct)
    {
        // (1) The invoice and its firm. No firm, no text.
        var invoiceQuery = new QueryExpression(FinanceBaseValues.InvoiceEntity)
        {
            ColumnSet = new ColumnSet(InvoiceContextService.InvoiceFirm),
            Criteria = { Conditions = { new ConditionExpression("sprk_invoiceid", ConditionOperator.Equal, invoiceId) } },
            TopCount = 1,
        };
        var invoice = (await _entities.RetrieveMultipleAsync(invoiceQuery, ct)).Entities.FirstOrDefault(r => r.Id == invoiceId);
        if (invoice?.GetAttributeValue<EntityReference>(InvoiceContextService.InvoiceFirm)?.Id is not { } firmId || firmId == Guid.Empty)
        {
            return FirmClauseText.None;
        }

        // (2) The invoice's evaluation snapshot (app-only; the invoice's own submission), and the version must be one it applied.
        // A missing, null, unknown-version or damaged snapshot reads as no snapshot. Nothing is re-resolved.
        var submission = await InvoiceBlockedHold.ReadOwnSubmissionAsync(_entities, invoiceId, [InvoiceSubmissionPersister.SubmissionFindingsJson], ct);
        var snapshot = ProcedureSnapshot.Read(submission?.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson));
        if (snapshot is null || !snapshot.AppliedPolicyVersionIds.Contains(policyVersionId))
        {
            return FirmClauseText.None;
        }

        // (3) The version, its Billing policy and its rule set.
        var versionQuery = new QueryExpression(VersionEntity)
        {
            ColumnSet = new ColumnSet("sprk_policy", "sprk_clausereference", "sprk_clauselocation"),
            Criteria = { Conditions = { new ConditionExpression("sprk_policyversionid", ConditionOperator.Equal, policyVersionId) } },
            TopCount = 1,
        };
        var version = (await _entities.RetrieveMultipleAsync(versionQuery, ct)).Entities.FirstOrDefault(r => r.Id == policyVersionId);
        if (version?.GetAttributeValue<EntityReference>("sprk_policy")?.Id is not { } policyId)
        {
            return FirmClauseText.None;
        }

        var policyQuery = PolicyQuery("sprk_policyid", ConditionOperator.Equal, new object[] { policyId });
        var policy = (await _entities.RetrieveMultipleAsync(policyQuery, ct)).Entities.FirstOrDefault(RuleDomains.IsBillingRow);
        if (policy?.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id is not { } ruleSetId)
        {
            return FirmClauseText.None;
        }

        // A record rule's set is record-scoped (the other readers of a set refuse it); a Published set holds a version that judged, and a Retired
        // one still holds the versions that judged earlier invoices. A draft or any other state holds none.
        var set = await LoadBillingRuleSetAsync(ruleSetId, ct);
        if (set is null || set.GetAttributeValue<OptionSetValue>("statuscode")?.Value is not (RuleSetPublished or RuleSetRetired))
        {
            return FirmClauseText.None;
        }

        // (4) The label: neutral for a record rule, else the Procedure's firm-facing name; a legacy set has no Procedure and no label.
        string? label;
        if (RecordRuleSet.IsRecordScoped(set))
        {
            label = RecordRuleSet.Name;
        }
        else if (set.GetAttributeValue<EntityReference>(ProcedureColumn)?.Id is { } procedureId)
        {
            label = await FirmFacingNameOfAsync(procedureId, ct);
            if (label is null)
            {
                return FirmClauseText.None;
            }
        }
        else
        {
            label = null;
        }

        // (5) The visible-to-firms flag decides last.
        return RuleSetClauseVisibility.ForFirm(
            set.GetAttributeValue<bool>("sprk_visibletofirms"),
            version.GetAttributeValue<string>("sprk_clausereference"),
            version.GetAttributeValue<string>("sprk_clauselocation"),
            label);
    }

    /// <summary>The Procedure's firm-facing name, or its name when that is blank; null when the Procedure cannot be read or has neither.</summary>
    private async Task<string?> FirmFacingNameOfAsync(Guid procedureId, CancellationToken ct)
    {
        var query = new QueryExpression(ProcedureEntity.Entity)
        {
            ColumnSet = new ColumnSet(ProcedureEntity.FirmFacingNameColumn, ProcedureEntity.NameColumn),
            Criteria = { Conditions = { new ConditionExpression("sprk_procedureid", ConditionOperator.Equal, procedureId) } },
            TopCount = 1,
        };
        var procedure = (await _entities.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault(r => r.Id == procedureId);
        if (procedure is null)
        {
            return null;
        }

        var firmFacing = procedure.GetAttributeValue<string>(ProcedureEntity.FirmFacingNameColumn);
        var name = string.IsNullOrWhiteSpace(firmFacing) ? procedure.GetAttributeValue<string>(ProcedureEntity.NameColumn) : firmFacing;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reading the rules of one rule set
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <param name="RowVersion">The row's <c>versionnumber</c> as read; the publish's pointer move is conditional on it.</param>
    /// <param name="Domain">Task 081: the row's <c>sprk_domain</c> as read (the Procedure Version link is checked against it).</param>
    /// <param name="Enabled">Task 081: the row's <c>sprk_enabled</c> as read (null when not read).</param>
    internal sealed record PolicyRow(Guid Id, string Code, string Name, Guid? CurrentVersionId, long? RowVersion = null, int? Domain = null, bool? Enabled = null);

    /// <param name="RowVersion">The row's <c>versionnumber</c> as read; the publish and decide writes are conditional on it.</param>
    internal sealed record VersionRow(
        Guid Id, int Number, int? RuleType, string? RuleBody, string? Template, string? Headline, int? Consequence, int? State,
        string? ClauseReference, string? ClauseLocation, DateTime? InForceFrom, DateTime? InForceTo, long? RowVersion = null,
        string? RejectionReason = null, Guid? ProcedureVersionId = null, int? PublishState = null)
    {
        public ProposalState StateOrProposed =>
            State is { } s && Enum.IsDefined(typeof(ProposalState), s) ? (ProposalState)s : ProposalState.Proposed;

        /// <summary>
        /// A draft: only a draft is ever updated in place. Task 081 (model 7.2): a version keyed to a Procedure Version is a draft until it
        /// is marked Published (<c>sprk_publishstate</c>), whatever the pointer says, because <c>sprk_currentversion</c> is display only for it;
        /// a legacy version (no Procedure Version) is a draft when it is not current and never ended, as before.
        /// </summary>
        public bool IsDraft(PolicyRow policy) => ProcedureVersionId is not null
            ? PublishState != PublishStatePublished
            : policy.CurrentVersionId != Id && InForceTo is null;

        /// <summary>Task 081: a version marked Published under a Procedure Version.</summary>
        public bool IsPublishedUnderProcedure => ProcedureVersionId is not null && PublishState == PublishStatePublished;
    }

    /// <summary>One rule: its policy, every version, the working (highest-numbered) version, and the version it would replace.</summary>
    /// <remarks>Task 081: when the rule was loaded for one Procedure Version (<see cref="LoadRulesAsync"/> with a version id), every row in
    /// <see cref="Versions"/> belongs to that Procedure Version, so the working version and the predecessor are per (policy, Procedure Version).</remarks>
    internal sealed record RuleSetRule(PolicyRow Policy, IReadOnlyList<VersionRow> Versions, VersionRow Working)
    {
        /// <summary>
        /// Legacy: the policy's current version when it is not the working one (what a publish of the working version stamps ended).
        /// Task 081 (model 7.2): for a version keyed to a Procedure Version, the latest PUBLISHED version under the same Procedure Version (the
        /// version a correction follows); nothing is stamped ended under the re-keyed chain, the Procedure Version's window ends it.
        /// </summary>
        public VersionRow? Predecessor => Working.ProcedureVersionId is { } procedureVersion
            ? Versions.Where(v => v.Id != Working.Id && v.ProcedureVersionId == procedureVersion && v.IsPublishedUnderProcedure).MaxBy(v => v.Number)
            : Policy.CurrentVersionId is { } current && current != Working.Id ? Versions.FirstOrDefault(v => v.Id == current) : null;
    }

    /// <summary>
    /// Task 116 fix round 5 (EB-D-216a): the id of <paramref name="procedureId"/>'s billing rule set (the one <see cref="GetDraftSourceAsync"/>
    /// drafts into), or null when it has none. Read app-only, so the drafting route can ask the caller's rights on the set BEFORE it reads
    /// anything about the version's drafts. A read fault throws (the route denies).
    /// </summary>
    /// <remarks>§11: <i>Existing</i> — <see cref="LoadProcedureRuleSetAsync"/>, which this forwards to. <i>Extension</i> — that method is not
    /// virtual (the route's tests substitute this one). <i>Cost of doing nothing</i> — the route would have to read the version's draft state
    /// first to learn the set, so a caller without the reject rights could learn it.</remarks>
    internal virtual async Task<Guid?> ProcedureRuleSetIdAsync(Guid procedureId, CancellationToken ct) =>
        (await LoadProcedureRuleSetAsync(procedureId, ct))?.Id;

    /// <summary>
    /// ebilling-r1 task 081 (uac-r2 review item D, procedure model I-P5): the Procedure a billing rule set belongs to
    /// (<c>sprk_ruleset.sprk_procedure</c>), or null for a legacy set or no such set. The proposal routes' authorization reads it (app-only)
    /// to require the caller's Write on that Procedure. A read fault throws (the caller denies).
    /// </summary>
    internal virtual async Task<Guid?> ProcedureOfRuleSetAsync(Guid ruleSetId, CancellationToken ct) =>
        (await LoadRuleSetAsync(ruleSetId, ct))?.GetAttributeValue<EntityReference>(ProcedureColumn)?.Id;

    /// <summary>
    /// ebilling-r1 task 089e: the record a record-scoped billing rule set is filed under, read app-only for the by-id routes' authorization
    /// (<c>RuleSetEndpoints.RequireAccessToTheRuleSet</c>), which asks it only AFTER the caller's own Read on the set. Null when there is no
    /// billing set at this id or the set is not record-scoped (the route then continues as before). A record-scoped set with no single core
    /// lookup has a null <see cref="RecordRuleSetHome.CoreTable"/>, which the filter denies. A read fault throws (the filter denies).
    /// </summary>
    internal virtual async Task<RecordRuleSetHome?> RecordHomeOfRuleSetAsync(Guid ruleSetId, CancellationToken ct) =>
        await LoadBillingRuleSetAsync(ruleSetId, ct) is { } set ? RecordRuleSet.HomeOf(set) : null;

    /// <summary>The uniform not found of every by-id route here (the same bytes for a missing set and, since 089e, an unreadable one).</summary>
    internal static RuleSetOutcome<T> RuleSetNotFound<T>() where T : class =>
        RuleSetOutcome<T>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found.");

    /// <summary>
    /// The one read of a rule set by id, under every route and plan here. Task 089c (089a K1): a record-scoped set reads as no set. It is
    /// its record's secure child, changed only through the record's override routes (<c>ProcedureAssignmentService</c>), so every mutation
    /// and read here answers it as the uniform not found. Task 089e: on the by-id routes the route filter answers such a set first (the
    /// uniform 404 unless the caller can Read the set and its record, then 409 <c>ruleset.record_scoped</c>); this stays the backstop for
    /// every other caller (plans, the firm clause read).
    /// </summary>
    private async Task<Entity?> LoadRuleSetAsync(Guid ruleSetId, CancellationToken ct) =>
        await LoadBillingRuleSetAsync(ruleSetId, ct) is { } set && !RecordRuleSet.IsRecordScoped(set) ? set : null;

    /// <summary>The billing rule set at this id, record-scoped or not, or null.</summary>
    private async Task<Entity?> LoadBillingRuleSetAsync(Guid ruleSetId, CancellationToken ct)
    {
        if (ruleSetId == Guid.Empty)
        {
            return null;
        }

        var query = new QueryExpression(RuleSetEntity)
        {
            ColumnSet = new ColumnSet(RuleSetColumns),
            Criteria = { Conditions = { new ConditionExpression("sprk_rulesetid", ConditionOperator.Equal, ruleSetId) } },
            TopCount = 1,
        };
        RuleDomains.AddBillingFilter(query);
        var rows = await _entities.RetrieveMultipleAsync(query, ct);
        return rows.Entities.FirstOrDefault(r => r.Id == ruleSetId && RuleDomains.IsBillingRow(r));
    }

    private static QueryExpression PolicyQuery(string column, ConditionOperator op, object[] values)
    {
        var query = new QueryExpression(PolicyEntity) { ColumnSet = new ColumnSet(PolicyColumns) };
        query.Criteria.AddCondition(column, op, values);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        RuleDomains.AddBillingFilter(query);
        return query;
    }

    /// <param name="procedureVersionId">Task 081: when given, only the policy versions keyed to this Procedure Version are loaded, so each rule
    /// is the (policy, Procedure Version) chain: its working version, draft state and predecessor are that chain's. A policy with no version
    /// under it is not a rule of that Procedure Version (a rule the version dropped).</param>
    private async Task<(List<RuleSetRule> Rules, bool Truncated)> LoadRulesAsync(Guid ruleSetId, CancellationToken ct, Guid? procedureVersionId = null)
    {
        var policies = await _entities.RetrieveMultipleAsync(PolicyQuery("sprk_ruleset", ConditionOperator.Equal, new object[] { ruleSetId }), ct);
        if (policies.MoreRecords)
        {
            return (new List<RuleSetRule>(), true);
        }

        var policyRows = policies.Entities
            .Where(p => RuleDomains.IsBillingRow(p) && p.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id == ruleSetId)
            .Select(p => new PolicyRow(
                p.Id,
                p.GetAttributeValue<string>("sprk_policycode") ?? p.Id.ToString(),
                p.GetAttributeValue<string>("sprk_name") ?? p.GetAttributeValue<string>("sprk_policycode") ?? p.Id.ToString(),
                p.GetAttributeValue<EntityReference>("sprk_currentversion")?.Id,
                p.GetAttributeValue<long?>(RowVersionColumn),
                p.GetAttributeValue<OptionSetValue>(RuleDomains.PolicyDomainColumn)?.Value,
                p.GetAttributeValue<bool?>("sprk_enabled")))
            .ToList();

        var versionsByPolicy = new Dictionary<Guid, List<VersionRow>>();
        foreach (var batch in policyRows.Select(p => (object)p.Id).Chunk(MaxInValues))
        {
            var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // fix round 4 (EB-D-216): a deactivated draft is not read
            if (procedureVersionId is { } procedureVersion)
            {
                query.Criteria.AddCondition(ProcedureVersionColumn, ConditionOperator.Equal, procedureVersion);
            }

            // A stable order, so the working version (the highest number, MaxBy below) is the same on every read even if two rows ever
            // share a number (rows created before edits had deterministic ids): the earliest created of them, then the lowest id.
            query.Orders.Add(new OrderExpression("sprk_versionnumber", OrderType.Ascending));
            query.Orders.Add(new OrderExpression("createdon", OrderType.Ascending));
            query.Orders.Add(new OrderExpression("sprk_policyversionid", OrderType.Ascending));
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            if (versions.MoreRecords)
            {
                return (new List<RuleSetRule>(), true);
            }

            foreach (var v in versions.Entities)
            {
                if (v.GetAttributeValue<EntityReference>("sprk_policy")?.Id is { } pid)
                {
                    if (!versionsByPolicy.TryGetValue(pid, out var list))
                    {
                        versionsByPolicy[pid] = list = new List<VersionRow>();
                    }

                    list.Add(ToVersion(v));
                }
            }
        }

        var rules = new List<RuleSetRule>();
        foreach (var policy in policyRows)
        {
            if (versionsByPolicy.TryGetValue(policy.Id, out var versions) && versions.Count > 0)
            {
                rules.Add(new RuleSetRule(policy, versions, versions.MaxBy(v => v.Number)!));
            }
        }

        return (rules, false);
    }

    private async Task<(RuleSetRule? Rule, RuleSetOutcome<T>? Refusal)> FindRuleAsync<T>(Guid ruleSetId, Guid policyId, CancellationToken ct)
        where T : class
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return (null, RuleSetOutcome<T>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found."));
        }

        if (set.GetAttributeValue<OptionSetValue>("statuscode")?.Value == RuleSetRetired)
        {
            return (null, RuleSetOutcome<T>.Refused(StatusCodes.Status409Conflict, CodeRetired, "A retired rule set cannot be changed."));
        }

        // Task 081 (model 7.2): a Procedure's rules are edited and decided on the Procedure's Draft version, so the rule is the (policy, Draft
        // version) chain. A published version's rules are never changed here (a correction under a published version is a later task).
        Guid? procedureVersionId = null;
        if (set.GetAttributeValue<EntityReference>(ProcedureColumn)?.Id is { } procedureId)
        {
            procedureVersionId = await FindDraftProcedureVersionAsync(procedureId, ct);
            if (procedureVersionId is null)
            {
                return (null, RuleSetOutcome<T>.Refused(
                    StatusCodes.Status409Conflict, CodeNoDraftVersion,
                    "This Procedure has no Draft version. Create a new version of the Procedure to change its rules."));
            }
        }

        var loaded = await LoadRulesAsync(ruleSetId, ct, procedureVersionId);
        if (loaded.Truncated)
        {
            return (null, RuleSetOutcome<T>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "The rule set has more rules than can be read in one pass."));
        }

        var rule = loaded.Rules.FirstOrDefault(r => r.Policy.Id == policyId);
        if (rule is not null && rule.Versions.Any(v => v.RuleType == (int)RuleType.SubmissionIntegrity))
        {
            // D-125: the system policy is never edited, accepted, rejected or given a new version here (an administrator may only turn it off).
            return (null, RuleSetOutcome<T>.Refused(StatusCodes.Status409Conflict, CodeSystemRule, "This is a system rule; it cannot be changed here."));
        }

        if (rule is null && await IsDeactivatedRuleOfAsync(ruleSetId, policyId, ct))
        {
            // Fix round 4 (EB-D-216): a draft a redraft deactivated is never edited, accepted, rejected or published; asked for by id it is 409.
            return (null, RuleSetOutcome<T>.Refused(
                StatusCodes.Status409Conflict, CodeDraftInactive, "This draft was replaced by a later drafting run and can no longer be changed."));
        }

        return rule is null
            ? (null, RuleSetOutcome<T>.Refused(StatusCodes.Status404NotFound, CodeProposalNotFound, "The rule was not found in this rule set."))
            : (rule, null);
    }

    /// <summary>Fix round 4: whether <paramref name="policyId"/> is a Billing policy of this set that is Inactive (by id: no statecode filter).</summary>
    private async Task<bool> IsDeactivatedRuleOfAsync(Guid ruleSetId, Guid policyId, CancellationToken ct)
    {
        var query = new QueryExpression(PolicyEntity) { ColumnSet = new ColumnSet("sprk_ruleset", "statecode", RuleDomains.PolicyDomainColumn), TopCount = 1 };
        query.Criteria.AddCondition("sprk_policyid", ConditionOperator.Equal, policyId);
        var row = (await _entities.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault(p => p.Id == policyId);
        return row is not null && RuleDomains.IsBillingRow(row) && !IsActive(row)
            && row.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id == ruleSetId;
    }

    private static VersionRow ToVersion(Entity v) => new(
        v.Id,
        v.GetAttributeValue<int?>("sprk_versionnumber") ?? 0,
        v.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value,
        v.GetAttributeValue<string>("sprk_rulebody"),
        v.GetAttributeValue<string>("sprk_messagetemplate"),
        v.GetAttributeValue<string>("sprk_shortheadline"),
        v.GetAttributeValue<OptionSetValue>("sprk_consequence")?.Value,
        v.GetAttributeValue<OptionSetValue>("sprk_proposalstatus")?.Value,
        v.GetAttributeValue<string>("sprk_clausereference"),
        v.GetAttributeValue<string>("sprk_clauselocation"),
        v.GetAttributeValue<DateTime?>("sprk_inforcefrom"),
        v.GetAttributeValue<DateTime?>("sprk_inforceto"),
        v.GetAttributeValue<long?>(RowVersionColumn),
        v.GetAttributeValue<string>(RejectionReasonColumn),
        v.GetAttributeValue<EntityReference>(ProcedureVersionColumn)?.Id,
        v.GetAttributeValue<OptionSetValue>(PublishStateColumn)?.Value);

    private static RuleSetSummary Summarize(Entity set, int ruleCount)
    {
        var status = set.GetAttributeValue<OptionSetValue>("statuscode")?.Value switch
        {
            RuleSetPublished => "Published",
            RuleSetRetired => "Retired",
            _ => "Draft",
        };
        var scope = set.GetAttributeValue<OptionSetValue>("sprk_scope")?.Value;
        return new RuleSetSummary(
            set.Id,
            set.GetAttributeValue<string>("sprk_name") ?? string.Empty,
            status,
            scope == (int)RuleSetScope.Firm ? nameof(RuleSetScope.Firm) : scope == (int)RuleSetScope.ClientDefault ? nameof(RuleSetScope.ClientDefault) : "Unassigned",
            set.GetAttributeValue<EntityReference>("sprk_firm")?.Id,
            set.GetAttributeValue<bool>("sprk_visibletofirms"),
            set.GetAttributeValue<DateTime?>("sprk_effectivefrom") is { } from ? DateOnly.FromDateTime(from) : null,
            set.GetAttributeValue<DateTime?>("sprk_effectiveto") is { } to ? DateOnly.FromDateTime(to) : null,
            ruleCount)
        {
            ProcedureId = set.GetAttributeValue<EntityReference>(ProcedureColumn)?.Id,
        };
    }

    /// <param name="carryRowVersion">
    /// True only for a read (<see cref="GetAsync"/>): the token then belongs to the content in the view. A view built after a write carries
    /// none, because the row's token after the write was not read back and a stale one would let the next decision pass the check.
    /// </param>
    private async Task<ProposalView> ViewAsync(RuleSetRule rule, CancellationToken ct, bool carryRowVersion = false)
    {
        var work = rule.Working;
        var validation = ValidateRule(rule.Policy, work.RuleType?.ToString(CultureInfo.InvariantCulture), work);

        string? text = null, formal = null, refusal = null;
        var description = await _describer.DescribeAsync(work.RuleBody, ct);
        if (description.Description is { Clauses.Count: > 0 } d)
        {
            text = d.Clauses[0].Text;
            formal = d.Clauses[0].Formal;
        }
        else
        {
            refusal = description.Refusal;
        }

        JsonNode? body = null;
        if (work.RuleBody is { Length: > 0 } json)
        {
            try
            {
                body = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                body = null;
            }
        }

        var ruleType = work.RuleType is { } rt && Enum.IsDefined(typeof(RuleType), rt) ? ((RuleType)rt).ToString() : null;
        return new ProposalView(
            rule.Policy.Id, rule.Policy.Code, rule.Policy.Name, work.StateOrProposed.ToString(), rule.Policy.CurrentVersionId == work.Id,
            rule.Policy.CurrentVersionId is not null || rule.Versions.Any(v => v.InForceTo is not null),
            work.Id, work.Number, ruleType, body, work.Template, work.Headline,
            work.Consequence is { } c && Enum.IsDefined(typeof(BillingConsequence), c) ? ((BillingConsequence)c).ToString() : null,
            text, formal, refusal, work.ClauseReference, work.ClauseLocation,
            validation.Select(e => $"{e.Field}: {e.Message}").ToList(),
            carryRowVersion ? work.RowVersion?.ToString(CultureInfo.InvariantCulture) : null,
            work.StateOrProposed == ProposalState.Rejected ? work.RejectionReason : null);
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Validation
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The one validation of a billing rule version, for accept, edit and publish: <see cref="PolicyVersionValidator.ValidateForSave"/>
    /// with <see cref="RuleDomain.Billing"/> (never <c>ValidateForAuthoring</c>, which is the Ontology admin's), then the required
    /// columns (consequence, headline, template). Task 073 (FR-26, I-5, D-89): the validator also gets the consequence, so a rule whose
    /// type may not carry it (Reject on an AiObservation rule, say) is refused on every one of these paths, publish included.
    /// </summary>
    /// <remarks>Internal for task 089c: a record rule (<c>ProcedureAssignmentService.ChangeRuleAsync</c>) is validated by this same method.</remarks>
    internal List<RuleSetError> ValidateRule(PolicyRow policy, string? ruleTypeRaw, VersionRow version)
    {
        var errors = new List<RuleSetError>();
        void Add(string field, string message, string? reason = null) =>
            errors.Add(new RuleSetError(policy.Id, policy.Name, field, message, reason));

        // A missing or unknown value is reported once below; only a defined one goes to the validator for the per-type check.
        int? definedConsequence = version.Consequence is { } stated && Enum.IsDefined(typeof(BillingConsequence), stated) ? stated : null;

        if (string.IsNullOrWhiteSpace(version.RuleBody))
        {
            Add("ruleBody", "The rule body is required.");
        }
        else
        {
            var result = _validator.ValidateForSave(ruleTypeRaw, version.RuleBody, version.Template, RuleDomain.Billing, definedConsequence);
            if (!result.IsValid)
            {
                var field = result.Reason switch
                {
                    OntologyWriterFailureReason.RuleTypeUnsupported or OntologyWriterFailureReason.RuleTypeWrongDomain => "ruleType",
                    OntologyWriterFailureReason.TemplateMalformed or OntologyWriterFailureReason.TemplateTokenOutsideReadSet => "messageTemplate",
                    OntologyWriterFailureReason.ConsequenceNotAllowed => "consequence",
                    _ => "ruleBody",
                };
                foreach (var message in result.Errors)
                {
                    Add(field, message, result.Reason);
                }
            }
        }

        if (version.Consequence is not { } c || !Enum.IsDefined(typeof(BillingConsequence), c))
        {
            Add("consequence", "The rule needs a consequence: Reject, Flag or SuggestAdjustment.");
        }

        if (string.IsNullOrWhiteSpace(version.Headline))
        {
            Add("shortHeadline", "The rule needs a short headline.");
        }

        if (string.IsNullOrWhiteSpace(version.Template))
        {
            Add("messageTemplate", "The rule needs a message template.");
        }

        return errors;
    }

    internal static bool TryParseConsequence(string? text, out BillingConsequence consequence)
    {
        consequence = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<BillingConsequence>())
        {
            if (string.Equals(text, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                consequence = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>The required free-text <c>sprk_proposedaction</c> of a billing version (019): a short code of the consequence. Not what the consequence is.</summary>
    internal static string ProposedActionText(int? consequence) => consequence switch
    {
        (int)BillingConsequence.Reject => "billing-reject",
        (int)BillingConsequence.Flag => "billing-flag",
        (int)BillingConsequence.SuggestAdjustment => "billing-suggest-adjustment",
        _ => "billing-rule",
    };

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ownership question for a HARD-DENY row this service or <see cref="Procedures.ProcedureService"/> writes — <c>sprk_ruleset</c>,
    /// <c>sprk_policy</c>, <c>sprk_policyversion</c> (EB-D-168 / EB-D-168a, UAC 194b R4b-2) — through the one builder,
    /// <see cref="RecordOwnershipContext.ForRow"/>, for <paramref name="table"/>. It names no parent: a billing rule set hangs off a Procedure,
    /// and a billing policy off its rule set, neither an ownership parent; no billing policy or version carries a matter (the census waiver's
    /// condition 1, <c>RuleSetServiceOwnerTests</c>). So the caller's business unit decides, and the resolver's ordinary exit turns that unit's
    /// default team into the customer's restricted-content owner team (a refusal, fail closed, for an answer in the root business unit or a
    /// team that cannot be used); a caller in the Secure Record business unit gets its named owner team.
    /// </summary>
    internal static RecordOwnershipContext ContentOwnerContext(string table, Guid? callerObjectId) =>
        RecordOwnershipContext.ForRow(table, Array.Empty<KeyValuePair<string, RecordOwnershipParent?>>(), callerObjectId: callerObjectId) with
        {
            RequestedBy = RecordRequester.Of(objectId: callerObjectId),
        };

    /// <summary>
    /// The ownership question for a row that is NOT hard-deny (a rule set's source document; a Procedure and its versions): the caller's own
    /// business-unit team (no parent named, I-6), as before 194b.
    /// </summary>
    internal static RecordOwnershipContext CallerOwnerContext(Guid? callerObjectId) => new()
    {
        CallerObjectId = callerObjectId,
        RequestedBy = RecordRequester.Of(objectId: callerObjectId),
    };

    private async Task<bool> ExistsAsync(string entity, string idColumn, Guid id, CancellationToken ct)
    {
        var result = await _entities.RetrieveMultipleAsync(new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet(idColumn),
            Criteria = { Conditions = { new ConditionExpression(idColumn, ConditionOperator.Equal, id) } },
            TopCount = 1,
        }, ct);
        return result.Entities.Count > 0;
    }

    private static DateTime ToUtcDate(DateOnly d) => new(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The media type of a Procedure source file, from its extension (task 116: the file route stores it server-derived, never the
    /// client's claim). Anything not listed is <c>application/octet-stream</c>.</summary>
    internal static string MimeTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };
}
