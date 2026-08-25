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
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Tests.Integration.Workspace;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Test host for <c>/provision-project</c>'s write path: an in-memory <c>sprk_project</c> row, a
/// controllable Secure Project business unit and owner team, a substituted SPE container creator, and
/// a record of every write the endpoint issued.
/// </summary>
/// <remarks>
/// <para><b>Payloads are recorded, not just entity-set names.</b> The old fixture recorded which entity
/// sets were written. That cannot express the assertion this task most needs — "provisioning never
/// writes <c>sprk_externalaccount</c>" — because that column lives INSIDE a payload aimed at
/// <c>sprk_projects</c>, an entity set provisioning legitimately writes. Recording the payload is what
/// makes the client-data-destruction guard expressible at all.</para>
///
/// <para><b>The project row is mutable and the ownership PATCH is applied to it.</b> The endpoint
/// assigns ownership and then READS THE OWNER BACK to confirm the write landed (the defence against a
/// silently ignored <c>@odata.bind</c>). A fixture whose row never changed would fail that read-back
/// on every happy path, so the double applies <c>ownerid@odata.bind</c> the way Dataverse would. The
/// switch that stops applying it is what the read-back perturbation flips.</para>
/// </remarks>
public sealed class ProvisionProjectTestFixture : WorkspaceTestFixture
{
    private const string ProjectEntitySet = "sprk_projects";

    /// <summary>The BU name the endpoint is configured to resolve, and which this double answers for.</summary>
    public const string SecureBuName = "Secure Project";

    public static readonly Guid SecureBuId = Guid.Parse("d9ec0b6f-0000-0000-0000-0000000000b0");
    public static readonly Guid SecureOwnerTeamId = Guid.Parse("daec0b6f-0000-0000-0000-0000000000e0");

    /// <summary>
    /// Teams that exist on the same business unit but must NEVER be chosen as its owner.
    /// </summary>
    /// <remarks>
    /// Not padding. A business unit really does carry several teams — the live root business unit has
    /// four owner teams and three access teams — so an owner-team lookup that forgets
    /// <c>isdefault</c>/<c>teamtype</c> resolves one of these instead. Without decoys in the roster
    /// that mistake is unobservable, which is exactly what the perturbation sweep exposed.
    /// </remarks>
    public static readonly Guid DecoyAccessTeamId = Guid.Parse("daec0b6f-0000-0000-0000-0000000000a1");
    public static readonly Guid DecoyOwnerTeamId = Guid.Parse("daec0b6f-0000-0000-0000-0000000000a2");

    /// <summary>The container id the substituted SpeFileStore returns for a successful creation.</summary>
    public const string ProvisionedContainerId = "b!provisioned-secure-container";

    private readonly ConcurrentDictionary<Guid, SeededProject> _projects = new();

    // ── Recorded writes ──────────────────────────────────────────────────────

    /// <summary>Entity sets the endpoint issued a CREATE against, in order.</summary>
    public ConcurrentBag<string> CreatedEntitySets { get; } = new();

    /// <summary>Every UPDATE the endpoint issued: entity set, record id, and the payload's keys/values.</summary>
    public ConcurrentBag<RecordedUpdate> Updates { get; } = new();

    /// <summary>Container display names passed to SPE, so a test can prove a container was created.</summary>
    public ConcurrentBag<string> CreatedContainerDisplayNames { get; } = new();

    public sealed record RecordedUpdate(
        string EntitySet,
        Guid RecordId,
        IReadOnlyDictionary<string, string?> Payload);

    // ── Controllable environment shape ───────────────────────────────────────

    /// <summary>How many business units answer the configured name. Default 1.</summary>
    public int SecureBuMatchCount { get; set; } = 1;

    /// <summary>How many default owner teams answer for the resolved BU. Default 1.</summary>
    public int OwnerTeamMatchCount { get; set; } = 1;

    /// <summary>When false, SPE container creation returns null (Graph failure). Default true.</summary>
    public bool SpeContainerCreationSucceeds { get; set; } = true;

    /// <summary>When false, an UPDATE whose payload carries <c>sprk_containerid</c> throws. Default true.</summary>
    public bool ContainerStampSucceeds { get; set; } = true;

