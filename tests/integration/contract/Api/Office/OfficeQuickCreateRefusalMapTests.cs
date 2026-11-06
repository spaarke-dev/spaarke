using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Office.Errors;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Field mapping task 018b (UAC-r2 condition (b) item iii, ADR-019, NFR-07): the Office quick-create refusal map
/// (<see cref="OfficeService.MapQuickCreateRefusal"/>), as a table. Every <see cref="RecordCreationRefusalKind"/> and every code
/// the unified create can return — read by reflection from the classes that declare them — must have a row here, so a new
/// kind or code that no one mapped fails this test instead of reaching Office as something nobody decided.
/// </summary>
public class OfficeQuickCreateRefusalMapTests
{
    private const string AssignedTo = "sprk_assignedtointernal";
    private const string InternalError = OfficeErrorCodes.InternalError;
    private const int Forbidden = StatusCodes.Status403Forbidden;
    private const int ServerError = StatusCodes.Status500InternalServerError;

    /// <summary>One row: the service's refusal (kind, code, the WhoAmI failure, the refused column) → the Office code and status.</summary>
    /// <remarks>The kind is carried by name: the enum is internal, and a public theory row cannot expose it.</remarks>
    public sealed record Row(
        string KindName, string Code, string? WhoAmIFailure, string? RefusedColumn, string OfficeCode, int Status, string? ReasonCode = null)
    {
        internal RecordCreationRefusalKind Kind => Enum.Parse<RecordCreationRefusalKind>(KindName);

        public override string ToString() => $"{Kind} {Code} [{WhoAmIFailure ?? "-"}|{RefusedColumn ?? "-"}] → {OfficeCode} {Status}{(ReasonCode is null ? "" : " / " + ReasonCode)}";
    }

    /// <summary>THE expected map. The Office codes are the old core's (NFR-07); "same" passes the service's code through.</summary>
    public static IReadOnlyList<Row> Map { get; } = BuildMap();

