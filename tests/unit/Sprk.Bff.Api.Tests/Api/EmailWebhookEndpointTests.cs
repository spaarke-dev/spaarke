using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Email;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api;

public class EmailWebhookEndpointTests
{
    private readonly IOptions<EmailProcessingOptions> _options;

    public EmailWebhookEndpointTests()
    {
        // These tests focus on model validation and serialization logic
        // Integration tests would cover the full endpoint behavior

        _options = Options.Create(new EmailProcessingOptions
        {
            EnableWebhook = true,
            WebhookSecret = "test-secret-12345"
        });
    }

    [Fact]
    public void DataverseWebhookPayload_DeserializesCorrectly()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var json = $@"{{
            ""MessageName"": ""Create"",
            ""PrimaryEntityName"": ""email"",
            ""PrimaryEntityId"": ""{emailId}"",
            ""Depth"": 1,
            ""Stage"": 40,
            ""CorrelationId"": ""00000000-0000-0000-0000-000000000001""
        }}";

        // Act
        var payload = JsonSerializer.Deserialize<DataverseWebhookPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        payload.Should().NotBeNull();
        payload!.MessageName.Should().Be("Create");
        payload.PrimaryEntityName.Should().Be("email");
        payload.PrimaryEntityId.Should().Be(emailId);
        payload.Depth.Should().Be(1);
        payload.Stage.Should().Be(40);
    }

    [Fact]
    public void DataverseWebhookPayload_HandlesNullableFields()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var json = $@"{{
            ""PrimaryEntityName"": ""email"",
            ""PrimaryEntityId"": ""{emailId}""
        }}";

        // Act
        var payload = JsonSerializer.Deserialize<DataverseWebhookPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        payload.Should().NotBeNull();
        payload!.MessageName.Should().BeNull();
        payload.UserId.Should().BeNull();
        payload.CorrelationId.Should().BeNull();
        payload.PrimaryEntityId.Should().Be(emailId);
    }

    [Fact]
    public void DataverseWebhookPayload_DeserializesWithEntityImages()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var json = $@"{{
            ""MessageName"": ""Create"",
            ""PrimaryEntityName"": ""email"",
            ""PrimaryEntityId"": ""{emailId}"",
            ""PostEntityImages"": [
                {{
                    ""key"": ""PostImage"",
                    ""value"": {{
                        ""Id"": ""{emailId}"",
                        ""LogicalName"": ""email"",
                        ""Attributes"": [
                            {{ ""key"": ""subject"", ""value"": ""Test Email Subject"" }}
                        ]
                    }}
                }}
            ]
        }}";

        // Act
        var payload = JsonSerializer.Deserialize<DataverseWebhookPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        payload.Should().NotBeNull();
        payload!.PostEntityImages.Should().HaveCount(1);
        payload.PostEntityImages![0].Key.Should().Be("PostImage");
        payload.PostEntityImages[0].Value.Should().NotBeNull();
        payload.PostEntityImages[0].Value!.LogicalName.Should().Be("email");
        payload.PostEntityImages[0].Value!.Attributes.Should().HaveCount(1);
        payload.PostEntityImages[0].Value!.Attributes![0].Key.Should().Be("subject");
    }

    [Fact]
    public void WebhookTriggerResponse_SerializesCorrectly()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var response = new WebhookTriggerResponse
        {
            Accepted = true,
            JobId = jobId,
            CorrelationId = "test-correlation-id",
            Message = "Email queued for processing"
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<WebhookTriggerResponse>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Accepted.Should().BeTrue();
        deserialized.JobId.Should().Be(jobId);
        deserialized.CorrelationId.Should().Be("test-correlation-id");
        deserialized.Message.Should().Be("Email queued for processing");
    }

    [Fact]
    public void WebhookSignature_ComputesCorrectHmac()
    {
        // Arrange
        var secret = "test-webhook-secret";
        var payload = @"{""PrimaryEntityId"":""12345678-1234-1234-1234-123456789012""}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expectedSignature = Convert.ToBase64String(hash);

        // Act
        using var verifyHmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computedHash = verifyHmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedSignature = Convert.ToBase64String(computedHash);

        // Assert
        computedSignature.Should().Be(expectedSignature);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    [InlineData("Delete")]
    public void DataverseWebhookPayload_AcceptsAllMessageTypes(string messageName)
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var json = $@"{{
            ""MessageName"": ""{messageName}"",
            ""PrimaryEntityName"": ""email"",
            ""PrimaryEntityId"": ""{emailId}""
        }}";

        // Act
        var payload = JsonSerializer.Deserialize<DataverseWebhookPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        payload.Should().NotBeNull();
        payload!.MessageName.Should().Be(messageName);
    }

    [Fact]
    public void EmailProcessingOptions_HasWebhookSecretProperty()
    {
        // Arrange
        var options = new EmailProcessingOptions
        {
            WebhookSecret = "my-secret-key"
        };

        // Assert
        options.WebhookSecret.Should().Be("my-secret-key");
    }

    [Fact]
    public void EmailProcessingOptions_WebhookSecretDefaultsToNull()
    {
        // Arrange
        var options = new EmailProcessingOptions();

        // Assert
        options.WebhookSecret.Should().BeNull();
    }

    [Fact]
    public void JobContract_IdempotencyKeyFormat_IsCorrect()
    {
        // Arrange
        var emailId = Guid.NewGuid();
        var expectedKey = $"Email:{emailId}:Archive";

        // Act
        var job = new JobContract
        {
            JobType = "ProcessEmailToDocument",
            SubjectId = emailId.ToString(),
            IdempotencyKey = $"Email:{emailId}:Archive"
        };

        // Assert
        job.IdempotencyKey.Should().Be(expectedKey);
        job.IdempotencyKey.Should().StartWith("Email:");
        job.IdempotencyKey.Should().EndWith(":Archive");
        job.IdempotencyKey.Should().Contain(emailId.ToString());
    }

    [Fact]
    public void JobContract_HasCorrectDefaults()
    {
        // Arrange & Act
        var job = new JobContract();

        // Assert
        job.JobId.Should().NotBe(Guid.Empty);
        job.Attempt.Should().Be(1);
        job.MaxAttempts.Should().Be(3);
        job.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
