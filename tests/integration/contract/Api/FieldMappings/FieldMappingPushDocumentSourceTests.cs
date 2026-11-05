using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using PushHost = Sprk.Bff.Api.Tests.Api.FieldMappings.FieldMappingPushAuthorizationContractTests.PushHost;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// field-mapping-server-write-path-r1 task 043 (owner 2026-11-04, AC5): <c>POST /api/v1/field-mappings/push</c> FROM A DOCUMENT,
/// through the REAL mapper and handler. The caller's Read on the document is asked on the Document access path's set
/// (<c>sprk_documents</c>); a caller who is not an internal systemuser is also put to THE external document narrowing (the REAL
/// <c>InternalPlaneDocumentNarrowing</c> over a stated caller and app-only world). Allowed: the profile's rules are pushed to the
/// children the push selects, read and written as the caller. Anything else: the push's existing uniform 404, nothing read or
/// written.
/// </summary>
/// <remarks>The host is <see cref="FieldMappingPushAuthorizationContractTests.PushHost"/> (module-boundary doubles only, ADR-038);
/// the document, its one child relationship and its children are arranged here.</remarks>
public class FieldMappingPushDocumentSourceTests
{
    private static readonly Guid DocumentId = Guid.Parse("04304304-1111-4000-8000-000000000043");
    private static readonly Guid AnalysisA = Guid.Parse("04304304-2222-4000-8000-000000000043");
    private static readonly Guid AnalysisB = Guid.Parse("04304304-3333-4000-8000-000000000043");
    private static readonly Guid OutputFileAnalysis = Guid.Parse("04304304-4444-4000-8000-000000000043");
    private static readonly Guid RegardingAnalysis = Guid.Parse("04304304-5555-4000-8000-000000000043");
    private const int Restricted = 100000002;

    private static async Task<(PushHost Host, AppOnlyDocumentWorld World)> StartAsync(NarrowingCallerMode caller)
    {
        var world = new AppOnlyDocumentWorld();
        var host = await PushHost.StartAsync(configure: services =>
            services.AddStatedNarrowingCaller(() => caller, () => world));
        host.Probe.RightsOn = DocumentId;
        return (host, world);
    }

    /// <summary>A Document to Analysis profile (one Copy rule), the document as the caller reads it, the analysis table's THREE
    /// lookups to the document as the real schema has them (<c>sprk_Document.xml</c>: <c>sprk_documentid</c>,
    /// <c>sprk_outputfileid</c>, <c>sprk_RegardingDocument</c>), and the analyses the caller can see under each: the two analysed
    /// ones under <c>sprk_documentid</c>, and one other under each of the output-file and regarding-document lookups.</summary>
    private static void ArrangeDocumentPush(PushHost host)
    {
        host.FieldMappings
            .Setup(f => f.GetFieldMappingProfileWithRulesAsync("sprk_document", "sprk_analysis", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "Document to Analysis",
                SourceEntity = "sprk_document",
                TargetEntity = "sprk_analysis",
                IsActive = true,
                Rules = [PushHost.Rule("sprk_documentdescription", 0, "sprk_description", 0)],
            });
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_document", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_documents");
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_analysises");
        host.Entities.Setup(e => e.RetrieveAsync("sprk_analysis", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new Microsoft.Xrm.Sdk.Entity(table, id));
        host.FieldMappings
            .Setup(f => f.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid id, Dictionary<string, object?> payload, Guid? _, CancellationToken _) =>
                host.Writes.Add((id, new Dictionary<string, object?>(payload))))
            .Returns(Task.CompletedTask);

