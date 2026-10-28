using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 167 (D-193): the one resolver of a Choice parameter's <c>OptionsSource</c> (live option-set metadata, active currencies) and of a
/// budget's currency (the matter's business unit, else the organization base currency). Maintain-class (ADR-038,
/// <c>tests/unit/domain/**</c>): the contract is what the wizard offers, what the commit accepts, and that nothing unreadable is guessed.
/// </summary>
public class DecisionParameterSourcesTests
{
    private static readonly Guid MatterId = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid BusinessUnitId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid BuCurrencyId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid BaseCurrencyId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private const string CategoryPath =
        "EntityDefinitions(LogicalName='sprk_budget')/Attributes(LogicalName='sprk_budgetcategory')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata";

    // ---------------------------------------------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------------------------------------------

    private sealed class FakeUser : IDataverseUserClient
    {
        public List<string> Gets { get; } = new();
        public DataverseUserResponse Response { get; set; } = DataverseUserResponse.Fail(404, "not_found", "no");

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            Gets.Add(relativePath);
            return Task.FromResult(Response);
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    /// <summary>A cache backed by a dictionary: what was set is what comes back, keyed as the real one is.</summary>
    private static (Mock<ITenantCache> Cache, Dictionary<string, object> Store) Cache()
    {
        var store = new Dictionary<string, object>(StringComparer.Ordinal);
        var cache = new Mock<ITenantCache>();
        cache.Setup(c => c.GetAsync<DecisionParameterOption[]>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string tenant, string resource, string id, int _, string _, CancellationToken _) =>
                Task.FromResult(store.TryGetValue($"{tenant}|{resource}|{id}", out var hit) ? (DecisionParameterOption[]?)hit : null));
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DecisionParameterOption[]>(), It.IsAny<TimeSpan?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string tenant, string resource, string id, int _, DecisionParameterOption[] value, TimeSpan? _, string _, CancellationToken _) =>
            {
                store[$"{tenant}|{resource}|{id}"] = value;
                return Task.CompletedTask;
            });
        return (cache, store);
    }

    private static HttpContext Http(string? tenant = "tenant-1")
    {
        var http = new DefaultHttpContext();
        if (tenant is not null)
            http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("tid", tenant)], "test"));
        return http;
    }

    private static DataverseUserResponse Picklist(params (int Value, string Label, bool Hidden)[] options) =>
        DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new
        {
            LogicalName = "sprk_budgetcategory",
            OptionSet = new
            {
                Options = options.Select(o => new
                {
                    o.Value,
                    IsHidden = o.Hidden,
                    Label = new { UserLocalizedLabel = new { Label = o.Label }, LocalizedLabels = new[] { new { Label = o.Label } } },
                }).ToArray(),
            },
        }));

    private static DecisionParameterSources Build(
        FakeUser? user = null, Mock<IGenericEntityService>? entities = null, Mock<ITenantCache>? cache = null) =>
        new(user ?? new FakeUser(), (entities ?? new Mock<IGenericEntityService>()).Object, (cache ?? Cache().Cache).Object,
            NullLogger<DecisionParameterSources>.Instance);

    // ---------------------------------------------------------------------------------------------------------------
    // Options: live option-set metadata
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Options_Metadata_ReturnsTheLiveOptionsWithTheirLabels_NeverAHardCodedList()
    {
        var user = new FakeUser { Response = Picklist((100000000, "Fees", false), (100000001, "Expenses", false), (100000002, "Retired", true)) };

        var options = await Build(user).OptionsAsync(Http(), "metadata:sprk_budget.sprk_budgetcategory", CancellationToken.None);

        options.Should().NotBeNull();
        options!.Select(o => (o.Value, o.Label)).Should().Equal(("100000000", "Fees"), ("100000001", "Expenses"));
        user.Gets.Should().ContainSingle().Which.Should().StartWith(CategoryPath);
    }

    [Fact]
    public async Task Options_Metadata_IsReadOncePerColumn_AndAShortTtlCacheServesTheRest()
    {
        var user = new FakeUser { Response = Picklist((0, "Annual", false)) };
        var (cache, store) = Cache();
        var sources = Build(user, cache: cache);

        await sources.OptionsAsync(Http(), "metadata:sprk_budget.sprk_budgetperiod", CancellationToken.None);
        var second = await sources.OptionsAsync(Http(), "metadata:sprk_budget.sprk_budgetperiod", CancellationToken.None);

        second!.Select(o => o.Label).Should().Equal("Annual");
        user.Gets.Should().HaveCount(1, "one metadata read per (table, column) inside the TTL");
        store.Should().ContainKey("tenant-1|decision-options|metadata:sprk_budget.sprk_budgetperiod");
        cache.Verify(c => c.SetAsync("tenant-1", "decision-options", "metadata:sprk_budget.sprk_budgetperiod", It.IsAny<int>(),
            It.IsAny<DecisionParameterOption[]>(), It.Is<TimeSpan?>(t => t != null && t.Value <= TimeSpan.FromMinutes(10)), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "a short TTL, so an owner relabel shows up");
    }

    [Fact]
    public async Task Options_Metadata_WithNoTenantClaim_StillReads_AndNeverCaches()
    {
        var user = new FakeUser { Response = Picklist((0, "Annual", false)) };
        var (cache, store) = Cache();

        var options = await Build(user, cache: cache).OptionsAsync(Http(tenant: null), "metadata:sprk_budget.sprk_budgetperiod", CancellationToken.None);

        options!.Should().ContainSingle();
        store.Should().BeEmpty();
    }

    [Fact]
    public async Task Options_Metadata_WhenTheReadFailsOrIsUnparseable_IsNull_NotAnEmptyList()
    {
        var failed = await Build(new FakeUser()).OptionsAsync(Http(), "metadata:sprk_budget.sprk_budgetcategory", CancellationToken.None);
        var garbled = await Build(new FakeUser { Response = DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { nothing = 1 })) })
            .OptionsAsync(Http(), "metadata:sprk_budget.sprk_budgetcategory", CancellationToken.None);

        failed.Should().BeNull("an unreadable list is not an empty one: the commit must not refuse every value, nor accept any");
        garbled.Should().BeNull();
    }

    [Theory]
    [InlineData("metadata:sprk_budget")]
    [InlineData("metadata:sprk_budget.sprk_x')/Attributes")]
    [InlineData("metadata:..")]
    [InlineData("nonsense")]
    [InlineData("")]
    public async Task Options_AnUnknownOrMalformedSource_IsNull_AndNothingIsAsked(string source)
    {
        var user = new FakeUser();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);

        (await Build(user, entities).OptionsAsync(Http(), source, CancellationToken.None)).Should().BeNull();

        user.Gets.Should().BeEmpty("a source string is never spliced into a request unless it is table.column");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Options: active currencies
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Options_ActiveCurrencies_AreTheActiveRowsOnly_LabelledWithTheirIsoCode()
    {
        QueryExpression? asked = null;
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => asked = q)
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                Currency(BuCurrencyId, "US Dollar", "USD"),
                Currency(BaseCurrencyId, "Canadian Dollar", "CAD"),
            }));

        var options = await Build(entities: entities).OptionsAsync(Http(), DecisionParameterSources.ActiveCurrencies, CancellationToken.None);

        options!.Select(o => (o.Value, o.Label)).Should().Equal((BuCurrencyId.ToString("D"), "US Dollar (USD)"), (BaseCurrencyId.ToString("D"), "Canadian Dollar (CAD)"));
        asked!.EntityName.Should().Be("transactioncurrency");
        asked.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "statecode" && Equals(c.Values.Single(), 0));
    }

    private static Entity Currency(Guid id, string name, string iso)
    {
        var e = new Entity("transactioncurrency", id);
        e["currencyname"] = name;
        e["isocurrencycode"] = iso;
        return e;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The budget's currency
    // ---------------------------------------------------------------------------------------------------------------

    private sealed class CurrencyWorld
    {
        public Mock<IGenericEntityService> Entities { get; } = new();
        public List<QueryExpression> Queries { get; } = new();

        /// <param name="buCurrency">The business unit's Default Currency (null: none).</param>
        /// <param name="baseCurrency">The organization's base currency (null: none).</param>
        /// <param name="activeCurrencies">The currencies that exist and are active.</param>
        public CurrencyWorld(Guid? buCurrency, Guid? baseCurrency, params (Guid Id, string Iso)[] activeCurrencies)
        {
            var matter = new Entity("sprk_matter", MatterId);
            matter["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId);
            Entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(matter);

            var bu = new Entity("businessunit", BusinessUnitId);
            if (buCurrency is { } c) bu["transactioncurrencyid"] = new EntityReference("transactioncurrency", c);
            Entities.Setup(e => e.RetrieveAsync("businessunit", BusinessUnitId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(bu);

            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression q, CancellationToken _) =>
                {
                    Queries.Add(q);
                    if (q.EntityName == "organization")
                    {
                        var org = new Entity("organization", Guid.NewGuid());
                        if (baseCurrency is { } b) org["basecurrencyid"] = new EntityReference("transactioncurrency", b);
                        return Task.FromResult(new EntityCollection(new List<Entity> { org }));
                    }

                    var wanted = q.Criteria.Conditions.Single(x => x.AttributeName == "transactioncurrencyid").Values.Single();
                    var hit = activeCurrencies.Where(a => Equals(a.Id, wanted)).Select(a => Currency(a.Id, "name", a.Iso)).ToList();
                    return Task.FromResult(new EntityCollection(hit));
                });
        }
    }

    [Fact]
    public async Task Currency_Omitted_IsTheMattersBusinessUnitsDefaultCurrency_WithItsIsoCode()
    {
        var world = new CurrencyWorld(BuCurrencyId, BaseCurrencyId, (BuCurrencyId, "EUR"), (BaseCurrencyId, "USD"));

        var currency = await Build(entities: world.Entities).ResolveBudgetCurrencyAsync(MatterId, null, CancellationToken.None);

        currency.Refusal.Should().BeNull();
        currency.Id.Should().Be(BuCurrencyId);
        currency.IsoCode.Should().Be("EUR");
        world.Queries.Should().NotContain(q => q.EntityName == "organization", "the base currency is read only when the business unit has none");
    }

    [Fact]
    public async Task Currency_Omitted_WhenTheBusinessUnitHasNone_FallsBackToTheOrganizationBaseCurrency()
    {
        var world = new CurrencyWorld(buCurrency: null, BaseCurrencyId, (BaseCurrencyId, "USD"));

        var currency = await Build(entities: world.Entities).ResolveBudgetCurrencyAsync(MatterId, null, CancellationToken.None);

        currency.Refusal.Should().BeNull();
        (currency.Id, currency.IsoCode).Should().Be((BaseCurrencyId, "USD"));
    }

    [Fact]
    public async Task Currency_Omitted_WhenNoCurrencyCanBeDetermined_FailsClosed_NeverABlank()
    {
        var world = new CurrencyWorld(buCurrency: null, baseCurrency: null);

        var currency = await Build(entities: world.Entities).ResolveBudgetCurrencyAsync(MatterId, null, CancellationToken.None);

        currency.Refusal.Should().NotBeNullOrWhiteSpace();
        currency.Id.Should().BeNull();
        currency.IsoCode.Should().BeNull();
    }

    [Fact]
    public async Task Currency_Explicit_IsUsedWhenItIsAnActiveCurrency_AndTheBusinessUnitIsNotAsked()
    {
        var chosen = Guid.Parse("cccccccc-0000-0000-0000-000000000009");
        var world = new CurrencyWorld(BuCurrencyId, BaseCurrencyId, (chosen, "GBP"));

        var currency = await Build(entities: world.Entities).ResolveBudgetCurrencyAsync(MatterId, chosen.ToString("D"), CancellationToken.None);

        (currency.Id, currency.IsoCode, currency.Refusal).Should().Be((chosen, "GBP", null));
        world.Entities.Verify(e => e.RetrieveAsync("businessunit", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        var query = world.Queries.Single();
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "statecode" && Equals(c.Values.Single(), 0), "an inactive currency is refused");
    }

    [Theory]
    [InlineData("cccccccc-0000-0000-0000-00000000dead")]  // a currency that does not exist, or is inactive: the active-only query finds nothing
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Currency_Explicit_NotAnExistingActiveCurrency_IsRefused(string requested)
    {
        var world = new CurrencyWorld(BuCurrencyId, BaseCurrencyId, (BuCurrencyId, "EUR"));

        var currency = await Build(entities: world.Entities).ResolveBudgetCurrencyAsync(MatterId, requested, CancellationToken.None);

        currency.Refusal.Should().NotBeNullOrWhiteSpace();
        currency.Id.Should().BeNull();
        currency.IsoCode.Should().BeNull();
    }

    [Fact]
    public async Task Currency_ACurrencyWithNoIsoCode_IsRefused_NeverABlankSprkCurrency()
    {
        var world = new CurrencyWorld(BuCurrencyId, BaseCurrencyId, (BuCurrencyId, ""));

        var currency = await Build(entities: world.Entities).ResolveBudgetCurrencyAsync(MatterId, null, CancellationToken.None);

        currency.Refusal.Should().NotBeNullOrWhiteSpace();
        currency.IsoCode.Should().BeNull();
    }

    [Fact]
    public async Task Currency_WhenDataverseCannotBeRead_Throws_SoTheCommitAnswers503_NotAGuessedCurrency()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("no answer"));

        var act = () => Build(entities: entities).ResolveBudgetCurrencyAsync(MatterId, null, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // What the plan route gets
    // ---------------------------------------------------------------------------------------------------------------

    private static ResolvedDecisionPlan PlanOf(params string[] codes) =>
        new(codes.Select(c => { DecisionActionCatalog.TryGet(c, out var d); return d; }).ToList(), [], []);

    [Fact]
    public async Task ForPlan_CreateBudget_ResolvesBothOptionLists_AndTheCurrencyDefault()
    {
        var user = new FakeUser { Response = Picklist((100000000, "Fees", false)) };
        var world = new CurrencyWorld(BuCurrencyId, null, (BuCurrencyId, "EUR"));
        world.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "transactioncurrency" && q.Criteria.Conditions.All(c => c.AttributeName != "transactioncurrencyid")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { Currency(BuCurrencyId, "Euro", "EUR") }));

        var facts = await Build(user, world.Entities).ForPlanAsync(Http(), PlanOf("create-budget", "approve-variance"), MatterId, CancellationToken.None);

        facts.OptionsBySource.Keys.Should().BeEquivalentTo(
            "metadata:sprk_budget.sprk_budgetcategory", "metadata:sprk_budget.sprk_budgetperiod", DecisionParameterSources.ActiveCurrencies);
        facts.OptionsBySource[DecisionParameterSources.ActiveCurrencies].Select(o => o.Label).Should().Equal("Euro (EUR)");
        facts.DefaultsBySource.Should().Contain(DecisionParameterSources.MatterCurrencyDefault, BuCurrencyId.ToString("D"));
    }

    [Fact]
    public async Task ForPlan_AnUnreadableList_IsEmpty_AndTheDefaultIsAbsent_NeverAFailedPlan()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

        var facts = await Build(new FakeUser(), entities).ForPlanAsync(Http(), PlanOf("create-budget"), MatterId, CancellationToken.None);

        facts.OptionsBySource.Values.Should().OnlyContain(v => v.Count == 0);
        facts.DefaultsBySource.Should().BeEmpty();
    }

    [Fact]
    public async Task ForPlan_APlanWithNoSourcedParameters_AsksNothing_MustNotFire()
    {
        var user = new FakeUser();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);

        var facts = await Build(user, entities).ForPlanAsync(Http(), PlanOf("revise-budget", "approve-variance", "send-budget-inquiry"), MatterId, CancellationToken.None);

        facts.OptionsBySource.Should().BeEmpty();
        facts.DefaultsBySource.Should().BeEmpty();
        user.Gets.Should().BeEmpty();
    }

    [Fact]
    public async Task ForPlan_WithNoMatter_HasNoCurrencyDefault()
    {
        var user = new FakeUser { Response = Picklist((0, "Annual", false)) };
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EntityCollection());

        var facts = await Build(user, entities).ForPlanAsync(Http(), PlanOf("create-budget"), null, CancellationToken.None);

        facts.DefaultsBySource.Should().BeEmpty();
        entities.Verify(e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
