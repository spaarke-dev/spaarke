using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.RecordMatching;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// Issue #3165 (Matter Type search filter), rollout safety: an index schema that predates <c>matterTypeId</c> rejects any document that
/// names the field (400 "property does not exist"), so a record or chunk with no type must leave the field off the wire, and the
/// chunk's "resolved, no type" marker must stay ON the wire (a merge-or-upload omits a null and would keep the old type).
/// </summary>
public sealed class MatterTypeIndexWireFormatTests
{
    [Fact]
    public void KnowledgeDocument_WithTheEmptyTypeMarker_WritesItAndWithANullTypeOmitsIt()
    {
        var resolvedNoType = JsonSerializer.Serialize(new KnowledgeDocument { Id = "c1", MatterTypeId = string.Empty, MatterTypeSourceId = string.Empty });
        var unresolved = JsonSerializer.Serialize(new KnowledgeDocument { Id = "c2" });

        resolvedNoType.Should().Contain("\"matterTypeId\":\"\"").And.Contain("\"matterTypeSourceId\":\"\"");
        unresolved.Should().NotContain("matterTypeId").And.NotContain("matterTypeSourceId");
    }

    [Fact]
    public void RecordSearchDocument_WithNoMatterType_OmitsTheFieldSoAnOlderRecordsIndexAcceptsTheWrite()
    {
        JsonSerializer.Serialize(new RecordSearchDocument { Id = "r1" }).Should().NotContain("matterTypeId");
        JsonSerializer.Serialize(new RecordSearchDocument { Id = "r2", MatterTypeId = "3165e000-0000-4000-8000-0000000000a1" })
            .Should().Contain("\"matterTypeId\":\"3165e000-0000-4000-8000-0000000000a1\"");
    }

    [Fact]
    public void SearchIndexDocument_WithNoMatterType_OmitsTheFieldSoAnOlderRecordsIndexAcceptsTheWrite()
    {
        JsonSerializer.Serialize(new SearchIndexDocument { Id = "r1" }).Should().NotContain("matterTypeId");
        JsonSerializer.Serialize(new SearchIndexDocument { Id = "r2", MatterTypeId = "3165e000-0000-4000-8000-0000000000a1" })
            .Should().Contain("\"matterTypeId\":\"3165e000-0000-4000-8000-0000000000a1\"");
    }
}
