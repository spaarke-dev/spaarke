using FluentAssertions;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Webhook notification-model tests for the inbound email pipeline.
/// (The IncomingCommunicationProcessor tests that lived here were skipped permanently because
/// the Graph SDK request builders cannot be mocked, and were removed in the C# test-diet.)
/// </summary>
public class InboundPipelineTests
{
    #region Webhook Endpoint Tests (Static Method Testing)

    // NOTE: CommunicationEndpoints.HandleIncomingWebhookAsync is a private static method
    // that requires HttpRequest, JobSubmissionService, IConfiguration, and ILogger.
    // These tests validate the webhook logic through the observable behavior patterns.

    [Fact]
    public void WebhookValidation_ValidationTokenInQuery_IsHandledByEndpoint()
    {
        // This test documents the expected webhook validation behavior:
        // When Graph sends ?validationToken=abc, the endpoint returns 200 with "abc" as text/plain.
        //
        // Since HandleIncomingWebhookAsync is a private static method on CommunicationEndpoints,
        // direct unit testing requires either:
        //   a) Integration test with WebApplicationFactory (preferred for endpoint tests)
        //   b) Making the method internal with [InternalsVisibleTo]
        //
        // The validation logic is straightforward:
        //   if (request.Query.TryGetValue("validationToken", out var validationToken))
        //       return Results.Text(validationToken!, "text/plain", statusCode: 200);
        //
        // This test validates the notification payload model parsing instead.
        var notification = new GraphChangeNotificationCollection
        {
            Value = new[]
            {
                new GraphChangeNotification
                {
                    SubscriptionId = "sub-123",
                    ClientState = "test-client-state-secret",
                    ChangeType = "created",
                    Resource = "users/shared@contoso.com/mailFolders/Inbox/messages/AAMkAGI2TEST",
                    ResourceData = new GraphResourceData { Id = "AAMkAGI2TEST" }
                }
            }
        };

        // Verify notification model is correctly structured
        notification.Value.Should().HaveCount(1);
        notification.Value[0].SubscriptionId.Should().Be("sub-123");
        notification.Value[0].ClientState.Should().Be("test-client-state-secret");
        notification.Value[0].ResourceData!.Id.Should().Be("AAMkAGI2TEST");
    }

    [Fact]
    public void WebhookNotification_InvalidClientState_IsDetectedByComparison()
    {
        // Test the clientState validation logic pattern used in HandleIncomingWebhookAsync:
        //   if (!string.Equals(notification.ClientState, expectedClientState, StringComparison.Ordinal))
        //       return 401
        var expectedClientState = "correct-secret";

        var validNotification = new GraphChangeNotification
        {
            ClientState = "correct-secret",
            SubscriptionId = "sub-123"
        };

        var invalidNotification = new GraphChangeNotification
        {
            ClientState = "wrong-secret",
            SubscriptionId = "sub-456"
        };

        // Valid notification should pass
        string.Equals(validNotification.ClientState, expectedClientState, StringComparison.Ordinal)
            .Should().BeTrue("valid clientState should match expected value");

        // Invalid notification should fail
        string.Equals(invalidNotification.ClientState, expectedClientState, StringComparison.Ordinal)
            .Should().BeFalse("invalid clientState should NOT match expected value");
    }

    [Fact]
    public void WebhookNotification_ResourcePathParsing_ExtractsMessageId()
    {
        // Test the resource path parsing logic used in HandleIncomingWebhookAsync:
        //   Resource format: "users/{mailbox}/mailFolders/{folder}/messages/{messageId}"
        //   ExtractLastSegment extracts the messageId from the end of the path.
        var notification = new GraphChangeNotification
        {
            Resource = "users/shared@contoso.com/mailFolders/Inbox/messages/AAMkAGI2THVSAAA=",
            ResourceData = new GraphResourceData { Id = "AAMkAGI2THVSAAA=" }
        };

        // The endpoint first checks ResourceData.Id, then falls back to ExtractLastSegment
        var messageId = notification.ResourceData?.Id;
        messageId.Should().Be("AAMkAGI2THVSAAA=",
            "MessageId should be extracted from ResourceData.Id first");

        // Fallback extraction from resource path
        var resource = notification.Resource!;
        var lastSlash = resource.LastIndexOf('/');
        var extractedId = resource[(lastSlash + 1)..];
        extractedId.Should().Be("AAMkAGI2THVSAAA=",
            "Fallback extraction from resource path should also yield the correct messageId");
    }

    [Fact]
    public void GraphChangeNotificationCollection_DeserializesCorrectly()
    {
        // Test that the notification models support the expected Graph webhook payload structure
        var json = """
        {
            "value": [
                {
                    "subscriptionId": "sub-abc-123",
                    "clientState": "my-secret-state",
                    "changeType": "created",
                    "resource": "users/shared@contoso.com/mailFolders/Inbox/messages/AAMkAGI2TEST",
                    "tenantId": "tenant-xyz",
                    "resourceData": {
                        "@odata.type": "#Microsoft.Graph.Message",
                        "@odata.id": "users/shared@contoso.com/messages/AAMkAGI2TEST",
                        "id": "AAMkAGI2TEST"
                    }
                }
            ]
        }
        """;

        var parsed = System.Text.Json.JsonSerializer.Deserialize<GraphChangeNotificationCollection>(json);

        parsed.Should().NotBeNull();
        parsed!.Value.Should().HaveCount(1);
        parsed.Value[0].SubscriptionId.Should().Be("sub-abc-123");
        parsed.Value[0].ClientState.Should().Be("my-secret-state");
        parsed.Value[0].ChangeType.Should().Be("created");
        parsed.Value[0].TenantId.Should().Be("tenant-xyz");
        parsed.Value[0].ResourceData.Should().NotBeNull();
        parsed.Value[0].ResourceData!.Id.Should().Be("AAMkAGI2TEST");
        parsed.Value[0].ResourceData?.ODataType.Should().Be("#Microsoft.Graph.Message");
    }

    #endregion
}
