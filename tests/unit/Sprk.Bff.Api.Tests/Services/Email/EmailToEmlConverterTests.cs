using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using Moq.Protected;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Email;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Email;

/// <summary>
/// A fake TokenCredential for testing that returns a pre-set token without calling Azure AD.
/// </summary>
internal class FakeTokenCredential : TokenCredential
{
    private readonly string _token;

    public FakeTokenCredential(string token = "fake-test-token")
    {
        _token = token;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1)));
    }
}

public class EmailToEmlConverterTests
{
    private readonly Mock<ILogger<EmailToEmlConverter>> _loggerMock;
    private readonly IOptions<EmailProcessingOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _fakeCredential;

    public EmailToEmlConverterTests()
    {
        _loggerMock = new Mock<ILogger<EmailToEmlConverter>>();
        _options = Options.Create(new EmailProcessingOptions
        {
            MaxAttachmentSizeMB = 25,
            MaxTotalSizeMB = 100,
            BlockedAttachmentExtensions = [".exe", ".dll", ".bat"],
            SignatureImagePatterns = [@"^image\d{3}\.(png|gif|jpg)$", @"^spacer\.gif$"],
            MinImageSizeKB = 5,
            MaxEmlFileNameLength = 100
        });

        var configData = new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com"
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri("https://test.crm.dynamics.com/api/data/v9.2/")
        };

