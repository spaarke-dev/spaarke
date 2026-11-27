using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Task 237 (uac-r2 note on #3293): <see cref="InquiryDispositionService.ClosesTodo"/> fails closed. A statecode/statuscode that names an
/// Inactive state of a To Do in ANY JSON form Dataverse could accept (number, integral float, numeric string), or that cannot be read at all,
/// counts as closing, so the open-inquiry guard cannot be skipped by re-typing the value. A true result only costs one open-inquiry read.
/// </summary>
public sealed class InquiryDispositionClosesTodoTests
{
    private static bool Closes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return InquiryDispositionService.ClosesTodo(doc.RootElement);
    }

    [Theory]
    [InlineData("{\"statecode\":1}")]
    [InlineData("{\"statecode\":\"1\"}")]
    [InlineData("{\"statecode\":\" 1 \"}")]
    [InlineData("{\"statecode\":1.0}")]
    [InlineData("{\"statecode\":1e0}")]
    [InlineData("{\"statecode\":true}")]
    [InlineData("{\"statecode\":\"garbage\"}")]
    [InlineData("{\"statecode\":\"\"}")]
    [InlineData("{\"statecode\":1.5}")]
    [InlineData("{\"statecode\":[1]}")]
    [InlineData("{\"statecode\":{\"x\":1}}")]
    [InlineData("{\"STATECODE\":\"1\"}")]
    [InlineData("{\" statecode \":\"1\"}")]
    [InlineData("{\"statuscode\":2}")]
    [InlineData("{\"statuscode\":\"2\"}")]
    [InlineData("{\"statuscode\":2.0}")]
    [InlineData("{\"statuscode\":659490002}")]
    [InlineData("{\"statuscode\":\"659490002\"}")]
    [InlineData("{\"statuscode\":false}")]
    [InlineData("{\"statuscode\":\"x\"}")]
    [InlineData("{\"sprk_name\":\"renamed\",\"statuscode\":\" 2 \"}")]
    public void MustFire_ClosingOrUnreadableStateValue(string json) => Closes(json).Should().BeTrue(json);

    [Theory]
    [InlineData("{\"statecode\":0}")]
    [InlineData("{\"statecode\":\"0\"}")]
    [InlineData("{\"statecode\":0.0}")]
    [InlineData("{\"statecode\":null}")]
    [InlineData("{\"statuscode\":null}")]
    [InlineData("{\"statuscode\":1}")]
    [InlineData("{\"statuscode\":\"1\"}")]
    [InlineData("{\"statuscode\":659490000}")]
    [InlineData("{\"sprk_priority\":1,\"sprk_status\":2}")]
    [InlineData("{\"sprk_statecode\":\"garbage\",\"sprk_name\":true}")]
    [InlineData("{\"sprk_name\":\"1\",\"sprk_count\":1}")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("1")]
    public void MustNotFire_OpenStateOrUnrelatedColumns(string json) => Closes(json).Should().BeFalse(json);
}
