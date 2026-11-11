using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// <see cref="SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync"/> — the ONE lookup of the Secure Record business unit
/// (unified-access-control-r2 task 194b R5, round-130 rule 3: moved beside <see cref="SecureRecordOwnerTeam.IsInSecureBusinessUnit"/>,
/// the ownership resolver's private copy deleted). The query is answered by evaluating its name condition against the
/// configured name, so a lookup that read another key or dropped the condition finds nothing.
/// </summary>
public class SecureRecordOwnerTeamSecureBusinessUnitTests
{
    private const string ConfiguredName = "Isolated Records";

    [Theory]
    [InlineData(0, null, false)]
    [InlineData(1, "b1", false)]
    [InlineData(2, null, true)]
    public async Task ResolveSecureBusinessUnit_ReadsTheConfiguredNameWithTopTwo(int matches, string? expected, bool ambiguous)
    {
        var ids = new[] { Guid.Parse("b1b1b1b1-0000-4000-8000-000000000001"), Guid.Parse("b2b2b2b2-0000-4000-8000-000000000002") };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SecureRecord:BusinessUnitName"] = ConfiguredName })
            .Build();
        QueryExpression? asked = null;
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => asked = q)
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var named = q.Criteria.Conditions.Any(c =>
                    c.AttributeName == "name" && c.Operator == ConditionOperator.Equal && Equals(c.Values.Single(), ConfiguredName));
                var rows = named ? ids.Take(matches).Select(id => new Entity("businessunit", id)) : [];
                return new EntityCollection(rows.ToList());
            });
        var logger = new Mock<ILogger>();

        var result = await SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync(
            dataverse.Object, configuration, CancellationToken.None, logger.Object);

        result.Id.Should().Be(expected is null ? null : ids[0]);
        result.Ambiguous.Should().Be(ambiguous);
        asked!.EntityName.Should().Be("businessunit");
        asked.TopCount.Should().Be(2, "a second match must be seen to call the name ambiguous");
        logger.Verify(
            l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            ambiguous ? Times.Once() : Times.Never(),
            "the ambiguity is the resolver's logged refusal (task 144)");
    }
}
