using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Domain.Dataverse;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3b: the ONE re-file core (<see cref="OwnedChildWrite.RefileAsync"/>) judges an
/// unfile on the row as its update leaves it. A Direct message re-filed from a matter to a contact is still linked to a record: it
/// is written, it is never refused for the Direct team, its participants are not granted and its owner does not move.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid DirectUnfileMessage = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b3");
    private static readonly Guid DirectUnfileContact = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003d1");
    private static readonly Guid DirectUnfileTeam = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003d2");

    [Theory(DisplayName = "fm 054 piece 3b: the re-file core moving a Direct message from a matter to a contact writes it, refuses nothing, grants nothing and moves no owner")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefileCore_ADirectMessageMovedFromAMatterToAContact_IsNotAnUnfile(bool lookupResolves)
    {
        _world.WithMessageFiledUnder(OrdinaryMatter, Directory.ChildBu, Directory.ChildTeam);
        var lookup = lookupResolves
            ? new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectUnfileTeam).Lookup()
            : DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.OwnerTeamNameConfigKey);
        var grants = new List<Guid>();

        var outcome = await OwnedChildWrite.RefileAsync(
            _user, _world.Resolver(directMessageOwnerTeam: lookup), "sprk_communication", DirectUnfileMessage, "sprk_communications",
            "sprk_communicationid",
            new DataverseWriteItemMapper.MappedItem(
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["sprk_RegardingMatter@odata.bind"] = null,
                    ["sprk_RegardingPerson@odata.bind"] = $"/contacts({DirectUnfileContact:D})",
                }),
                ["sprk_regardingmatter", "sprk_regardingperson"])
            {
                Lookups = [new DataverseWriteItemMapper.MappedLookup("sprk_regardingperson", "contact", "contacts", DirectUnfileContact)],
                ClearedColumns = ["sprk_regardingmatter"],
            },
            callerObjectId: null, CancellationToken.None, directOwner: DirectUnfileStep(lookup, grants));

        outcome.OwnerRefusal.Should().BeNull("a message re-filed to a contact is still linked to a record: no unfile");
        outcome.Written.Should().BeTrue();
        _user.Patches.Should().ContainSingle();
        grants.Should().BeEmpty("no participant is granted for a message that does not move");
        _world.Assignments.Should().BeEmpty();
        _world.Row("sprk_communication", DirectUnfileMessage).GetAttributeValue<EntityReference>("owningteam")?.Id
            .Should().Be(Directory.ChildTeam);
        _world.Row("sprk_communication", DirectUnfileMessage).GetAttributeValue<EntityReference>("sprk_regardingperson")?.Id
            .Should().Be(DirectUnfileContact);
    }

    [Fact(DisplayName = "fm 054 piece 3b pass 2: the re-file route unfiling a Direct message grants, assigns, grants again, and only then runs the step after a re-file")]
    public async Task RefileRoute_UnfilingADirectMessage_GrantsAssignsGrants_ThenRunsTheStepAfterTheRefile()
    {
        _world.WithMessageFiledUnder(OrdinaryMatter, Directory.ChildBu, Directory.ChildTeam);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectUnfileTeam).Lookup();
        var calls = new List<string>();
        var step = DirectUnfileStep(lookup, new List<Guid>(), calls);
        var threads = new Mock<IThreadResolver>();
        threads
            .Setup(t => t.ReconcileThreadForFilingAsync(
                It.IsAny<Guid>(), It.IsAny<FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(),
                It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("step after the re-file"))
            .Returns(Task.CompletedTask);
        var http = HttpContextOfCaller();
        http.RequestServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton(step).BuildServiceProvider();

        var result = await Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            "sprk_communication", DirectUnfileMessage, Payload(new() { ["sprk_RegardingMatter@odata.bind"] = null }), _user,
            _world.Resolver(directMessageOwnerTeam: lookup), Restamper(), Children(threads.Object), http,
            NullLogger<Program>.Instance, CancellationToken.None, filingOnly: true);

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _world.Row("sprk_communication", DirectUnfileMessage).GetAttributeValue<EntityReference>("owningteam")?.Id
            .Should().Be(DirectUnfileTeam);
        calls.Should().Equal("grant", "assign sprk_communication", "grant", "step after the re-file");
    }

    [Fact(DisplayName = "fm 054 piece 3b pass 3: the chat update tool clearing the contact of an email moves none of its parents, so no step after a re-file runs")]
    public async Task ChatUpdate_ClearingTheContactOfAnEmail_RunsNoStepAfterARefile()
    {
        _world.WithMessageLinkedOnlyToAContact(Directory.ChildBu, Directory.ChildTeam, CommunicationType.Email);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectUnfileTeam).Lookup();
        var calls = new List<string>();
        var threads = StepAfterRefileRecorded(calls);

        var result = await ChatUpdateWithStepAfterRefile(lookup, threads.Object, calls, ("sprk_regardingperson", JsonNull));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _world.Row("sprk_communication", DirectUnfileMessage).GetAttributeValue<EntityReference>("sprk_regardingperson").Should().BeNull();
        calls.Should().BeEmpty("no thread reconcile, no Restricted bar, no grant and no owner write");
        _world.Assignments.Should().BeEmpty();
        _world.Row("sprk_communication", DirectUnfileMessage).GetAttributeValue<EntityReference>("owningteam")?.Id
            .Should().Be(Directory.ChildTeam);
    }

    [Fact(DisplayName = "fm 054 piece 3b pass 3: the chat update tool clearing the last contact of a Direct message returns it to the Direct team, then runs the step after a re-file")]
    public async Task ChatUpdate_ClearingTheLastContactOfADirectMessage_MovesIt_ThenRunsTheStepAfterARefile()
    {
        _world.WithMessageLinkedOnlyToAContact(Directory.ChildBu, Directory.ChildTeam);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectUnfileTeam).Lookup();
        var calls = new List<string>();
        var threads = StepAfterRefileRecorded(calls);

        var result = await ChatUpdateWithStepAfterRefile(lookup, threads.Object, calls, ("sprk_regardingperson", JsonNull));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _world.Row("sprk_communication", DirectUnfileMessage).GetAttributeValue<EntityReference>("owningteam")?.Id
            .Should().Be(DirectUnfileTeam);
        calls.Should().Equal("grant", "assign sprk_communication", "grant", "step after the re-file");
    }

    private static Mock<IThreadResolver> StepAfterRefileRecorded(List<string> calls)
    {
        var threads = new Mock<IThreadResolver>();
        threads
            .Setup(t => t.ReconcileThreadForFilingAsync(
                It.IsAny<Guid>(), It.IsAny<FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(),
                It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("step after the re-file"))
            .Returns(Task.CompletedTask);
        return threads;
    }

    /// <summary>The chat update tool with the Direct step and a scope that resolves the REAL reconciler over the share world,
    /// its thread reconcile recorded.</summary>
    private Task<Sprk.Bff.Api.Services.Ai.ToolResult> ChatUpdateWithStepAfterRefile(
        DirectMessageOwnerTeam lookup, IThreadResolver threads, List<string> calls, params (string Column, JsonElement Value)[] item)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Children(threads));
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler(
                _user,
                new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp,
                NullLogger<Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler>.Instance,
                _world.Resolver(directMessageOwnerTeam: lookup),
                SecureRootFilingGateFixtures.NothingSecure(),
                scopes,
                directOwner: DirectUnfileStep(lookup, new List<Guid>(), calls))
            .ExecuteChatAsync(
                BuildChatInvocationContext(
                    toolArgumentsJson: JsonSerializer.Serialize(new
                    {
                        tablename = "sprk_communication",
                        recordId = DirectUnfileMessage,
                        item = item.ToDictionary(i => i.Column, i => i.Value),
                    })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler)), CancellationToken.None);
    }

    /// <summary>The real step over the directory: rows read by id, grants recorded.</summary>
    private DirectMessageOwnerStep DirectUnfileStep(DirectMessageOwnerTeam lookup, List<Guid> grants, List<string>? calls = null)
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
                q.Criteria.Conditions.Any(c => c.AttributeName == q.EntityName + "id") ? _world.Answer(q) : new EntityCollection());
        dataverse
            .Setup(d => d.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, Dictionary<string, object>, CancellationToken>((table, id, version, fields, _) =>
            {
                calls?.Add("assign " + table);
                _world.UpdateIfVersion(table, id, version, fields);
            })
            .Returns(Task.CompletedTask);
        var access = new Mock<IDirectThreadAccessService>();
        access
            .Setup(a => a.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ThreadAssignment, CancellationToken>((id, _, _) =>
            {
                grants.Add(id);
                calls?.Add("grant");
            })
            .Returns(Task.CompletedTask);
        return new DirectMessageOwnerStep(access.Object, dataverse.Object, lookup, NullLogger<DirectMessageOwnerStep>.Instance);
    }
}
