using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Intake;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.AccessControl;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Intake;

/// <summary>
/// The shared arrangement of the intake approval tests (spaarke-intake-pipeline-r1 task 017): a run brought to Awaiting approval by the
/// REAL orchestrator over <see cref="IntakeRunTestBed"/>'s in-memory Dataverse, the REAL <see cref="IntakeApprovalService"/> and
/// <see cref="IntakeRunStore"/>, and doubles only at the boundaries the service asks: the caller's rights on the request (the probe's
/// virtual question), the caller's user id, their team memberships, their linked contact, the SPE version read and the job queue.
/// Callers are told apart by their bearer token, so two people can act in one test.
/// </summary>
internal sealed class IntakeApprovalTestBed
{
    public static readonly Guid TeamId = Guid.Parse("01700000-0000-4000-8000-0000000000f1");
    public static readonly Guid OtherTeamId = Guid.Parse("01700000-0000-4000-8000-0000000000f2");
    public const string DriveId = "b!drive-017";
    public const string ItemId = "01ITEM017";
    public const string ApprovedVersion = "3.0";

    /// <summary>The default caller: an owning-team member with Read and Write and one linked contact.</summary>
    public static readonly Caller Alice = new("token-alice", Guid.Parse("01700000-0000-4000-8000-00000000a001"), Guid.Parse("01700000-0000-4000-8000-00000000c001"));

    /// <summary>A second owning-team member.</summary>
    public static readonly Caller Bob = new("token-bob", Guid.Parse("01700000-0000-4000-8000-00000000a002"), Guid.Parse("01700000-0000-4000-8000-00000000c002"));

    public IntakeRunTestBed Run { get; } = new();
    public Mock<CallerRecordAccessProbe> Probe { get; }
    public Mock<IIdentityNormalizationService> Identity { get; } = new();
    public Mock<ISpeFileOperations> Files { get; } = new();

    /// <summary>Per caller: the rights the probe answers on the request (default Read | Write).</summary>
    public Dictionary<string, AccessRights> Rights { get; } = new();

    /// <summary>Per caller: the probe throws instead of answering.</summary>
    public HashSet<string> RightsFault { get; } = new();

    /// <summary>Per user: the teams the identity read answers (default: the owning team).</summary>
    public Dictionary<Guid, IReadOnlyList<Guid>> Teams { get; } = new();

    /// <summary>Per user: the identity read reports its team read faulted.</summary>
    public HashSet<Guid> TeamsFault { get; } = new();

    /// <summary>Per user: the contact resolution (default: their one contact).</summary>
    public Dictionary<Guid, UserContactResolution> Contacts { get; } = new();

    public Guid RunId { get; private set; }

    public IntakeApprovalTestBed()
    {
        var configuration = new ConfigurationBuilder().Build();
        Probe = new Mock<CallerRecordAccessProbe>(MockBehavior.Loose, new HttpClient(), configuration, NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);
        Probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((string? token, string set, Guid id, CancellationToken _) =>
            {
                if (token is not null && RightsFault.Contains(token))
                {
                    throw new HttpRequestException("probe fault");
                }

                // Only the request's own entity set is ever asked; any other question is an empty answer (fail closed).
                return Task.FromResult(set == IntakeApprovalService.RequestEntitySet && token is not null
                    ? Rights.GetValueOrDefault(token, AccessRights.Read | AccessRights.Write)
                    : AccessRights.None);
            });
        Probe.Setup(p => p.GetCallerSystemUserIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((string? token, CancellationToken _) => Task.FromResult(Caller.ByToken(token)?.UserId));

        Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, CancellationToken _) => Task.FromResult(new PersonIdentity(user, TeamIds: Teams.GetValueOrDefault(user, [TeamId]))
            {
                Faults = TeamsFault.Contains(user) ? IdentityReadFaults.Teams : IdentityReadFaults.None,
            }));

