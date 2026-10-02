using Moq;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// <see cref="IIdentityNormalizationService"/> stand-ins for writers that look up a user's LINKED contact
/// (unified-access-control-r2 task 152: TaskActionCore, RecordCreationService, POST /api/v1/events).
/// </summary>
internal static class IdentityNormalizationFixtures
{
    /// <summary>Every user resolves with NO linked contact — the writer falls back (or leaves the column blank).</summary>
    public static IIdentityNormalizationService NoLinkedContact() => WithContact(null).Object;

    /// <summary>Every user resolves to <paramref name="contactId"/> (null = no link).</summary>
    public static Mock<IIdentityNormalizationService> WithContact(Guid? contactId)
    {
        var mock = new Mock<IIdentityNormalizationService>();
        mock.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new PersonIdentity(id, ContactId: contactId));
        return mock;
    }
}
