using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Foundry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Foundry;

/// <summary>
/// Task 122 — the Redis key that decides WHICH Foundry conversation you rejoin.
///
/// <para><b>The defect these pin.</b> A Foundry thread (Azure AI Foundry <i>Agent Service</i>, via
/// <c>Azure.AI.Projects</c> — not Foundry IQ, not the M365 Agents SDK) is a server-side CONVERSATION:
/// <c>CreateMessageAsync</c> appends to it and the agent replies with the whole accumulated thread as
/// context. Redis stores which thread to rejoin, for 60 minutes of inactivity.</para>
///
/// <para>Before this task the key was <c>spaarke:tenant:{tenantId}:agent-thread:thread:v1</c> — the
/// resource AND the id were compile-time constants, so the whole key varied by tenant alone. Two people
/// in one tenant chatting within the TTL rejoined the SAME conversation, and the agent's answer to the
/// second was conditioned on the first one's messages. Two of the four callers were worse still: they
/// passed a literal into the TENANT argument, collapsing every tenant onto one global thread.</para>
///
/// <para><b>Why there was no test before.</b> There was no test file for this client at all, and
/// <c>BuildThreadCacheKey</c> — the one thing that could have shown the collapse — was referenced by
/// nothing. A key whose shape nobody asserts is a key that can quietly stop discriminating.</para>
/// </summary>
public class AgentThreadConversationScopeTests
{
    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string SessionA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string SessionB = "bbbbbbbb-0000-0000-0000-000000000002";

    // ─────────────────────────────────────────────────────────────────────────
    // NEGATIVE CONTROL — the detector fires on the shape that caused the defect
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 122 negative control: two conversations in ONE tenant do not share a key")]
    public void TwoConversationsInOneTenant_ProduceDifferentKeys()
    {
        // This is the whole defect in one assertion. Under the old scheme both sides of this comparison
        // were `agent-thread:{tenant}` and it passed vacuously — which is why the collapse was invisible.
        var keyA = AgentServiceClient.BuildThreadCacheKey(TenantA, SessionA);
        var keyB = AgentServiceClient.BuildThreadCacheKey(TenantA, SessionB);

        keyA.Should().NotBe(
            keyB,
            "two conversations in the same tenant must resolve to two Foundry threads; sharing one means "
            + "the agent answers the second caller with the first caller's messages in context");
    }

    [Fact(DisplayName = "Task 122 negative control: the conversation scope reaches the key, not just the signature")]
    public void ConversationScope_AppearsInTheKey()
    {
        // Guards the "parameter accepted and ignored" regression: a signature can take a scope and the
        // key can still be built from the tenant alone, which would pass the test above only by accident
        // of two different tenants. Assert the scope is actually IN the key.
        AgentServiceClient.BuildThreadCacheKey(TenantA, SessionA)
            .Should().Contain(SessionA);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POSITIVE CONTROL — it does NOT fire on the sanctioned shape
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 122 positive control: the SAME conversation still resumes one thread")]
    public void SameConversation_ProducesAStableKey()
    {
        // Resumability is the feature; segregation must not have broken it. A guard that flags the code
        // it protects gets deleted rather than obeyed (tests/CLAUDE.md).
        AgentServiceClient.BuildThreadCacheKey(TenantA, SessionA)
            .Should().Be(AgentServiceClient.BuildThreadCacheKey(TenantA, SessionA));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FAIL CLOSED — an absent scope is rejected, never defaulted (ADR-003)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Task 122: an absent conversation scope is REJECTED, not defaulted")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateOrResumeThread_WithNoConversationScope_Throws(string? scope)
    {
        var client = BuildClient();

        var act = async () => await client.CreateOrResumeThreadAsync(TenantA, scope!, CancellationToken.None);

        // The failure mode this prevents: defaulting an absent scope to a constant or to the tenant id
        // silently reinstates one-conversation-per-tenant, and would read as working.
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("conversationScope");
    }

    [Theory(DisplayName = "Task 122: invalidation also requires a conversation scope")]
    [InlineData(null)]
    [InlineData("")]
    public async Task InvalidateThreadCache_WithNoConversationScope_Throws(string? scope)
    {
        // Before this task, invalidation evicted the single tenant-wide thread — so one caller restarting
        // its conversation restarted everyone's. An unscoped invalidation must not be expressible.
        var client = BuildClient();

        var act = async () => await client.InvalidateThreadCacheAsync(TenantA, scope!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("conversationScope");
    }

    [Fact(DisplayName = "Task 122: scope validation runs BEFORE the kill switch, so it cannot hide behind Enabled=false")]
    public async Task ScopeValidation_RunsEvenWhenTheFeatureIsDisabled()
    {
        // AgentServiceOptions.Enabled defaults false and GuardEnabled throws FeatureDisabledException.
        // If validation sat after that guard, every one of these tests would pass for the wrong reason —
        // and the defect would be latent-but-unfixed the day someone flips the switch.
        var client = BuildClient(enabled: false);

        var act = async () => await client.CreateOrResumeThreadAsync(TenantA, "  ", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("conversationScope");
    }

    private static AgentServiceClient BuildClient(bool enabled = false)
    {
        var options = Options.Create(new AgentServiceOptions
        {
            Enabled = enabled,
            AgentId = "asst_test",
            Endpoint = new Uri("https://example-foundry.services.ai.azure.com/")
        });

        // The cache is never reached in these tests — validation throws first, which is itself part of
        // what is being asserted.
        var cache = new Mock<ITenantCache>(MockBehavior.Loose);
        var credential = new Mock<TokenCredential>(MockBehavior.Loose);

        return new AgentServiceClient(
            options,
            cache.Object,
            credential.Object,
            NullLogger<AgentServiceClient>.Instance);
    }
}