    private static List<Row> BuildMap()
    {
        var rows = new List<Row>();
        void Add(RecordCreationRefusalKind kind, string code, string officeCode, int status, string? who = null, string? column = null,
            string? reason = null)
            => rows.Add(new Row(kind.ToString(), code, who, column, officeCode, status, reason));

        // PrincipalUnresolved (G2): no Dataverse user → 403 owner_unresolved; no usable token, none or another → 500.
        var unresolved = ChildRecordEndpoints.CallerUnresolvedReasonCode;
        Add(RecordCreationRefusalKind.PrincipalUnresolved, unresolved, "owner_unresolved", Forbidden, DataverseUserClientErrorCodes.AccessDenied);
        Add(RecordCreationRefusalKind.PrincipalUnresolved, unresolved, "caller_rights_unverifiable", ServerError, DataverseUserClientErrorCodes.UserContextRequired);
        Add(RecordCreationRefusalKind.PrincipalUnresolved, unresolved, "caller_rights_unverifiable", ServerError, DataverseUserClientErrorCodes.OboNotConfigured);
        Add(RecordCreationRefusalKind.PrincipalUnresolved, unresolved, "caller_rights_unverifiable", ServerError, DataverseUserClientErrorCodes.OboExchangeFailed);
        Add(RecordCreationRefusalKind.PrincipalUnresolved, unresolved, "caller_rights_unverifiable", ServerError, who: null);
        Add(RecordCreationRefusalKind.PrincipalUnresolved, unresolved, "caller_rights_unverifiable", ServerError, DataverseUserClientErrorCodes.ServiceError);

        // BlockedColumn: the adapter built the payload, so this is a server defect.
        Add(RecordCreationRefusalKind.BlockedColumn, RecordCreationRefusal.BlockedColumnCode, InternalError, ServerError);

        // Forbidden.
        Add(RecordCreationRefusalKind.Forbidden, RecordCreationRefusal.DeniedCode, "caller_cannot_create", Forbidden);
        Add(RecordCreationRefusalKind.Forbidden, ProvisionProjectEndpoint.ReasonCreatorNoAccess, ProvisionProjectEndpoint.ReasonCreatorNoAccess, Forbidden);
        Add(RecordCreationRefusalKind.Forbidden, AccessFollowsParent.SecureFlagReasonCode, AccessFollowsParent.SecureFlagReasonCode, Forbidden);
        Add(RecordCreationRefusalKind.Forbidden, AccessInheritance.ServerOnlyReasonCode, AccessInheritance.ServerOnlyReasonCode, Forbidden);
        Add(RecordCreationRefusalKind.Forbidden, RecordCreationRefusal.SystemPrincipalUnsupportedCode, InternalError, ServerError);

        // NotFoundOrDenied (G5): the Assigned To column → the Office assignee refusal, in the route filter's own shape (errorCode
        // OFFICE_009 with reasonCode assignee_inaccessible); any other column, or none → filing.
        var notFound = RecordCreationRefusal.NotFoundCode;
        Add(RecordCreationRefusalKind.NotFoundOrDenied, notFound, OfficeErrorCodes.AccessDenied, Forbidden, column: AssignedTo,
            reason: QuickCreateSourceAccessFilter.AssigneeDeniedReasonCode);
        Add(RecordCreationRefusalKind.NotFoundOrDenied, notFound, OfficeErrorCodes.AccessDenied, Forbidden, column: "SPRK_AssignedToInternal",
            reason: QuickCreateSourceAccessFilter.AssigneeDeniedReasonCode);
        Add(RecordCreationRefusalKind.NotFoundOrDenied, notFound, "caller_cannot_file_under_parent", Forbidden, column: SecureRootInheritance.PairIdColumn);
        Add(RecordCreationRefusalKind.NotFoundOrDenied, notFound, "caller_cannot_file_under_parent", Forbidden, column: "sprk_client");
        Add(RecordCreationRefusalKind.NotFoundOrDenied, notFound, "caller_cannot_file_under_parent", Forbidden, column: null);

        // Invalid: the adapter built the payload (a server defect).
        Add(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.InvalidPayloadCode, InternalError, ServerError);
        Add(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.TableUnsupportedCode, InternalError, ServerError);

        // OwnerUnresolved stays 403: the filed-under parent that cannot be told keeps its code, every other is OFFICE_022.
        Add(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.ParentUndetermined, RecordOwnerRefusal.ParentUndetermined, Forbidden);
        Add(RecordCreationRefusalKind.OwnerUnresolved, RecordCreationRefusal.OwnershipExceptionCode, OfficeErrorCodes.RecordOwnerUnresolved, Forbidden);
        foreach (var resolverCode in ConstantsOf(typeof(RecordOwnerRefusal)).Where(c => c != RecordOwnerRefusal.ParentUndetermined))
            Add(RecordCreationRefusalKind.OwnerUnresolved, resolverCode, OfficeErrorCodes.RecordOwnerUnresolved, Forbidden);

        // Conflict.
        Add(RecordCreationRefusalKind.Conflict, RecordCreationRefusal.NumberUnavailableCode, RecordCreationRefusal.NumberUnavailableCode, StatusCodes.Status409Conflict);

        // Undecided: the plan's and the isolated create's own codes pass through (500); the service's own are the generic 500.
        // Every code provisioning declares (ProvisionProjectEndpoint.Reason*) is passed through at 500 with its detail — the isolated
        // row's removal carries the one that failed (F2-1, NFR-07: creator_share_failed "Try again", creator_no_access "an
        // administrator needs to review your access"). Read by reflection here, so a new constant is a row, not a silent 500.
        foreach (var provisioning in ProvisioningReasonCodes())
            Add(RecordCreationRefusalKind.Undecided, provisioning, provisioning, ServerError);
        Add(RecordCreationRefusalKind.Undecided, RecordOwnerRefusal.SecureOwnerTeamUnresolved, RecordOwnerRefusal.SecureOwnerTeamUnresolved, ServerError);
        foreach (var inherit in InheritReasonCodes())
            Add(RecordCreationRefusalKind.Undecided, inherit, inherit, ServerError);
        Add(RecordCreationRefusalKind.Undecided, RecordCreationRefusal.NoDecisionCode, InternalError, ServerError);
        Add(RecordCreationRefusalKind.Undecided, RecordCreationRefusal.ServerSetFilingLookupCode, InternalError, ServerError);

        // Unavailable: a check or the owner decision faulted (the old core's faults reached the generic 500).
        Add(RecordCreationRefusalKind.Unavailable, RecordCreationRefusal.CheckFailedCode, InternalError, ServerError);
        Add(RecordCreationRefusalKind.Unavailable, RecordCreationRefusal.OwnerUnavailableCode, InternalError, ServerError);

        // Task 017 Phase 2d-1 (owner decision 2a): step 10's I-1 refusals. Office creates roots, which carry no I-1 stamp, so
        // neither reaches it today; if one did, it is the generic 500 (fail closed), as every other service-only code.
        Add(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.StampUnderivableCode, InternalError, ServerError);
        Add(RecordCreationRefusalKind.Unavailable, RecordCreationRefusal.StampUnavailableCode, InternalError, ServerError);

        // Upstream: a caller's Dataverse failure.
        Add(RecordCreationRefusalKind.Upstream, RecordCreationRefusal.UpstreamCode, InternalError, ServerError);
        foreach (var clientCode in ConstantsOf(typeof(DataverseUserClientErrorCodes)))
            Add(RecordCreationRefusalKind.Upstream, clientCode, InternalError, ServerError);

        return rows;
    }