        Files.Setup(f => f.GetCurrentVersionIdAsync(DriveId, ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(ApprovedVersion);

        // The request belongs to the owning team; the output document has its SPE location.
        var request = Run.Dataverse.Get(IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId);
        request["ownerid"] = new EntityReference("team", TeamId);
        Run.Dataverse.Seed(request);
        Run.Dataverse.Seed(new Entity(IntakeRunStore.DocumentEntity, IntakeRunTestBed.OutputDocumentId)
        {
            [IntakeRunStore.DocumentDriveId] = DriveId,
            [IntakeRunStore.DocumentItemId] = ItemId,
        });
    }

    /// <summary>Runs the real orchestrator until the run waits in Awaiting approval.</summary>
    public async Task<IntakeApprovalTestBed> AwaitingApprovalAsync()
    {
        RunId = await Run.CreateRunAsync();
        await Run.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(RunId), CancellationToken.None);
        return this;
    }

    /// <summary>The service as production builds it, with the caller's contact answered by <see cref="Contacts"/>.</summary>
    public IntakeApprovalService Service()
    {
        var service = new Mock<IntakeApprovalService>(
            Run.Store,
            Probe.Object,
            Identity.Object,
            AssignedAccessTestDoubles.Representation(new AssignedAccessTestDoubles.FakeAssignedAccessStore()),
            Files.Object,
            Run.Jobs.Object,
            Run.Options,
            Run.Logs.CreateLogger<IntakeApprovalService>())
        { CallBase = true };
        service.Setup(s => s.ResolveCallerContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, CancellationToken _) => Task.FromResult(Contacts.TryGetValue(user, out var answer)
                ? answer
                : Caller.ByUser(user) is { } caller
                    ? new UserContactResolution(UserContactStatus.One, caller.ContactId, false, null, null)
                    : new UserContactResolution(UserContactStatus.None, null, false, null, null)));
        return service.Object;
    }

    public static HttpContext Http(Caller? caller)
    {
        var http = new DefaultHttpContext();
        if (caller is not null)
        {
            http.Request.Headers.Authorization = "Bearer " + caller.Token;
        }

        return http;
    }

    /// <summary>The decision commit route's request to an intake executor, as the caller.</summary>
    public static DecisionActionRequest ActionRequest(Caller caller, IReadOnlyDictionary<string, string?>? parameters = null) =>
        new(Http(caller), Guid.NewGuid(),
            new DecisionRecordRef(IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId),
            new DecisionCoreRecord(IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId),
            MatterId: null,
            parameters ?? new Dictionary<string, string?>());

    public Guid? ClaimedBy() =>
        Run.Dataverse.Get(IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId).GetAttributeValue<EntityReference>(IntakeRunStore.RequestClaimedBy)?.Id;

    /// <summary>Sets the claimer directly (a claim that happened earlier, or an edit on the form).</summary>
    public void SetClaimer(Guid? contactId)
    {
        var request = Run.Dataverse.Get(IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId);
        if (contactId is { } id)
        {
            request[IntakeRunStore.RequestClaimedBy] = new EntityReference("contact", id);
        }
        else
        {
            request.Attributes.Remove(IntakeRunStore.RequestClaimedBy);
        }

        Run.Dataverse.Seed(request);
    }

    public void SetRequestColumn(string column, object? value)
    {
        var request = Run.Dataverse.Get(IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId);
        if (value is null)
        {
            request.Attributes.Remove(column);
        }
        else
        {
            request[column] = value;
        }

        Run.Dataverse.Seed(request);
    }

    /// <summary>Every write after <paramref name="from"/>.</summary>
    public IReadOnlyList<WriteRecord> WritesSince(int from) => Run.Dataverse.Writes.Skip(from).ToList();

    internal sealed record Caller(string Token, Guid UserId, Guid ContactId)
    {
        public static Caller? ByToken(string? token) => token == Alice.Token ? Alice : token == Bob.Token ? Bob : null;

        public static Caller? ByUser(Guid user) => user == Alice.UserId ? Alice : user == Bob.UserId ? Bob : null;
    }
}
