using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Fix round 3g, census: <see cref="CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync"/> reads each communication's
/// thread lookup from a Web API row. A communication whose row did not carry that lookup is withheld, exactly as one whose
/// lookup is an explicit null is: the visible set holds only a communication whose flag reads false, whose thread lookup is
/// set, and whose thread reads Open. The select must list the lookup, or every communication would be withheld.
/// </summary>
public class CommunicationOpenThreadGateAbsentLookupTests
{
    /// <summary>The thread privacy choice value for Open (the value the gate's own "explicitly Open" test accepts).</summary>
    private const int PrivacyStateOpen = 100000000;

    private static readonly Guid CommunicationId = Guid.Parse("cccccccc-0000-0000-0000-00000000f001");
    private static readonly Guid ThreadId = Guid.Parse("dddddddd-0000-0000-0000-00000000f001");

    private static Dictionary<string, JsonElement> Row(params (string Column, string RawJson)[] columns)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            "{" + string.Join(",", columns.Select(c => $"\"{c.Column}\":{c.RawJson}")) + "}")!;

    private static CommunicationOpenThreadGate.RowQuery Query(string? threadLookup, List<string>? asked = null)
        => (entitySet, odata, _) =>
        {
            asked?.Add(odata);
            if (entitySet == CommunicationOpenThreadGate.CommunicationSet)
            {
                var columns = new List<(string, string)>
                {
                    (CommunicationOpenThreadGate.CommunicationPkField, $"\"{CommunicationId}\""),
                    (CommunicationInternalOnlyFloor.InternalOnlyField, "false"),
                };
                if (threadLookup is not null)
                    columns.Add((CommunicationOpenThreadGate.MessageThreadLookupValue, threadLookup));
                return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(new[] { Row(columns.ToArray()) });
            }

            return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(new[]
            {
                Row((CommunicationOpenThreadGate.ThreadPkField, $"\"{ThreadId}\""),
                    (CommunicationInternalOnlyFloor.ThreadPrivacyField, PrivacyStateOpen.ToString())),
            });
        };

    [Fact]
    public async Task ACommunicationWhoseRowDidNotCarryTheThreadLookup_IsNotVisible()
    {
        var visible = await CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync(
            new[] { CommunicationId }, Query(threadLookup: null), CancellationToken.None);

        visible.Should().BeEmpty("a thread that was not returned cannot be shown to be Open");
    }

    [Fact]
    public async Task ACommunicationWhoseThreadLookupIsAnExplicitNull_IsNotVisible()
    {
        var visible = await CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync(
            new[] { CommunicationId }, Query(threadLookup: "null"), CancellationToken.None);

        visible.Should().BeEmpty();
    }

    [Fact]
    public async Task ACommunicationWhoseThreadLookupIsSet_AndWhoseThreadIsOpen_IsVisible_AndTheSelectNamesTheLookup()
    {
        var asked = new List<string>();

        var visible = await CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync(
            new[] { CommunicationId }, Query($"\"{ThreadId}\"", asked), CancellationToken.None);

        visible.Should().Equal(CommunicationId);
        asked.Should().Contain(q => q.Contains(CommunicationOpenThreadGate.MessageThreadLookupValue, StringComparison.Ordinal),
            "the select must return the thread lookup, or every communication would be withheld");
    }
}
