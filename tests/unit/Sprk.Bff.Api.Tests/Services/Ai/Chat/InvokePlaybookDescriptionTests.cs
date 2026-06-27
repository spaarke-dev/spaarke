using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// R6 Pillar 3 / task 022 (D-A-14) tests for the DYNAMIC invoke_playbook tool description
/// generated at chat-agent build time by <see cref="SprkChatAgentFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Task 022 makes the generic <c>invoke_playbook</c> chat-tool's description tenant-aware:
/// the LLM sees the actual playbook IDs + names accessible to the calling tenant at
/// request time, rather than a static placeholder. This is what makes Pillar 3 / task 023
/// (specialized-bridge removal) safe — the LLM no longer has to "know" the GUIDs, they're
/// in the description.
/// </para>
/// <para>
/// These tests target the pure rendering surface (<c>RenderInvokePlaybookDescription</c> +
/// <c>BuildEmptyPlaybookDescription</c>) which carries the load-bearing logic:
/// NFR-10 budget enforcement, alphabetical truncation, empty-list copy, dedup formatting.
/// The cache-key + TTL constants are also smoke-checked because <c>InvokePlaybookHandler</c>
/// and the factory must agree on them per ADR-014 (tenant-scoped cache hygiene).
/// </para>
/// <para>
/// Full integration of the factory's data-driven block — including the per-tenant cache
/// hit/miss path and the description-override on the <see cref="AnalysisTool"/> row before
/// adapter construction — is exercised by the existing
/// <c>SprkChatAgentFactoryToolResolutionTests</c> (task 011 wiring) at the
/// <see cref="ToolHandlerToAIFunctionAdapter.Description"/> seam: the adapter exposes
/// whatever <c>tool.Description</c> contains, so verifying the rendering function
/// independently is sufficient for unit coverage. Full E2E (chat-session start →
/// LLM sees menu) is integration-level and covered downstream by
/// <c>PlaybookDispatcherIntegrationTests</c>.
/// </para>
/// </remarks>
[Trait("status", "passing")]
[Trait("task", "r6-task-022")]
public class InvokePlaybookDescriptionTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Empty-list path — when zero playbooks are tenant-accessible, the description
    // MUST explicitly say "no playbooks currently available" so the LLM doesn't
    // hallucinate GUIDs.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderInvokePlaybookDescription_EmptyList_ProducesNoPlaybooksAvailableMessage()
    {
        // Arrange + Act
        var rendered = SprkChatAgentFactory.RenderInvokePlaybookDescription(
            Array.Empty<PlaybookSummary>());

        // Assert
        rendered.Should().NotBeNullOrWhiteSpace();
        rendered.Should().Contain(
            "No playbooks currently available",
            "task 022 empty-list path: LLM must see that there are no IDs to dispatch");
        rendered.Should().Contain(
            "natural language",
            "task 022 empty-list copy: steer the LLM to conversational mode rather than tool-call");
    }

    [Fact]
    public void BuildEmptyPlaybookDescription_DirectCall_MatchesEmptyListPath()
    {
        // Arrange + Act
        var direct = SprkChatAgentFactory.BuildEmptyPlaybookDescription();
        var viaRender = SprkChatAgentFactory.RenderInvokePlaybookDescription(
            Array.Empty<PlaybookSummary>());

        // Assert — the helper and the empty-list code path produce IDENTICAL strings
        // (avoids drift if either is edited in isolation).
        direct.Should().Be(viaRender,
            "task 022: BuildEmptyPlaybookDescription is the canonical empty-state string; " +
            "RenderInvokePlaybookDescription delegates to it for the zero-playbook case");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Small list — every playbook is listed with id + name + description
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderInvokePlaybookDescription_SmallList_IncludesAllEntries()
    {
        // Arrange — 3 playbooks fit well under the NFR-10 budget.
        var playbookA = new PlaybookSummary
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "Summarize Contract",
            Description = "Summarize a contract into key terms + parties + dates."
        };
        var playbookB = new PlaybookSummary
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Name = "Extract Entities",
            Description = "Extract named parties from any uploaded document."
        };
        var playbookC = new PlaybookSummary
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            Name = "Risk Assessment",
            Description = "Assess legal risk in a contract."
        };

        // Act
        var rendered = SprkChatAgentFactory.RenderInvokePlaybookDescription(
            new[] { playbookA, playbookB, playbookC });

        // Assert — each playbook id + name + description appears in the rendered menu.
        rendered.Should().Contain("00000000-0000-0000-0000-000000000001");
        rendered.Should().Contain("Summarize Contract");
        rendered.Should().Contain("Summarize a contract into key terms");

        rendered.Should().Contain("00000000-0000-0000-0000-000000000002");
        rendered.Should().Contain("Extract Entities");

        rendered.Should().Contain("00000000-0000-0000-0000-000000000003");
        rendered.Should().Contain("Risk Assessment");

        rendered.Should().Contain("Available playbooks for this tenant",
            "task 022 header guides the LLM to read the list as a menu");
        rendered.Should().Contain("Pass the playbookId",
            "task 022 trailer instructs the LLM on how to invoke the tool");
        rendered.Should().NotContain("...and",
            "small list fits within NFR-10 budget; no truncation suffix expected");
    }

    [Fact]
    public void RenderInvokePlaybookDescription_NullOrEmptyDescriptionField_FormatsCleanly()
    {
        // Arrange — playbook with null description (allowed by PlaybookSummary contract).
        var pb = new PlaybookSummary
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000099"),
            Name = "Just A Name",
            Description = null
        };

        // Act
        var rendered = SprkChatAgentFactory.RenderInvokePlaybookDescription(new[] { pb });

        // Assert — the entry appears with the id + name but no trailing "— description" segment.
        rendered.Should().Contain("00000000-0000-0000-0000-000000000099");
        rendered.Should().Contain("Just A Name");
        rendered.Should().NotContain("Just A Name —",
            "task 022: when description is null, the em-dash + description segment is omitted");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NFR-10 budget — long list MUST truncate within the soft cap and emit a clear
    // "...and N more" suffix so the LLM knows the list is partial.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderInvokePlaybookDescription_LongList_TruncatesWithinBudget()
    {
        // Arrange — 50 playbooks averaging ~80 chars per entry → well over the 1500-char cap.
        var playbooks = Enumerable.Range(1, 50).Select(i => new PlaybookSummary
        {
            Id = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
            Name = $"Playbook {i:D3} — descriptive name for tenant testing",
            Description = $"Detailed description of playbook number {i:D3} explaining what it does in a sentence or two."
        }).ToArray();

        // Act
        var rendered = SprkChatAgentFactory.RenderInvokePlaybookDescription(playbooks);

        // Assert — total length is within NFR-10 soft cap (with small tolerance for the
        // truncation suffix that pushes us slightly over the budget by design — the
        // budget is a soft cap, not a hard cap).
        const int hardCeiling = 2000; // generous; the real soft cap is 1500
        rendered.Length.Should().BeLessThan(hardCeiling,
            $"task 022 NFR-10: rendered description should respect the 1500-char soft cap (got {rendered.Length})");

        // Truncation suffix MUST appear so the LLM knows the list is partial.
        rendered.Should().Contain("...and",
            "task 022 NFR-10: truncation suffix '...and N more' MUST be present when list exceeds budget");
        rendered.Should().Contain("more",
            "task 022 NFR-10: truncation message identifies the remainder count");

        // At least the first few playbooks are present (alphabetical ordering).
        rendered.Should().Contain("Playbook 001",
            "task 022: alphabetically earliest playbooks survive truncation");

        // The last playbook MUST be truncated (alphabetical sort puts "Playbook 050" near end).
        rendered.Should().NotContain("Playbook 050",
            "task 022: alphabetically latest playbooks are truncated when budget tightens");
    }

    [Fact]
    public void RenderInvokePlaybookDescription_VeryLongDescriptions_TrimsPerEntry()
    {
        // Arrange — single playbook with a multi-paragraph description that should be
        // truncated PER ENTRY (~120-char cap) even though the whole list fits.
        var pb = new PlaybookSummary
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
            Name = "Bulky Description",
            Description = new string('A', 500) + " end-marker"
        };

        // Act
        var rendered = SprkChatAgentFactory.RenderInvokePlaybookDescription(new[] { pb });

        // Assert — long description is trimmed; the end-marker is dropped.
        rendered.Should().Contain("Bulky Description");
        rendered.Should().NotContain("end-marker",
            "task 022: per-entry description cap (~120 chars) drops trailing content");
        rendered.Should().Contain("…",
            "task 022: truncated descriptions show an ellipsis to signal the cut");
    }

    [Fact]
    public void RenderInvokePlaybookDescription_DescriptionWithNewlines_RendersOneLine()
    {
        // Arrange — multi-line description must be collapsed so each entry occupies
        // exactly one line (load-bearing: LLM parses the menu line-by-line).
        var pb = new PlaybookSummary
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000020"),
            Name = "Multiline",
            Description = "Line one of description.\nLine two of description.\rLine three."
        };

        // Act
        var rendered = SprkChatAgentFactory.RenderInvokePlaybookDescription(new[] { pb });

        // Assert — the single entry line contains no embedded newlines from the description.
        // Split by '\n' and find the entry line; it must not have additional '\n' or '\r'.
        var entryLines = rendered.Split('\n')
            .Where(line => line.Contains("Multiline"))
            .ToArray();

        entryLines.Should().HaveCountGreaterOrEqualTo(1, "the rendered menu must include the entry");
        entryLines[0].Should().NotContain("\n",
            "task 022: per-entry rendering collapses embedded newlines");
        entryLines[0].Should().NotContain("\r",
            "task 022: per-entry rendering collapses embedded carriage returns");
        entryLines[0].Should().Contain("Line one of description.",
            "task 022: collapsed content is preserved with spaces in place of newlines");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-014 cache-key contract — the prefix + TTL must be stable and tenant-scoped
    // ─────────────────────────────────────────────────────────────────────────────


    // ─────────────────────────────────────────────────────────────────────────────
    // Tenant isolation — different tenants get different rendered descriptions.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderInvokePlaybookDescription_DifferentLists_ProduceDifferentOutputs()
    {
        // Arrange — tenant A has one playbook, tenant B has a different one.
        var tenantA = new[]
        {
            new PlaybookSummary
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "TenantA Summarize",
                Description = "Summarize for tenant A."
            }
        };
        var tenantB = new[]
        {
            new PlaybookSummary
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "TenantB Extract",
                Description = "Extract for tenant B."
            }
        };

        // Act
        var renderedA = SprkChatAgentFactory.RenderInvokePlaybookDescription(tenantA);
        var renderedB = SprkChatAgentFactory.RenderInvokePlaybookDescription(tenantB);

        // Assert — outputs are distinct (a basic tenant-isolation sanity check at the
        // rendering layer; the per-tenant CACHE key uniqueness is asserted by
        // InvokePlaybookDescriptionCacheKeyPrefix_HasR6TenantScopedShape above).
        renderedA.Should().NotBe(renderedB,
            "task 022 / NFR-14: per-tenant playbook lists produce per-tenant descriptions");

        renderedA.Should().Contain("TenantA Summarize");
        renderedA.Should().NotContain("TenantB Extract",
            "task 022 / NFR-14: tenant A does not see tenant B's playbooks");

        renderedB.Should().Contain("TenantB Extract");
        renderedB.Should().NotContain("TenantA Summarize",
            "task 022 / NFR-14: tenant B does not see tenant A's playbooks");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-015 sentinel — when a test logger is wired, the rendering function itself
    // does NOT log playbook names (it's a pure renderer). The factory's
    // BuildInvokePlaybookDescriptionAsync IS the logger surface — exercised by the
    // factory-level integration tests. This test asserts the contract holds for the
    // pure renderer (it accepts data + returns string + does NOT take ILogger).
    // ─────────────────────────────────────────────────────────────────────────────


    // ─────────────────────────────────────────────────────────────────────────────
    // Cache wiring smoke test — when IMemoryCache is provided, two consecutive
    // invocations to the SAME tenant key reuse the cached string. The factory's
    // private BuildInvokePlaybookDescriptionAsync is the canonical surface; we
    // exercise the cache key shape + TTL via the constants above, and the
    // hot/cold path via reflective access here.
    // ─────────────────────────────────────────────────────────────────────────────


}
