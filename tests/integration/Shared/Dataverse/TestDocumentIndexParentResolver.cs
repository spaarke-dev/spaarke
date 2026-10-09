using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// The REAL <see cref="DocumentIndexParentResolver"/> (unified-access-control-r2 task 177, #1510) over a Dataverse double —
/// for tests that construct an indexing seam (the job handler, the enqueuer) by hand. The class is sealed with
/// non-virtual methods, and its derivation is the behaviour those seams now depend on, so it is substituted only at its
/// seam: <see cref="IGenericEntityService"/> (the document row, and an event's row for the event hop). Without a double
/// the row is not found, which is "no parent" — every hand-built seam keeps its previous behaviour.
/// </summary>
internal static class TestDocumentIndexParentResolver
{
    /// <summary>The columns an event carries that name its root (live: all four sprk_regarding{core}).</summary>
    private static readonly IReadOnlySet<string> EventRootColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_regardingproject", "sprk_regardingmatter", "sprk_regardingworkassignment", "sprk_regardingservicerequest",
    };

    public static DocumentIndexParentResolver Over(IGenericEntityService? dataverse = null)
    {
        var rows = dataverse ?? Mock.Of<IGenericEntityService>();
        return new DocumentIndexParentResolver(
            rows,
            new CoreAncestorResolver(
                rows,
                (string _, CancellationToken _) => Task.FromResult(EventRootColumns),
                NullLogger<CoreAncestorResolver>.Instance),
            NullLogger<DocumentIndexParentResolver>.Instance);
    }
}
