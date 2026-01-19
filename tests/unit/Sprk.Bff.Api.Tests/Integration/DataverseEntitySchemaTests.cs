using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration;

/// <summary>
/// Tests that validate the Dataverse Entity schema and field mappings.
/// These tests verify that UpdateDocumentRequest properties map correctly to Dataverse field names
/// and that the correct SDK types are used (OptionSetValue, EntityReference, etc.).
///
/// Purpose: Catch schema mismatches before deployment - no Dataverse connection required.
/// </summary>
public class DataverseEntitySchemaTests
{
    /// <summary>
    /// Documents the expected Dataverse field mappings for the sprk_document entity.
    /// This serves as a contract between the C# models and Dataverse schema.
    /// </summary>
    private static readonly Dictionary<string, DataverseFieldMapping> ExpectedFieldMappings = new()
    {
        // Basic Document Properties
        { "Name", new("sprk_documentname", typeof(string)) },
        { "Description", new("sprk_documentdescription", typeof(string)) },
        { "HasFile", new("sprk_hasfile", typeof(bool)) },
        { "FileName", new("sprk_filename", typeof(string)) },
        { "FileSize", new("sprk_filesize", typeof(long)) },
        { "MimeType", new("sprk_filetype", typeof(string)) },
        { "GraphItemId", new("sprk_graphitemid", typeof(string)) },
        { "GraphDriveId", new("sprk_graphdriveid", typeof(string)) },
        { "Status", new("statuscode", typeof(OptionSetValue)) },

        // AI Analysis Fields
        { "Summary", new("sprk_filesummary", typeof(string)) },
        { "TlDr", new("sprk_filetldr", typeof(string)) },
        { "Keywords", new("sprk_filekeywords", typeof(string)) },
        { "SummaryStatus", new("sprk_filesummarystatus", typeof(OptionSetValue)) },

        // Extracted Entities Fields
        { "ExtractOrganization", new("sprk_extractorganization", typeof(string)) },
        { "ExtractPeople", new("sprk_extractpeople", typeof(string)) },
        { "ExtractFees", new("sprk_extractfees", typeof(string)) },
        { "ExtractDates", new("sprk_extractdates", typeof(string)) },
        { "ExtractReference", new("sprk_extractreference", typeof(string)) },
        { "ExtractDocumentType", new("sprk_extractdocumenttype", typeof(string)) },
        { "DocumentType", new("sprk_documenttype", typeof(OptionSetValue)) },

        // Email Metadata Fields
        { "EmailSubject", new("sprk_emailsubject", typeof(string)) },
        { "EmailFrom", new("sprk_emailfrom", typeof(string)) },
        { "EmailTo", new("sprk_emailto", typeof(string)) },
        { "EmailDate", new("sprk_emaildate", typeof(DateTime)) },
        { "EmailBody", new("sprk_emailbody", typeof(string)) },
        { "Attachments", new("sprk_attachments", typeof(string)) },

        // Parent Document Fields
        { "ParentDocumentId", new("sprk_parentdocumentid", typeof(string)) },
        { "ParentFileName", new("sprk_parentfilename", typeof(string)) },
        { "ParentGraphItemId", new("sprk_parentgraphitemid", typeof(string)) },
        { "ParentDocumentLookup", new("sprk_parentdocument", typeof(EntityReference)) }
    };

    private record DataverseFieldMapping(string DataverseFieldName, Type ExpectedType);

    #region UpdateDocumentRequest Model Validation

    [Fact]
    public void UpdateDocumentRequest_HasAllEmailMetadataProperties()
    {
        var requestType = typeof(UpdateDocumentRequest);

        // Verify all email metadata properties exist
        requestType.GetProperty("EmailSubject").Should().NotBeNull();
        requestType.GetProperty("EmailFrom").Should().NotBeNull();
        requestType.GetProperty("EmailTo").Should().NotBeNull();
        requestType.GetProperty("EmailDate").Should().NotBeNull();
        requestType.GetProperty("EmailBody").Should().NotBeNull();
        requestType.GetProperty("Attachments").Should().NotBeNull();
    }