        host.Impersonated
            .Setup(q => q.QueryAsync("sprk_documents", It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string set, string? query, Guid caller, CancellationToken _) =>
            {
                host.SourceReads.Add((set, query, caller));
                return new List<Dictionary<string, JsonElement>>
                {
                    new()
                    {
                        ["sprk_documentid"] = JsonSerializer.SerializeToElement(DocumentId.ToString()),
                        ["sprk_documentdescription"] = JsonSerializer.SerializeToElement("From the document"),
                    },
                };
            });
        host.Impersonated
            .Setup(q => q.QueryAsync("sprk_analysises", It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string set, string? query, Guid caller, CancellationToken _) =>
            {
                host.ChildQueries.Add((set, query, caller));
                Guid[] children = (query ?? string.Empty) switch
                {
                    var q when q.StartsWith("$filter=_sprk_documentid_value ", StringComparison.Ordinal) => [AnalysisA, AnalysisB],
                    var q when q.StartsWith("$filter=_sprk_outputfileid_value ", StringComparison.Ordinal) => [OutputFileAnalysis],
                    var q when q.StartsWith("$filter=_sprk_regardingdocument_value ", StringComparison.Ordinal) => [RegardingAnalysis],
                    _ => [],
                };
                return children
                    .Select(id => new Dictionary<string, JsonElement> { ["sprk_analysisid"] = JsonSerializer.SerializeToElement(id.ToString()) })
                    .ToList();
            });
        host.Impersonated
            .Setup(q => q.QueryAsync(
                It.Is<string>(path => path.StartsWith("EntityDefinitions", StringComparison.Ordinal)),
                It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, string? query, Guid caller, CancellationToken _) =>
            {
                host.MetadataQueries.Add((path, query, caller));
                return path switch
                {
                    "EntityDefinitions(LogicalName='sprk_document')/Attributes" =>
                    [
                        Row(("LogicalName", "sprk_documentid"), ("AttributeType", "Uniqueidentifier"), ("IsValidODataAttribute", true)),
                        Row(("LogicalName", "sprk_documentdescription"), ("AttributeType", "Memo"), ("IsValidODataAttribute", true)),
                    ],
                    "EntityDefinitions(LogicalName='sprk_analysis')/ManyToOneRelationships" =>
                    [
                        Row(("ReferencingAttribute", "sprk_documentid"), ("ReferencedEntity", "sprk_document"),
                            ("ReferencingEntityNavigationPropertyName", "sprk_DocumentId")),
                        Row(("ReferencingAttribute", "sprk_outputfileid"), ("ReferencedEntity", "sprk_document"),
                            ("ReferencingEntityNavigationPropertyName", "sprk_OutputFileId")),
                        Row(("ReferencingAttribute", "sprk_regardingdocument"), ("ReferencedEntity", "sprk_document"),
                            ("ReferencingEntityNavigationPropertyName", "sprk_RegardingDocument")),
                        Row(("ReferencingAttribute", "sprk_regardingmatter"), ("ReferencedEntity", "sprk_matter"),
                            ("ReferencingEntityNavigationPropertyName", "sprk_RegardingMatter")),
                    ],
                    "EntityDefinitions(LogicalName='sprk_billingexport')/ManyToOneRelationships" =>
                    [
                        Row(("ReferencingAttribute", "sprk_csvdocument"), ("ReferencedEntity", "sprk_document"),
                            ("ReferencingEntityNavigationPropertyName", "sprk_CsvDocument")),
                        Row(("ReferencingAttribute", "sprk_jsondocument"), ("ReferencedEntity", "sprk_document"),
                            ("ReferencingEntityNavigationPropertyName", "sprk_JsonDocument")),
                    ],
                    _ => new List<Dictionary<string, JsonElement>>(),
                };
            });
    }

    private static Dictionary<string, JsonElement> Row(params (string Key, object Value)[] columns) =>
        columns.ToDictionary(c => c.Key, c => JsonSerializer.SerializeToElement(c.Value));

    private static HttpRequestMessage Push(Guid? sourceId = null, string targetEntity = "sprk_analysis")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/field-mappings/push")
        {
            Content = JsonContent.Create(new { sourceEntity = "sprk_document", sourceRecordId = sourceId ?? DocumentId, targetEntity }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PushAuthzTestAuthHandler.CallerHeader, "present");
        return request;
    }

    private static string Normalize(string problemJson)
    {
        var node = JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("traceId");
        node.Remove("correlationId");
        return node.ToJsonString();
    }

    [Theory(DisplayName = "043 AC5: a caller who can read the document (internal, or external with it shown) pushes the profile's rules to the analyses the push selects, as the caller")]
    [InlineData(NarrowingCallerMode.Internal)]
    [InlineData(NarrowingCallerMode.External)]
    public async Task Push_FromADocumentTheCallerCanRead_WritesTheChildrenAsTheCaller(NarrowingCallerMode caller)
    {
        var (host, world) = await StartAsync(caller);
        await using var _ = host;
        world.Document(DocumentId);
        host.Probe.Rights = AccessRights.Read;
        ArrangeDocumentPush(host);

        var response = await host.SendAsync(Push());

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonNode.Parse(body)!["updatedCount"]!.GetValue<int>().Should().Be(2);
        host.ProbedSources.Should().Equal(new[] { ("sprk_documents", DocumentId) }, "the caller's Read is asked on the Document access path's set");
        host.Writes.Select(w => w.Id).Should().BeEquivalentTo(new[] { AnalysisA, AnalysisB },
            "only the analyses of the document (sprk_documentid); the output-file and regarding-document analyses are not touched");
        host.Writes.Should().OnlyContain(w => Equals(w.Payload["sprk_description"], "From the document"));
        var childQuery = host.ChildQueries.Should().ContainSingle().Subject;
        childQuery.Caller.Should().NotBeEmpty("children are read impersonating the caller");
        childQuery.Query.Should().Be($"$filter=_sprk_documentid_value eq {DocumentId:D}&$select=sprk_analysisid&$top=501",
            "review round 2 (F1): of the analysis's three lookups to sprk_document, its ownership primary names the parent");
    }

    /// <summary>
    /// Review round 2 (F1, iii): a document source whose target has several lookups to <c>sprk_document</c> and no primary order
    /// that names one of them (<c>sprk_billingexport</c> is not in <c>SecureChildLineage</c>) is still the existing 409, with the
    /// same reason code and wording, and no child is read or written.
    /// </summary>
    [Fact]
    public async Task Push_FromADocument_ToATargetWithSeveralDocumentLookupsAndNoDefinedParent_IsTheExisting409()
    {
        var (host, world) = await StartAsync(NarrowingCallerMode.Internal);
        await using var _ = host;
        world.Document(DocumentId);
        host.Probe.Rights = AccessRights.Read;
        ArrangeDocumentPush(host);
        host.FieldMappings
            .Setup(f => f.GetFieldMappingProfileWithRulesAsync("sprk_document", "sprk_billingexport", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "Document to Billing Export",
                SourceEntity = "sprk_document",
                TargetEntity = "sprk_billingexport",
                IsActive = true,
                Rules = [PushHost.Rule("sprk_documentdescription", 0, "sprk_description", 0)],
            });

        var response = await host.SendAsync(Push(targetEntity: "sprk_billingexport"));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        var problem = JsonNode.Parse(body)!;
        problem["reasonCode"]!.GetValue<string>().Should().Be(FieldMappingEndpoints.ParentLookupAmbiguousReasonCode);
        problem["detail"]!.GetValue<string>().Should().Be(
            "'sprk_billingexport' has 2 lookups to 'sprk_document', so which one names the parent is ambiguous. Nothing was updated.");
        host.ChildQueries.Should().BeEmpty("the route never guesses which lookup names the parent");
        host.Writes.Should().BeEmpty();
    }

    /// <summary>
    /// Review round 2 (F1): every target in the schema with several lookups to <c>sprk_document</c>
    /// (<c>src/dataverse/solutions/SpaarkeMaster/Other/Relationships</c>, 2026-11-05) and the one column the rule picks, so a
    /// change to the ownership order that moves a push is seen here. <c>null</c>: the existing 409. Only the analysis is resolved
    /// (the owner's rule is Document to Analysis); a document's own lookups and an invoice submission's keep the 409.
    /// </summary>
    [Theory]
    [InlineData("sprk_analysis", "sprk_regardingdocument,sprk_outputfileid,sprk_documentid", "sprk_documentid")]
    [InlineData("sprk_document", "sprk_canonicaldocument,sprk_parentdocument", null)]
    [InlineData("sprk_invoicesubmission", "sprk_reportdocument,sprk_document", null)]
    [InlineData("sprk_billingexport", "sprk_csvdocument,sprk_jsondocument", null)]
    public void DocumentOwnershipParentColumn_IsFixedByTheTargetTable(string target, string lookups, string? expected) =>
        FieldMappingEndpoints.DocumentOwnershipParentColumn(target, lookups.Split(',')).Should().Be(expected);

    public static TheoryData<string> RefusedDocumentShapes => new() { "no-read", "external-hidden", "external-narrowing-faults" };

    [Theory(DisplayName = "043 AC5: a document the caller cannot read, or one the narrowing does not show to an external caller, is the push's uniform 404 - nothing read or written")]
    [MemberData(nameof(RefusedDocumentShapes))]
    public async Task Push_FromADocumentTheCallerMayNotUse_IsTheUniform404_AndNothingIsReadOrWritten(string shape)
    {
        var (host, world) = await StartAsync(shape == "no-read" ? NarrowingCallerMode.Internal : NarrowingCallerMode.External);
        await using var _ = host;
        world.Document(DocumentId, permission: shape == "external-hidden" ? Restricted : null);
        if (shape == "external-narrowing-faults")
        {
            world.Faults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;
        }

        host.Probe.Rights = shape == "no-read" ? AccessRights.Write | AccessRights.AppendTo : AccessRights.Read;
        ArrangeDocumentPush(host);

        var response = await host.SendAsync(Push());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Normalize(await response.Content.ReadAsStringAsync()).Should().Be(Normalize(await UnknownSourceBodyAsync()),
            "the same body a source that does not exist gets");
        host.VerifyNothingReadOrWritten();
        host.Writes.Should().BeEmpty();
    }

    private static async Task<string> UnknownSourceBodyAsync()
    {
        var (host, _) = await StartAsync(NarrowingCallerMode.Internal);
        await using var __ = host;
        var response = await host.SendAsync(Push(Guid.NewGuid()));
        return await response.Content.ReadAsStringAsync();
    }
}
