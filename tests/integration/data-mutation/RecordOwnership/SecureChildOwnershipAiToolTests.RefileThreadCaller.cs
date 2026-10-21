using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// #2143 round 7 (F1): an email re-filed through <c>PATCH /api/communications/{id}/filing</c> (the ONE re-file core,
/// <see cref="Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync"/>) has its thread follow the filing, and that thread move acts
/// for the PERSON who re-filed it — the REAL resolver decides both the re-file and the thread move, the REAL
/// <see cref="ThreadResolver"/> reconciles the thread inside the REAL reconciler's step after the re-file.
/// </summary>
/// <remarks>
/// Before round 7 the re-file of an email regarding secure S, sitting in S's own thread, asked no F3 (the thread kept S),
/// and the thread move after it acted for nobody: it was refused (no caller) and swallowed, so the email ended regarding the
/// ordinary record, in S's thread, Secure-owned for good.
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid RefiledEmail = Guid.Parse("a2143000-0000-4000-8000-0000000000c7");
    private static readonly Guid SecureMattersThread = Guid.Parse("a2143000-0000-4000-8000-0000000000e7");
    private static readonly Guid OrdinaryMattersThread = Guid.Parse("a2143000-0000-4000-8000-0000000000e8");
    private static readonly Guid SecondOrdinaryMatter = Guid.Parse("a2143000-0000-4000-8000-0000000000a9");
    private static readonly Guid SecondOrdinaryMattersThread = Guid.Parse("a2143000-0000-4000-8000-0000000000e9");

    [Fact(DisplayName = "#2143 r7 F1: a Full Access holder re-filing an email off secure S, in S's own thread, onto ordinary O leaves it owned by O's team in O's thread")]
    public async Task FilingRoute_AFullAccessHolderRefilesAnEmailOffItsSecureRecordsThread_ItEndsInTheOrdinaryRecordsThread_OwnedByItsTeam()
    {
        AnEmailIn(SecureMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _user.FullAccessOn.Add(SecureMatter);

        var result = await RefileTo(OrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(OrdinaryMatter);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id
            .Should().Be(OrdinaryMattersThread, "the thread move acted for the person who re-filed it, and was permitted");
        email.GetAttributeValue<EntityReference>("owningteam")?.Id.Should().Be(Directory.ChildTeam, "O's team");
        _world.Assignments.Should().Equal(new[] { ("sprk_communication", RefiledEmail, Directory.ChildTeam) },
            "one owner write, once the thread followed: never an ordinary owner while it sat in S's thread");
    }

    [Fact(DisplayName = "#2143 r7 F1: a caller without Full Access on S re-filing an email off S, in S's own thread, is refused 403 not_permitted and nothing is written")]
    public async Task FilingRoute_ACallerWithoutFullAccessRefilesAnEmailOffItsSecureRecordsThread_IsRefused_AndNothingIsWritten()
    {
        AnEmailIn(SecureMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);

        var result = await RefileTo(OrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden, Detail(result));
        ReasonCode(result).Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        _user.Patches.Should().BeEmpty("refused before the caller's PATCH");
        _world.Assignments.Should().BeEmpty();
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(SecureMatter);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id.Should().Be(SecureMattersThread);
    }

    [Fact(DisplayName = "#2143 r7 F1: an ordinary-to-ordinary re-file whose thread moves is unaffected: no F3, it ends in the new record's thread")]
    public async Task FilingRoute_AnOrdinaryToOrdinaryRefile_ItsThreadMoves_AsBefore()
    {
        AnEmailIn(OrdinaryMatter, OrdinaryMattersThread, Directory.ChildBu, Directory.ChildTeam);

        var result = await RefileTo(SecondOrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id.Should().Be(SecondOrdinaryMattersThread);
        email.GetAttributeValue<EntityReference>("owningteam")?.Id.Should().Be(Directory.GeneralTeam, "the second matter's team");
        _user.FullAccessOn.Should().BeEmpty("no Full Access is needed: no secure record is left");
    }

    [Fact(DisplayName = "#2143 r7b: re-filing an email between ordinary records while it sits in secure S2's thread, by a caller without Full Access on S2, is refused 403 not_permitted and nothing is written")]
    public async Task FilingRoute_AnOrdinaryRefileOfAnEmailInASecureRecordsThread_ByACallerWithoutFullAccessOnIt_IsRefused()
    {
        AnEmailIn(OrdinaryMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);

        var result = await RefileTo(SecondOrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden, Detail(result));
        ReasonCode(result).Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        _user.Patches.Should().BeEmpty("refused before the caller's PATCH: the thread move it would cause leaves S2");
        _world.Assignments.Should().BeEmpty();
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(OrdinaryMatter);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id.Should().Be(SecureMattersThread);
    }

    [Fact(DisplayName = "#2143 r7b: the same re-file by a Full Access holder on S2 ends in the new record's thread, owned by its team")]
    public async Task FilingRoute_AnOrdinaryRefileOfAnEmailInASecureRecordsThread_ByAFullAccessHolderOnIt_EndsInTheNewRecordsThread()
    {
        AnEmailIn(OrdinaryMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _user.FullAccessOn.Add(SecureMatter);

        var result = await RefileTo(SecondOrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(SecondOrdinaryMatter);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id.Should().Be(SecondOrdinaryMattersThread);
        email.GetAttributeValue<EntityReference>("owningteam")?.Id.Should().Be(Directory.GeneralTeam, "the second matter's team");
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An email regarding <paramref name="matter"/>, sitting in <paramref name="thread"/> (that matter's default record thread),
    /// owned by <paramref name="owningTeam"/> in <paramref name="owningBu"/> — and the default record threads of the ordinary
    /// matters it can be re-filed onto. Every row is in the resolver's directory, which the thread resolver reads too.
    /// </summary>
    private void AnEmailIn(Guid matter, Guid thread, Guid owningBu, Guid owningTeam)
    {
        _world.WithRecord("sprk_matter", SecondOrdinaryMatter, Directory.GeneralBu, owningTeam: Directory.GeneralTeam);
        RecordThread(SecureMattersThread, SecureMatter, Directory.SecureBu, Directory.SecureNamedTeam);
        RecordThread(OrdinaryMattersThread, OrdinaryMatter, Directory.ChildBu, Directory.ChildTeam);
        RecordThread(SecondOrdinaryMattersThread, SecondOrdinaryMatter, Directory.GeneralBu, Directory.GeneralTeam);
        _world.WithRecord("sprk_communication", RefiledEmail, owningBu, owningTeam: owningTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
            [ThreadResolver.ThreadLookupOnCommunication] = new EntityReference("sprk_communicationthread", thread),
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email),
            ["createdby"] = new EntityReference("systemuser", Guid.Parse("a2143000-0000-4000-8000-0000000000ee")),
        });
        if (owningTeam == Directory.SecureNamedTeam)
            ShareWorld.SecureChild("sprk_communication", RefiledEmail, ("sprk_regardingmatter", "sprk_matter", matter));
        else
            ShareWorld.OrdinaryChild("sprk_communication", RefiledEmail, ("sprk_regardingmatter", "sprk_matter", matter));
    }

    /// <summary>A matter's default record thread: Record-anchored, Open, active, owned by the matter's team.</summary>
    private void RecordThread(Guid thread, Guid matter, Guid owningBu, Guid owningTeam) =>
        _world.WithRecord("sprk_communicationthread", thread, owningBu, owningTeam: owningTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
            ["sprk_regardingrecordid"] = matter.ToString(),
            ["sprk_threadtype"] = new OptionSetValue(100000000),
            ["sprk_privacystate"] = new OptionSetValue(100000000),
            ["sprk_isdefaultthread"] = true,
            ["statecode"] = new OptionSetValue(0),
        });

    /// <summary>The filing route's re-file of the email's regarding onto <paramref name="matter"/>, as the caller.</summary>
    private Task<Microsoft.AspNetCore.Http.IResult> RefileTo(Guid matter) =>
        Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            "sprk_communication", RefiledEmail,
            Payload(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})" }),
            _user, _world.Resolver(), Restamper(), Children(ThreadsOverTheDirectory()), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None, filingOnly: true);

    /// <summary>
    /// The REAL thread resolver over the resolver's directory: rows are read from it, a matter's default thread is found by its
    /// key, and the email's thread-lookup stamp lands on its row.
    /// </summary>
    private ThreadResolver ThreadsOverTheDirectory()
    {
        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => _world.Row(table, id));
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) => DefaultThreadOf(query));
        entities
            .Setup(e => e.UpdateAsync("sprk_communication", RefiledEmail, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) =>
            {
                foreach (var (column, value) in fields)
                    _world.Row("sprk_communication", RefiledEmail)[column] = value;
            })
            .Returns(Task.CompletedTask);
        return new ThreadResolver(
            Array.Empty<IThreadKeyStrategy>(), entities.Object, _world.Resolver(), NullLogger<ThreadResolver>.Instance);
    }

    /// <summary>A default-thread lookup (FindDefaultThreadAsync: keyed on <c>sprk_regardingrecordid</c>) answers the matter's thread.</summary>
    private static EntityCollection DefaultThreadOf(QueryExpression query)
    {
        var key = query.Criteria.Conditions
            .FirstOrDefault(c => c.AttributeName == "sprk_regardingrecordid")?.Values.FirstOrDefault()?.ToString();
        var thread = key == OrdinaryMatter.ToString() ? OrdinaryMattersThread
            : key == SecondOrdinaryMatter.ToString() ? SecondOrdinaryMattersThread
            : key == SecureMatter.ToString() ? SecureMattersThread
            : (Guid?)null;
        return thread is { } id && query.EntityName == "sprk_communicationthread"
            ? new EntityCollection(new List<Entity> { new("sprk_communicationthread", id) })
            : new EntityCollection();
    }
}