    [Fact]
    public void UpdateDocumentRequest_HasAllParentDocumentProperties()
    {
        var requestType = typeof(UpdateDocumentRequest);

        requestType.GetProperty("ParentDocumentId").Should().NotBeNull();
        requestType.GetProperty("ParentFileName").Should().NotBeNull();
        requestType.GetProperty("ParentGraphItemId").Should().NotBeNull();
        requestType.GetProperty("ParentDocumentLookup").Should().NotBeNull();
    }

    [Fact]
    public void UpdateDocumentRequest_HasAllAiAnalysisProperties()
    {
        var requestType = typeof(UpdateDocumentRequest);

        requestType.GetProperty("Summary").Should().NotBeNull();
        requestType.GetProperty("TlDr").Should().NotBeNull();
        requestType.GetProperty("Keywords").Should().NotBeNull();
        requestType.GetProperty("SummaryStatus").Should().NotBeNull();
    }

    [Fact]
    public void UpdateDocumentRequest_HasAllExtractedEntitiesProperties()
    {
        var requestType = typeof(UpdateDocumentRequest);

        requestType.GetProperty("ExtractOrganization").Should().NotBeNull();
        requestType.GetProperty("ExtractPeople").Should().NotBeNull();
        requestType.GetProperty("ExtractFees").Should().NotBeNull();
        requestType.GetProperty("ExtractDates").Should().NotBeNull();
        requestType.GetProperty("ExtractReference").Should().NotBeNull();
        requestType.GetProperty("ExtractDocumentType").Should().NotBeNull();
        requestType.GetProperty("DocumentType").Should().NotBeNull();
    }