    /// <summary>
    /// When false, the ownership PATCH is accepted but NOT applied to the in-memory row — Dataverse's
    /// real behaviour for an unrecognised <c>@odata.bind</c> property, and the reason the endpoint
    /// verifies by read-back rather than trusting the 204.
    /// </summary>
    public bool OwnershipPatchIsApplied { get; set; } = true;

    private sealed record SeededProject(
        Guid Id, Guid? OwningTeamId, string? ContainerId, Guid? LegacySecurityBuId, bool IsSecure);

    /// <summary>Seeds a project row.</summary>
    public void SeedProject(
        Guid projectId,
        Guid? owningTeamId = null,
        string? containerId = null,
        Guid? legacySecurityBuId = null,
        bool isSecure = true)
        => _projects[projectId] = new SeededProject(
            projectId, owningTeamId, containerId, legacySecurityBuId, isSecure);

    /// <summary>The current owner of a seeded project — what the endpoint's read-back would observe.</summary>
    public Guid? OwningTeamOf(Guid projectId)
        => _projects.TryGetValue(projectId, out var p) ? p.OwningTeamId : null;

    /// <summary>The current container recorded on a seeded project.</summary>
    public string? ContainerIdOf(Guid projectId)
        => _projects.TryGetValue(projectId, out var p) ? p.ContainerId : null;

