using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 130-0 fix round 3d (the closing sweep of the "absent lookup column" class): every read of a lookup column
/// (<c>_..._value</c>) in the external-access source is a REVIEWED site. A new one fails this guard until it is reviewed, so
/// the "absent is read as not filed" shape cannot return unseen.
/// </summary>
/// <remarks>
/// <para><b>The class.</b> A Web API (JSON) row lists a lookup it was asked for as an explicit <c>null</c> when it is not
/// filed. A column the row does not carry is ABSENT: it was never read, which says nothing about whether it is filed. A
/// reader that maps both to "no link" makes the <c>$select</c> the security boundary. The rule is: absent is
/// unreadable (the decision's own fail-closed value), explicit null is not filed. SDK / FetchXML rows omit a null
/// attribute, so there "absent is not filed" is correct only where the query provably projects the attribute (and that
/// projection is pinned by a test). Typed rows must record whether the column was read (see <c>DocumentRow</c>,
/// <c>TodoRow</c>, <c>EventRow</c>).</para>
/// <para><b>How the scan recognises a site</b> (<see cref="SourceScan"/>: line comments stripped; strings are skipped when
/// the enclosing member is worked out). In the files of <see cref="ScopeDirectories"/> and <see cref="ScopeFiles"/>:</para>
/// <list type="number">
/// <item>a call to <c>TryGetValue</c>, <c>TryGetProperty</c>, <c>ContainsKey</c>, <c>GuidValue</c>, <c>TryGuid</c> or
/// <c>GuidOf</c> with an argument that contains a <c>"_..._value"</c> literal (also <c>$"_{x}_value"</c>) or names a
/// <c>string</c> constant whose initialiser does (transitively, across the scanned files);</item>
/// <item>a <c>[JsonPropertyName(...)]</c> attribute whose argument is such a literal or constant (a typed row property);</item>
/// <item>an SDK read: <c>GetAttributeValue&lt;EntityReference|Guid|Guid?&gt;</c>, <c>Attributes.TryGetValue</c>,
/// <c>Attributes.Contains</c>, <c>.Attributes[</c>, <c>is EntityReference</c>, <c>case EntityReference</c>.</item>
/// </list>
/// <para>Sites are grouped by file and enclosing member (<c>repo/relative/path/File.cs::Type.Member</c>). The path is relative
/// to the repo root, so two files with the same name cannot collide. The member is the declared name of the method, property
/// or field the read sits in, found by matching braces and by the <c>=&gt;</c> or <c>=</c> that opens an expression body or an
/// initialiser (so a read in an expression-bodied member is keyed to that member, not to its type); a
/// <c>[JsonPropertyName]</c> attribute is keyed to its row type. Each group must be in
/// <see cref="ReviewedSites"/> with its exact count, a category and a one-line reason. An unreviewed group, a changed
/// count, a stale entry (the site is gone) and a changed number of <see cref="Category.KnownOpen"/> entries all fail, so the
/// list only moves by review (a ratchet).</para>
/// <para><b>Sanctioned fail-closed helpers are not sites.</b> A read through <c>ExternalMultiRootRule.LookupOf</c>,
/// <c>RootStamp</c>, <c>RootLinkOf</c> or <c>LinkOf</c> is the absent-is-unreadable form itself and needs no entry.</para>
/// <para><b>What it misses</b> (covered by unit and contract tests instead): a read whose column name reaches the call
/// through a variable, a parameter or a data table rather than a <c>_..._value</c> literal or constant (for example the
/// column arrays of <c>ExternalCallerCommunicationGuard</c> and the registry's lookup attribute lists, and the
/// <c>LookupValueAttribute</c>-style readers that take the column name as a parameter); a CHOICE column such as
/// <c>sprk_accesspermission</c> (it is not a <c>_..._value</c> lookup, so it is outside the guard; the select and projection
/// pins and the unit and route tests of fix round 3f cover it); a helper that
/// re-implements <c>LinkOf</c> under another name and returns null for an absent column (the helper's own body takes a
/// column parameter); a file outside the scope list; and whether a reviewed site STAYS correct once its code changes
/// inside a counted group. The guard fixes WHERE reads are, not what they do: the reason on each entry is the reviewer's
/// statement, and the behaviour is pinned by the tests named in the fix-round notes.</para>
/// <para>Enforced by: this guard (ArchTests, CI and the Stop hook). Ratchet baseline after fix round 3g: every reviewed group
/// is <see cref="Category.FailsClosed"/>, <see cref="Category.ProjectedAndPinned"/>, <see cref="Category.PositiveOnly"/> or
/// <see cref="Category.NotAnAccessDecision"/>, and the number of <see cref="Category.KnownOpen"/> entries is zero; that
/// count is pinned by <see cref="KnownOpenSites"/>, so a new site or a changed count fails until it is reviewed.</para>
/// </remarks>
public class ExternalLookupAbsentReadGuardTests
{
    private const string Bff = "src/server/api/Sprk.Bff.Api/";

    internal static readonly string[] ScopeDirectories =
    {
        Bff + "Infrastructure/ExternalAccess/",
        Bff + "Api/ExternalAccess/",
        Bff + "Services/ExternalAccess/",
    };

    internal static readonly string[] ScopeFiles =
    {
        Bff + "Services/Communication/CommunicationThreadReadService.cs",
        Bff + "Services/Ai/Handlers/Dataverse/ExternalCallerCommunicationGuard.cs",
        Bff + "Services/Communication/CommunicationOpenThreadGate.cs",
        Bff + "Api/Filters/CommunicationRecordAuthorizationFilter.cs",
    };

    internal enum Category
    {
        /// <summary>An absent column is unreadable / malformed / withheld by this site (or by the type it reads into).</summary>
        FailsClosed,

        /// <summary>An SDK / FetchXML read: the query projects the attribute (cited) and a test pins the projection.</summary>
        ProjectedAndPinned,

        /// <summary>The read adds a match only when the column is present (positive-only): an absent column adds nothing.</summary>
        PositiveOnly,

        /// <summary>Not an access decision for an external caller (the decision it does feed is named).</summary>
        NotAnAccessDecision,