    public static TheoryData<Row> Rows()
    {
        var data = new TheoryData<Row>();
        foreach (var row in Map)
            data.Add(row);
        return data;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void EachServiceRefusal_MapsToItsOfficeCodeAndStatus(Row row)
    {
        var mapped = OfficeService.MapQuickCreateRefusal(Refusal(row.Kind, row.Code, row.WhoAmIFailure, row.RefusedColumn), "matter", AssignedTo, filedUnderParent: false);

        mapped.Code.Should().Be(row.OfficeCode);
        mapped.Status.Should().Be(row.Status);
        mapped.ReasonCode.Should().Be(row.ReasonCode, "only the Assigned To refusal carries the filter's reasonCode");
        mapped.Status.Should().BeGreaterThanOrEqualTo(400, "a refusal is never a success");
        mapped.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EveryRefusalKind_HasARow()
    {
        Map.Select(r => r.Kind).Distinct().Should().BeEquivalentTo(Enum.GetValues<RecordCreationRefusalKind>(),
            "a refusal kind the map has no row for would reach Office unmapped");
    }

    [Fact]
    public void EveryCodeTheServiceDeclares_HasARow()
    {
        // The codes the unified create returns: its own, the WhoAmI refusal's, the resolver's, the secure-create plan's and
        // provisioning's, and the isolated create's completion codes. A new constant in any of them fails here.
        var declared = ConstantsOf(typeof(RecordCreationRefusal))
            .Concat(ConstantsOf(typeof(RecordOwnerRefusal)))
            .Concat(InheritReasonCodes())
            .Concat(ProvisioningReasonCodes())
            .Concat([
                ChildRecordEndpoints.CallerUnresolvedReasonCode,
                AccessFollowsParent.SecureFlagReasonCode,
                AccessInheritance.ServerOnlyReasonCode,
            ])
            .Distinct()
            .ToList();

        declared.Should().NotBeEmpty();
        declared.Except(Map.Select(r => r.Code)).Should().BeEmpty("every code the service can return has a decided Office mapping");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AnUnknownCode_FailsClosed_NeverA2xx_AndNeverPassesThrough(string kindName)
    {
        var kind = Enum.Parse<RecordCreationRefusalKind>(kindName);
        var mapped = OfficeService.MapQuickCreateRefusal(Refusal(kind, "record_creation.a_code_no_one_mapped", null, null), "project", AssignedTo, filedUnderParent: false);

        mapped.Code.Should().NotBe("record_creation.a_code_no_one_mapped", "an unmapped code is never passed through");
        mapped.Status.Should().BeGreaterThanOrEqualTo(400);
        var expected = kind switch
        {
            RecordCreationRefusalKind.PrincipalUnresolved => ("caller_rights_unverifiable", ServerError),
            RecordCreationRefusalKind.NotFoundOrDenied => ("caller_cannot_file_under_parent", Forbidden),
            RecordCreationRefusalKind.OwnerUnresolved => (OfficeErrorCodes.RecordOwnerUnresolved, Forbidden),
            _ => (InternalError, ServerError),
        };
        (mapped.Code, mapped.Status).Should().Be(expected);
    }

    [Fact]
    public void AnUnknownKind_FailsClosedToTheGenericOffice500()
    {
        var mapped = OfficeService.MapQuickCreateRefusal(
            Refusal((RecordCreationRefusalKind)999, RecordCreationRefusal.NotFoundCode, null, AssignedTo), "matter", AssignedTo, filedUnderParent: false);

        (mapped.Code, mapped.Status).Should().Be((InternalError, ServerError));
    }

    [Fact]
    public void TheAssignedToRefusal_NeverReadsAsMatterNotFound()
    {
        var mapped = OfficeService.MapQuickCreateRefusal(
            Refusal(RecordCreationRefusalKind.NotFoundOrDenied, RecordCreationRefusal.NotFoundCode, null, AssignedTo), "matter", AssignedTo, filedUnderParent: false);

        mapped.Detail.Should().Contain("Assigned To").And.NotContainEquivalentOf("not found");
    }

    [Fact]
    public void EveryProvisioningCode_WhenUndecided_PassesThroughAt500WithItsOwnDetail()
    {
        var codes = ProvisioningReasonCodes().ToList();
        codes.Should().NotBeEmpty("the reflection found provisioning's Reason* constants");
        codes.Should().OnlyContain(c => c.StartsWith("sdap.provision.", StringComparison.Ordinal),
            "ReasonKey (the body key) is not a code");

        foreach (var code in codes)
        {
            var mapped = OfficeService.MapQuickCreateRefusal(
                new RecordCreationRefusal(RecordCreationRefusalKind.Undecided, code, "the isolated create's detail"), "project", AssignedTo, filedUnderParent: false);

            (mapped.Code, mapped.Status, mapped.Detail).Should().Be((code, ServerError, "the isolated create's detail"), code);
            mapped.ReasonCode.Should().BeNull();
        }
    }

    /// <summary>The caller's own business unit is named only where it really is the cause (018b fix round 2, F1-1).</summary>
    [Theory]
    [InlineData(RecordOwnerRefusal.NoDefaultOwnerTeam)]
    [InlineData(RecordOwnerRefusal.ActingUserUnresolved)]
    public void OwnerUnresolved_WhereTheCallersOwnUnitIsTheCause_NamesTheCallersBusinessUnit(string code)
    {
        var mapped = OfficeService.MapQuickCreateRefusal(
            new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved, code, "the service's own detail"), "project", AssignedTo, filedUnderParent: false);

        (mapped.Code, mapped.Status).Should().Be((OfficeErrorCodes.RecordOwnerUnresolved, Forbidden));
        mapped.Detail.Should().Contain("your business unit's team").And.Contain("default team").And.NotContain("the service's own detail");
    }

    /// <summary>
    /// A project filed under a record is owned by that record's unit (record-first): its missing default team is not the
    /// caller's, so the wording is neutral. Without a parent the caller's unit decides. The acting-user refusal is the
    /// caller's in both cases.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NoDefaultTeam_NamesTheCallersUnit_OnlyWhenTheCreateIsNotFiledUnderAParent(bool filedUnderParent, bool namesCallersUnit)
    {
        var mapped = OfficeService.MapQuickCreateRefusal(
            new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.NoDefaultOwnerTeam, "the service's own detail"),
            "project", AssignedTo, filedUnderParent);

        (mapped.Code, mapped.Status).Should().Be((OfficeErrorCodes.RecordOwnerUnresolved, Forbidden));
        if (namesCallersUnit)
        {
            mapped.Detail.Should().Contain("your business unit's team");
        }
        else
        {
            mapped.Detail.Should().NotContainEquivalentOf("your business unit")
                .And.Contain("the business unit of the record it is filed under has no default owner team")
                .And.Contain("Nothing was created.");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActingUserUnresolved_KeepsTheCallerWording_WhetherOrNotFiledUnderAParent(bool filedUnderParent)
    {
        var mapped = OfficeService.MapQuickCreateRefusal(
            new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.ActingUserUnresolved, "the service's own detail"),
            "project", AssignedTo, filedUnderParent);

        (mapped.Code, mapped.Status).Should().Be((OfficeErrorCodes.RecordOwnerUnresolved, Forbidden));
        mapped.Detail.Should().Contain("your business unit's team");
    }

    /// <summary>
    /// Every other owner code is not about the caller's unit (record-first ownership: a project filed under a matter in another
    /// unit that has no owner team, a plan/resolver race's "Try again", the ownership exception), so the service's text passes.
    /// </summary>
    [Theory]
    [InlineData(RecordOwnerRefusal.NoOwnerSource, "The matter changed while the project was being created. Try again.")]
    [InlineData(RecordOwnerRefusal.ParentUnresolved, "the matter it is filed under has no owning business unit")]
    [InlineData(RecordOwnerRefusal.SecureParentNotIsolated, "the parent matter is marked secure but is not isolated")]
    [InlineData(RecordOwnerRefusal.SecureBusinessUnitAmbiguous, "more than one business unit carries the Secure Record name")]
    [InlineData(RecordCreationRefusal.OwnershipExceptionCode, "a project filed under nothing keeps its creator")]
    [InlineData(RecordOwnerRefusal.ParentUnresolved, "a read failed")]
    public void OwnerUnresolved_WhereTheCallersOwnUnitIsNotTheCause_PassesTheServicesDetailThrough(string code, string detail)
    {
        var mapped = OfficeService.MapQuickCreateRefusal(
            new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved, code, detail), "project", AssignedTo, filedUnderParent: false);

        (mapped.Code, mapped.Status).Should().Be((OfficeErrorCodes.RecordOwnerUnresolved, Forbidden));
        mapped.Detail.Should().Be($"The project was not created: {detail.TrimEnd('.')}.",
            "a service owner detail may be a fragment, so it is wrapped, never shown bare (UAC-r2 018b re-check F4)");
        mapped.Detail.Should().NotContainEquivalentOf("your business unit");
    }

    [Fact]
    public void OwnerUnresolved_NoOwnerSource_KeepsTheTryAgainTheServiceGave()
    {
        var mapped = OfficeService.MapQuickCreateRefusal(
            new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.NoOwnerSource,
                "Whether this 'sprk_project' is filed under a secure record could not be decided consistently. It was NOT created. Try again."),
            "project", AssignedTo, filedUnderParent: false);

        mapped.Detail.Should().Contain("Try again");
    }