    /// <summary>
    /// Clears seeded rows, the write log and every environment switch. Called from the test class
    /// constructor, which xUnit runs before EVERY test — the fixture is shared across the class, so
    /// without this a "wrote nothing" assertion fails on another test's residue, or worse passes on it.
    /// </summary>
    public void Reset()
    {
        _projects.Clear();
        CreatedEntitySets.Clear();
        Updates.Clear();
        CreatedContainerDisplayNames.Clear();
        SecureBuMatchCount = 1;
        OwnerTeamMatchCount = 1;
        SpeContainerCreationSucceeds = true;
        ContainerStampSucceeds = true;
        OwnershipPatchIsApplied = true;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // The endpoint resolves the BU by this name. Set explicitly rather than relying on the
                // production default, so a test proves the CONFIGURED name is honoured.
                ["SecureProject:BusinessUnitName"] = SecureBuName,
                ["SharePointEmbedded:ContainerTypeId"] = "11111111-2222-3333-4444-555555555555"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Entitled caller — the delegation gate (task 008) is not what these tests measure.
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(new WritableCallerRecordAccessProbe());

            var client = new Mock<DataverseWebApiClient>(
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance);

            // The handler reads every row through its OWN private DTOs, which this assembly cannot
            // name. So the double answers in Dataverse's WIRE shape (JSON) and lets the handler's own
            // deserialization run — keeping the [JsonPropertyName] bindings under test rather than
            // bypassed. That matters for `_owningteam_value` in particular: it is the field the
            // idempotency marker AND the ownership read-back both read, so a wrong attribute name
            // there would make both silently inert.
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

                    var top = invocation.Arguments[3] as int?;

                    var json = RowsJsonFor(entitySet, filter, select, top);
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
                .Returns((string entitySet, Guid id, object payload, CancellationToken _) =>
                    ApplyUpdate(entitySet, id, payload));

            services.RemoveAll<DataverseWebApiClient>();
            services.AddSingleton(client.Object);

            // Container creation, substituted at the ADR-007 facade. Without this there is no way to
            // assert the success path — the real store reaches unavailable Graph services.
            services.RemoveAll<SpeFileStore>();
            services.AddSingleton<SpeFileStore>(sp => new StubSpeFileStore(sp, this));
        });
    }

    /// <summary>
    /// Applies one UPDATE to the in-memory row, recording it first.
    /// </summary>
    /// <remarks>
    /// Recording happens BEFORE the failure switches are consulted, so a test asserting "this column
    /// was never written" is not silently satisfied by a write that was rejected. What we forbid is the
    /// attempt, not merely the effect.
    /// </remarks>
    private Task ApplyUpdate(string entitySet, Guid id, object payload)
    {
        var flat = Flatten(payload);
        Updates.Add(new RecordedUpdate(entitySet, id, flat));

        if (flat.ContainsKey("sprk_containerid") && !ContainerStampSucceeds)
        {
            throw new InvalidOperationException(
                "Dataverse 400: simulated failure recording sprk_containerid.");
        }

        if (entitySet == ProjectEntitySet && _projects.TryGetValue(id, out var project))
        {
            if (flat.TryGetValue("ownerid@odata.bind", out var ownerBind)
                && ownerBind is not null
                && OwnershipPatchIsApplied)
            {
                var teamId = ParseTeamIdFromBind(ownerBind);
                if (teamId is { } parsed)
                    _projects[id] = project with { OwningTeamId = parsed };
            }

            if (flat.TryGetValue("sprk_containerid", out var container))
            {
                project = _projects[id];
                _projects[id] = project with { ContainerId = container };
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Extracts the GUID from an <c>/teams(guid)</c> OData bind value.</summary>
    private static Guid? ParseTeamIdFromBind(string bind)
    {
        var open = bind.IndexOf('(');
        var close = bind.IndexOf(')');
        if (open < 0 || close <= open) return null;
        return Guid.TryParse(bind[(open + 1)..close], out var id) ? id : null;
    }

    /// <summary>Flattens a write payload to string values so tests can assert on keys and contents.</summary>
    private static IReadOnlyDictionary<string, string?> Flatten(object payload)
    {
        if (payload is IDictionary<string, object?> dict)
        {
            return dict.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }

        // Any other shape still needs to be inspectable — a payload that escaped the assertions by
        // not being a dictionary would defeat the point of recording it.
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return doc.RootElement.EnumerateObject()
            .ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every column live <c>sprk_project</c> actually exposes that this endpoint may read.
    /// </summary>
    /// <remarks>
    /// Verified against live metadata 2026-08-25. Note what is NOT here and why it matters:
    /// <c>sprk_securitybuid</c>, <c>sprk_specontainerid</c>, <c>sprk_externalaccountid</c> and
    /// <c>sprk_name</c> do not exist on this table. The first two are what commit <c>95d3f0f68</c>
    /// wrongly added to the Step 1 projection, breaking provisioning outright and rendering its own
    /// idempotency guard inert.
    /// </remarks>
    internal static readonly HashSet<string> LiveProjectColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "sprk_projectdescription",
        "sprk_issecure", "sprk_accesspermission", "sprk_containerid", "sprk_searchindexname",
        "_sprk_securitybu_value", "_sprk_externalaccount_value", "_sprk_mattertype_value",
        "_sprk_practicearea_value", "statecode", "statuscode", "createdon", "modifiedon",
        "ownerid", "_owningteam_value", "_owninguser_value", "_owningbusinessunit_value"
    };

    /// <summary>
    /// Dataverse's own behaviour: a projection naming a column the table lacks is a 400.
    /// </summary>
    /// <remarks>
    /// <para>THIS IS THE GUARD THAT WAS MISSING. Task 016 built exactly this for the closure cascade
    /// (<c>ProjectClosureCascadeTests.RejectUnknownColumns</c>) and wrote in its notes that "a fake that
    /// returned canned rows regardless of the projection would have gone green on the exact code that
    /// shipped A-12". This fixture had no such check, so when the same session added two nonexistent
    /// columns to the provisioning projection, all five idempotency tests stayed green while the
    /// endpoint was 500ing in production. The guard existed and was not ported one directory over.</para>
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

    /// <summary>
    /// The Dataverse wire payload this double returns for one query.
    /// </summary>
    /// <remarks>
    /// <para><b>This double honours <c>$top</c> and the discriminating <c>$filter</c> predicates, and
    /// that is load-bearing rather than fastidious.</b> The first version of it ignored both, and the
    /// perturbation sweep caught two silent coverage holes as a result:</para>
    /// <list type="bullet">
    ///   <item>Changing the business-unit lookup from <c>$top=2</c> to <c>$top=1</c> broke NOTHING,
    ///   because the fake returned two rows either way. Against real Dataverse that change makes
    ///   ambiguity invisible — the endpoint silently accepts whichever business unit comes back
    ///   first.</item>
    ///   <item>Dropping <c>isdefault eq true and teamtype eq 0</c> from the owner-team lookup broke
    ///   NOTHING, because the fake answered on the business-unit id alone. Real business units carry
    ///   several teams — the live root business unit has four owner teams and three access teams — so
    ///   that query would return the wrong team.</item>
    /// </list>
    /// <para>Same failure mode as the <c>$select</c>-ignoring fake task 016 replaced: a double that
    /// discards part of the query goes green on code that builds that part wrongly. A fake is only
    /// evidence to the extent it refuses what Dataverse would refuse.</para>
    /// </remarks>
    private string RowsJsonFor(string entitySet, string? filter, string? select = null, int? top = null)
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
                        ["sprk_issecure"] = seeded.IsSecure
                    };

                    if (seeded.LegacySecurityBuId is { } legacy)
                        row["_sprk_securitybu_value"] = legacy;

                    if (seeded.ContainerId is { } container)
                        row["sprk_containerid"] = container;

                    if (seeded.OwningTeamId is { } team)
                        row["_owningteam_value"] = team;

                    payload.Add(row);
                }
                break;

            case "businessunits":
                // Answer only the CONFIGURED name. A double that answered any name would let a test
                // pass while the endpoint resolved something else entirely.
                if (filter is not null && filter.Contains($"'{SecureBuName}'", StringComparison.Ordinal))
                {
                    for (var i = 0; i < SecureBuMatchCount; i++)
                    {
                        payload.Add(new Dictionary<string, object?>
                        {
                            // Distinct ids per row so an ambiguity test cannot pass by coincidence.
                            ["businessunitid"] = i == 0 ? SecureBuId : Guid.NewGuid(),
                            ["name"] = SecureBuName
                        });
                    }
                }
                break;

            case "teams":
                if (filter is not null && filter.Contains(SecureBuId.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    // A realistic roster for one business unit. The DECOYS come first deliberately:
                    // a query that forgets isdefault/teamtype gets a non-default or access team as its
                    // first row, so "took an arbitrary team" is a visible failure rather than a
                    // coincidence that happens to work.
                    var roster = new List<(Guid Id, string Name, bool IsDefault, int TeamType)>
                    {
                        (DecoyAccessTeamId, "Secure Project Access", false, 1),
                        (DecoyOwnerTeamId, "Secure Project Extra Owners", false, 0)
                    };

                    for (var i = 0; i < OwnerTeamMatchCount; i++)
                    {
                        roster.Add((i == 0 ? SecureOwnerTeamId : Guid.NewGuid(), SecureBuName, true, 0));
                    }

                    // Apply only the predicates the caller actually asked for.
                    var requiresDefault = filter.Contains("isdefault eq true", StringComparison.OrdinalIgnoreCase);
                    var requiresOwnerType = filter.Contains("teamtype eq 0", StringComparison.OrdinalIgnoreCase);

                    foreach (var team in roster)
                    {
                        if (requiresDefault && !team.IsDefault) continue;
                        if (requiresOwnerType && team.TeamType != 0) continue;

                        payload.Add(new Dictionary<string, object?>
                        {
                            ["teamid"] = team.Id,
                            ["name"] = team.Name
                        });
                    }
                }
                break;
        }

        // Honour $top. A double that ignores it lets a wrong $top pass unnoticed — see the remarks
        // above; this is how the ambiguity guard's $top=2 stopped being covered.
        if (top is { } limit && payload.Count > limit)
            payload = payload.Take(limit).ToList();

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

    /// <summary>Container creation without Graph, recording what was asked for.</summary>
    private sealed class StubSpeFileStore : SpeFileStore
    {
        private readonly ProvisionProjectTestFixture _fixture;

        public StubSpeFileStore(IServiceProvider sp, ProvisionProjectTestFixture fixture)
            : base(sp.GetRequiredService<ContainerOperations>(),
                   sp.GetRequiredService<DriveItemOperations>(),
                   sp.GetRequiredService<UploadSessionManager>(),
                   sp.GetRequiredService<UserOperations>())
        {
            _fixture = fixture;
        }

        public override Task<ContainerDto?> CreateContainerAsync(
            Guid containerTypeId, string displayName, string? description = null, CancellationToken ct = default)
        {
            _fixture.CreatedContainerDisplayNames.Add(displayName);

            return Task.FromResult<ContainerDto?>(
                _fixture.SpeContainerCreationSucceeds
                    ? new ContainerDto(
                        Id: ProvisionedContainerId,
                        DisplayName: displayName,
                        Description: description,
                        CreatedDateTime: DateTimeOffset.UnixEpoch)
                    : null);
        }
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