        /// <summary>A hardening item recorded in the fix-round notes and not yet changed (the ratchet baseline). Zero since fix round 3e.</summary>
        KnownOpen,
    }

    internal sealed record Reviewed(int Count, Category Category, string Reason);

    /// <summary>The number of <see cref="Category.KnownOpen"/> entries below. A new one needs a reviewer; fewer is a ratchet to update.</summary>
    internal const int KnownOpenSites = 0;

    /// <summary>The reviewed sites, keyed <c>repo/relative/path/File.cs::Type.Member</c> (see <see cref="MemberAt"/>).</summary>
    internal static readonly IReadOnlyDictionary<string, Reviewed> ReviewedSites = new Dictionary<string, Reviewed>(StringComparer.Ordinal)
    {
        [Bff + "Api/ExternalAccess/Dtos/ExternalProjectDtos.cs::ExternalContactDto"] = new(1, Category.NotAnAccessDecision, "response DTO property serialised to the SPA; never read back from a Dataverse row"),
        [Bff + "Api/ExternalAccess/Dtos/ExternalProjectDtos.cs::ExternalDocumentDto"] = new(3, Category.NotAnAccessDecision, "response DTO property serialised to the SPA; never read back from a Dataverse row"),
        [Bff + "Api/ExternalAccess/Dtos/ExternalProjectDtos.cs::ExternalEventDto"] = new(3, Category.NotAnAccessDecision, "response DTO property serialised to the SPA; never read back from a Dataverse row"),
        [Bff + "Api/ExternalAccess/Dtos/ExternalProjectDtos.cs::ExternalTodoDto"] = new(3, Category.NotAnAccessDecision, "response DTO property serialised to the SPA; never read back from a Dataverse row"),
        [Bff + "Api/ExternalAccess/ExternalModuleDataEndpoints.cs::ExternalModuleDataEndpoints.RedactRootInvoiceLinksAsync"] = new(1, Category.NotAnAccessDecision, "response redaction of link values in returned module rows (FetchXML); an unreturned pair is not served, and the visibility decision is HideInvoiceLinkedRows"),
        [Bff + "Api/ExternalAccess/ProvisionProjectEndpoint.cs::ProvisionProjectEndpoint.CreatorPersonRow"] = new(1, Category.FailsClosed, "management plane behind DelegationRuleFilter; ReadCreatorPersonAsync turns an absent or empty person into no person, and the resume then refuses (409 or 500) rather than share the record to anyone else"),
        [Bff + "Api/ExternalAccess/ProvisionProjectEndpoint.cs::ProvisionProjectEndpoint.RootRow"] = new(5, Category.NotAnAccessDecision, "secure-designation provisioning (management plane behind DelegationRuleFilter); not an external caller data read"),
        [Bff + "Api/ExternalAccess/RecordAccessGateEndpoint.cs::RecordAccessGateEndpoint.OwnerRow"] = new(3, Category.NotAnAccessDecision, "owner facts in the can-manage-access answer (informational; DelegationRuleFilter is the gate)"),
        [Bff + "Api/ExternalAccess/UnsecureProjectEndpoint.cs::UnsecureProjectEndpoint.SecurityRow"] = new(4, Category.NotAnAccessDecision, "secure-designation removal (management plane behind DelegationRuleFilter); an empty flag is refused as could-not-tell (task 150); the owner read-back (task 175) compares the owner it read with the one it wrote, so an absent column is a mismatch and the call fails"),
        [Bff + "Api/Filters/CommunicationRecordAuthorizationFilter.cs::CommunicationRecordAuthorizationFilter.Gate.LoadProposalAsync"] = new(1, Category.FailsClosed, "SDK read of the proposal row; a missing or empty communication reference gives no proposal, and a request with no proposal is answered as not found (AuthorizeVisibleProposalAsync, AuthorizeProposalCreateTaskApplyAsync); the column list names ProposalCommunicationColumn"),
        [Bff + "Infrastructure/ExternalAccess/ContactIdentityStore.cs::DataverseContactIdentityStore.FindSystemUsersLinkingAsync"] = new(1, Category.PositiveOnly, "identity link read; positive-only: an absent link adds no binding"),
        [Bff + "Infrastructure/ExternalAccess/ContactIdentityStore.cs::DataverseContactIdentityStore.ParseSystemUserRow"] = new(1, Category.PositiveOnly, "identity link read; positive-only: an absent link adds no binding"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.AccessLinkRow"] = new(1, Category.NotAnAccessDecision, "participant list of a record (who has access, shown to a manager); grants nothing"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.ContactRow"] = new(1, Category.NotAnAccessDecision, "display of a participant contact (its organization name); grants nothing"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.DocumentRow"] = new(6, Category.FailsClosed, "records which root link columns were read; RootLinkOf reads an unread link as unreadable (fix round 3b)"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.EventRow"] = new(3, Category.FailsClosed, "fix round 3d: presence of each root stamp is recorded in its setter and RootStamps() reads an unread stamp as unreadable; creator stamps unread match nobody"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.GetDocumentParentAndNameAsync"] = new(1, Category.FailsClosed, "an absent parent id reads as not under the requested record (refused, fix round 3c pin)"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.GetRowLookupsAsync"] = new(1, Category.FailsClosed, "fix round 3d: an absent column is passed to the gate as an unreadable value; only an explicit null is omitted (the shared body of GetInvoiceLookupsAsync and GetSubmissionLookupsAsync since 130b fix round 6)"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.GetInvoiceParentsAsync"] = new(3, Category.PositiveOnly, "the union of an invoice's parents adds a match only when the column is present; positive-only"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.GetRecentGrantsAsync"] = new(3, Category.PositiveOnly, "notification feed items, re-gated by the endpoint; a row with no single root is skipped; positive-only"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.GetServiceRequestRequesterAndStateAsync"] = new(1, Category.PositiveOnly, "a requester match adds a match only when the column is present; positive-only"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.GetServiceRequestRequesterAsync"] = new(1, Category.PositiveOnly, "a requester match adds a match only when the column is present; positive-only"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.GetServiceRequestsRequestedByAsync"] = new(2, Category.PositiveOnly, "a requester match over a server-side filter adds a row only when the column is present; positive-only"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.InvoiceLinksOf"] = new(1, Category.FailsClosed, "fix round 3: an absent invoice link column is unreadable (the row is hidden)"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.ParseDeleteTarget"] = new(2, Category.FailsClosed, "fix round 3d: an absent parent lookup refuses the delete; creator stamps unread match nobody"),
        [Bff + "Infrastructure/ExternalAccess/ExternalDataService.cs::ExternalDataService.TodoRow"] = new(3, Category.FailsClosed, "fix round 3d: presence of each root stamp is recorded in its setter and RootStamps() reads an unread stamp as unreadable; creator stamps unread match nobody"),
        [Bff + "Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs::ExternalGrantRow"] = new(11, Category.FailsClosed, "fix round 3g: the typed row records which lookups were read (setters; KeyLookupsRead, IssuerRead). The revoke, removal and expiry consumers take an unread lookup as undecidable (the existing refusal, retry or fault), the lists and derivations ignore such a row, and the selects are pinned"),
        [Bff + "Infrastructure/ExternalAccess/ExternalVisibility.cs::ExternalVisibility.Run.ExpandAsync"] = new(1, Category.ProjectedAndPinned, "uac-r2 191b: SDK QueryExpression whose ColumnSet is the table's LineageColumns (ReadRowsAsync), so an absent lookup is a null one (no parent); a row whose closure then reaches no root is Undecided (NoRoot). Pinned by ExternalVisibilityTests, whose fake projects through the ColumnSet (every taint test fails if a lineage column is not projected)"),
        [Bff + "Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs::ExternalInvoiceVisibility.ReadFirmHeldInvoicesAsync"] = new(2, Category.ProjectedAndPinned, "SDK ColumnSet projects the hold and the firm (ExternalInvoiceVisibility.cs ReadFirmHeldInvoicesAsync); pinned by TheFirmHeldInvoiceRead_ProjectsTheHoldAndTheFirm; no firm hides the invoice under the own-firms rule"),
        [Bff + "Infrastructure/ExternalAccess/ExternalModuleRegistry.cs::ExternalModuleDescriptor.TryGetAttributeId"] = new(1, Category.ProjectedAndPinned, "FetchXML / SDK row id read; modules project RootRuleAttributes (pinned by EveryFetchXmlModule_ProjectsEveryLiveParentAttribute and the record gate pin)"),
        [Bff + "Infrastructure/ExternalAccess/ExternalMultiRootRule.cs::ExternalMultiRootRule.LiveParentsOf"] = new(1, Category.FailsClosed, "LiveParentsOf Web API overload reads an absent column as unresolved (fix round 3c)"),
        [Bff + "Infrastructure/ExternalAccess/ExternalOrganizationMembership.cs::ExternalOrganizationMembership.ContactOrganizationRow"] = new(2, Category.FailsClosed, "fix round 3e: a membership row that does not carry the lookup is unresolved (UnreadableRows), counted by the caller as an undecidable member; an explicit null names nobody; the selects are class constants, pinned"),
        [Bff + "Infrastructure/ExternalAccess/ExternalParticipationService.cs::ExternalParticipationService.ColleagueMembershipRow"] = new(2, Category.PositiveOnly, "membership rows; positive-only: an absent organization or contact adds nothing"),
        [Bff + "Infrastructure/ExternalAccess/ExternalParticipationService.cs::ExternalParticipationService.ContactOrgRow"] = new(1, Category.PositiveOnly, "membership rows; positive-only: an absent organization or contact adds nothing"),
        [Bff + "Infrastructure/ExternalAccess/ExternalParticipationService.cs::ExternalParticipationService.ExternalAccessRow"] = new(4, Category.PositiveOnly, "grant derivation rows (ISS-026 guard); positive-only: an absent root lookup adds nothing"),
        [Bff + "Infrastructure/ExternalAccess/ExternalParticipationService.cs::ExternalParticipationService.OrganizationReferenceRow"] = new(2, Category.FailsClosed, "EveryLookupReturned: an unreturned firm or organization lookup is Unresolved (ebilling-r1 task 051)"),
        [Bff + "Infrastructure/ExternalAccess/NoAccessEnforcementStore.cs::NoAccessEnforcementStore.EntryFrom"] = new(6, Category.FailsClosed, "fix round 3e: a row that does not carry every EntryRequiredColumns column is undetermined and throws (the entry is recorded as unreadable, counted and retried); an explicit null is empty; the select is a class constant, pinned"),
        [Bff + "Infrastructure/ExternalAccess/NoAccessEnforcementStore.cs::NoAccessEnforcementStore.OwnerFrom"] = new(2, Category.FailsClosed, "fix round 3e: a row that does not carry both owner columns is undetermined and throws (the enforcer counts an owner it could not read as a failure and retries); the select is a class constant, pinned"),
        [Bff + "Infrastructure/ExternalAccess/NoAccessListReader.cs::NoAccessEntryRow"] = new(5, Category.FailsClosed, "fix round 3d: ParseRows fails the chunk closed when a row lacks a selected column"),
        [Bff + "Infrastructure/ExternalAccess/SecuredColumnReadProof.cs::SdkColumnReadPermissionSource.ColumnsGrantingReadAsync"] = new(1, Category.NotAnAccessDecision, "reads for the field-security proof; not a lookup on a record, and an absent value proves nothing"),
        [Bff + "Infrastructure/ExternalAccess/SecuredColumnReadProof.cs::SdkColumnReadPermissionSource.ReadOwnUserIdAsync"] = new(1, Category.NotAnAccessDecision, "reads for the field-security proof; not a lookup on a record, and an absent value proves nothing"),
        [Bff + "Services/Ai/Handlers/Dataverse/ExternalCallerCommunicationGuard.cs::ExternalCallerCommunicationGuard.ThreadIdOf"] = new(1, Category.FailsClosed, "ThreadIdOf: an absent thread lookup reads as an empty id, which every caller treats as hidden (DataverseDescribeHandler.cs:482, DataverseReadQueryHandler.cs:457) and which ReadOpenThreadIdsAsync drops (CommunicationOpenThreadGate.cs:91)"),
        [Bff + "Services/Communication/CommunicationOpenThreadGate.cs::CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync"] = new(1, Category.FailsClosed, "a communication row whose thread lookup was not returned (or is null) is left out of the visible set, so it is withheld; the select lists MessageThreadLookupValue (CommunicationOpenThreadGate.cs:141-142)"),
        [Bff + "Services/Communication/CommunicationThreadReadService.cs::CommunicationThreadReadService.BuildParticipantClauseAsync"] = new(1, Category.PositiveOnly, "participant message ids build a positive filter; positive-only: an absent id adds nothing to it"),
        [Bff + "Services/Communication/CommunicationThreadReadService.cs::CommunicationThreadReadService.LoadAttachmentsAsync"] = new(2, Category.PositiveOnly, "attachment rows of already-authorised messages; positive-only: an absent message or document id adds no attachment"),
        [Bff + "Services/Communication/CommunicationThreadReadService.cs::CommunicationThreadReadService.ParseMessageRow"] = new(3, Category.PositiveOnly, "thread id absent reads as empty (not in the open-thread set, so hidden); an absent author never matches own; positive-only"),
        [Bff + "Services/Communication/CommunicationThreadReadService.cs::CommunicationThreadReadService.ReadVisibleCommunicationAsync"] = new(1, Category.FailsClosed, "an absent thread id is empty, which is not in the open-thread set, so the message is hidden"),
        [Bff + "Services/Communication/CommunicationThreadReadService.cs::CommunicationThreadReadService.ResolveRegardingFromRow"] = new(1, Category.FailsClosed, "fix round 3e: a thread row that does not carry every typed regarding column has an unknown anchor (withheld on the list, null for the single read, which the send route refuses); both selects are built from ThreadRegardingFields and pinned"),
        [Bff + "Services/ExternalAccess/AssignedAccessStore.cs::AssignedAccessLedgerRow"] = new(8, Category.PositiveOnly, "assigned-access ledger rows (admin plane); positive-only: an absent lookup adds nothing"),
        [Bff + "Services/ExternalAccess/AssignedAccessStore.cs::AssignedAccessStore.LinkCandidateRow"] = new(1, Category.PositiveOnly, "assigned-access ledger rows (admin plane); positive-only: an absent lookup adds nothing"),
        [Bff + "Services/ExternalAccess/AssignedAccessStore.cs::AssignedAccessStore.ReadRootAsync"] = new(1, Category.PositiveOnly, "assigned-access ledger rows (admin plane); positive-only: an absent lookup adds nothing"),
        [Bff + "Services/ExternalAccess/ExternalAccessReconciliationJob.cs::ExternalAccessReconciliationJob.Aliased"] = new(1, Category.ProjectedAndPinned, "grant reconciliation scan (SDK rows): BuildGrantScanFetchXml selects every root lookup, the organization and the issuer attribute (ExternalAccessReconciliationJob.cs:1170-1181); pinned by TheGrantScan_ProjectsEveryLookupItReads"),
        [Bff + "Services/ExternalAccess/ExternalAccessReconciliationJob.cs::ExternalAccessReconciliationJob.PlanGrantChange"] = new(2, Category.ProjectedAndPinned, "grant reconciliation scan (SDK rows): BuildGrantScanFetchXml selects every root lookup, the organization and the issuer attribute (ExternalAccessReconciliationJob.cs:1170-1181); pinned by TheGrantScan_ProjectsEveryLookupItReads"),
        [Bff + "Services/ExternalAccess/ExternalAccessReconciliationJob.cs::ExternalAccessReconciliationJob.RootOf"] = new(1, Category.ProjectedAndPinned, "grant reconciliation scan (SDK rows): BuildGrantScanFetchXml selects every root lookup, the organization and the issuer attribute (ExternalAccessReconciliationJob.cs:1170-1181); pinned by TheGrantScan_ProjectsEveryLookupItReads"),
        [Bff + "Services/ExternalAccess/GrantExpiryReminderJob.cs::GrantExpiryReminderJob.DueGrant.Aliased"] = new(1, Category.NotAnAccessDecision, "internal expiry reminder job (sends a reminder); grants nothing"),
        [Bff + "Services/ExternalAccess/SecureRecordIsolationCensus.cs::SecureRecordIsolationCensus.OptionalGuid"] = new(1, Category.NotAnAccessDecision, "diagnostic census of secure-record isolation; reports, grants nothing"),
    };

    // ── Recognition ───────────────────────────────────────────────────────────────────────────────────

    internal sealed record Site(string Member, int Line, string Kind);

    private static readonly Regex ConstDeclaration = new(
        @"\b(?:const\s+string|static\s+readonly\s+string|string)\s+(\w+)\s*(?:=>|=)\s*([^;]*);",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LookupLiteral = new(@"""_[^""]*_value""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReadCall = new(
        @"\b(TryGetValue|TryGetProperty|ContainsKey|GuidValue|TryGuid|GuidOf)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JsonPropertyAttribute = new(@"\[\s*JsonPropertyName\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SdkRead = new(
        @"GetAttributeValue\s*<\s*(?:EntityReference|Guid\??)\s*>|\bAttributes\s*\.\s*(?:TryGetValue|Contains)\s*\(|\.Attributes\s*\[|\bis\s+(?:Microsoft\.Xrm\.Sdk\.)?EntityReference\b|\bcase\s+(?:Microsoft\.Xrm\.Sdk\.)?EntityReference\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TypeHeader = new(
        @"\b(?:record\s+struct|record\s+class|class|record|struct|interface|enum)\s+(\w+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The identifiers of <c>string</c> constants / fields whose initialiser holds a lookup literal, directly or through
    /// another such identifier, across <paramref name="codes"/>.</summary>
    internal static HashSet<string> LookupNames(IEnumerable<string> codes)
    {
        var declarations = new List<(string Name, string Initializer)>();
        foreach (var code in codes)
        {
            foreach (Match m in ConstDeclaration.Matches(code))
                declarations.Add((m.Groups[1].Value, m.Groups[2].Value));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var (name, initializer) in declarations)
            {
                if (names.Contains(name)) continue;
                if (LookupLiteral.IsMatch(initializer) || names.Any(n => Regex.IsMatch(initializer, @"\b" + Regex.Escape(n) + @"\b")))
                {
                    names.Add(name);
                    changed = true;
                }
            }
        }
        while (changed);

        return names;
    }

    private static bool NamesALookup(string arguments, HashSet<string> names) =>
        LookupLiteral.IsMatch(arguments) || names.Any(n => Regex.IsMatch(arguments, @"\b" + Regex.Escape(n) + @"\b"));

    /// <summary>Every lookup-column read site in one file's code text (line comments stripped).</summary>
    internal static IReadOnlyList<Site> Sites(string code, HashSet<string> names)
    {
        var sites = new List<Site>();

        foreach (Match m in ReadCall.Matches(code))
        {
            var args = ArgumentText(code, m.Index + m.Length);
            if (NamesALookup(args, names))
                sites.Add(new Site(MemberAt(code, m.Index), LineOf(code, m.Index), m.Groups[1].Value));
        }

        foreach (Match m in JsonPropertyAttribute.Matches(code))
        {
            var args = ArgumentText(code, m.Index + m.Length);
            if (NamesALookup(args, names))
                sites.Add(new Site(MemberAt(code, m.Index), LineOf(code, m.Index), "JsonPropertyName"));
        }

        foreach (Match m in SdkRead.Matches(code))
            sites.Add(new Site(MemberAt(code, m.Index), LineOf(code, m.Index), "sdk"));

        return sites;
    }

    /// <summary>The text of the argument list that starts at <paramref name="start"/> (just after the opening parenthesis).</summary>
    private static string ArgumentText(string code, int start)
    {
        var args = ExternalChildRowLiveParentGuardTests.Arguments(code, start);
        if (args is not null) return string.Join(",", args);

        var end = code.IndexOf('\n', start);
        return end < 0 ? code[start..] : code[start..end];
    }

    private static int LineOf(string code, int index) => code.AsSpan(0, index).Count('\n') + 1;

    /// <summary>
    /// The innermost named enclosing member of <paramref name="index"/> as <c>Type.Member</c> (nested types and methods
    /// joined by dots), worked out by matching braces from the start of the text. A block with no declaration name (an
    /// <c>if</c>, a lambda, an object initialiser) adds nothing. Strings and character literals are skipped.
    /// </summary>
    internal static string MemberAt(string code, int index)
    {
        // Kinds: T type, N namespace (unnamed), M method / constructor body, P property or accessor block, E an expression-bodied
        // member, a property or a field initialiser (open from its "=>" or "=" to its ";"), B any block inside a member (unnamed).
        // Only a declaration at type level is a member: a block, lambda or local function inside it belongs to it.
        var stack = new List<(char Kind, string? Name)>();
        var segmentStart = 0;
        var parens = 0;
        var brackets = 0;
        string? blockName = null;
        for (var i = 0; i < index && i < code.Length; i++)
        {
            var c = code[i];
            if (c == '"')
            {
                i = SkipString(code, i);
                continue;
            }

            if (c == '\'')
            {
                i = SkipChar(code, i);
                continue;
            }

            var atTypeLevel = stack.All(x => x.Kind is 'T' or 'N');
            switch (c)
            {
                case '(':
                    parens++;
                    break;
                case ')':
                    if (parens > 0) parens--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    if (brackets > 0) brackets--;
                    break;
                case '{':
                    if (atTypeLevel && parens == 0 && brackets == 0)
                        stack.Add(BlockOf(code[segmentStart..i]));
                    else
                        stack.Add(('B', null));
                    segmentStart = i + 1;
                    break;
                case '}':
                    if (stack.Count > 0 && stack[^1].Kind == 'E') stack.RemoveAt(stack.Count - 1);
                    if (stack.Count > 0)
                    {
                        // A property's block can be followed by its initialiser ("{ get; } = ..."): that "=" belongs to the property.
                        var closed = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                        blockName = closed.Kind == 'P' ? closed.Name : null;
                    }

                    segmentStart = i + 1;
                    parens = 0;
                    brackets = 0;
                    break;
                case ';':
                    if (stack.Count > 0 && stack[^1].Kind == 'E' && parens == 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                        segmentStart = i + 1;
                        blockName = null;
                    }
                    else if (atTypeLevel)
                    {
                        segmentStart = i + 1;
                        blockName = null;
                    }

                    break;
                case '=':
                    if (atTypeLevel && parens == 0 && brackets == 0)
                    {
                        var arrow = i + 1 < code.Length && code[i + 1] == '>';
                        var comparison = i > 0 && "<>=!".Contains(code[i - 1]) || (i + 1 < code.Length && code[i + 1] == '=');
                        if (arrow || !comparison)
                        {
                            var header = code[segmentStart..i];
                            stack.Add(('E', string.IsNullOrWhiteSpace(header) ? blockName : DeclaredName(header)));
                            blockName = null;
                            if (arrow) i++;
                        }
                    }

                    break;
            }
        }

        return string.Join('.', stack.Where(x => x.Name is not null).Select(x => x.Name));
    }

    /// <summary>What a <c>{</c> at type level opens: a type, a namespace, a method / constructor body, or a property block.</summary>
    private static (char Kind, string? Name) BlockOf(string header)
    {
        header = StripAttributes(header).Trim();
        var constraint = Regex.Match(header, @"\bwhere\s+\w+\s*:");
        var type = TypeHeader.Match(constraint.Success ? header[..constraint.Index] : header);
        if (type.Success) return ('T', type.Groups[1].Value);
        if (header.StartsWith("namespace", StringComparison.Ordinal)) return ('N', null);

        return (HasParameterList(header) ? 'M' : 'P', DeclaredName(header));
    }

    private static string StripAttributes(string header)
    {
        string previous;
        do
        {
            previous = header;
            header = Regex.Replace(header, @"\[[^\[\]]*\]", " ");
        }
        while (header != previous);

        return header;
    }

    /// <summary>Whether the declaration ends in a parameter list (a method), as opposed to a tuple-typed property.</summary>
    private static bool HasParameterList(string header)
    {
        var group = LastParenGroup(header);
        return group is { } g && Regex.IsMatch(header[(g.End + 1)..], @"^\s*(?:where\b.*)?$", RegexOptions.Singleline);
    }

    private static (int Start, int End)? LastParenGroup(string header)
    {
        var where = Regex.Match(header, @"\bwhere\s+\w+\s*:");
        var limit = where.Success ? where.Index : header.Length;
        (int Start, int End)? last = null;
        var depth = 0;
        var start = -1;
        for (var i = 0; i < limit; i++)
        {
            if (header[i] == '(')
            {
                if (depth++ == 0) start = i;
            }
            else if (header[i] == ')' && depth > 0 && --depth == 0)
            {
                last = (start, i);
            }
        }

        return last;
    }

    /// <summary>The declared name in a member header: the identifier before its parameter list, else its last identifier.</summary>
    private static string? DeclaredName(string header)
    {
        header = StripAttributes(header).Trim();
        var where = Regex.Match(header, @"\bwhere\s+\w+\s*:");
        if (where.Success) header = header[..where.Index].TrimEnd();

        if (HasParameterList(header) && LastParenGroup(header) is { } group)
            header = header[..group.Start];

        var name = Regex.Match(header, @"(\w+)\s*(?:<[^<>]*(?:<[^<>]*>[^<>]*)*>)?\s*$");
        return name.Success ? name.Groups[1].Value : null;
    }

    private static int SkipString(string code, int open)
    {
        if (open + 2 < code.Length && code[open + 1] == '"' && code[open + 2] == '"')
        {
            var close = code.IndexOf("\"\"\"", open + 3, StringComparison.Ordinal);
            return close < 0 ? code.Length : close + 2;
        }

        var verbatim = (open > 0 && code[open - 1] == '@') || (open > 1 && code[open - 1] == '$' && code[open - 2] == '@');
        for (var i = open + 1; i < code.Length; i++)
        {
            if (verbatim)
            {
                if (code[i] == '"')
                {
                    if (i + 1 < code.Length && code[i + 1] == '"') { i++; continue; }
                    return i;
                }
            }
            else
            {
                if (code[i] == '\\') { i++; continue; }
                if (code[i] == '"') return i;
                if (code[i] == '\n') return i; // an unterminated literal: stop at the line end
            }
        }

        return code.Length;
    }

    private static int SkipChar(string code, int open)
    {
        for (var i = open + 1; i < code.Length && i < open + 4; i++)
        {
            if (code[i] == '\\') { i++; continue; }
            if (code[i] == '\'') return i;
        }

        return open; // not a character literal (an apostrophe): carry on
    }

    // ── The rule ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The rule over one file: the violations (an unreviewed group, a changed count), empty when it holds.</summary>
    internal static IReadOnlyList<string> Violations(
        string fileName, string code, HashSet<string> names, IReadOnlyDictionary<string, Reviewed> reviewed, ISet<string>? seen = null)
    {
        var violations = new List<string>();
        foreach (var group in Sites(code, names).GroupBy(s => $"{fileName}::{s.Member}"))
        {
            seen?.Add(group.Key);
            var count = group.Count();
            var lines = string.Join(",", group.Select(s => s.Line));
            if (!reviewed.TryGetValue(group.Key, out var entry))
            {
                violations.Add($"{group.Key}: {count} lookup-column read(s) (lines {lines}) not in ReviewedSites. "
                    + "Decide what an ABSENT column does there: unreadable (the decision's fail-closed value), never \"not filed\".");
            }
            else if (entry.Count != count)
            {
                violations.Add($"{group.Key}: {count} lookup-column read(s) (lines {lines}), reviewed {entry.Count}. Review the change and update the entry.");
            }
        }

        return violations;
    }

    private static IEnumerable<string> ScopeFilePaths()
    {
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = RelativePath(file);
            if (ScopeDirectories.Any(d => relative.StartsWith(d, StringComparison.OrdinalIgnoreCase))
                || ScopeFiles.Any(f => string.Equals(relative, f, StringComparison.OrdinalIgnoreCase)))
                yield return file;
        }
    }

    /// <summary>The path of a source file relative to the repo root, with forward slashes: the file part of a <see cref="ReviewedSites"/> key.</summary>
    internal static string RelativePath(string file) => Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/');

    [Fact(DisplayName = "3d: every lookup-column read in the external-access source is a reviewed site")]
    public void EveryLookupColumnRead_IsAReviewedSite()
    {
        var files = ScopeFilePaths().ToList();
        var codes = files.ToDictionary(f => f, f => SourceScan.CodeText(File.ReadAllLines(f)));
        var names = LookupNames(codes.Values);

        var violations = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var siteCount = 0;
        foreach (var (file, code) in codes)
        {
            violations.AddRange(Violations(RelativePath(file), code, names, ReviewedSites, seen));
            siteCount += Sites(code, names).Count;
        }

        foreach (var stale in ReviewedSites.Keys.Where(k => !seen.Contains(k)))
            violations.Add($"{stale}: reviewed, but no lookup-column read is there any more. Remove the entry (the ratchet moves down).");

        // Non-vacuity: the scope found its files and the census found its sites.
        Assert.True(files.Count >= 40, $"only {files.Count} files in scope: the scan would be vacuous");
        Assert.Contains(files, f => Path.GetFileName(f) == "ExternalDataService.cs");
        Assert.Contains(files, f => Path.GetFileName(f) == "CommunicationThreadReadService.cs");
        Assert.True(siteCount >= 60, $"only {siteCount} lookup-column read sites found: the recognition would be vacuous");

        Assert.True(violations.Count == 0,
            "A lookup column read from a Web API row is ABSENT when the select did not return it, which is not the same as "
            + "\"not filed\". Review each site below (ReviewedSites in ExternalLookupAbsentReadGuardTests): a Web API read treats an "
            + "absent column as unreadable and an explicit JSON null as not filed; an SDK / FetchXML read may treat absent as not filed only "
            + "where its query projects the attribute and a test pins that.\n" + string.Join("\n", violations));
    }

    [Fact(DisplayName = "3d: every reviewed site has a reason, and the known-open baseline is exactly pinned")]
    public void ReviewedSites_CarryAReason_AndTheBaselineIsPinned()
    {
        Assert.All(ReviewedSites, e =>
        {
            Assert.True(e.Value.Count >= 1, $"{e.Key}: count must be at least 1");
            Assert.False(string.IsNullOrWhiteSpace(e.Value.Reason), $"{e.Key}: a reviewed site needs a one-line reason");
            Assert.DoesNotContain('\n', e.Value.Reason);
        });

        Assert.Equal(KnownOpenSites, ReviewedSites.Values.Count(v => v.Category == Category.KnownOpen));
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, Reviewed> NothingReviewed = new Dictionary<string, Reviewed>();

    private static IReadOnlyList<string> Scan(string code, IReadOnlyDictionary<string, Reviewed>? reviewed = null, string path = "Synthetic.cs")
        => Violations(path, code, LookupNames(new[] { code }), reviewed ?? NothingReviewed);

    private const string UnreviewedRead = """
        internal sealed class NewReader
        {
            public Guid? ParentOf(IReadOnlyDictionary<string, JsonElement> row) =>
                row.TryGetValue("_sprk_newparent_value", out var v) && v.ValueKind == JsonValueKind.String ? Guid.Parse(v.GetString()!) : null;
        }
        """;

    [Fact(DisplayName = "3d guard, must-fire control: an unreviewed absent-is-none read is caught")]
    public void Control_AnUnreviewedRead_IsCaught()
    {
        var violations = Scan(UnreviewedRead);
        var violation = Assert.Single(violations);
        Assert.Contains("Synthetic.cs::NewReader.ParentOf", violation, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "3d guard, must-fire control: each spelling of a lookup read is caught")]
    [InlineData("class A { void M(Dictionary<string, JsonElement> row) { var ok = row.TryGetProperty(\"_sprk_x_value\", out var v); } }")]
    [InlineData("class A { void M(Dictionary<string, JsonElement> row) { var ok = row.TryGetValue($\"_{col}_value\", out var v); } }")]
    [InlineData("class A { private const string Col = \"_sprk_x_value\"; void M(Dictionary<string, JsonElement> row) { var ok = row.TryGetValue(Col, out var v); } }")]
    [InlineData("class A { private const string Col = \"_sprk_x_value\"; private const string Other = Col; Guid? M(Row r) => GuidOf(r, Other); }")]
    [InlineData("class A { Guid? M(Row r) => TryGuid(r, \"_sprk_x_value\"); }")]
    [InlineData("class A { bool M(Row r) => r.ContainsKey(\"_sprk_x_value\"); }")]
    [InlineData("class A { Guid? M(Row r) => GuidValue(r, \"_sprk_x_value\"); }")]
    [InlineData("class A { class Row { [JsonPropertyName(\"_sprk_x_value\")] public string? X { get; set; } } }")]
    [InlineData("class A { Guid? M(Entity e) => e.GetAttributeValue<EntityReference>(\"sprk_x\")?.Id; }")]
    [InlineData("class A { Guid? M(Entity e) => e.GetAttributeValue<Guid?>(\"sprk_x\"); }")]
    [InlineData("class A { bool M(Entity e) => e.Attributes.TryGetValue(\"sprk_x\", out var v); }")]
    [InlineData("class A { bool M(Entity e) => e.Attributes.Contains(\"sprk_x\"); }")]
    [InlineData("class A { bool M(Entity e) => e.Attributes[\"sprk_x\"] is EntityReference; }")]
    public void Control_EachSpellingOfALookupRead_IsCaught(string code)
        => Assert.NotEmpty(Scan(code));

    [Fact(DisplayName = "3d guard, must-not-fire control: a reviewed site with its reviewed count passes")]
    public void Control_AReviewedSite_Passes()
    {
        // The read is inside an expression-bodied method: its member is the method.
        var reviewed = new Dictionary<string, Reviewed> { ["Synthetic.cs::NewReader.ParentOf"] = new(1, Category.FailsClosed, "a control entry") };
        var otherMember = new Dictionary<string, Reviewed> { ["Synthetic.cs::NewReader.OtherReader"] = new(1, Category.FailsClosed, "a control entry") };
        var theType = new Dictionary<string, Reviewed> { ["Synthetic.cs::NewReader"] = new(1, Category.FailsClosed, "a control entry") };

        Assert.Empty(Scan(UnreviewedRead, reviewed));
        Assert.NotEmpty(Scan(UnreviewedRead, otherMember)); // another member's entry does not review it
        Assert.NotEmpty(Scan(UnreviewedRead, theType));     // nor does the type's: the key is the real member
    }

    [Fact(DisplayName = "3d guard, ratchet control: a changed count is caught in both directions")]
    public void Control_AChangedCount_IsCaught()
    {
        var one = new Dictionary<string, Reviewed> { ["Synthetic.cs::NewReader.ParentOf"] = new(1, Category.FailsClosed, "a control entry") };
        var two = new Dictionary<string, Reviewed> { ["Synthetic.cs::NewReader.ParentOf"] = new(2, Category.FailsClosed, "a control entry") };
        var extra = UnreviewedRead.Replace("null;", "null;\n    public Guid? Other(IReadOnlyDictionary<string, JsonElement> row) => row.TryGetValue(\"_sprk_other_value\", out _) ? Guid.Empty : null;");
        var sameMember = UnreviewedRead.Replace("? Guid.Parse", "&& row.TryGetValue(\"_sprk_other_value\", out _) ? Guid.Parse");

        Assert.Empty(Scan(UnreviewedRead, one));
        Assert.NotEmpty(Scan(UnreviewedRead, two)); // fewer reads than reviewed
        Assert.NotEmpty(Scan(extra, one));          // a new member's read is unreviewed
        Assert.NotEmpty(Scan(sameMember, one));     // more reads than reviewed in the same member
        Assert.Empty(Scan(sameMember, two));
    }

    [Theory(DisplayName = "3g guard, must-fire control: an expression-bodied member (or a property, or an initialiser) keys to its own name")]
    [InlineData("class A { Guid? Parent(Row r) => TryGuid(r, \"_sprk_x_value\"); }", "A.Parent")]
    [InlineData("class A { Guid? Parent => TryGuid(row, \"_sprk_x_value\"); }", "A.Parent")]
    [InlineData("class A { public async Task<Guid?> Parent<T>(Row r, int n = 3) where T : class => await TryGuid(r, \"_sprk_x_value\"); }", "A.Parent")]
    [InlineData("class A { (Guid A, string B)[] Parent(Row r) => TryGuid(r, \"_sprk_x_value\"); }", "A.Parent")]
    [InlineData("class A { Guid? Parent { get => TryGuid(row, \"_sprk_x_value\"); } }", "A.Parent")]
    [InlineData("class A { Guid? Parent { get { return TryGuid(row, \"_sprk_x_value\"); } } }", "A.Parent")]
    [InlineData("class A { static readonly Func<Row, Guid?> Parent = r => TryGuid(r, \"_sprk_x_value\"); }", "A.Parent")]
    [InlineData("class A { Guid? Parent(Row r) => rows.Select(x => { return TryGuid(x, \"_sprk_x_value\"); }).FirstOrDefault(); }", "A.Parent")]
    [InlineData("class A { Guid? Parent(Row r) => new Holder { Value = TryGuid(r, \"_sprk_x_value\") }.Value; Guid? Second(Row r) => null; }", "A.Parent")]
    [InlineData("class A { class Inner { Guid? Parent(Row r) => TryGuid(r, \"_sprk_x_value\"); } }", "A.Inner.Parent")]
    [InlineData("record struct A(int N) { Guid? Parent(Row r) => TryGuid(r, \"_sprk_x_value\"); }", "A.Parent")]
    public void Control_AnExpressionBodiedMember_KeysToItsOwnName(string code, string member)
    {
        var site = Assert.Single(Sites(code, LookupNames(new[] { code })));
        Assert.Equal(member, site.Member);
    }

    [Fact(DisplayName = "3g guard, control: two members read in turn each key to their own name, an expression body does not leak into the next member")]
    public void Control_AnExpressionBody_DoesNotLeakIntoTheNextMember()
    {
        const string code = """
            class A
            {
                Guid? First(Row r) => TryGuid(r, "_sprk_a_value");
                Guid? Second(Row r) => GuidOf(r, "_sprk_b_value");
                Guid? Third { get; } = TryGuid(row, "_sprk_c_value");
                Guid? Fourth(Row r)
                {
                    return TryGuid(r, "_sprk_d_value");
                }
            }
            """;

        Assert.Equal(
            new[] { "A.First", "A.Second", "A.Third", "A.Fourth" },
            Sites(code, LookupNames(new[] { code })).Select(s => s.Member).ToArray());
    }

    [Fact(DisplayName = "3g guard, must-not-fire control: a repo-relative path keys the entry, so two files with the same name cannot share one")]
    public void Control_TheKeyIsTheRepoRelativePath_NotTheFileName()
    {
        var reviewedInA = new Dictionary<string, Reviewed> { ["src/a/Same.cs::NewReader.ParentOf"] = new(1, Category.FailsClosed, "a control entry") };

        Assert.Empty(Scan(UnreviewedRead, reviewedInA, "src/a/Same.cs"));
        var other = Assert.Single(Scan(UnreviewedRead, reviewedInA, "src/b/Same.cs")); // the same name elsewhere is a different file
        Assert.Contains("src/b/Same.cs::NewReader.ParentOf", other, StringComparison.Ordinal);
        Assert.Equal("src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs",
            RelativePath(Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Infrastructure", "ExternalAccess", "ExternalDataService.cs")));
    }

    [Theory(DisplayName = "3d guard, must-not-fire control: the sanctioned helpers, comments and non-lookup columns are not sites")]
    [InlineData("class A { string? M(Row row) => ExternalMultiRootRule.LookupOf(row, \"_sprk_x_value\"); }")]
    [InlineData("class A { string? M(Row row) => RootStamp(row, ProjectStampColumn); private const string ProjectStampColumn = \"_sprk_regardingproject_value\"; }")]
    [InlineData("class A { Guid? M(Row row) => LinkOf(row, \"_sprk_invoice_value\"); }")]
    [InlineData("class A { Guid? M(Row row) => RootLinkOf(row, \"_sprk_matter_value\"); }")]
    [InlineData("class A { bool M(Row row) => row.TryGetValue(\"sprk_name\", out _); }")]
    [InlineData("class A { bool M(Row row) => row.TryGetValue(\"sprk_accesspermission\", out _); }")]
    [InlineData("class A { string M() => \"a note that says _sprk_x_value is read with TryGetValue(\"; }")]
    public void Control_SanctionedAndUnrelatedCode_IsNotASite(string code)
        => Assert.Empty(Scan(SourceScan.CodeText(code.Split('\n'))));

    [Fact(DisplayName = "3d guard, must-not-fire control: a comment that mentions a read is not a site")]
    public void Control_AComment_IsNotASite()
        => Assert.Empty(Scan(SourceScan.CodeText(new[]
        {
            "class A {",
            "    // row.TryGetValue(\"_sprk_x_value\", out var v) is how the old code read it",
            "    /// <c>TryGuid(row, \"_sprk_y_value\")</c>",
            "}",
        })));

    [Fact(DisplayName = "3d guard, control: the enclosing member is the method, strings and braces in strings do not confuse it")]
    public void Control_TheEnclosingMember_IsTheMethod()
    {
        const string code = """
            internal sealed class Outer
            {
                private const string Col = "_sprk_x_value";

                public async Task<Guid?> ReadAsync(Row row)
                {
                    var text = "}{ not a block }";
                    if (row is not null)
                    {
                        var x = rows.Select(r =>
                        {
                            return r.TryGetValue(Col, out var v) ? v : default;
                        });
                    }
                    return null;
                }

                public Guid? Second(Row row) { return GuidOf(row, Col); }
            }
            """;

        var members = Sites(code, LookupNames(new[] { code })).Select(s => s.Member).ToList();
        Assert.Equal(new[] { "Outer.ReadAsync", "Outer.Second" }, members);
    }
}
