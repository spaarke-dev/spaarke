using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The REAL <see cref="DocumentIndexParentResolver"/> (unified-access-control-r2 task 177, #1510) over its seams — for tests
/// that construct an indexing seam (the job handler, the enqueuer) by hand, and for the rule's own tests. The class is sealed
/// with non-virtual methods and its decision is the behaviour those seams depend on, so it is substituted only at its seams:
/// <see cref="IGenericEntityService"/> (the document row, an event's row, the filing walk) and the secure-flag reader
/// (<see cref="GrantPolicyTestDoubles.FlagStubParticipationService"/>, the subclass-and-override double every flag test uses).
/// Without a Dataverse double the row is not found, which is "no parent" — every hand-built seam keeps its previous behaviour.
/// </summary>
internal static class TestDocumentIndexParentResolver
{
    /// <summary>The columns an event carries that name its root (live: all four sprk_regarding{core}).</summary>
    private static readonly IReadOnlySet<string> EventRootColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_regardingproject", "sprk_regardingmatter", "sprk_regardingworkassignment", "sprk_regardingservicerequest",
    };

    /// <param name="dataverse">The document / event rows and the filing walk; default: nothing is found.</param>
    /// <param name="flags">The secure-flag reader; default: every record is not secure.</param>
    public static DocumentIndexParentResolver Over(
        IGenericEntityService? dataverse = null,
        ExternalParticipationService? flags = null)
    {
        var rows = dataverse ?? Mock.Of<IGenericEntityService>();
        return new DocumentIndexParentResolver(
            rows,
            new CoreAncestorResolver(
                rows,
                (string _, CancellationToken _) => Task.FromResult(EventRootColumns),
                NullLogger<CoreAncestorResolver>.Instance),
            flags ?? new GrantPolicyTestDoubles.FlagStubParticipationService(new RootRecordFlags(IsSecure: false, IsRestricted: false)),
            NullLogger<DocumentIndexParentResolver>.Instance);
    }
}
