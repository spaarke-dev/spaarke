using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// unified-access-control-r2 task 188 round 4c: the five must-fire shapes of THE external document narrowing for a caller
/// who is not a resolved internal systemuser, stated over an <see cref="AppOnlyDocumentWorld"/> (the REAL predicate behind
/// the real seam): the document's own value is Restricted; it is an internal-only communication's file; its parent is
/// Restricted; the narrowing read faults; the caller lookup faults (counted as external, so the narrowing applies).
/// </summary>
internal static class HiddenDocumentCases
{
    public const string OwnRestricted = "own-restricted";
    public const string InternalOnlyCommunication = "internal-only-communication";
    public const string RestrictedParent = "restricted-parent";
    public const string NarrowingFault = "narrowing-fault";
    public const string LookupFault = "lookup-fault";

    /// <summary><c>sprk_permission</c> Restricted.</summary>
    private const int Restricted = 100000002;

    /// <summary>Every case, for a theory.</summary>
    public static TheoryData<string> All => new()
    {
        OwnRestricted, InternalOnlyCommunication, RestrictedParent, NarrowingFault, LookupFault,
    };

    /// <summary>
    /// Seeds <paramref name="documentId"/> in <paramref name="world"/> so the narrowing hides it by <paramref name="case"/>,
    /// and returns the caller to state (<see cref="NarrowingCallerMode.LookupFault"/> for that case, otherwise a flagged
    /// external systemuser).
    /// </summary>
    public static NarrowingCallerMode Arrange(AppOnlyDocumentWorld world, Guid documentId, string @case)
    {
        switch (@case)
        {
            case OwnRestricted:
                world.Document(documentId, permission: Restricted);
                return NarrowingCallerMode.External;
            case InternalOnlyCommunication:
                var communication = Guid.NewGuid();
                var thread = Guid.NewGuid();
                world.Thread(thread, 100000000);
                world.Communication(communication, internalOnly: true, thread);
                world.Document(documentId, communication: communication);
                return NarrowingCallerMode.External;
            case RestrictedParent:
                world.Document(documentId, parent: world.Document(Guid.NewGuid(), permission: Restricted));
                return NarrowingCallerMode.External;
            case NarrowingFault:
                world.Document(documentId);
                world.Faults["sprk_documents"] = System.Net.HttpStatusCode.ServiceUnavailable;
                return NarrowingCallerMode.External;
            case LookupFault:
                world.Document(documentId, permission: Restricted);
                return NarrowingCallerMode.LookupFault;
            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }
    }
}
