using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.Workspace;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Test host for <c>/provision-project</c>'s write path: an in-memory <c>sprk_project</c> row plus a
/// record of every entity set the endpoint CREATED or UPDATED.
/// </summary>
/// <remarks>
/// <para>The create/update recording is the point. "Did it return 409?" is a weak assertion on its own —
/// a refusal that still created a Business Unit would be the original defect wearing a better status
/// code. What must be true is that nothing was written.</para>
///
/// <para>The delegation filter (task 008) sits in front of this endpoint, so the caller is given Write
/// via a substituted <see cref="CallerRecordAccessProbe"/>. These tests are about idempotency, not
/// authorization; who may provision is asserted in <c>tests/integration/auth/UnifiedAccessControl/</c>.</para>
/// </remarks>
public sealed class ProvisionProjectTestFixture : WorkspaceTestFixture
{
    private const string ProjectEntitySet = "sprk_projects";

    private readonly ConcurrentDictionary<Guid, SeededProject> _projects = new();

    /// <summary>Entity sets the endpoint issued a CREATE against, in order.</summary>
    public ConcurrentBag<string> CreatedEntitySets { get; } = new();

    /// <summary>Entity sets the endpoint issued an UPDATE against, in order.</summary>
    public ConcurrentBag<string> UpdatedEntitySets { get; } = new();

    private sealed record SeededProject(Guid Id, Guid? BusinessUnitId, string? SpeContainerId);

    /// <summary>Seeds a secure project, optionally already carrying provisioned infrastructure.</summary>
    public void SeedSecureProject(Guid projectId, Guid? businessUnitId, string? speContainerId)
        => _projects[projectId] = new SeededProject(projectId, businessUnitId, speContainerId);

