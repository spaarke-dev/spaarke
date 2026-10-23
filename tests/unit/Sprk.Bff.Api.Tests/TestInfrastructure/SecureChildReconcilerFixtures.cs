using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// #2091 — the <see cref="SecureChildReconciler"/> a re-file writer test passes when it is NOT about the step after a re-file
/// (the reconciler is a REQUIRED dependency of those writers, never null): the REAL reconciler over an in-memory Dataverse
/// that holds no child rows, so no row reads as isolated and the step moves and shares nothing. For a communication it still
/// calls <paramref name="threads"/>' filing-thread reconcile, as the host's does.
/// </summary>
internal static class SecureChildReconcilerFixtures
{
    public static SecureChildReconciler OverEmptyWorld(IThreadResolver? threads = null)
    {
        var world = SecureChildShareWorld.Standard();
        return SecureChildShareWorld.ReconcilerOver(() => world, new FakeRecordShareTable(), null!, threads: threads);
    }
}
