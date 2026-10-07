using System.Net;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// The Dataverse shapes <see cref="PlaybookService"/> sends and reads for the playbook LIST and CREATE (unified-access-control-r2
/// dev gates 2026-10-06, defects D-G6-1 and D-G6-2). Pure members, pinned directly (ADR-038 §7 B1/B8: the defect class is an
/// OData string, which only the member that builds it can show without a transport mock).
/// </summary>
public class PlaybookServiceDataverseShapeTests
{
    private const string OwnerFilter = "_ownerid_value eq 1d02f31c-1872-f011-b4cb-7c1e52671ad0";

    // =========================================================================================
    // D-G6-1 — the list query is a form Dataverse accepts
    // =========================================================================================

    [Fact]
    public void ListQuery_CountsOnTheCollection_NeverThroughTheCountSegment_AndNeverSkips()
    {
        // Both refused forms were verified live: "/$count?$filter=" → 400 "Could not find a property named
        // '_ownerid_value' on type 'Edm.Int32'", and "$skip" → 400 "Skip Clause is not supported in CRM".
        var url = PlaybookService.BuildListQueryUrl(OwnerFilter, "modifiedon desc", windowEnd: 40);

        url.Should().StartWith("sprk_analysisplaybooks?");
        url.Should().Contain("&$count=true");
        url.Should().NotContain("/$count");
        url.Should().NotContain("$skip");
        url.Should().Contain("$filter=" + Uri.EscapeDataString(OwnerFilter));
        url.Should().Contain("&$orderby=modifiedon desc");
        url.Should().Contain("&$top=40&");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(5000, 5000)]
    [InlineData(100_020, 5000)]
    public void ListQuery_ReadsUpToTheEndOfThePage_CappedAtDataversesWindow(int windowEnd, int expectedTop)
    {
        PlaybookService.BuildListQueryUrl(OwnerFilter, "sprk_name", windowEnd)
            .Should().Contain($"&$top={expectedTop}&");
    }

    [Fact]
    public void ListPage_IsCutFromTheWindow_AndTheTotalIsDataversesCount_NotTheRowCount()
    {
        var json = ListJson(totalCount: 34, rowCount: 25);

        var (page2, total) = PlaybookService.ReadListPage(json, skip: 20, pageSize: 20);

        total.Should().Be(34);
        page2.Select(p => p.Name).Should().Equal("Playbook 21", "Playbook 22", "Playbook 23", "Playbook 24", "Playbook 25");
    }

    [Fact]
    public void ListPage_BeyondTheRows_IsEmpty_AndStillCarriesTheTotal()
    {
        var (items, total) = PlaybookService.ReadListPage(ListJson(totalCount: 3, rowCount: 3), skip: 20, pageSize: 20);

        items.Should().BeEmpty();
        total.Should().Be(3);
    }

    [Fact]
    public void ListPage_MapsEachRow_AndAnUnsetFlagIsFalse_NotAThrow()
    {
        var owner = Guid.NewGuid();
        var json = "{\"@odata.count\":2,\"value\":[" +
            "{\"sprk_analysisplaybookid\":\"" + Guid.Empty.ToString().Replace('0', '1') + "\",\"sprk_name\":\"Public\"," +
            "\"sprk_description\":\"d\",\"sprk_ispublic\":true,\"_ownerid_value\":\"" + owner + "\",\"modifiedon\":\"2026-10-06T19:00:00Z\"}," +
            "{\"sprk_analysisplaybookid\":\"" + Guid.Empty.ToString().Replace('0', '2') + "\",\"sprk_name\":null," +
            "\"sprk_description\":null,\"sprk_ispublic\":null,\"_ownerid_value\":\"" + owner + "\",\"modifiedon\":\"2026-10-06T19:00:00Z\"}]}";

        var (items, _) = PlaybookService.ReadListPage(json, skip: 0, pageSize: 20);

        items.Should().HaveCount(2);
        items[0].IsPublic.Should().BeTrue();
        items[0].OwnerId.Should().Be(owner);
        items[1].IsPublic.Should().BeFalse();
        items[1].Name.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{\"value\":[]}")]                 // no count — never read as "zero playbooks"
    [InlineData("{\"@odata.count\":0}")]           // no rows array
    public void ListPage_WithoutTheCountOrTheRows_Throws(string json)
    {
        var read = () => PlaybookService.ReadListPage(json, skip: 0, pageSize: 20);

        read.Should().Throw<InvalidOperationException>();
    }