        _fakeCredential = new FakeTokenCredential();
    }

    private EmailToEmlConverter CreateConverter()
    {
        return new EmailToEmlConverter(_httpClient, _options, _configuration, _loggerMock.Object, _fakeCredential);
    }

    #region GenerateEmlFileNameAsync Tests

    [Fact]
    public async Task GenerateEmlFileNameAsync_WithValidEmail_ReturnsFormattedFileName()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var emailResponse = new
        {
            subject = "Test Email Subject",
            createdon = "2024-06-15T10:30:00Z"
        };

        SetupHttpResponse($"emails({emailId})", HttpStatusCode.OK, emailResponse);
        var converter = CreateConverter();

        // Act
        var result = await converter.GenerateEmlFileNameAsync(emailId);

        // Assert
        result.Should().StartWith("2024-06-15_");
        result.Should().Contain("Test_Email_Subject");
        result.Should().EndWith(".eml");
    }

    [Fact]
    public async Task GenerateEmlFileNameAsync_WithSpecialCharacters_SanitizesFileName()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var emailResponse = new
        {
            subject = "Re: Meeting @ 3PM - Budget/Plan <Important>",
            createdon = "2024-06-15T10:30:00Z"
        };

        SetupHttpResponse($"emails({emailId})", HttpStatusCode.OK, emailResponse);
        var converter = CreateConverter();

        // Act
        var result = await converter.GenerateEmlFileNameAsync(emailId);

        // Assert
        // Only characters invalid in Windows filenames are replaced: : < > " / \ | ? *
        // @ and - are valid characters in filenames
        result.Should().NotContain("/");
        result.Should().NotContain("<");
        result.Should().NotContain(">");
        result.Should().NotContain(":");
        result.Should().EndWith(".eml");
    }

    [Fact]
    public async Task GenerateEmlFileNameAsync_WithLongSubject_TruncatesFileName()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var longSubject = new string('A', 200);
        var emailResponse = new
        {
            subject = longSubject,
            createdon = "2024-06-15T10:30:00Z"
        };

        SetupHttpResponse($"emails({emailId})", HttpStatusCode.OK, emailResponse);
        var converter = CreateConverter();

        // Act
        var result = await converter.GenerateEmlFileNameAsync(emailId);

        // Assert
        result.Length.Should().BeLessOrEqualTo(104); // 100 max + ".eml"
    }

    [Fact]
    public async Task GenerateEmlFileNameAsync_WithEmptySubject_UsesDefaultName()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var emailResponse = new
        {
            subject = (string?)null,
            createdon = "2024-06-15T10:30:00Z"
        };

        SetupHttpResponse($"emails({emailId})", HttpStatusCode.OK, emailResponse);
        var converter = CreateConverter();

        // Act
        var result = await converter.GenerateEmlFileNameAsync(emailId);

        // Assert
        result.Should().StartWith("2024-06-15_");
        // When subject is null, the converter uses "No Subject" which gets sanitized to "No_Subject"
        result.Should().Contain("No_Subject");
        result.Should().EndWith(".eml");
    }

    #endregion

    #region ConvertToEmlAsync Tests

    [Fact]
    public async Task ConvertToEmlAsync_WithValidEmail_ReturnsSuccessResult()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        SetupEmailActivityResponse(emailId, "Test Subject", "sender@test.com", "recipient@test.com", "Email body content");
        SetupAttachmentsResponse(emailId, []);

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();
        result.EmlStream.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Subject.Should().Be("Test Subject");
        result.Metadata.From.Should().Contain("sender@test.com");
        result.Metadata.To.Should().Contain("recipient@test.com");
        result.FileSizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConvertToEmlAsync_GeneratesValidMimeMessage()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        SetupEmailActivityResponse(emailId, "Test RFC 5322", "from@test.com", "to@test.com", "Plain text body");
        SetupAttachmentsResponse(emailId, []);

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();

        // Parse the generated EML to verify it's valid
        result.EmlStream!.Position = 0;
        var message = await MimeMessage.LoadAsync(result.EmlStream);

        message.Subject.Should().Be("Test RFC 5322");
        message.From.ToString().Should().Contain("from@test.com");
        message.To.ToString().Should().Contain("to@test.com");
    }

    [Fact]
    public async Task ConvertToEmlAsync_WithHtmlBody_CreatesMultipartAlternative()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        SetupEmailActivityResponse(
            emailId,
            "HTML Email",
            "sender@test.com",
            "recipient@test.com",
            "<html><body><p>HTML content</p></body></html>",
            isHtml: true);
        SetupAttachmentsResponse(emailId, []);

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();
        result.EmlStream!.Position = 0;
        var message = await MimeMessage.LoadAsync(result.EmlStream);

        // Should have body with HTML content
        message.HtmlBody.Should().Contain("HTML content");
    }

    [Fact]
    public async Task ConvertToEmlAsync_WithAttachments_EmbedsAttachmentsInMime()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        SetupEmailActivityResponse(emailId, "Email with Attachment", "sender@test.com", "recipient@test.com", "Body");

        var attachments = new[]
        {
            new { filename = "document.pdf", mimetype = "application/pdf", body = Convert.ToBase64String(Encoding.UTF8.GetBytes("PDF content")), filesize = 11L }
        };
        SetupAttachmentsResponse(emailId, attachments);

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();
        result.Attachments.Should().HaveCount(1);
        result.Attachments[0].FileName.Should().Be("document.pdf");
        result.Attachments[0].MimeType.Should().Be("application/pdf");

        // Verify attachment is in MIME structure
        result.EmlStream!.Position = 0;
        var message = await MimeMessage.LoadAsync(result.EmlStream);
        message.Attachments.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConvertToEmlAsync_WithBlockedAttachment_SkipsBlockedExtension()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        SetupEmailActivityResponse(emailId, "Email with Blocked Attachment", "sender@test.com", "recipient@test.com", "Body");

        var attachments = new[]
        {
            new { filename = "malware.exe", mimetype = "application/octet-stream", body = Convert.ToBase64String(Encoding.UTF8.GetBytes("bad")), filesize = 3L },
            new { filename = "document.pdf", mimetype = "application/pdf", body = Convert.ToBase64String(Encoding.UTF8.GetBytes("good")), filesize = 4L }
        };
        SetupAttachmentsResponse(emailId, attachments);

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();
        // Should have 2 attachments but only 1 marked for document creation
        var shouldCreate = result.Attachments.Where(a => a.ShouldCreateDocument).ToList();
        shouldCreate.Should().HaveCount(1);
        shouldCreate[0].FileName.Should().Be("document.pdf");
    }

    [Fact]
    public async Task ConvertToEmlAsync_WithSignatureImage_SkipsSignatureImages()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        SetupEmailActivityResponse(emailId, "Email with Signature", "sender@test.com", "recipient@test.com", "Body");

        var attachments = new[]
        {
            new { filename = "image001.png", mimetype = "image/png", body = Convert.ToBase64String(new byte[100]), filesize = 100L }, // Matches signature pattern
            new { filename = "report.pdf", mimetype = "application/pdf", body = Convert.ToBase64String(Encoding.UTF8.GetBytes("report")), filesize = 6L }
        };
        SetupAttachmentsResponse(emailId, attachments);

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();
        var skipped = result.Attachments.FirstOrDefault(a => a.FileName == "image001.png");
        skipped.Should().NotBeNull();
        skipped!.ShouldCreateDocument.Should().BeFalse();
        skipped.SkipReason.Should().Contain("signature");
    }

    [Fact]
    public async Task ConvertToEmlAsync_WithoutAttachments_OmitsAttachmentFetch()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        SetupEmailActivityResponse(emailId, "No Attachments", "sender@test.com", "recipient@test.com", "Body");
        // Don't setup attachments response - it shouldn't be called

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: false);

        // Assert
        result.Success.Should().BeTrue();
        result.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task ConvertToEmlAsync_WithCcRecipients_IncludesCcInMessage()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var emailResponse = CreateEmailResponse(
            subject: "CC Email",
            sender: "sender@test.com",
            toRecipients: "to@test.com",
            ccRecipients: "cc1@test.com;cc2@test.com",
            body: "Body");

        SetupHttpResponse($"emails({emailId})", HttpStatusCode.OK, emailResponse);
        SetupAttachmentsResponse(emailId, []);

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();
        result.Metadata!.Cc.Should().Contain("cc1@test.com");
        result.Metadata.Cc.Should().Contain("cc2@test.com");

        result.EmlStream!.Position = 0;
        var message = await MimeMessage.LoadAsync(result.EmlStream);
        message.Cc.Count.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task ConvertToEmlAsync_SetsCorrectEmailDirection_ForInbound()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        // In Dataverse: directioncode = true means Incoming (received email)
        var emailResponse = CreateEmailResponse(
            subject: "Received Email",
            sender: "external@other.com",
            toRecipients: "me@test.com",
            body: "Inbound email",
            directionCode: true); // true = Incoming in Dataverse

        SetupHttpResponse($"emails({emailId})", HttpStatusCode.OK, emailResponse);
        SetupAttachmentsResponse(emailId, []);

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();
        result.Metadata!.Direction.Should().Be(100000000); // Received
    }

    [Fact]
    public async Task ConvertToEmlAsync_SetsCorrectEmailDirection_ForOutbound()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        // In Dataverse: directioncode = false means Outgoing (sent email)
        var emailResponse = CreateEmailResponse(
            subject: "Sent Email",
            sender: "me@test.com",
            toRecipients: "external@other.com",
            body: "Outbound email",
            directionCode: false); // false = Outgoing in Dataverse

        SetupHttpResponse($"emails({emailId})", HttpStatusCode.OK, emailResponse);
        SetupAttachmentsResponse(emailId, []);

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();
        result.Metadata!.Direction.Should().Be(100000001); // Sent
    }

    [Fact]
    public async Task ConvertToEmlAsync_IncludesTrackingToken_WhenPresent()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var emailResponse = CreateEmailResponse(
            subject: "Tracked Email",
            sender: "sender@test.com",
            toRecipients: "recipient@test.com",
            body: "Body",
            trackingToken: "CRM:12345");

        SetupHttpResponse($"emails({emailId})", HttpStatusCode.OK, emailResponse);
        SetupAttachmentsResponse(emailId, []);

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();
        result.Metadata!.TrackingToken.Should().Be("CRM:12345");
    }

    [Fact]
    public async Task ConvertToEmlAsync_EmailNotFound_ReturnsFailure()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        SetupHttpResponse($"emails({emailId})", HttpStatusCode.NotFound, new { error = new { message = "Not found" } });

        var converter = CreateConverter();

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ConvertToEmlAsync_WithLargeAttachment_SkipsOversizedAttachment()
    {
        // Arrange - set max to 1MB for test
        var smallOptions = Options.Create(new EmailProcessingOptions
        {
            MaxAttachmentSizeMB = 1,
            MaxTotalSizeMB = 100,
            BlockedAttachmentExtensions = [],
            SignatureImagePatterns = [],
            MinImageSizeKB = 5
        });

        var emailId = Guid.NewGuid();
        SetupEmailActivityResponse(emailId, "Large Attachment Email", "sender@test.com", "recipient@test.com", "Body");

        var attachments = new[]
        {
            new { filename = "huge.pdf", mimetype = "application/pdf", body = Convert.ToBase64String(new byte[10]), filesize = 2_000_000L } // 2MB > 1MB limit
        };
        SetupAttachmentsResponse(emailId, attachments);

        var converter = new EmailToEmlConverter(_httpClient, smallOptions, _configuration, _loggerMock.Object, _fakeCredential);

        // Act
        var result = await converter.ConvertToEmlAsync(emailId, includeAttachments: true);

        // Assert
        result.Success.Should().BeTrue();
        var attachment = result.Attachments.First();
        attachment.ShouldCreateDocument.Should().BeFalse();
        attachment.SkipReason.Should().Contain("size");
    }

    #endregion

    #region Helper Methods

    private void SetupHttpResponse(string urlContains, HttpStatusCode statusCode, object response)
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains(urlContains)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json")
            });
    }

    private void SetupEmailActivityResponse(
        Guid emailId,
        string subject,
        string sender,
        string toRecipients,
        string body,
        bool isHtml = false)
    {
        var response = CreateEmailResponse(subject, sender, toRecipients, body: body, isHtml: isHtml);
        SetupHttpResponse($"emails({emailId})", HttpStatusCode.OK, response);
    }

    private object CreateEmailResponse(
        string subject,
        string sender,
        string toRecipients,
        string? ccRecipients = null,
        string body = "Body content",
        bool isHtml = false,
        bool directionCode = true,
        string? trackingToken = null,
        string? conversationIndex = null)
    {
        return new
        {
            subject,
            sender,
            torecipients = toRecipients,
            ccrecipients = ccRecipients,
            description = body,
            directioncode = directionCode,
            createdon = "2024-06-15T10:30:00Z",
            actualend = "2024-06-15T10:30:00Z",
            messageid = $"<{Guid.NewGuid()}@test.com>",
            trackingtoken = trackingToken,
            conversationindex = conversationIndex,
            _regardingobjectid_value = (string?)null
        };
    }

    private void SetupAttachmentsResponse(Guid emailId, object[] attachments)
    {
        var response = new { value = attachments };
        // URL pattern: activitymimeattachments?$filter=_objectid_value eq {emailId}
        SetupHttpResponse($"activitymimeattachments", HttpStatusCode.OK, response);
    }

    #endregion
}

