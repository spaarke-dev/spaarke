using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// Builders for the <see cref="SpeContainerOwnershipGuard"/> every app-only SharePoint Embedded class now
/// takes (task 227d).
/// </summary>
/// <remarks>
/// <para>Most tests of those classes are about something else — upload paths, archive embeds, dedup — and
/// only need a guard that does not change what they assert. <see cref="AllowAll"/> is that: it treats every
/// container as owned, so no ownership read is sent and the scripted Graph handler sees exactly the calls
/// it did before. The client still comes from the test's own factory.</para>
/// <para>The guard's real behaviour is pinned in <c>SpeAppOnlyContainerIsolationTests</c> (tests/integration/tenant/Spe).
/// <c>SpeAppOnlyContainerGuardTests</c> (ArchTests) fails the build if a subclass like this one appears
/// in <c>src/</c>.</para>
/// </remarks>
public static class TestSpeOwnership
{
    /// <summary>A guard that owns every container and hands out clients from <paramref name="factory"/>.</summary>
    public static SpeContainerOwnershipGuard AllowAll(IGraphClientFactory factory)
        => new AllowAllGuard(factory);

    /// <summary>
    /// A real guard over <paramref name="factory"/> for customer <paramref name="customerId"/>. The VALUES of
    /// <paramref name="settings"/> are the stamp's configured containers (keys as in
    /// <c>GraphModule.StampContainerSettingKeys</c>, for readability).
    /// </summary>
    public static SpeContainerOwnershipGuard Real(
        IGraphClientFactory factory,
        string customerId = "cust1",
        IReadOnlyDictionary<string, string?>? settings = null,
        GraphMetadataCache? cache = null)
        => new(factory, settings?.Values ?? Array.Empty<string?>(), Customer(customerId), NullLogger<SpeContainerOwnershipGuard>.Instance, cache);

    internal static IConfiguration Configuration(IReadOnlyDictionary<string, string?>? settings = null)
        => new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();

    internal static CustomerIdentity Customer(string customerId)
        => new(
            Options.Create(new CustomerOptions { Id = customerId }),
            Configuration(),
            NullLogger<CustomerIdentity>.Instance);

    private sealed class AllowAllGuard(IGraphClientFactory factory)
        : SpeContainerOwnershipGuard(factory, Array.Empty<string?>(), Customer("test"), NullLogger<SpeContainerOwnershipGuard>.Instance)
    {
        public override Task<bool> IsOwnedAsync(string containerOrDriveId, CancellationToken ct = default)
            => Task.FromResult(true);

        public override Task<bool> IsDeletedContainerOwnedAsync(string containerId, CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
