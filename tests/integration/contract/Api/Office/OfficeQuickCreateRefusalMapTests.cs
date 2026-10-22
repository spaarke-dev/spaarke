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
        string KindName, string Code, string? WhoAmIFailure, string? RefusedColumn, string OfficeCode, int Status)
    {
        internal RecordCreationRefusalKind Kind => Enum.Parse<RecordCreationRefusalKind>(KindName);

        public override string ToString() => $"{Kind} {Code} [{WhoAmIFailure ?? "-"}|{RefusedColumn ?? "-"}] → {OfficeCode} {Status}";
    }

    /// <summary>THE expected map. The Office codes are the old core's (NFR-07); "same" passes the service's code through.</summary>
    public static IReadOnlyList<Row> Map { get; } = BuildMap();

    private static List<Row> BuildMap()
    {
        var rows = new List<Row>();
        void Add(RecordCreationRefusalKind kind, string code, string officeCode, int status, string? who = null, string? column = null)
            => rows.Add(new Row(kind.ToString(), code, who, column, officeCode, status));

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

        // NotFoundOrDenied (G5): the Assigned To column → the Office assignee refusal; any other column, or none → filing.
        var notFound = RecordCreationRefusal.NotFoundCode;
        Add(RecordCreationRefusalKind.NotFoundOrDenied, notFound, QuickCreateSourceAccessFilter.AssigneeDeniedReasonCode, Forbidden, column: AssignedTo);
        Add(RecordCreationRefusalKind.NotFoundOrDenied, notFound, QuickCreateSourceAccessFilter.AssigneeDeniedReasonCode, Forbidden, column: "SPRK_AssignedToInternal");
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
        Add(RecordCreationRefusalKind.Undecided, ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable, ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable, ServerError);
        Add(RecordCreationRefusalKind.Undecided, RecordOwnerRefusal.SecureOwnerTeamUnresolved, RecordOwnerRefusal.SecureOwnerTeamUnresolved, ServerError);
        foreach (var inherit in InheritReasonCodes())
            Add(RecordCreationRefusalKind.Undecided, inherit, inherit, ServerError);
        Add(RecordCreationRefusalKind.Undecided, RecordCreationRefusal.NoDecisionCode, InternalError, ServerError);
        Add(RecordCreationRefusalKind.Undecided, RecordCreationRefusal.ServerSetFilingLookupCode, InternalError, ServerError);

        // Unavailable: a check or the owner decision faulted (the old core's faults reached the generic 500).
        Add(RecordCreationRefusalKind.Unavailable, RecordCreationRefusal.CheckFailedCode, InternalError, ServerError);
        Add(RecordCreationRefusalKind.Unavailable, RecordCreationRefusal.OwnerUnavailableCode, InternalError, ServerError);

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
        var mapped = OfficeService.MapQuickCreateRefusal(Refusal(row.Kind, row.Code, row.WhoAmIFailure, row.RefusedColumn), "matter", AssignedTo);

        mapped.Code.Should().Be(row.OfficeCode);
        mapped.Status.Should().Be(row.Status);
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
            .Concat([
                ChildRecordEndpoints.CallerUnresolvedReasonCode,
                ProvisionProjectEndpoint.ReasonCreatorNoAccess,
                ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable,
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
        var mapped = OfficeService.MapQuickCreateRefusal(Refusal(kind, "record_creation.a_code_no_one_mapped", null, null), "project", AssignedTo);

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
            Refusal((RecordCreationRefusalKind)999, RecordCreationRefusal.NotFoundCode, null, AssignedTo), "matter", AssignedTo);

        (mapped.Code, mapped.Status).Should().Be((InternalError, ServerError));
    }

    [Fact]
    public void TheAssignedToRefusal_NeverReadsAsMatterNotFound()
    {
        var mapped = OfficeService.MapQuickCreateRefusal(
            Refusal(RecordCreationRefusalKind.NotFoundOrDenied, RecordCreationRefusal.NotFoundCode, null, AssignedTo), "matter", AssignedTo);

        mapped.Detail.Should().Contain("Assigned To").And.NotContainEquivalentOf("not found");
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

    /// <summary>Every <c>const string</c> a type declares (public or not).</summary>
    private static IEnumerable<string> ConstantsOf(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    /// <summary>The isolated create's completion codes (<c>SecureRootInheritance.Reason*</c>).</summary>
    private static IEnumerable<string> InheritReasonCodes() =>
        typeof(SecureRootInheritance).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string) && f.Name.StartsWith("Reason", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(c => c.StartsWith(OfficeService.SecureInheritReasonPrefix, StringComparison.Ordinal));
}
