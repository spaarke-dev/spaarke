using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Membership;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Api.Communication;

/// <summary>
/// fm task 056b: <see cref="CommunicationRecordAuthorizationHost"/> (the REAL send route, filters and <c>CommunicationService</c>)
/// with two module boundaries replaced: the POA share read (<see cref="IDataverseRecordShareService"/>, a mock the pair-shape rule
/// reads), and the private-message gate's Dataverse-facing seams (<see cref="ScriptedPrivateMessageGate"/>: the ordering, the
/// thread and reply checks and the answer mapping are the production <see cref="PrivateMessageSendGate"/>).
/// </summary>
public sealed class PrivateMessageSendHost : IDisposable
{
    public const string ThreadSet = "sprk_communicationthreads";
    public const string ThreadTable = "sprk_communicationthread";
    public const int ThreadTypeDirect = 100000001;
    public const int ThreadTypeRecordAnchored = 100000000;
    public const int ReadMask = 1;

    public static readonly Guid Caller = CommunicationRecordAuthorizationHost.CallerSystemUserId;
    public static readonly Guid Recipient = Guid.Parse("05605605-0560-4560-8560-0000000000b2");

    public CommunicationRecordAuthorizationHost Host { get; } = new();

    public Mock<IDataverseRecordShareService> Shares { get; } = new();

    public GateScript Script { get; } = new();

    /// <summary>Every row the send created (the prepared <c>sprk_communication</c> among them).</summary>
    public List<DataverseEntity> Created { get; } = new();

    /// <summary>Every app-only update (logical name, id, fields): the thread filing write among them.</summary>
    public List<(string Table, Guid Id, Dictionary<string, object> Fields)> Updates { get; } = new();

    private readonly Dictionary<Guid, DataverseEntity> _threads = new();
    private readonly Dictionary<Guid, List<DataversePrincipalAccess>> _threadShares = new();
    private readonly HashSet<Guid> _faultingThreads = new();
    private readonly WebApplicationFactory<Program> _factory;

    public PrivateMessageSendHost()
    {
        _factory = Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataverseRecordShareService>();
            services.AddSingleton(Shares.Object);
            services.RemoveAll<PrivateMessageSendGate>();
            services.AddScoped<PrivateMessageSendGate>(sp => new ScriptedPrivateMessageGate(
                Script, sp.GetRequiredService<IGenericEntityService>(), Shares.Object));
        }));
    }

    /// <summary>Restores the host's doubles, then a world with no threads, every gate seam answering "allowed".</summary>
    public void Reset()
    {
        Host.Reset();
        Shares.Reset();
        Script.Reset();
        Created.Clear();
        Updates.Clear();
        _threads.Clear();
        _threadShares.Clear();
        _faultingThreads.Clear();

        Host.Entities.Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback((DataverseEntity e, CancellationToken _) => Created.Add(e))
            .ReturnsAsync(() => Guid.NewGuid());
        Host.Entities.Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback((string table, Guid id, Dictionary<string, object> fields, CancellationToken _) => Updates.Add((table, id, fields)))
            .Returns(Task.CompletedTask);
        Host.Entities
            .Setup(e => e.RetrieveAsync(ThreadTable, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                _faultingThreads.Contains(id)
                    ? throw new HttpRequestException("thread read faulted")
                    : _threads.TryGetValue(id, out var t) ? t : new DataverseEntity(ThreadTable, Guid.Empty));
        Shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync(ThreadTable, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, CancellationToken _) =>
                (IReadOnlyList<DataversePrincipalAccess>)(_threadShares.TryGetValue(id, out var s) ? s : new List<DataversePrincipalAccess>()));
        Shares
            .Setup(s => s.GetPrincipalAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, CancellationToken _) =>
                (IReadOnlyList<DataversePrincipalAccess>)(_threadShares.TryGetValue(id, out var s) ? s : new List<DataversePrincipalAccess>()));
    }

    /// <summary>
    /// A Direct thread as <c>POST /threads/direct</c> creates it — owned by <paramref name="owner"/>, Open, no regarding, one Read
    /// share to <paramref name="other"/> — visible to the caller (the send route's thread filter).
    /// </summary>
    public Guid PairThread(Guid? owner = null, Guid? other = null, bool defaultThread = false, int threadType = ThreadTypeDirect)
    {
        var id = Guid.NewGuid();
        var row = new DataverseEntity(ThreadTable, id)
        {
            ["sprk_threadtype"] = new OptionSetValue(threadType),
            ["ownerid"] = new EntityReference("systemuser", owner ?? Caller),
            ["sprk_privacystate"] = new OptionSetValue((int)ThreadPrivacyState.Open),
        };
        if (defaultThread)
            row["sprk_isdefaultthread"] = true;
        _threads[id] = row;
        _threadShares[id] = new List<DataversePrincipalAccess>
        {
            new(DataversePrincipalRef.User(other ?? Recipient), ReadMask, DateTimeOffset.UtcNow),
        };
        VisibleThread(id);
        return id;
    }

    /// <summary>A thread the caller can see whose shape read faults.</summary>
    public Guid FaultingThread()
    {
        var id = Guid.NewGuid();
        _faultingThreads.Add(id);
        VisibleThread(id);
        return id;
    }

    /// <summary>A record-anchored thread (a matter's) the caller can see.</summary>
    public Guid RecordThread(Guid matter)
    {
        var id = Guid.NewGuid();
        _threads[id] = new DataverseEntity(ThreadTable, id)
        {
            ["sprk_threadtype"] = new OptionSetValue(ThreadTypeRecordAnchored),
            ["ownerid"] = new EntityReference("team", Guid.NewGuid()),
            ["sprk_privacystate"] = new OptionSetValue((int)ThreadPrivacyState.Open),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
        };
        VisibleThread(id, ("sprk_regardingmatter", matter));
        return id;
    }

    /// <summary>A message (as a reply parent) filed in <paramref name="thread"/>, readable app-only.</summary>
    public void MessageInThread(Guid message, Guid thread) =>
        Host.Entities
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"
                                            && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_communicationid"
                                                                              && c.Values.Contains(message))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity>
            {
                new("sprk_communication", message) { ["sprk_communicationthread"] = new EntityReference(ThreadTable, thread) },
            }));

    private void VisibleThread(Guid thread, params (string Column, Guid RecordId)[] regarding)
    {
        var columns = new Dictionary<string, object?> { ["sprk_communicationthreadid"] = thread.ToString() };
        foreach (var field in Sprk.Bff.Api.Services.Communication.Engine.RegardingFieldMap.All
                     .Where(x => x.RegardingField != "sprk_regardingreportcard"))
            columns[$"_{field.RegardingField}_value"] = null;
        foreach (var (column, recordId) in regarding)
            columns[$"_{column}_value"] = recordId.ToString();
        Host.Query.Visible(ThreadSet, thread, JsonSerializer.Serialize(columns));
    }

    /// <summary>A private-message send body: no recipients, one context record, into <paramref name="thread"/>.</summary>
    public static object PrivateBody(
        Guid? thread,
        string entityType = "sprk_matter",
        Guid? recordId = null,
        string type = "Message",
        string[]? to = null,
        string[]? cc = null,
        string[]? bcc = null,
        (string Type, Guid Id)? association = null,
        Guid? inheritFrom = null,
        string? inReplyTo = null,
        bool withContext = true) => new
        {
            to = to ?? Array.Empty<string>(),
            cc,
            bcc,
            subject = "About the matter",
            body = "Only for you.",
            communicationType = type,
            threadId = thread,
            associations = association is { } a ? new object[] { new { entityType = a.Type, entityId = a.Id } } : null,
            inheritRegardingFromCommunicationId = inheritFrom,
            inReplyToMessageId = inReplyTo,
            privateContext = withContext ? new { entityType, id = recordId ?? Guid.NewGuid() } : null,
        };

    public async Task<HttpResponseMessage> SendAsync(object body)
    {
        using var client = _factory.CreateClient();
        return await client.SendAsync(CommunicationRecordAuthorizationHost.Request(HttpMethod.Post, "/api/communications/send", body));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Host.Dispose();
    }
}

