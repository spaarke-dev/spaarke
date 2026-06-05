using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai;

/// <summary>
/// Wire-format regression tests for <see cref="KnowledgeDocument"/>.
///
/// The same model class is serialized into multiple Azure Search indexes whose schemas differ.
/// Azure Search rejects documents with unknown properties (400 "property does not exist on
/// type 'search.documentFields'"). When a property is null/default and the target schema
/// does not declare it, the model must omit the property from the serialized payload — which
/// is what <c>[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]</c> and
/// <c>WhenWritingDefault</c> achieve.
///
/// These tests lock that contract so a future field added to the model without the suppression
/// attribute trips here, not in production at the next SC-18 walkthrough.
///
/// History: R5 SC-18 walkthrough surfaced four cycles of these bugs (Tags=null, then
/// deploymentId/deploymentModel/knowledgeSourceId/parentEntity*/privilege_group_ids as
/// successive 400 "property does not exist" rejections). PR #360 (2026-06-05) applied
/// JsonIgnore.WhenWritingNull / WhenWritingDefault uniformly + added this test.
/// </summary>
public class KnowledgeDocumentSerializationTests
{
    /// <summary>
    /// Canonical field set for the <c>spaarke-session-files</c> index, kept in sync with
    /// <c>infrastructure/ai-search/spaarke-session-files.json</c>. Test failure when adding a
    /// field to the schema is intentional — update this set + the model in lockstep.
    /// </summary>
    private static readonly HashSet<string> SessionFilesIndexFields = new(StringComparer.Ordinal)
    {
        "id",
        "tenantId",
        "sessionId",
        "documentId",
        "speFileId",
        "documentName",
        "fileName",
        "documentType",
        "fileType",
        "chunkIndex",
        "chunkCount",
        "content",
        "contentVector3072",
        "documentVector3072",
        "metadata",
        "tags",
        "createdAt",
        "updatedAt"
    };

    [Fact]
    public void SessionFilesMode_SerializedKeys_AreSubsetOfSessionFilesSchema()
    {
        // Arrange — build a doc the same way RagIndexingPipeline.BuildKnowledgeDocuments does
        // for a session-files write (sessionId != null path).
        var doc = new KnowledgeDocument
        {
            Id = "doc1_s_0",
            TenantId = "tenant-a",
            SessionId = "session-1",
            DocumentId = "doc1",
            SpeFileId = "spe-1",
            FileName = "engagement-letter.docx",
            Content = "chunk body",
            ChunkIndex = 0,
            ChunkCount = 1,
            ContentVector = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f }),
            Tags = Array.Empty<string>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeploymentModel = null,
            PrivilegeGroupIds = null
        };

        // Act
        var json = JsonSerializer.Serialize(doc);
        using var parsed = JsonDocument.Parse(json);
        var actualKeys = parsed.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        // Assert — every key in the payload must exist in the session-files schema.
        var unknownKeys = actualKeys.Except(SessionFilesIndexFields).ToList();
        unknownKeys.Should().BeEmpty(
            "every JSON key written to spaarke-session-files must exist in the index schema; " +
            "found {0} unknown key(s): [{1}]. " +
            "Either add the field to infrastructure/ai-search/spaarke-session-files.json " +
            "(and SessionFilesIndexFields here), or add " +
            "[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] (or WhenWritingDefault) " +
            "to the property on KnowledgeDocument.",
            unknownKeys.Count,
            string.Join(", ", unknownKeys));
    }

    [Fact]
    public void SessionFilesMode_SerializedKeys_ExcludeCustomerCorpusOnlyFields()
    {
        // Arrange — same configuration as RagIndexingPipeline session-files write.
        var doc = new KnowledgeDocument
        {
            Id = "doc1_s_0",
            TenantId = "tenant-a",
            SessionId = "session-1",
            DocumentId = "doc1",
            SpeFileId = "spe-1",
            FileName = "engagement-letter.docx",
            Content = "chunk body",
            ChunkIndex = 0,
            ChunkCount = 1,
            ContentVector = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f }),
            Tags = Array.Empty<string>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeploymentModel = null,
            PrivilegeGroupIds = null
        };

        // Act
        var json = JsonSerializer.Serialize(doc);

        // Assert — explicitly enumerate the customer-corpus-only fields that have caused
        // 400 rejections historically. These MUST NOT appear in a session-files payload.
        var customerCorpusOnlyFields = new[]
        {
            "deploymentId",
            "deploymentModel",
            "knowledgeSourceId",
            "knowledgeSourceName",
            "parentEntityType",
            "parentEntityId",
            "parentEntityname",
            "privilege_group_ids",
            "documentVector3072"
        };

        foreach (var field in customerCorpusOnlyFields)
        {
            json.Should().NotContain($"\"{field}\"",
                "customer-corpus-only field '{0}' must be suppressed when writing to session-files",
                field);
        }
    }

    [Fact]
    public void CustomerCorpusMode_SerializedKeys_RetainDefaults()
    {
        // Arrange — a customer-corpus write does NOT override DeploymentModel or
        // PrivilegeGroupIds; they keep their model defaults ("Shared" and empty list).
        var doc = new KnowledgeDocument
        {
            Id = "doc1_k_0",
            TenantId = "tenant-a",
            DocumentId = "doc1",
            SpeFileId = "spe-1",
            FileName = "policy.pdf",
            Content = "chunk body",
            ChunkIndex = 0,
            ChunkCount = 1,
            ContentVector = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f }),
            Tags = Array.Empty<string>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
            // DeploymentModel left at "Shared", PrivilegeGroupIds left at empty list,
            // SessionId left null (suppressed).
        };

        // Act
        var json = JsonSerializer.Serialize(doc);

        // Assert — customer-corpus defaults are still on the wire (no regression).
        json.Should().Contain("\"deploymentModel\":\"Shared\"");
        json.Should().Contain("\"privilege_group_ids\":[]");
        // SessionId remains suppressed when null (existing canonical pattern).
        json.Should().NotContain("\"sessionId\"");
    }
}