    public static TheoryData<string> Kinds()
    {
        var data = new TheoryData<string>();
        foreach (var kind in Enum.GetValues<RecordCreationRefusalKind>())
            data.Add(kind.ToString());
        return data;
    }

    private static RecordCreationRefusal Refusal(RecordCreationRefusalKind kind, string code, string? whoAmIFailure, string? column) =>
        new(kind, code, "the service's own detail")
        {
            ClientFailure = whoAmIFailure is null ? null : DataverseUserResponse.Fail(403, whoAmIFailure, "WhoAmI failed"),
            RefusedColumn = column,
        };

    [Fact]
    public void TheOnlyProvisioningReasonConstantOutsideTheCodePrefix_IsTheBodyKey()
    {
        // 018b re-check K1: the map passes provisioning codes through by the sdap.provision. prefix. A new Reason* code
        // under another prefix would fall silently to OFFICE_INTERNAL, so any constant outside the prefix must be decided here.
        var outside = typeof(ProvisionProjectEndpoint).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string) && f.Name.StartsWith("Reason", StringComparison.Ordinal))
            .Where(f => !((string)f.GetRawConstantValue()!).StartsWith("sdap.provision.", StringComparison.Ordinal))
            .Select(f => f.Name);

        outside.Should().BeEquivalentTo(["ReasonKey"],
            "a Reason* code outside sdap.provision. would not pass through MapQuickCreateRefusal; give it a decided row");
    }

    /// <summary>Every <c>const string</c> a type declares (public or not).</summary>
    private static IEnumerable<string> ConstantsOf(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    /// <summary>Every code <see cref="ProvisionProjectEndpoint"/> declares (<c>Reason*</c> constants under <c>sdap.provision.</c>).</summary>
    private static IEnumerable<string> ProvisioningReasonCodes() =>
        typeof(ProvisionProjectEndpoint).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string) && f.Name.StartsWith("Reason", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(c => c.StartsWith("sdap.provision.", StringComparison.Ordinal));

    /// <summary>The isolated create's completion codes (<c>SecureRootInheritance.Reason*</c>).</summary>
    private static IEnumerable<string> InheritReasonCodes() =>
        typeof(SecureRootInheritance).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string) && f.Name.StartsWith("Reason", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(c => c.StartsWith(OfficeService.SecureInheritReasonPrefix, StringComparison.Ordinal));
}