/// <summary>What the scripted gate's seams answer, and every question asked of them, in order.</summary>
public sealed class GateScript
{
    public AccessRights SenderRights { get; set; }
    public Exception? SenderRightsFault { get; set; }
    public AccessRights? RecipientRights { get; set; }
    public Dictionary<Guid, SecureShareWallOutcome> Wall { get; } = new();
    public bool? Restricted { get; set; }
    public Dictionary<Guid, bool?> External { get; } = new();
    public bool? ColumnsPresent { get; set; }

    /// <summary>"sender_rights {set}({id})", "recipient_rights {user} {set}({id})", "wall {user}", "restricted", "external {user}", "columns".</summary>
    public List<string> Calls { get; } = new();

    public void Reset()
    {
        SenderRights = AccessRights.Read;
        SenderRightsFault = null;
        RecipientRights = AccessRights.Read;
        Wall.Clear();
        Restricted = false;
        External.Clear();
        ColumnsPresent = true;
        Calls.Clear();
    }

    public bool AskedAboutRecipient => Calls.Any(c => c.StartsWith("recipient_rights", StringComparison.Ordinal)
                                                     || c == $"wall {PrivateMessageSendHost.Recipient}"
                                                     || c == $"external {PrivateMessageSendHost.Recipient}");
}

/// <summary>The production <see cref="PrivateMessageSendGate"/> with its Dataverse-facing seams answered by a <see cref="GateScript"/>.</summary>
internal sealed class ScriptedPrivateMessageGate : PrivateMessageSendGate
{
    private readonly GateScript _script;

    public ScriptedPrivateMessageGate(GateScript script, IGenericEntityService entities, IDataverseRecordShareService shares)
        : base(null!, null!, null!, entities, shares, NullLogger<PrivateMessageSendGate>.Instance) =>
        _script = script;

    internal override Task<AccessRights> SenderRightsAsync(string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct)
    {
        _script.Calls.Add($"sender_rights {entitySet}({recordId})");
        return _script.SenderRightsFault is { } fault ? Task.FromException<AccessRights>(fault) : Task.FromResult(_script.SenderRights);
    }

    internal override Task<AccessRights?> RecipientRightsAsync(Guid user, string entitySet, Guid recordId, CancellationToken ct)
    {
        _script.Calls.Add($"recipient_rights {user} {entitySet}({recordId})");
        return Task.FromResult(_script.RecipientRights);
    }

    internal override Task<SecureShareWallOutcome> WallAsync(string table, Guid recordId, Guid user, CancellationToken ct)
    {
        _script.Calls.Add($"wall {user}");
        return Task.FromResult(_script.Wall.GetValueOrDefault(user, SecureShareWallOutcome.NotSecure));
    }

    internal override Task<bool?> EffectiveRestrictedAsync(string table, Guid recordId, CancellationToken ct)
    {
        _script.Calls.Add("restricted");
        return Task.FromResult(_script.Restricted);
    }

    internal override Task<bool?> IsExternalAsync(Guid user, CancellationToken ct)
    {
        _script.Calls.Add($"external {user}");
        return Task.FromResult(_script.External.TryGetValue(user, out var v) ? v : false);
    }

    internal override Task<bool?> ReferenceColumnsPresentAsync(CancellationToken ct)
    {
        _script.Calls.Add("columns");
        return Task.FromResult(_script.ColumnsPresent);
    }
}
