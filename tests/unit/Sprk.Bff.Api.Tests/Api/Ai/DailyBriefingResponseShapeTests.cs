using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Backward-compatibility tests for the /api/ai/daily-briefing/narrate
/// response shape (R4 task 032 — FR-12 / AC-12b).
///
/// Locks in the JSON contract consumed by <c>useBriefingNarration.ts</c> in
/// <c>@spaarke/daily-briefing-components</c>. The widget reads:
///   - <c>tldr.summary</c> / <c>tldr.keyTakeaways</c> / <c>tldr.topAction</c>
///     / <c>tldr.categoryCount</c> / <c>tldr.priorityItemCount</c>
///   - <c>channelNarratives[].category</c>
///   - <c>channelNarratives[].bullets[].narrative</c> / <c>itemIds</c> /
///     <c>primaryEntityType</c> / <c>primaryEntityId</c> / <c>primaryEntityName</c>
///   - <c>generatedAtUtc</c>
///
/// The frozen R3 sample at
/// <c>projects/spaarke-daily-update-service-r4/notes/samples/narrate-response-r3.json</c>
/// is the load-bearing golden fixture. Drift here is a widget-parser break.
/// </summary>
[Trait("status", "task-032-r4")]
public sealed class DailyBriefingResponseShapeTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions WidgetSerializerOptions = new()
    {
        // Matches ASP.NET Core default System.Text.Json serialization
        // (which Minimal API uses to write the response body).
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static async Task<DailyBriefingNarrateResponse> InvokeHandleNarrateAndReadResponseAsync(
        DailyBriefingNarrateRequest request,
        IConsumerRoutingService routing,
        IInvokePlaybookAi invokePlaybookAi)
    {
        var method = typeof(DailyBriefingEndpoints)
            .GetMethod("HandleNarrate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("HandleNarrate not found via reflection");

        // R7 Wave 11 T116 narrator spike: HandleNarrate signature gained two parameters
        // (IConfiguration, DailyBriefingNarrator). These tests verify the playbook-engine
        // path (default with feature flag off), so pass an empty IConfiguration (flag
        // absent → defaults to false) and null narrator (never invoked when flag off).
        var emptyConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        var task = (Task<IResult>)method.Invoke(null, new object?[]
        {
            request,
            NullLoggerFactory.Instance,
            routing,
            invokePlaybookAi,
            emptyConfig,
            null,  // narrator — never accessed when feature flag is off
            new DefaultHttpContext(),
            CancellationToken.None
        })!;

        var result = await task;
        var ok = result.Should().BeOfType<Ok<DailyBriefingNarrateResponse>>().Subject;
        ok.Value.Should().NotBeNull();
        return ok.Value!;
    }

    private static DailyBriefingNarrateRequest BuildNonEmptyRequest() => new()
    {
        Categories =
        [
            new NotificationCategoryDto { Name = "Tasks Overdue", Count = 1, UnreadCount = 1 }
        ],
        PriorityItems =
        [
            new PriorityItemDto { Category = "Tasks", Title = "Review engagement letter" }
        ],
        TotalNotificationCount = 1,
        Channels =
        [
            new ChannelNarrationInput
            {
                Category = "tasks",
                Label = "Tasks Overdue",
                Items =
                [
                    new ChannelItemDto
                    {
                        Id = "notif-1",
                        Title = "Review engagement letter",
                        RegardingName = "Acme Corp",
                        RegardingEntityType = "sprk_matter",
                        RegardingId = "00000000-0000-0000-0000-000000000001",
                        Priority = "high"
                    }
                ]
            }
        ]
    };

    private static Mock<IConsumerRoutingService> BuildRoutingMock(Guid resolvedPlaybookId)
    {
        var mock = new Mock<IConsumerRoutingService>(MockBehavior.Strict);
        mock.Setup(r => r.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedPlaybookId);
        return mock;
    }

    private static Mock<IInvokePlaybookAi> BuildInvokeMockFromJson(string structuredJson)
    {
        using var doc = JsonDocument.Parse(structuredJson);
        var result = new PlaybookInvocationResult
        {
            RunId = Guid.NewGuid(),
            Success = true,
            StructuredData = doc.RootElement.Clone()
        };
        var mock = new Mock<IInvokePlaybookAi>(MockBehavior.Strict);
        mock.Setup(i => i.InvokePlaybookAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<Sprk.Bff.Api.Services.Ai.DocumentContext?>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static string LoadFrozenR3Sample()
    {
        // The sample lives in projects/spaarke-daily-update-service-r4/notes/samples/.
        // Walk up from the test assembly's directory to find the repo root.
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(
                dir,
                "projects",
                "spaarke-daily-update-service-r4",
                "notes",
                "samples",
                "narrate-response-r3.json");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            "Frozen R3 sample narrate-response-r3.json not found. " +
            "Expected at projects/spaarke-daily-update-service-r4/notes/samples/.");
    }

    // ── Tests: response shape backward compat (AC-12b) ────────────────────────

    [Fact]
    public async Task HandleNarrate_ResponseShape_MatchesDailyBriefingNarrateResponse()
    {
        // Arrange — playbook returns the canonical structured shape.
        var request = BuildNonEmptyRequest();
        var routing = BuildRoutingMock(Guid.NewGuid());
        var invokePlaybookAi = BuildInvokeMockFromJson("""
            {
              "tldr": {
                "summary": "Sum.",
                "keyTakeaways": ["A", "B"],
                "topAction": "Do A."
              },
              "channelNarratives": [
                {
                  "category": "tasks",
                  "bullets": [
                    {
                      "narrative": "Do the thing.",
                      "itemIds": ["notif-1"],
                      "primaryEntityType": "sprk_matter",
                      "primaryEntityId": "00000000-0000-0000-0000-000000000001",
                      "primaryEntityName": "Acme Corp"
                    }
                  ]
                }
              ]
            }
            """);

        // Act
        var response = await InvokeHandleNarrateAndReadResponseAsync(
            request, routing.Object, invokePlaybookAi.Object);

        // Assert — every field consumed by the widget parser is populated.
        response.Tldr.Should().NotBeNull();
        response.Tldr.Summary.Should().Be("Sum.");
        response.Tldr.KeyTakeaways.Should().BeEquivalentTo(new[] { "A", "B" });
        response.Tldr.TopAction.Should().Be("Do A.");
        response.Tldr.CategoryCount.Should().Be(request.Categories.Length);
        response.Tldr.PriorityItemCount.Should().Be(request.PriorityItems.Length);

        response.ChannelNarratives.Should().HaveCount(1);
        var ch0 = response.ChannelNarratives[0];
        ch0.Category.Should().Be("tasks");
        ch0.Bullets.Should().HaveCount(1);
        var b0 = ch0.Bullets[0];
        b0.Narrative.Should().Be("Do the thing.");
        b0.ItemIds.Should().BeEquivalentTo(new[] { "notif-1" });
        b0.PrimaryEntityType.Should().Be("sprk_matter");
        b0.PrimaryEntityId.Should().Be("00000000-0000-0000-0000-000000000001");
        b0.PrimaryEntityName.Should().Be("Acme Corp");

        response.GeneratedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task HandleNarrate_ResponseShape_FieldNamesUseCamelCase()
    {
        // Arrange
        var request = BuildNonEmptyRequest();
        var routing = BuildRoutingMock(Guid.NewGuid());
        var invokePlaybookAi = BuildInvokeMockFromJson("""
            {
              "tldr": { "summary": "s", "keyTakeaways": ["k1"], "topAction": "t" },
              "channelNarratives": [
                {
                  "category": "tasks",
                  "bullets": [
                    {
                      "narrative": "n",
                      "itemIds": ["i1"],
                      "primaryEntityType": "sprk_matter",
                      "primaryEntityId": "00000000-0000-0000-0000-000000000001",
                      "primaryEntityName": "Acme"
                    }
                  ]
                }
              ]
            }
            """);

        var response = await InvokeHandleNarrateAndReadResponseAsync(
            request, routing.Object, invokePlaybookAi.Object);

        // Act — serialize with the same options Minimal API uses to write the
        // response body. The widget's `parseNotificationData` reads camelCase
        // keys (e.g. `tldr`, `channelNarratives`, `generatedAtUtc`).
        var json = JsonSerializer.Serialize(response, WidgetSerializerOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert — top-level camelCase property names present.
        root.TryGetProperty("tldr", out var tldrEl).Should().BeTrue("widget reads 'tldr'");
        root.TryGetProperty("channelNarratives", out var chEl).Should().BeTrue("widget reads 'channelNarratives'");
        root.TryGetProperty("generatedAtUtc", out _).Should().BeTrue("widget reads 'generatedAtUtc'");

        // tldr.* camelCase
        tldrEl.TryGetProperty("summary", out _).Should().BeTrue();
        tldrEl.TryGetProperty("keyTakeaways", out _).Should().BeTrue();
        tldrEl.TryGetProperty("topAction", out _).Should().BeTrue();
        tldrEl.TryGetProperty("categoryCount", out _).Should().BeTrue();
        tldrEl.TryGetProperty("priorityItemCount", out _).Should().BeTrue();

        // channelNarratives[].* camelCase
        chEl.GetArrayLength().Should().BeGreaterThan(0);
        var bullets = chEl[0].GetProperty("bullets");
        bullets.GetArrayLength().Should().BeGreaterThan(0);
        var bullet = bullets[0];
        bullet.TryGetProperty("narrative", out _).Should().BeTrue();
        bullet.TryGetProperty("itemIds", out _).Should().BeTrue();
        bullet.TryGetProperty("primaryEntityType", out _).Should().BeTrue();
        bullet.TryGetProperty("primaryEntityId", out _).Should().BeTrue();
        bullet.TryGetProperty("primaryEntityName", out _).Should().BeTrue();

        // Defense: no PascalCase property leaks (would be a widget-parser break).
        json.Should().NotContain("\"Tldr\"");
        json.Should().NotContain("\"ChannelNarratives\"");
        json.Should().NotContain("\"GeneratedAtUtc\"");
    }

    [Fact]
    public async Task HandleNarrate_ResponseShape_FrozenR3Sample()
    {
        // Arrange — load the frozen golden sample and feed it back to the
        // endpoint as the playbook's structured output. The response we get
        // out MUST round-trip into the same widget-consumable shape.
        var sampleJson = LoadFrozenR3Sample();
        using var sampleDoc = JsonDocument.Parse(sampleJson);
        var sampleRoot = sampleDoc.RootElement;

        var request = BuildNonEmptyRequest();
        var routing = BuildRoutingMock(Guid.NewGuid());

        // The frozen sample's tldr+channelNarratives subtree IS the playbook's
        // structured output (the endpoint re-injects category/priority counts
        // from the request + stamps generatedAtUtc).
        var playbookOutput = new
        {
            tldr = new
            {
                summary = sampleRoot.GetProperty("tldr").GetProperty("summary").GetString(),
                keyTakeaways = sampleRoot.GetProperty("tldr").GetProperty("keyTakeaways")
                    .EnumerateArray().Select(e => e.GetString()).ToArray(),
                topAction = sampleRoot.GetProperty("tldr").GetProperty("topAction").GetString()
            },
            channelNarratives = sampleRoot.GetProperty("channelNarratives")
        };
        var playbookJson = JsonSerializer.Serialize(playbookOutput, WidgetSerializerOptions);
        var invokePlaybookAi = BuildInvokeMockFromJson(playbookJson);

        // Act
        var response = await InvokeHandleNarrateAndReadResponseAsync(
            request, routing.Object, invokePlaybookAi.Object);

        // Serialize the response and assert structural overlap with the frozen
        // sample. We DO NOT diff `categoryCount`/`priorityItemCount`/`generatedAtUtc`
        // because the endpoint re-derives those from the request + UtcNow —
        // their per-call values are correctly NOT identical to the sample.
        var responseJson = JsonSerializer.Serialize(response, WidgetSerializerOptions);
        using var responseDoc = JsonDocument.Parse(responseJson);
        var responseRoot = responseDoc.RootElement;

        // 1. Same top-level shape (presence of tldr / channelNarratives /
        //    generatedAtUtc — all three are widget-required).
        foreach (var propName in new[] { "tldr", "channelNarratives", "generatedAtUtc" })
        {
            sampleRoot.TryGetProperty(propName, out _)
                .Should().BeTrue($"frozen sample must contain '{propName}'");
            responseRoot.TryGetProperty(propName, out _)
                .Should().BeTrue($"response must contain '{propName}'");
        }

        // 2. TL;DR text fields preserved byte-identical from the sample.
        responseRoot.GetProperty("tldr").GetProperty("summary").GetString()
            .Should().Be(sampleRoot.GetProperty("tldr").GetProperty("summary").GetString());
        responseRoot.GetProperty("tldr").GetProperty("topAction").GetString()
            .Should().Be(sampleRoot.GetProperty("tldr").GetProperty("topAction").GetString());

        var sampleTakeaways = sampleRoot.GetProperty("tldr").GetProperty("keyTakeaways")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        var responseTakeaways = responseRoot.GetProperty("tldr").GetProperty("keyTakeaways")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        responseTakeaways.Should().BeEquivalentTo(sampleTakeaways);

        // 3. channelNarratives length + bullet shape preserved.
        var sampleChannels = sampleRoot.GetProperty("channelNarratives");
        var responseChannels = responseRoot.GetProperty("channelNarratives");
        responseChannels.GetArrayLength().Should().Be(sampleChannels.GetArrayLength());

        for (var i = 0; i < sampleChannels.GetArrayLength(); i++)
        {
            var sampleCh = sampleChannels[i];
            var responseCh = responseChannels[i];

            responseCh.GetProperty("category").GetString()
                .Should().Be(sampleCh.GetProperty("category").GetString());

            var sampleBullets = sampleCh.GetProperty("bullets");
            var responseBullets = responseCh.GetProperty("bullets");
            responseBullets.GetArrayLength().Should().Be(sampleBullets.GetArrayLength());

            for (var j = 0; j < sampleBullets.GetArrayLength(); j++)
            {
                var sb = sampleBullets[j];
                var rb = responseBullets[j];
                rb.GetProperty("narrative").GetString().Should().Be(sb.GetProperty("narrative").GetString());
                rb.GetProperty("primaryEntityType").GetString().Should().Be(sb.GetProperty("primaryEntityType").GetString());
                rb.GetProperty("primaryEntityId").GetString().Should().Be(sb.GetProperty("primaryEntityId").GetString());
                rb.GetProperty("primaryEntityName").GetString().Should().Be(sb.GetProperty("primaryEntityName").GetString());
            }
        }

        // 4. categoryCount + priorityItemCount are re-injected from request
        //    (NOT from the sample) — assert against the request, not the sample.
        responseRoot.GetProperty("tldr").GetProperty("categoryCount").GetInt32()
            .Should().Be(request.Categories.Length);
        responseRoot.GetProperty("tldr").GetProperty("priorityItemCount").GetInt32()
            .Should().Be(request.PriorityItems.Length);
    }
}