public class EmlConversionResultTests
{
    [Fact]
    public void Succeeded_CreatesSuccessfulResult()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        var metadata = new EmailActivityMetadata { Subject = "Test" };

        var result = EmlConversionResult.Succeeded(stream, metadata, [], 4);

        result.Success.Should().BeTrue();
        result.EmlStream.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        result.FileSizeBytes.Should().Be(4);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failed_CreatesFailedResult()
    {
        var result = EmlConversionResult.Failed("Test error message");

        result.Success.Should().BeFalse();
        result.EmlStream.Should().BeNull();
        result.Metadata.Should().BeNull();
        result.ErrorMessage.Should().Be("Test error message");
    }
}

public class EmailActivityMetadataTests
{
    [Fact]
    public void EmailActivityMetadata_DefaultValues_AreCorrect()
    {
        var metadata = new EmailActivityMetadata();

        metadata.ActivityId.Should().Be(Guid.Empty);
        metadata.Subject.Should().BeEmpty();
        metadata.From.Should().BeEmpty();
        metadata.To.Should().BeEmpty();
        metadata.Cc.Should().BeNull();
        metadata.Body.Should().BeNull();
        metadata.MessageId.Should().BeNull();
        metadata.Direction.Should().Be(0);
        metadata.EmailDate.Should().BeNull();
    }
}

public class EmailAttachmentInfoTests
{
    [Fact]
    public void EmailAttachmentInfo_DefaultValues_AreCorrect()
    {
        var info = new EmailAttachmentInfo();

        info.AttachmentId.Should().Be(Guid.Empty);
        info.FileName.Should().BeEmpty();
        info.MimeType.Should().Be("application/octet-stream");
        info.Content.Should().BeNull();
        info.SizeBytes.Should().Be(0);
        info.ShouldCreateDocument.Should().BeTrue();
        info.SkipReason.Should().BeNull();
    }
}