    /// <summary>
    /// Clears the seeded rows and the write log. Called from the test class constructor, which xUnit
    /// runs before EVERY test — the fixture itself is shared across the class, so without this the
    /// "unprovisioned project provisions" case leaves a <c>businessunits</c> create in the bag and the
    /// "creates nothing" assertions in the other tests fail on its residue rather than their own.
    /// </summary>
    public void Reset()
    {
        _projects.Clear();
        CreatedEntitySets.Clear();
        UpdatedEntitySets.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            // Entitled caller — the delegation gate is not what these tests are measuring.
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(new WritableCallerRecordAccessProbe());

            var client = new Mock<DataverseWebApiClient>(
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance);

            // The handler reads every row through its OWN private DTOs, which this assembly cannot name.
            // So the double answers in Dataverse's WIRE shape (JSON) and lets the handler's own
            // deserialization run — which keeps the [JsonPropertyName] bindings under test rather than
            // bypassed. That matters specifically for `_sprk_securitybu_value`: it is the field the guard
            // reads, and a wrong attribute name there makes the guard silently never fire — which is
            // exactly what happened, so the projection is now validated by RejectUnknownColumns below.
            client
                .Setup(c => c.QueryAsync<It.IsAnyType>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .Returns(new InvocationFunc(invocation =>
                {
                    var rowType = invocation.Method.GetGenericArguments()[0];
                    var entitySet = (string)invocation.Arguments[0];
                    var filter = invocation.Arguments[1] as string;
                    var select = invocation.Arguments[2] as string;

                    var json = RowsJsonFor(entitySet, filter, select);
                    var listType = typeof(List<>).MakeGenericType(rowType);
                    var rows = JsonSerializer.Deserialize(json, listType)
                               ?? Activator.CreateInstance(listType)!;

                    return typeof(Task)
                        .GetMethod(nameof(Task.FromResult))!
                        .MakeGenericMethod(listType)
                        .Invoke(null, new[] { rows })!;
                }));

            client
                .Setup(c => c.CreateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entitySet, object _, CancellationToken _) =>
                {
                    CreatedEntitySets.Add(entitySet);
                    return Guid.NewGuid();
                });

            client
                .Setup(c => c.UpdateAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns((string entitySet, Guid _, object _, CancellationToken _) =>
                {
                    UpdatedEntitySets.Add(entitySet);
                    return Task.CompletedTask;
                });

            services.RemoveAll<DataverseWebApiClient>();
            services.AddSingleton(client.Object);
        });
    }

    /// <summary>
    /// The Dataverse wire payload this double returns for one query, by entity set.
    /// </summary>
    /// <remarks>
    /// <c>businessunits</c> and <c>accounts</c> answer plausibly so the "unprovisioned project proceeds"
    /// case can actually reach Business Unit creation — otherwise root-BU resolution fails first and the
    /// test would pass for the wrong reason (no 409, but also nothing created).
    /// </remarks>
    /// <summary>
    /// Every column live <c>sprk_project</c> actually exposes that this endpoint may read.
    /// </summary>
    /// <remarks>
    /// Verified against live metadata 2026-08-24. Note what is NOT here and why it matters:
    /// <c>sprk_securitybuid</c>, <c>sprk_specontainerid</c> and <c>sprk_name</c> do not exist on this
    /// table — the first two are what commit <c>95d3f0f68</c> wrongly added to the Step 1 projection,
    /// breaking provisioning outright and rendering its own idempotency guard inert.
    /// </remarks>
    internal static readonly HashSet<string> LiveProjectColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "sprk_projectdescription",
        "sprk_issecure", "sprk_accesspermission", "sprk_containerid", "sprk_searchindexname",
        "_sprk_securitybu_value", "_sprk_externalaccount_value", "_sprk_mattertype_value",
        "_sprk_practicearea_value", "statecode", "statuscode", "createdon", "modifiedon", "ownerid"
    };

    /// <summary>
    /// Dataverse's own behaviour: a projection naming a column the table lacks is a 400.
    /// </summary>
    /// <remarks>
    /// <para>THIS IS THE GUARD THAT WAS MISSING. Task 016 built exactly this for the closure cascade
    /// (<c>ProjectClosureCascadeTests.RejectUnknownColumns</c>) and wrote in its notes that "a fake that
    /// returned canned rows regardless of the projection would have gone green on the exact code that
    /// shipped A-12". This fixture had no such check, so when the same session added two nonexistent
    /// columns to the provisioning projection, all five idempotency tests stayed green while the endpoint
    /// was 500ing in production. The guard existed and was not ported one directory over.</para>
    /// </remarks>
    private static void RejectUnknownColumns(string entitySet, string? select)
    {
        if (entitySet != ProjectEntitySet || string.IsNullOrWhiteSpace(select)) return;

        foreach (var column in select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!LiveProjectColumns.Contains(column))
            {
                throw new InvalidOperationException(
                    $"Dataverse 400: Could not find a property named '{column}' on type " +
                    "'Microsoft.Dynamics.CRM.sprk_project'.");
            }
        }
    }

    private string RowsJsonFor(string entitySet, string? filter, string? select = null)
    {
        RejectUnknownColumns(entitySet, select);

        var payload = new List<Dictionary<string, object?>>();

        switch (entitySet)
        {
            case ProjectEntitySet:
                var seeded = _projects.Values.FirstOrDefault(
                    p => filter is not null && filter.Contains(p.Id.ToString(), StringComparison.OrdinalIgnoreCase));

                if (seeded is not null)
                {
                    var row = new Dictionary<string, object?>
                    {
                        ["sprk_projectid"] = seeded.Id,
                        ["sprk_projectname"] = "Seeded Secure Project",
                        ["sprk_issecure"] = true
                    };

                    if (seeded.BusinessUnitId is { } bu)
                        row["_sprk_securitybu_value"] = bu;

                    if (seeded.SpeContainerId is { } container)
                        row["sprk_containerid"] = container;

                    payload.Add(row);
                }
                break;

            case "businessunits":
                payload.Add(new Dictionary<string, object?>
                {
                    ["businessunitid"] = Guid.Parse("0b0b0b0b-0000-0000-0000-000000000001"),
                    ["name"] = "Root Business Unit"
                });
                break;

            case "accounts":
                payload.Add(new Dictionary<string, object?>
                {
                    ["accountid"] = Guid.Parse("0c0c0c0c-0000-0000-0000-000000000001"),
                    ["name"] = "External Access Account"
                });
                break;
        }

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Config sufficient for the real <see cref="DataverseWebApiClient"/> constructor (Moq invokes it).</summary>
    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb"
        }).Build();

    public HttpClient CreateEntitledClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "provision-test-token");
        return client;
    }

    private sealed class WritableCallerRecordAccessProbe : CallerRecordAccessProbe
    {
        public WritableCallerRecordAccessProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(),
                   NullLogger<CallerRecordAccessProbe>.Instance)
        { }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(AccessRights.Read | AccessRights.Write);
    }
}
