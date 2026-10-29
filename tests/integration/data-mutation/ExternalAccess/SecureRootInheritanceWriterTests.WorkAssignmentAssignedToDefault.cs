using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;
using Harness = Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.Harness;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// field-mapping-server-write-path-r1 task 037 fix round 2 (UAC-r2's review, acceptance criterion 6): the copied Assigned To
/// default on the SECURE-parent create — <c>POST /api/v1/child-records/sprk_workassignment</c> under a secure matter, where
/// the REAL <see cref="Sprk.Bff.Api.Services.Access.SecureRootFilingGate"/> plans an isolated create and
/// <c>OwnedChildWrite.CreateIntoIsolationAsync</c> writes the row. The default must be in THAT create body (flagged secure in
/// the create itself), and the REAL "Assigned *" materialisation that runs after the completion must read it as a
/// suggestion only.
/// </summary>
public partial class SecureRootInheritanceWriterTests
{
    private static readonly Guid FmAssignedToInternal = Guid.Parse("a0370000-0000-4000-8000-0000000000d1");

    [Fact]
    public async Task ChildRecordRoute_AWorkAssignmentUnderASecureMatter_IsCreatedIntoIsolationWithTheCopiedAssignedTo_AndItIsOnlySuggested()
    {
        var (secure, _) = Matters();
        var harness = new Harness { Participations = new(new RootRecordFlags(IsSecure: true, IsRestricted: false)) };
        harness.Contact(FmAssignedToInternal);
        var services = new ServiceCollection();
        services.AddScoped(_ => harness.Materializer);

        // The created row as Dataverse holds it: what the materializer's root read returns.
        var appOnly = AppCreatesIntoTheWorld(afterCreate: id =>
        {
            var fields = _writes.Single(w => w.Id == id).Fields;
            harness.Store.Root(ExternalGrantRootType.WorkAssignment, id);
            foreach (var key in fields.Keys.Where(k =>
                         k.StartsWith("sprk_Assigned", StringComparison.Ordinal) && k.EndsWith("@odata.bind", StringComparison.Ordinal)))
            {
                if (BindPath(fields[key]) is { } path && path.StartsWith("/contacts(", StringComparison.Ordinal))
                {
                    harness.Store.Assign(ExternalGrantRootType.WorkAssignment, id, key[..^"@odata.bind".Length].ToLowerInvariant(),
                        Guid.Parse(path["/contacts(".Length..^1]));
                }
            }
        });
        var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["sprk_name"] = "Review the lease",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({secure:D})",
            ["sprk_AssignedToInternal@odata.bind"] = $"/contacts({FmAssignedToInternal:D})",
        });

        IResult result;
        using (var scope = _fixture.Services.CreateScope())
        {
            result = await ChildRecordEndpoints.CreateAsync(
                "sprk_workassignment", payload,
                new SecureChildOwnershipAiToolTests.ScriptedUserClient(Creator),
                new RecordOwnershipResolver(
                    SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                    NullLogger<RecordOwnershipResolver>.Instance),
                appOnly.Object,
                new StampWorld().Restamper,
                scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(),
                _gate,
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new DefaultHttpContext(), NullLogger<Program>.Instance, CancellationToken.None);
        }

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var (_, created, fields) = _writes.Should().ContainSingle().Subject;
        fields["sprk_issecure"].Should().Be(true, "created INTO isolation: flagged in the create itself");
        BoundId(fields, "ownerid@odata.bind").Should().Be(SecureTeam, "the isolated create, owned by the named team");
        BindPath(fields.GetValueOrDefault("sprk_AssignedTo@odata.bind")).Should().Be($"/contacts({FmAssignedToInternal:D})",
            "FR-36 on the isolated create: the row's own Assigned To Internal, copied as-is, in the create body itself");
        var row = harness.Store.RowsOf(created, FmAssignedToInternal).Should().ContainSingle(r => r.SourceField == "sprk_assignedto",
            "the materializer evaluated the copied Assigned To on the isolated row").Subject;
        row.State.Should().Be(AssignedAccessState.PendingConfirmation, "Secure: a suggestion only");
        harness.Grants.ActiveRowsOf(created, FmAssignedToInternal).Should().BeEmpty();
        harness.Shares.Writes.Should().BeEmpty();
    }

    /// <summary>A bind's path as the create body holds it (a string, or the caller's JSON string).</summary>
    private static string? BindPath(object? value) =>
        value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value as string;
}