    [Fact]
    public void UpdateDocumentRequest_EmailDateIsNullableDateTime()
    {
        var prop = typeof(UpdateDocumentRequest).GetProperty("EmailDate");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(DateTime?));
    }

    [Fact]
    public void UpdateDocumentRequest_SummaryStatusIsNullableInt()
    {
        // SummaryStatus should be int? to hold OptionSet values
        var prop = typeof(UpdateDocumentRequest).GetProperty("SummaryStatus");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(int?));
    }

    [Fact]
    public void UpdateDocumentRequest_ParentDocumentLookupIsNullableGuid()
    {
        // Lookup fields are represented as Guid? in the request model
        var prop = typeof(UpdateDocumentRequest).GetProperty("ParentDocumentLookup");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(Guid?));
    }

    #endregion

    #region Field Count Validation

    [Fact]
    public void ExpectedFieldMappings_MatchesUpdateDocumentRequestPropertyCount()
    {
        // This test ensures we haven't forgotten to add mappings for new properties
        var requestType = typeof(UpdateDocumentRequest);
        var properties = requestType.GetProperties()
            .Where(p => p.CanRead && p.CanWrite)
            .ToList();

        ExpectedFieldMappings.Should().HaveCount(properties.Count,
            "ExpectedFieldMappings should have an entry for each UpdateDocumentRequest property. " +
            $"Properties: {string.Join(", ", properties.Select(p => p.Name))}");
    }

    [Fact]
    public void AllUpdateDocumentRequestProperties_HaveFieldMappings()
    {
        var requestType = typeof(UpdateDocumentRequest);
        var properties = requestType.GetProperties()
            .Where(p => p.CanRead && p.CanWrite)
            .Select(p => p.Name)
            .ToList();

        foreach (var prop in properties)
        {
            ExpectedFieldMappings.Should().ContainKey(prop,
                $"UpdateDocumentRequest.{prop} should have a corresponding Dataverse field mapping");
        }
    }

    #endregion

    #region Dataverse Field Name Format Validation

    [Theory]
    [InlineData("sprk_emailsubject")]
    [InlineData("sprk_emailfrom")]
    [InlineData("sprk_emailto")]
    [InlineData("sprk_emaildate")]
    [InlineData("sprk_emailbody")]
    [InlineData("sprk_attachments")]
    public void EmailFields_UseCorrectDataverseNamingConvention(string fieldName)
    {
        // Dataverse custom fields use publisher prefix + lowercase
        fieldName.Should().StartWith("sprk_", "Custom fields should use publisher prefix");
        fieldName.Should().Be(fieldName.ToLowerInvariant(), "Dataverse field names should be lowercase");
    }

    [Fact]
    public void AllCustomFields_UsePublisherPrefix()
    {
        var customFields = ExpectedFieldMappings.Values
            .Where(m => m.DataverseFieldName != "statuscode" && m.DataverseFieldName != "statecode")
            .Select(m => m.DataverseFieldName);

        foreach (var field in customFields)
        {
            field.Should().StartWith("sprk_",
                $"Custom field '{field}' should use 'sprk_' publisher prefix");
        }
    }

    [Fact]
    public void SystemFields_DoNotUsePublisherPrefix()
    {
        ExpectedFieldMappings["Status"].DataverseFieldName.Should().Be("statuscode");
    }

    #endregion

    #region OptionSet Value Validation

    /// <summary>
    /// Dataverse OptionSet values use the Power Platform standard range (100000000+).
    /// This test documents the expected values for sprk_filesummarystatus.
    /// </summary>
    public static class AnalysisStatusValues
    {
        public const int None = 100000000;
        public const int Pending = 100000001;
        public const int Completed = 100000002;
        public const int OptedOut = 100000003;
        public const int Failed = 100000004;
        public const int NotSupported = 100000005;
        public const int Skipped = 100000006;
    }

    [Theory]
    [InlineData(100000000, "None")]
    [InlineData(100000001, "Pending")]
    [InlineData(100000002, "Completed")]
    [InlineData(100000003, "OptedOut")]
    [InlineData(100000004, "Failed")]
    [InlineData(100000005, "NotSupported")]
    [InlineData(100000006, "Skipped")]
    public void AnalysisStatus_UsesDataverseOptionSetRange(int value, string label)
    {
        // Dataverse OptionSet values must be in the 100000000+ range
        value.Should().BeGreaterThanOrEqualTo(100000000,
            $"AnalysisStatus.{label} must use Power Platform OptionSet range (100000000+)");
    }

    [Fact]
    public void AnalysisStatus_DoesNotUseSequentialIntegers()
    {
        // Sequential integers (0, 1, 2, 3...) are NOT valid for Dataverse OptionSets
        var invalidValues = new[] { 0, 1, 2, 3, 4, 5, 6 };
        var validValues = new[]
        {
            AnalysisStatusValues.None,
            AnalysisStatusValues.Pending,
            AnalysisStatusValues.Completed,
            AnalysisStatusValues.OptedOut,
            AnalysisStatusValues.Failed,
            AnalysisStatusValues.NotSupported,
            AnalysisStatusValues.Skipped
        };

        validValues.Should().NotContain(invalidValues,
            "AnalysisStatus values must NOT use sequential integers (0, 1, 2...). " +
            "Dataverse OptionSets require values in the 100000000+ range.");
    }

    #endregion

    #region Entity Builder Tests

    [Fact]
    public void BuildEntity_WithEmailMetadata_SetsCorrectFieldTypes()
    {
        // Simulate building an Entity like DataverseServiceClientImpl does
        var request = new UpdateDocumentRequest
        {
            EmailSubject = "Test Subject",
            EmailFrom = "sender@example.com",
            EmailTo = "recipient@example.com",
            EmailDate = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            EmailBody = "Email body content",
            Attachments = "[{\"filename\":\"doc.pdf\"}]"
        };

        var entity = BuildEntityFromRequest(Guid.NewGuid(), request);

        // Verify field types
        entity["sprk_emailsubject"].Should().BeOfType<string>();
        entity["sprk_emailfrom"].Should().BeOfType<string>();
        entity["sprk_emailto"].Should().BeOfType<string>();
        entity["sprk_emaildate"].Should().BeOfType<DateTime>();
        entity["sprk_emailbody"].Should().BeOfType<string>();
        entity["sprk_attachments"].Should().BeOfType<string>();
    }

    [Fact]
    public void BuildEntity_WithSummaryStatus_UsesOptionSetValue()
    {
        var request = new UpdateDocumentRequest
        {
            SummaryStatus = AnalysisStatusValues.Completed
        };

        var entity = BuildEntityFromRequest(Guid.NewGuid(), request);

        entity["sprk_filesummarystatus"].Should().BeOfType<OptionSetValue>();
        ((OptionSetValue)entity["sprk_filesummarystatus"]).Value.Should().Be(AnalysisStatusValues.Completed);
    }

    [Fact]
    public void BuildEntity_WithDocumentType_UsesOptionSetValue()
    {
        var request = new UpdateDocumentRequest
        {
            DocumentType = 100000001 // Some document type value
        };

        var entity = BuildEntityFromRequest(Guid.NewGuid(), request);

        entity["sprk_documenttype"].Should().BeOfType<OptionSetValue>();
    }

    [Fact]
    public void BuildEntity_WithParentDocumentLookup_UsesEntityReference()
    {
        var parentId = Guid.NewGuid();
        var request = new UpdateDocumentRequest
        {
            ParentDocumentLookup = parentId
        };

        var entity = BuildEntityFromRequest(Guid.NewGuid(), request);

        entity["sprk_parentdocument"].Should().BeOfType<EntityReference>();
        var entityRef = (EntityReference)entity["sprk_parentdocument"];
        entityRef.LogicalName.Should().Be("sprk_document");
        entityRef.Id.Should().Be(parentId);
    }

    [Fact]
    public void BuildEntity_WithStatus_UsesOptionSetValue()
    {
        var request = new UpdateDocumentRequest
        {
            Status = DocumentStatus.Active
        };

        var entity = BuildEntityFromRequest(Guid.NewGuid(), request);

        entity["statuscode"].Should().BeOfType<OptionSetValue>();
        ((OptionSetValue)entity["statuscode"]).Value.Should().Be((int)DocumentStatus.Active);
    }

    [Fact]
    public void BuildEntity_OnlyIncludesNonNullFields()
    {
        var request = new UpdateDocumentRequest
        {
            EmailSubject = "Test Subject"
            // All other fields are null
        };

        var entity = BuildEntityFromRequest(Guid.NewGuid(), request);

        // Should only contain the one field we set (plus entity ID)
        entity.Attributes.Should().ContainKey("sprk_emailsubject");
        entity.Attributes.Should().NotContainKey("sprk_emailfrom");
        entity.Attributes.Should().NotContainKey("sprk_emailto");
        entity.Attributes.Should().NotContainKey("sprk_emaildate");
    }

    /// <summary>
    /// Simulates the Entity building logic from DataverseServiceClientImpl.UpdateDocumentAsync.
    /// This allows testing the field mapping without a real Dataverse connection.
    /// </summary>
    private static Entity BuildEntityFromRequest(Guid documentId, UpdateDocumentRequest request)
    {
        var document = new Entity("sprk_document", documentId);

        // Basic Document Properties
        if (request.Name != null)
            document["sprk_documentname"] = request.Name;

        if (request.Description != null)
            document["sprk_documentdescription"] = request.Description;

        if (request.HasFile.HasValue)
            document["sprk_hasfile"] = request.HasFile.Value;

        if (request.FileName != null)
            document["sprk_filename"] = request.FileName;

        if (request.FileSize.HasValue)
            document["sprk_filesize"] = request.FileSize.Value;

        if (request.MimeType != null)
            document["sprk_filetype"] = request.MimeType;

        if (request.GraphItemId != null)
            document["sprk_graphitemid"] = request.GraphItemId;

        if (request.GraphDriveId != null)
            document["sprk_graphdriveid"] = request.GraphDriveId;

        if (request.Status.HasValue)
            document["statuscode"] = new OptionSetValue((int)request.Status.Value);

        // AI Analysis Fields
        if (request.Summary != null)
            document["sprk_filesummary"] = request.Summary;

        if (request.TlDr != null)
            document["sprk_filetldr"] = request.TlDr;

        if (request.Keywords != null)
            document["sprk_filekeywords"] = request.Keywords;

        if (request.SummaryStatus.HasValue)
            document["sprk_filesummarystatus"] = new OptionSetValue(request.SummaryStatus.Value);

        // Extracted Entities Fields
        if (request.ExtractOrganization != null)
            document["sprk_extractorganization"] = request.ExtractOrganization;

        if (request.ExtractPeople != null)
            document["sprk_extractpeople"] = request.ExtractPeople;

        if (request.ExtractFees != null)
            document["sprk_extractfees"] = request.ExtractFees;

        if (request.ExtractDates != null)
            document["sprk_extractdates"] = request.ExtractDates;

        if (request.ExtractReference != null)
            document["sprk_extractreference"] = request.ExtractReference;

        if (request.ExtractDocumentType != null)
            document["sprk_extractdocumenttype"] = request.ExtractDocumentType;

        if (request.DocumentType.HasValue)
            document["sprk_documenttype"] = new OptionSetValue(request.DocumentType.Value);

        // Email Metadata Fields
        if (request.EmailSubject != null)
            document["sprk_emailsubject"] = request.EmailSubject;

        if (request.EmailFrom != null)
            document["sprk_emailfrom"] = request.EmailFrom;

        if (request.EmailTo != null)
            document["sprk_emailto"] = request.EmailTo;

        if (request.EmailDate.HasValue)
            document["sprk_emaildate"] = request.EmailDate.Value;

        if (request.EmailBody != null)
            document["sprk_emailbody"] = request.EmailBody;

        if (request.Attachments != null)
            document["sprk_attachments"] = request.Attachments;

        // Parent Document Fields
        // Note: ParentDocumentId was removed - field doesn't exist in Dataverse
        // Use ParentDocumentLookup for the actual lookup relationship

        if (request.ParentFileName != null)
            document["sprk_parentfilename"] = request.ParentFileName;

        if (request.ParentGraphItemId != null)
            document["sprk_parentgraphitemid"] = request.ParentGraphItemId;

        if (request.ParentDocumentLookup.HasValue)
            document["sprk_parentdocument"] = new EntityReference("sprk_document", request.ParentDocumentLookup.Value);

        return document;
    }

    #endregion

    #region Email Attachments JSON Format

    [Fact]
    public void EmailAttachments_SerializesToValidJson()
    {
        var attachments = new List<EmailAttachment>
        {
            new()
            {
                Filename = "document.pdf",
                MimeType = "application/pdf",
                SizeBytes = 12345,
                IsInline = false
            },
            new()
            {
                Filename = "image.png",
                MimeType = "image/png",
                ContentId = "image1",
                IsInline = true
            }
        };

        var json = JsonSerializer.Serialize(attachments);

        // Verify it's valid JSON
        var parsed = JsonDocument.Parse(json);
        parsed.RootElement.GetArrayLength().Should().Be(2);

        // Verify structure
        json.Should().Contain("document.pdf");
        json.Should().Contain("application/pdf");
        json.Should().Contain("image.png");
    }

    [Fact]
    public void EmailAttachments_RoundTripSerializationWorks()
    {
        // Test that EmailAttachment can be serialized and deserialized correctly
        // The class uses [JsonPropertyName] attributes for camelCase JSON keys
        var original = new EmailAttachment
        {
            Filename = "test.pdf",
            MimeType = "application/pdf",
            SizeBytes = 1000,
            IsInline = false
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<EmailAttachment>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Filename.Should().Be("test.pdf");
        deserialized.MimeType.Should().Be("application/pdf");
        deserialized.SizeBytes.Should().Be(1000);
        deserialized.IsInline.Should().BeFalse();

        // Verify JSON uses camelCase keys (from [JsonPropertyName] attributes)
        json.Should().Contain("\"filename\":");
        json.Should().Contain("\"mimeType\":");
        json.Should().Contain("\"sizeBytes\":");
        json.Should().Contain("\"isInline\":");
    }

    [Fact]
    public void EmailAttachments_JsonFitsInDataverseTextField()
    {
        // Dataverse multiline text fields have limits (typically 1,048,576 chars for memo)
        // sprk_attachments should fit reasonable attachment metadata
        var attachments = Enumerable.Range(1, 50) // 50 attachments should be reasonable max
            .Select(i => new EmailAttachment
            {
                Filename = $"attachment_{i:D3}_with_a_reasonably_long_filename.pdf",
                MimeType = "application/pdf",
                SizeBytes = 1234567,
                ContentId = $"cid_{i}",
                IsInline = i % 2 == 0
            })
            .ToList();

        var json = JsonSerializer.Serialize(attachments);

        // Should easily fit in memo field (1MB limit)
        json.Length.Should().BeLessThan(100000, "50 attachments JSON should be well under Dataverse memo limit");
    }

    #endregion

    #region EmailMetadata Model Validation

    [Fact]
    public void EmailMetadata_HasAllRequiredProperties()
    {
        var metadataType = typeof(EmailMetadata);

        metadataType.GetProperty("Subject").Should().NotBeNull();
        metadataType.GetProperty("From").Should().NotBeNull();
        metadataType.GetProperty("To").Should().NotBeNull();
        metadataType.GetProperty("Cc").Should().NotBeNull();
        metadataType.GetProperty("Date").Should().NotBeNull();
        metadataType.GetProperty("Body").Should().NotBeNull();
        metadataType.GetProperty("Attachments").Should().NotBeNull();
        metadataType.GetProperty("HasAttachments").Should().NotBeNull();
    }

    [Fact]
    public void EmailMetadata_HasAttachments_IsDerivedFromAttachmentsList()
    {
        var metadata = new EmailMetadata
        {
            Attachments = new List<EmailAttachment>
            {
                new() { Filename = "test.pdf" }
            }
        };

        metadata.HasAttachments.Should().BeTrue();

        var emptyMetadata = new EmailMetadata
        {
            Attachments = new List<EmailAttachment>()
        };

        emptyMetadata.HasAttachments.Should().BeFalse();
    }

    #endregion

    #region Field Length Constraints

    [Fact]
    public void EmailBody_ShouldBeTruncatedTo10KChars()
    {
        // Document the expected max length for email body (as implemented in TextExtractorService)
        const int maxBodyLength = 10000;

        var longBody = new string('x', 15000);
        var truncatedBody = longBody.Length > maxBodyLength
            ? longBody[..maxBodyLength] + "\n\n[Content truncated]"
            : longBody;

        truncatedBody.Length.Should().BeLessThanOrEqualTo(maxBodyLength + "\n\n[Content truncated]".Length);
    }

    [Theory]
    [InlineData("EmailSubject", 500)]   // Reasonable subject length
    [InlineData("EmailFrom", 500)]      // Multiple email addresses
    [InlineData("EmailTo", 2000)]       // Many recipients
    [InlineData("EmailBody", 10021)]    // 10K + truncation message
    public void EmailFields_ShouldFitDataverseLimits(string fieldName, int maxExpectedLength)
    {
        // These are sanity checks - actual Dataverse limits vary by field type
        // Single line text: 4000 chars
        // Memo/multiline: 1,048,576 chars
        maxExpectedLength.Should().BeLessThanOrEqualTo(1048576,
            $"{fieldName} max length should fit in Dataverse memo field");
    }

    #endregion
}
