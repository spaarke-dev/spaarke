using FluentAssertions;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Email;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Tests for GraphAttachmentAdapter that maps Graph FileAttachment to EmailAttachmentInfo.
/// </summary>
public class GraphAttachmentAdapterTests
{
    #region ToAttachmentInfo - Single Attachment

    [Fact]
    public void ToAttachmentInfo_MapsAllFieldsCorrectly()
    {
        // Arrange
        var content = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // ZIP magic bytes
        var graphAttachment = new FileAttachment
        {
            Name = "report.pdf",
            ContentType = "application/pdf",
            ContentBytes = content,
            IsInline = false,
            ContentId = null
        };

        // Act
        var result = GraphAttachmentAdapter.ToAttachmentInfo(graphAttachment);

        // Assert
        result.AttachmentId.Should().Be(Guid.Empty);
        result.FileName.Should().Be("report.pdf");
        result.MimeType.Should().Be("application/pdf");
        result.SizeBytes.Should().Be(4);
        result.IsInline.Should().BeFalse();
        result.ContentId.Should().BeNull();
        result.ShouldCreateDocument.Should().BeTrue();

        // Verify stream content
        using var ms = new MemoryStream();
        result.Content!.CopyTo(ms);
        ms.ToArray().Should().BeEquivalentTo(content);
    }

    [Fact]
    public void ToAttachmentInfo_InlineAttachment_SetsIsInlineAndContentId()
    {
        // Arrange
        var graphAttachment = new FileAttachment
        {
            Name = "logo.png",
            ContentType = "image/png",
            ContentBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            IsInline = true,
            ContentId = "logo@company.com"
        };

        // Act
        var result = GraphAttachmentAdapter.ToAttachmentInfo(graphAttachment);

        // Assert
        result.IsInline.Should().BeTrue();
        result.ContentId.Should().Be("logo@company.com");
    }

    [Fact]
    public void ToAttachmentInfo_MissingContentType_DefaultsToOctetStream()
    {
        // Arrange
        var graphAttachment = new FileAttachment
        {
            Name = "data.bin",
            ContentType = null,
            ContentBytes = new byte[] { 0x01 }
        };

        // Act
        var result = GraphAttachmentAdapter.ToAttachmentInfo(graphAttachment);

        // Assert
        result.MimeType.Should().Be("application/octet-stream");
    }

    [Fact]
    public void ToAttachmentInfo_MissingName_DefaultsToAttachment()
    {
        // Arrange
        var graphAttachment = new FileAttachment
        {
            Name = null,
            ContentType = "text/plain",
            ContentBytes = new byte[] { 0x41 }
        };

        // Act
        var result = GraphAttachmentAdapter.ToAttachmentInfo(graphAttachment);

        // Assert
        result.FileName.Should().Be("attachment");
    }

    [Fact]
    public void ToAttachmentInfo_NullContentBytes_ReturnsEmptyStreamWithZeroSize()
    {
        // Arrange
        var graphAttachment = new FileAttachment
        {
            Name = "empty.txt",
            ContentType = "text/plain",
            ContentBytes = null
        };

        // Act
        var result = GraphAttachmentAdapter.ToAttachmentInfo(graphAttachment);

        // Assert
        result.SizeBytes.Should().Be(0);
        result.Content.Should().NotBeNull();
        result.Content!.Length.Should().Be(0);
    }

    #endregion

    #region ToAttachmentInfoList - Collection

    [Fact]
    public void ToAttachmentInfoList_FiltersNonFileAttachmentTypes()
    {
        // Arrange - mix of FileAttachment and ItemAttachment
        var attachments = new List<Attachment>
        {
            new FileAttachment
            {
                Name = "doc.pdf",
                ContentType = "application/pdf",
                ContentBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }
            },
            new ItemAttachment
            {
                Name = "forwarded-message"
            },
            new FileAttachment
            {
                Name = "image.jpg",
                ContentType = "image/jpeg",
                ContentBytes = new byte[] { 0xFF, 0xD8, 0xFF }
            }
        };

        // Act
        var result = GraphAttachmentAdapter.ToAttachmentInfoList(attachments);

        // Assert
        result.Should().HaveCount(2);
        result[0].FileName.Should().Be("doc.pdf");
        result[1].FileName.Should().Be("image.jpg");
    }

    [Fact]
    public void ToAttachmentInfoList_NullInput_ReturnsEmptyList()
    {
        // Act
        var result = GraphAttachmentAdapter.ToAttachmentInfoList(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ToAttachmentInfoList_SkipsFileAttachmentsWithEmptyContentBytes()
    {
        // Arrange
        var attachments = new List<Attachment>
        {
            new FileAttachment
            {
                Name = "valid.pdf",
                ContentType = "application/pdf",
                ContentBytes = new byte[] { 0x25, 0x50 }
            },
            new FileAttachment
            {
                Name = "empty.pdf",
                ContentType = "application/pdf",
                ContentBytes = Array.Empty<byte>()
            },
            new FileAttachment
            {
                Name = "null-content.pdf",
                ContentType = "application/pdf",
                ContentBytes = null
            }
        };

        // Act
        var result = GraphAttachmentAdapter.ToAttachmentInfoList(attachments);

        // Assert
        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("valid.pdf");
    }

    #endregion
}
