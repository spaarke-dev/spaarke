using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="ProgressiveRenderGuard"/> — the ADR-040 store-before-render
/// assertion at the progressive-render boundary (spaarke-ai-architecture-redesign-r2 task 039,
/// FR-A1-10 / D-F5).
///
/// <para>
/// <b>KEEP rationale (maintain-class)</b>: these facts anchor the ADR-040 render-boundary
/// contract that <see cref="Services.Ai.Chat.SessionDispatchOrchestrator.DispatchAsync"/>
/// depends on. The negative case is the acceptance-criteria-mandated proof that "an attempt
/// to render a section before its ledger write fails/throws".
/// </para>
/// </summary>
public class ProgressiveRenderGuardTests
{
    private static SessionOutput BuildStoredEntry(DateTimeOffset createdAt) => new()
    {
        Key = "binding-1@t1",
        BindingId = "binding-1",
        UcId = "chat-summarize",
        Turn = 1,
        Disposition = "informational",
        Payload = JsonSerializer.SerializeToElement(new { summary = "stored" }),
        CreatedAt = createdAt,
    };

    [Fact]
    public void EnsureStored_WhenEntryCarriesAWriteTimestamp_ReturnsTheSameEntryUnchanged()
    {
        var stored = BuildStoredEntry(DateTimeOffset.UtcNow);

        var result = ProgressiveRenderGuard.EnsureStored(stored);

        result.Should().BeSameAs(stored,
            "the guard only asserts provenance — it must never clone, mutate, or replace the stored payload");
        result.Payload.GetRawText().Should().Be(stored.Payload.GetRawText());
    }

    [Fact]
    public void EnsureStored_WhenEntryHasNoWriteTimestamp_ThrowsInvalidOperationException()
    {
        // CreatedAt = default simulates an entry that never went through
        // IOutputRouter.RouteAsync's ledger-write call site (e.g. a hand-built SessionOutput
        // or a future refactor that accidentally renders from pre-store state).
        var neverStored = BuildStoredEntry(default);

        var act = () => ProgressiveRenderGuard.EnsureStored(neverStored);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ADR-040*",
                "the failure must be loud and must cite the invariant it protects — never a silent render");
    }

}