    // =========================================================================================
    // D-G6-1 — only an unknown TABLE is "not provisioned"; a refused query is a failure
    // =========================================================================================

    [Fact]
    public void MissingEntity_IsDataversesUnknownSegmentAnswer_ForThePlaybookSet()
    {
        const string body =
            "{\"error\":{\"code\":\"0x80060888\",\"message\":\"Resource not found for the segment 'sprk_analysisplaybooks'.\"}}";

        PlaybookService.IsMissingEntityResponse(HttpStatusCode.NotFound, body).Should().BeTrue();
    }

    [Theory]
    // The exact answer that hid D-G6-1: the refused count query, previously read as "table not provisioned".
    [InlineData(HttpStatusCode.BadRequest,
        "{\"error\":{\"code\":\"0x80060888\",\"message\":\"Could not find a property named '_ownerid_value' on type 'Edm.Int32'.\"}}")]
    [InlineData(HttpStatusCode.BadRequest,
        "{\"error\":{\"code\":\"0x80060888\",\"message\":\"Skip Clause is not supported in CRM\"}}")]
    [InlineData(HttpStatusCode.BadRequest,
        "{\"error\":{\"code\":\"0x80060888\",\"message\":\"Resource not found for the segment 'sprk_analysisplaybooks'.\"}}")]
    [InlineData(HttpStatusCode.NotFound,
        "{\"error\":{\"code\":\"0x80060888\",\"message\":\"Resource not found for the segment 'sprk_somethingelse'.\"}}")]
    [InlineData(HttpStatusCode.NotFound, "")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"error\":{\"message\":\"does not exist\"}}")]
    public void MissingEntity_IsNeverARefusedQuery_AnotherSegment_OrAnEmptyAnswer(HttpStatusCode status, string body)
    {
        PlaybookService.IsMissingEntityResponse(status, body).Should().BeFalse();
    }

    // =========================================================================================
    // D-G6-2 — a created playbook is owned by the person who asked for it
    // =========================================================================================

    [Fact]
    public void CreatePayload_BindsTheOwner_ToTheCallersSystemUser()
    {
        var owner = Guid.NewGuid();
        var outputType = Guid.NewGuid();

        var payload = PlaybookService.BuildCreatePayload(
            new SavePlaybookRequest { Name = "Mine", Description = "d", IsPublic = true, OutputTypeId = outputType }, owner);

        payload.Should().Contain("ownerid@odata.bind", $"/systemusers({owner})");
        payload.Should().Contain("sprk_name", "Mine");
        payload.Should().Contain("sprk_ispublic", true);
        payload.Should().Contain("sprk_OutputTypeId@odata.bind", $"/sprk_aioutputtypes({outputType})");
    }

    [Fact]
    public void CreatePayload_WithNoOwner_IsRefused_NeverLeftToTheApplicationUser()
    {
        var build = () => PlaybookService.BuildCreatePayload(new SavePlaybookRequest { Name = "Mine" }, Guid.Empty);

        build.Should().Throw<ArgumentException>();
    }

    private static string ListJson(int totalCount, int rowCount) =>
        "{\"@odata.count\":" + totalCount + ",\"value\":[" + string.Join(",", Enumerable.Range(1, rowCount).Select(i =>
            "{\"sprk_analysisplaybookid\":\"" + Guid.NewGuid() + "\",\"sprk_name\":\"Playbook " + i + "\"," +
            "\"sprk_ispublic\":false,\"_ownerid_value\":\"" + Guid.NewGuid() + "\",\"modifiedon\":\"2026-10-06T19:00:00Z\"}")) + "]}";
}
