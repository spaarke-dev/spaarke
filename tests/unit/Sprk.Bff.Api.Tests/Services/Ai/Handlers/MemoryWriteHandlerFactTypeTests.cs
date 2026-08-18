using System;
using System.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// FR-07 (spaarkeai-assistant-enhancements-r4 task 030) — regression net for the
/// <see cref="MemoryWriteHandler"/> factType wire map.
/// </summary>
/// <remarks>
/// The wire map (<see cref="MemoryWriteHandler.SupportedFactTypes"/>) resolves the camelCase token a
/// caller supplies to a <see cref="MemoryFactType"/>. FR-07 adds the governed <c>preference</c>
/// channel; these tests lock its presence and guard against a future enum member being added without
/// a wire token (which would make that fact type unwritable through the handler). Note: the LLM-facing
/// memory.write schema deliberately still offers only the four fact-about-the-record types — a
/// preference is authored by the governed E3 feedback pipeline (task 031) / narrow-allow-list producer
/// (task 032), not freely by the model.
/// </remarks>
public sealed class MemoryWriteHandlerFactTypeTests
{
    [Fact]
    public void SupportedFactTypes_MapsPreferenceToken_ToPreferenceFactType()
    {
        MemoryWriteHandler.SupportedFactTypes.Should().ContainKey("preference");
        MemoryWriteHandler.SupportedFactTypes["preference"].Should().Be(MemoryFactType.Preference);
    }

    [Fact]
    public void SupportedFactTypes_ResolvesPreference_CaseInsensitively()
    {
        // The map is OrdinalIgnoreCase, so a pipeline/maker supplying "Preference" still resolves.
        MemoryWriteHandler.SupportedFactTypes.ContainsKey("Preference").Should().BeTrue();
    }

    [Fact]
    public void SupportedFactTypes_CoversEveryMemoryFactType_SoNoEnumMemberIsUnwired()
    {
        var mapped = MemoryWriteHandler.SupportedFactTypes.Values.ToHashSet();

        foreach (var type in Enum.GetValues<MemoryFactType>())
        {
            mapped.Should().Contain(
                type,
                because: $"every MemoryFactType needs a memory.write wire token — '{type}' is unwired, " +
                         "so no caller could ever write that fact type through the handler");
        }
    }
}
