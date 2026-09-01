using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// The two collaborators issue #858 added to <c>ComposeService</c>'s constructor, built once here
/// instead of five times across the Compose test files.
/// </summary>
/// <remarks>
/// <para><b>Why they are constructor-REQUIRED and not optional-with-null.</b> Both decide where bytes
/// are written and whether the caller may write them. An optional null would mean "no container
/// resolution and no authorization" — the exact state #858 removes — and it would fail at RUNTIME on a
/// path nothing exercises rather than at COMPILE time. Making them required cost four test files an
/// edit; that is the trade, and it is the right way round.</para>
///
/// <para><b>Mocking boundaries only</b> (ADR-038 §4). <see cref="RecordContainerResolver"/> is
/// <c>sealed</c> and therefore cannot be mocked, so it is constructed for REAL with its two boundaries
/// substituted — the shape ADR-038 prefers anyway. <see cref="CallerRecordAccessProbe"/> exposes
/// <c>GetCallerRightsAsync</c> as <c>public virtual</c> precisely so tests can substitute the
/// authorization answer without mocking its HttpClient transport (ban B1).</para>
/// </remarks>
internal static class ComposeServiceCollaborators
{
    /// <summary>Dataverse rights string for a caller who MAY file a document against the record.</summary>
    internal const string CanAssociate = "ReadAccess,WriteAccess,AppendToAccess";

    /// <summary>Dataverse rights string for a caller who can see the record but not attach to it.</summary>
    internal const string ReadOnly = "ReadAccess";

    /// <summary>
    /// A probe that reports <paramref name="rights"/> for every record.
    /// </summary>
    internal static Mock<CallerRecordAccessProbe> Probe(string rights = CanAssociate)
    {
        var probe = new Mock<CallerRecordAccessProbe>(
            new HttpClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<CallerRecordAccessProbe>.Instance,
            null!)
        { CallBase = false };

        probe.Setup(p => p.GetCallerRightsAsync(
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseAccessRightsMapper.FromAccessRightsString(rights));

        return probe;
    }

    /// <summary>
    /// A real <see cref="RecordContainerResolver"/> over a substituted registry + the caller's own
    /// Dataverse double.
    /// </summary>
    /// <param name="dataverse">
    /// The test's existing <c>IGenericEntityService</c> double. Reused rather than replaced so a test
    /// that has already set up its matter / business-unit rows keeps them.
    /// </param>
    /// <param name="securable">
    /// Whether the entity carries <c>sprk_issecure</c>. <see langword="false"/> (default) takes the
    /// business-unit fallback path; <see langword="true"/> exercises the secure branch, where the
    /// record's own <c>sprk_containerid</c> wins and its absence FAILS CLOSED.
    /// </param>
    internal static RecordContainerResolver Resolver(
        IGenericEntityService dataverse, bool securable = false)
    {
        var registry = new Mock<ISecurableEntityRegistry>();

        registry.Setup(r => r.IsSecurableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(securable);

        registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(securable
                ? new HashSet<string>(StringComparer.Ordinal) { "sprk_matter" }
                : new HashSet<string>(StringComparer.Ordinal));

        return new RecordContainerResolver(
            registry.Object, dataverse, NullLogger<RecordContainerResolver>.Instance);
    }

    /// <summary>
    /// Configure a Dataverse double so the NO-RECORD container path
    /// (<c>RecordContainerResolver.ResolveForActingUserAsync</c>) resolves to
    /// <paramref name="containerId"/>.
    /// </summary>
    /// <remarks>
    /// <para>Issue #858 deleted <c>SaveComposeDocumentRequest.ContainerId</c>, so a create-on-save test
    /// no longer states the container in its request — the server derives it. This sets up the two reads
    /// that derivation makes: <c>systemuser</c> filtered on <c>azureactivedirectoryobjectid</c> (the
    /// oid → user TRANSLATION, never a comparison), then that user's business unit.</para>
    ///
    /// <para>Call this AFTER any broader <c>RetrieveAsync</c>/<c>RetrieveMultipleAsync</c> setups in the
    /// same fixture: Moq resolves overlapping expressions last-setup-wins, and this one must not be
    /// shadowed by an <c>It.IsAny</c> catch-all.</para>
    /// </remarks>
    internal static void SetupActingUserContainer(
        Mock<IGenericEntityService> dataverse, string containerId)
    {
        var businessUnitId = Guid.Parse("0b5e0000-0000-0000-0000-00000000bbbb");

        dataverse
            .Setup(d => d.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var row = new Entity("systemuser", Guid.NewGuid())
                {
                    ["businessunitid"] = new EntityReference("businessunit", businessUnitId)
                };
                var collection = new EntityCollection();
                collection.Entities.Add(row);
                return collection;
            });

        dataverse
            .Setup(d => d.RetrieveAsync(
                "businessunit", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Entity("businessunit", businessUnitId)
            {
                ["sprk_containerid"] = containerId
            });
    }
}
