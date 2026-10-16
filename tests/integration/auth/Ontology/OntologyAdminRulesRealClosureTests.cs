using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.Ontology;

/// <summary>
/// Ontology platform R1 task 100, review F3: Retire through the REAL closure. Every other Retire test substitutes the
/// <c>PolicyRetiredClosure</c> delegate, so nothing proved that the production registration (<c>SignalsModule</c>:
/// <c>SignalClosureService.ClosePolicyRetiredNowAsync</c>) closes a Signal as PolicyRetired with no Decision Record. Here the production
/// delegate, <see cref="SignalClosureService"/> and <see cref="SignalWriter"/> all run; only the writer's Dataverse clients
/// (<see cref="OntologyWriterDataverseClient"/> over a fake organization service, and the shared <see cref="IGenericEntityService"/>) are
/// substituted, with one open Signal seeded for the policy.
/// </summary>
/// <remarks>Placement: <c>tests/integration/auth/**</c> (the security-auth KEEP path).</remarks>
public class OntologyAdminRulesRealClosureTests : IClassFixture<OntologyAdminRulesRealClosureFixture>
{
    private const string Base = "/api/v1/ontology/admin/rules";

    private readonly OntologyAdminRulesRealClosureFixture _fixture;

    public OntologyAdminRulesRealClosureTests(OntologyAdminRulesRealClosureFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task Retire_ThroughTheProductionClosure_ClosesTheOpenSignalAsPolicyRetired_WithNoDecisionRecord()
    {
        var rule = _fixture.World.AddPolicy("POL-REAL-CLOSE");
        var signal = _fixture.SeedOpenSignal(rule.Id);

        var response = await _fixture.ClientFor("oid-admin").PostAsync(
            $"{Base}/{rule.Id:D}/retire",
            new StringContent("{\"reason\":\"Replaced by POL-NEW\"}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var closure = doc.RootElement.GetProperty("closure");
        closure.GetProperty("closed").GetInt32().Should().Be(1);
        closure.GetProperty("complete").GetBoolean().Should().BeTrue();
        closure.GetProperty("unavailable").GetBoolean().Should().BeFalse();

        // The rule was retired as the caller first, and only then was the Signal written.
        rule.StatusCode.Should().Be(100000000);
        _fixture.World.Order.Should().ContainInOrder($"PATCH sprk_policies({rule.Id:D})", $"SIGNAL-UPDATE {signal.Id:D}");

        // The Signal is Resolved with the PolicyRetired resolution, by a conditional write on the row version it was read at.
        signal.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value.Should().Be(SignalWriter.SignalStatusResolved);
        signal.GetAttributeValue<OptionSetValue>("sprk_resolutiontype").Value.Should().Be(SignalWriter.ResolutionPolicyRetired);
        signal.Contains("sprk_decisionrecord").Should().BeFalse("a system closure never links a Decision Record");
        signal.Contains("sprk_resolvedby").Should().BeFalse("no person resolved it");
        signal.Contains("sprk_suppresseduntil").Should().BeFalse("a closure is never a dismissal");
        _fixture.SignalUpdates.Should().ContainSingle().Which.Should().Match<UpdateRequest>(
            u => u.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches && u.Target.RowVersion == OntologyAdminRulesRealClosureFixture.SeededRowVersion);

        // No Decision Record was created, through either Dataverse client.
        _fixture.Organization.Invocations.Select(i => i.Method.Name).Should().NotContain(n => n.StartsWith("Create", StringComparison.Ordinal));
        _fixture.Entities.Invocations.Select(i => i.Method.Name).Should().NotContain(n => n.StartsWith("Create", StringComparison.Ordinal) || n.StartsWith("Upsert", StringComparison.Ordinal));
        _fixture.World.Requests.Should().NotContain(r => r.Contains("sprk_decisionrecord", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retire_OfARuleWithNoOpenSignal_ThroughTheProductionClosure_WritesNothingToTheSignals()
    {
        var rule = _fixture.World.AddPolicy("POL-REAL-NONE");

        var response = await _fixture.ClientFor("oid-admin").PostAsync(
            $"{Base}/{rule.Id:D}/retire",
            new StringContent("{\"reason\":\"gone\"}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.SignalUpdates.Should().BeEmpty();
    }
}

/// <summary>
/// The Rules test host with the PRODUCTION <c>PolicyRetiredClosure</c> registration kept. The Signal writer's dedicated client is
/// built over a fake organization service and the shared sysadmin entity service is a fake; both hold one Signal table.
/// </summary>
public sealed class OntologyAdminRulesRealClosureFixture : OntologyAdminRulesTestFixture
{
    public const string SeededRowVersion = "7";

    /// <summary>The Signal writer organization service (writes and reads through the writer client). One mock for the fixture life: the
    /// writer client caches its connection after first use.</summary>
    public Mock<IOrganizationServiceAsync2> Organization { get; }

    /// <summary>The shared sysadmin entity service (the closure reads).</summary>
    public Mock<IGenericEntityService> Entities { get; }

    /// <summary>The Signals in the fake Dataverse, by id.</summary>
    public Dictionary<Guid, Entity> Signals { get; } = new();

    /// <summary>The conditional updates that reached the fake organization service.</summary>
    public List<UpdateRequest> SignalUpdates { get; } = new();

    public OntologyAdminRulesRealClosureFixture()
    {
        Organization = BuildOrganization();
        Entities = BuildEntities();
    }

    public new void Reset()
    {
        base.Reset();
        Signals.Clear();
        SignalUpdates.Clear();
        Organization.Invocations.Clear();
        Entities.Invocations.Clear();
    }

    /// <summary>One Open Signal of the policy, at row version <see cref="SeededRowVersion"/>.</summary>
    public Entity SeedOpenSignal(Guid policyId)
    {
        var signal = new Entity("sprk_signal", Guid.NewGuid())
        {
            RowVersion = SeededRowVersion,
            ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusOpen),
            ["sprk_policy"] = new EntityReference("sprk_policy", policyId),
            ["sprk_policyversion"] = new EntityReference("sprk_policyversion", Guid.NewGuid()),
            ["sprk_dedupekey"] = "POL-REAL-CLOSE|sprk_event|x|1",
        };
        Signals[signal.Id] = signal;
        return signal;
    }

    protected override void ConfigureClosure(IServiceCollection services)
    {
        // Keep SignalsModule's PolicyRetiredClosure -> SignalClosureService -> SignalWriter chain. Replace only the Dataverse clients under it.
        services.RemoveAll<OntologyWriterDataverseClient>();
        services.AddSingleton(_ => new OntologyWriterDataverseClient(() => Organization.Object, NullLogger<OntologyWriterDataverseClient>.Instance));
        services.RemoveAll<IGenericEntityService>();
        services.AddSingleton<IGenericEntityService>(_ => Entities.Object);
    }

    private Mock<IOrganizationServiceAsync2> BuildOrganization()
    {
        var org = new Mock<IOrganizationServiceAsync2>();
        org.Setup(o => o.ExecuteAsync(It.IsAny<OrganizationRequest>(), It.IsAny<CancellationToken>()))
            .Returns((OrganizationRequest request, CancellationToken _) =>
            {
                if (request is not UpdateRequest update || update.Target.LogicalName != "sprk_signal")
                {
                    throw new InvalidOperationException($"The Signal writer sent something unexpected: {request.RequestName}");
                }

                var row = Signals[update.Target.Id];
                if (update.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches && update.Target.RowVersion != row.RowVersion)
                {
                    throw new InvalidOperationException("ConcurrencyVersionMismatch");
                }

                SignalUpdates.Add(update);
                World.Order.Add($"SIGNAL-UPDATE {update.Target.Id:D}");
                foreach (var attribute in update.Target.Attributes)
                {
                    row[attribute.Key] = attribute.Value;
                }

                return Task.FromResult<OrganizationResponse>(new UpdateResponse());
            });
        return org;
    }

    private Mock<IGenericEntityService> BuildEntities()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_policy", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                new Entity("sprk_policy", id) { ["sprk_policycode"] = World.Policies.Single(p => p.Id == id).Code });
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                var result = new EntityCollection();
                if (query.EntityName != "sprk_signal")
                {
                    return result;
                }

                var policy = query.Criteria.Conditions.Single(c => c.AttributeName == "sprk_policy" && c.Operator == ConditionOperator.Equal);
                var open = query.Criteria.Conditions.Single(c => c.AttributeName == "sprk_signalstatus" && c.Operator == ConditionOperator.In);
                var openStatuses = open.Values.Cast<object>().Select(v => (int)v).ToHashSet();
                foreach (var signal in Signals.Values)
                {
                    if (signal.GetAttributeValue<EntityReference>("sprk_policy").Id == (Guid)policy.Values[0]
                        && openStatuses.Contains(signal.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value))
                    {
                        result.Entities.Add(signal);
                    }
                }

                return result;
            });
        return entities;
    }
}
