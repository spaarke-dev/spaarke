// unified-access-control-r2 task 152 — shared fixtures for the owner-event publishing tests (the four create sites)
// and the publisher↔reconciliation key-parity tests.

using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership.Events;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;

/// <summary>Captures every event handed to it — the publisher boundary the create sites call.</summary>
internal sealed class RecordingMembershipEventPublisher : IMembershipEventPublisher
{
    public List<MembershipChangedEvent> Published { get; } = new();

    public Task PublishAsync(MembershipChangedEvent evt, CancellationToken ct)
    {
        Published.Add(evt);
        return Task.CompletedTask;
    }
}

internal static class OwnerEventTestKit
{
    public static readonly Guid HumanUserId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
    public static readonly Guid ApplicationUserId = Guid.Parse("bbbbbbbb-2222-2222-2222-bbbbbbbbbbbb");
    public static readonly Guid TeamId = Guid.Parse("cccccccc-3333-3333-3333-cccccccccccc");
    public static readonly Guid CallerOid = Guid.Parse("dddddddd-4444-4444-4444-dddddddddddd");

    /// <summary>
    /// App-only Dataverse stand-in: <paramref name="rowOwner"/> is the owner a read-back of any row returns;
    /// <see cref="ApplicationUserId"/> carries an <c>applicationid</c>, every other systemuser is a human.
    /// </summary>
    public static Mock<IGenericEntityService> Dataverse(EntityReference? rowOwner = null)
    {
        var mock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        mock.Setup(d => d.RetrieveAsync("systemuser", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
            {
                var user = new Entity("systemuser", id);
                if (id == ApplicationUserId)
                {
                    user["applicationid"] = Guid.Parse("eeeeeeee-5555-5555-5555-eeeeeeeeeeee");
                }
                return user;
            });
        mock.Setup(d => d.RetrieveAsync(
                It.Is<string>(e => e != "systemuser"), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) =>
            {
                var row = new Entity(entity, id);
                if (rowOwner is not null)
                {
                    row["ownerid"] = rowOwner;
                }
                return row;
            });
        return mock;
    }

    /// <summary>Reads a BFF source file (for the per-site wiring guards).</summary>
    public static string ReadSource(params string[] pathUnderBff)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return File.ReadAllText(Path.Combine(new[] { dir, "src", "server", "api", "Sprk.Bff.Api" }.Concat(pathUnderBff).ToArray())).Replace("\r\n", "\n");
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
