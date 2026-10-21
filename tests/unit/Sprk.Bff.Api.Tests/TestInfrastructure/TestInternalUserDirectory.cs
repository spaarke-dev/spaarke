using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// The Dataverse a contact-permission removal checks a matched holder against (#2104 round 2, K3): which Entra object ids
/// are <c>systemuser</c>s of this environment. <see cref="SpeContainerMembershipService"/> asks it through
/// <see cref="DataverseWebApiClient.QueryAsync{T}"/> (the class's <c>virtual</c> seam, ADR-038 §4), so the double answers
/// exactly that read by interpreting the production <c>$filter</c>, and records every one it received.
/// </summary>
public sealed class TestInternalUserDirectory
{
    private static readonly Regex ObjectIdClause = new(
        @"azureactivedirectoryobjectid eq ([0-9a-fA-F-]{36})", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HashSet<Guid> _internalObjectIds;

    private TestInternalUserDirectory(IEnumerable<Guid> internalObjectIds, Exception? failure)
    {
        _internalObjectIds = new HashSet<Guid>(internalObjectIds);
        Mock = new Mock<DataverseWebApiClient>(
            Config(), NullLogger<DataverseWebApiClient>.Instance,
            // Moq matches a class-proxy constructor EXACTLY, and the two credential slots are optional parameters, so
            // they are passed positionally (see ProjectClosureCascadeTests.FakeGrantTable.BuildMock). This double never
            // authenticates.
            null!, null!);

        Mock.Setup(c => c.QueryAsync<SpeContainerMembershipService.SystemUserObjectIdRow>(
                SpeContainerMembershipService.SystemUserEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? filter, string? _, int? _, int? _, CancellationToken _) =>
            {
                Filters.Add(filter ?? string.Empty);
                if (failure is not null)
                {
                    throw failure;
                }

                return ObjectIdClause.Matches(filter ?? string.Empty)
                    .Select(m => Guid.Parse(m.Groups[1].Value))
                    .Where(_internalObjectIds.Contains)
                    .Select(oid => new SpeContainerMembershipService.SystemUserObjectIdRow
                    {
                        systemuserid = Guid.NewGuid(),
                        azureactivedirectoryobjectid = oid,
                    })
                    .ToList();
            });
    }

    /// <summary>A directory in which exactly <paramref name="internalObjectIds"/> are systemusers.</summary>
    public static TestInternalUserDirectory With(params Guid[] internalObjectIds) => new(internalObjectIds, null);

    /// <summary>A directory with no systemuser among the holders asked about.</summary>
    public static TestInternalUserDirectory Empty() => new([], null);

    /// <summary>A directory whose lookup throws <paramref name="failure"/>.</summary>
    public static TestInternalUserDirectory Failing(Exception failure) => new([], failure);

    public Mock<DataverseWebApiClient> Mock { get; }

    public DataverseWebApiClient Client => Mock.Object;

    /// <summary>Every <c>systemusers</c> <c>$filter</c> the removal sent, in order.</summary>
    public List<string> Filters { get; } = [];

    /// <summary>Config sufficient for the real <see cref="DataverseWebApiClient"/> constructor (Moq invokes it); nothing
    /// dials out (copied from ProjectClosureCascadeTests.ClientConfig).</summary>
    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["Graph:ManagedIdentity:Enabled"] = "true",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000cc",
        }).Build();
}
