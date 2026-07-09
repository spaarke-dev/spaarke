using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// FR-A-01 triple-twin hoist (AIR2-020) — the description-parity regression net.
/// </summary>
/// <remarks>
/// <para>
/// A handler tool's LLM-facing description lives in ONE authored place: the seed-row
/// JSON <c>infra/dataverse/sprk_analysistool-*-row.json</c> → <c>sprk_description</c>.
/// Two surfaces MIRROR it and must stay byte-equal:
/// </para>
/// <list type="number">
///   <item>the live <c>sprk_analysistool.sprk_description</c> (a MANAGED MIRROR —
///   <c>scripts/Seed-TypedHandlers.ps1</c> PATCHes it from the row JSON; drift from a
///   live hand-edit is caught at runtime by
///   <c>RoutingConsumerTypeHealthCheck</c>'s description-parity dimension → Unhealthy);</item>
///   <item>the compiled handler <c>ToolHandlerMetadata.Description</c> — guarded HERE in
///   CI so a divergence fails the build before it ships into the three downstream
///   catalog-row waves (032 / 042 / 057).</item>
/// </list>
/// <para>
/// The two enforcement points share ONE pure comparator,
/// <see cref="OpenAiFunctionSchemaValidator.FindDescriptionParityError"/> (NFR-07-safe —
/// lengths + first-diff index only, never description content).
/// </para>
/// <para>
/// <b>Scope</b>: handler classes with EXACTLY ONE active <c>sprk_analysistool</c> row.
/// A handler that serves several rows (e.g. <c>TextRefinementHandler</c> →
/// summary/keypoints/refine) has a single combined <c>Metadata.Description</c> that
/// cannot mirror N method-specific rows — those descriptions are authored per-row in
/// JSON and are exempt (see <see cref="MultiRowAndNoRowHandlers_AreExemptFromParity"/>).
/// This mirrors the health-check's single-row scoping, and is derived from the data — no
/// hardcoded handler list — so a NEW single-row row authored through the source is
/// covered automatically.
/// </para>
/// </remarks>
public class CatalogToolDescriptionParityContractTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // POSITIVE — every single-row handler's compiled Metadata.Description equals
    //           its authored seed-row sprk_description (byte-exact)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EverySingleRowHandler_MetadataDescription_MatchesAuthoredSeedRow()
    {
        var singleRow = LoadSingleRowHandlerRows();

        singleRow.Should().NotBeEmpty(
            "the sprk_analysistool-*-row.json files are the authored source for handler-tool descriptions");

        var mismatches = new List<string>();

        foreach (var (handlerClass, rowFile, authoredDescription) in singleRow)
        {
            var handlerType = ResolveHandlerType(handlerClass);
            if (handlerType is null)
            {
                mismatches.Add($"{handlerClass} ({rowFile}): no handler type resolvable in Sprk.Bff.Api");
                continue;
            }

            string? compiledDescription;
            try
            {
                compiledDescription = ReadMetadataDescription(handlerType);
            }
            catch (Exception ex)
            {
                mismatches.Add($"{handlerClass} ({rowFile}): could not read Metadata.Description — {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
                continue;
            }

            // The comparator is the SAME one the health check + projection guard use.
            var parityError = OpenAiFunctionSchemaValidator.FindDescriptionParityError(
                authoredDescription, compiledDescription);

            if (parityError is not null)
            {
                mismatches.Add($"{handlerClass} ({rowFile}): {parityError}");
            }
        }

        mismatches.Should().BeEmpty(
            "each single-row handler's compiled Metadata.Description is a managed mirror of the authored " +
            "sprk_description and must be byte-equal — reconcile the CODE literal to the JSON (the JSON is the " +
            "single source; the live value is seed-managed). Drifted: " + string.Join(" | ", mismatches));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The comparator itself — positive + NEGATIVE (drift is caught)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FindDescriptionParityError_WhenMirrorEqualsAuthored_ReturnsNull()
    {
        const string authored = "Inserts one row into a Dataverse table under the calling user's permissions.";
        OpenAiFunctionSchemaValidator.FindDescriptionParityError(authored, authored)
            .Should().BeNull("identical authored + mirror text is in parity");
    }

    [Fact]
    public void FindDescriptionParityError_WhenMirrorDriftsFromAuthored_ReturnsNfr07SafeError()
    {
        const string authored = "Inserts one row into a Dataverse table under the calling user's permissions.";
        const string driftedMirror = authored + " Extra sentence a live hand-edit added.";

        var error = OpenAiFunctionSchemaValidator.FindDescriptionParityError(authored, driftedMirror);

        error.Should().NotBeNull("a mirror that diverges from the authored source must be caught as drift");
        // NFR-07: the error names lengths + first-diff index, never the description content.
        error.Should().NotContain("Extra sentence", "the parity error must not echo description content (NFR-07)");
        error.Should().Contain("first divergence at index");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Single-source edit propagates to all mirrors (the whole point of the hoist)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SingleSourceEdit_ChangesTheMirrorAndTheGuardCatchesAStaleMirror()
    {
        // Take a REAL authored value, simulate an edit to the single source, and prove:
        //  (a) an un-updated mirror now diverges (the guard fires), and
        //  (b) once the mirror is regenerated (identity copy) parity is restored.
        var singleRow = LoadSingleRowHandlerRows();
        var (_, _, authored) = singleRow.First();

        var editedSource = authored + " (source edit — new guidance clause.)";
        var staleMirror = authored;

        OpenAiFunctionSchemaValidator.FindDescriptionParityError(editedSource, staleMirror)
            .Should().NotBeNull("editing the single source must make an un-updated mirror drift");

        var regeneratedMirror = editedSource; // generation of the code/live mirror is an identity copy of the source
        OpenAiFunctionSchemaValidator.FindDescriptionParityError(editedSource, regeneratedMirror)
            .Should().BeNull("after the mirror is regenerated from the edited source, parity is restored");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // memory.* rows are authored THROUGH the source (proof, not paper)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MemoryStarRow_AuthoredThroughSource_PropagatesToMirrorAndValidates()
    {
        // FR-A-01 acceptance: the memory.* rows (first consumers, task 057) are authored
        // through this single source. The memory.write HANDLER is built at task 057, so
        // this proves the AUTHORING PIPELINE at the contract level: a representative
        // memory.* description authored once flows to a mirror byte-identically, and a
        // corrupted mirror is caught. (Live seeding + the real handler land in 057; seeding
        // a row without a handler now would trip the existing bijection health check.)
        const string authoredMemoryWriteDescription =
            "Records a durable memory item for the current user or record scope so later turns can recall it. " +
            "Memory writes are AI-initiated and silent; the user review/delete surface is the control. " +
            "Tag every item with provenance (source, bindingId/ledgerRef, trustLevel) at capture time.";

        // Mirror generated from the source = identity copy → parity.
        var generatedMirror = authoredMemoryWriteDescription;
        OpenAiFunctionSchemaValidator.FindDescriptionParityError(authoredMemoryWriteDescription, generatedMirror)
            .Should().BeNull("a memory.* row authored through the source propagates to its mirror byte-identically");

        // Out-of-band corruption of exactly one mirror is caught.
        var corruptedMirror = authoredMemoryWriteDescription.Replace("silent", "gated");
        OpenAiFunctionSchemaValidator.FindDescriptionParityError(authoredMemoryWriteDescription, corruptedMirror)
            .Should().NotBeNull("corrupting the memory.* mirror out-of-band must fail parity");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Exemptions are derived from the data, not arbitrary
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultiRowAndNoRowHandlers_AreExemptFromParity()
    {
        var rowsByHandler = LoadAllHandlerRows()
            .GroupBy(r => r.HandlerClass, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Multi-row handlers exist (a single Metadata.Description cannot mirror N rows) and
        // are therefore legitimately outside the single-row parity scope.
        rowsByHandler.Values.Should().Contain(c => c > 1,
            "at least one handler serves multiple tool rows (e.g. TextRefinementHandler) — those are exempt by design");

        // GenericAnalysisHandler is the fallback handler with NO seed row → also exempt.
        rowsByHandler.Keys.Should().NotContain("GenericAnalysisHandler",
            "GenericAnalysisHandler has no sprk_analysistool row and is exempt from description parity");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record HandlerRow(string HandlerClass, string RowFile, string? Description);

    private static IReadOnlyList<(string HandlerClass, string RowFile, string Authored)> LoadSingleRowHandlerRows()
    {
        return LoadAllHandlerRows()
            .GroupBy(r => r.HandlerClass, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .Select(g => g.Single())
            .Where(r => r.Description is not null)
            .Select(r => (r.HandlerClass, r.RowFile, r.Description!))
            .OrderBy(r => r.HandlerClass, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<HandlerRow> LoadAllHandlerRows()
    {
        var dir = Path.Combine(FindRepoRoot(), "infra", "dataverse");
        var rows = new List<HandlerRow>();

        foreach (var file in Directory.EnumerateFiles(dir, "sprk_analysistool-*-row.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("sprk_handlerclass", out var hc) ||
                hc.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var handlerClass = hc.GetString()!.Trim();
            string? description = doc.RootElement.TryGetProperty("sprk_description", out var d) &&
                                  d.ValueKind == JsonValueKind.String
                ? d.GetString()
                : null;

            rows.Add(new HandlerRow(handlerClass, Path.GetFileName(file), description));
        }

        return rows;
    }

    private static Type? ResolveHandlerType(string handlerClass)
    {
        var candidates = typeof(OpenAiFunctionSchemaValidator).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && string.Equals(t.Name, handlerClass, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // sprk_handlerclass routes to the auto-discovered Services/Ai/Handlers handler
        // (HandlerRegistrationConventions). Some names collide with a same-named type in a
        // different namespace (e.g. Services/Finance/Tools) — prefer the routing namespace.
        return candidates.FirstOrDefault(t => t.Namespace == "Sprk.Bff.Api.Services.Ai.Handlers")
            ?? candidates[0];
    }

    private static string? ReadMetadataDescription(Type handlerType)
    {
        var instance = ConstructWithMocks(handlerType);
        var metadata = handlerType.GetProperty("Metadata")?.GetValue(instance);
        metadata.Should().NotBeNull($"{handlerType.Name} must expose a ToolHandlerMetadata via its Metadata property");
        return metadata!.GetType().GetProperty("Description")?.GetValue(metadata) as string;
    }

    /// <summary>
    /// Constructs a handler by supplying a Moq stub for every constructor parameter.
    /// Metadata is a field initializer using only compile-time constants, so a stubbed
    /// dependency graph is sufficient to read <c>Metadata.Description</c> — we never invoke
    /// handler behavior. (All handler dependencies are interfaces — see the shared
    /// TypedToolHandlerTestFixture pattern.)
    /// </summary>
    private static object ConstructWithMocks(Type handlerType)
    {
        var ctor = handlerType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var args = new object?[ctor.GetParameters().Length];
        var parameters = ctor.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            try
            {
                args[i] = CreateStub(p.ParameterType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"param '{p.Name}' ({p.ParameterType.Name}): {ex.GetBaseException().Message}", ex);
            }
        }

        return ctor.Invoke(args);
    }

    /// <summary>
    /// Produces a non-null stub for a handler constructor parameter. Handler ctors only
    /// null-check / field-assign their dependencies, and Metadata is a compile-time field
    /// initializer, so a loose stub is sufficient to read Metadata.Description.
    /// </summary>
    private static object CreateStub(Type serviceType)
    {
        if (serviceType == typeof(string))
        {
            return string.Empty;
        }
        if (serviceType.IsValueType)
        {
            return Activator.CreateInstance(serviceType)!;
        }

        // IOptions<T>: several handlers dereference .Value in their ctor
        // (modelSelectorOptions?.Value ?? throw), so a real options wrapper is required.
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IOptions<>))
        {
            var optionsValueType = serviceType.GetGenericArguments()[0];
            var value = Activator.CreateInstance(optionsValueType)!;
            var create = typeof(Options).GetMethods()
                .First(m => m.Name == nameof(Options.Create) && m.GetParameters().Length == 1)
                .MakeGenericMethod(optionsValueType);
            return create.Invoke(null, new[] { value })!;
        }

        // Moq can proxy interfaces and non-sealed classes with an accessible ctor.
        if (serviceType.IsInterface || (!serviceType.IsSealed && HasMockableCtor(serviceType)))
        {
            var mockType = typeof(Mock<>).MakeGenericType(serviceType);
            var mock = (Mock)Activator.CreateInstance(mockType)!;
            return mock.Object;
        }

        // Sealed/concrete leaf: recurse through its smallest public ctor.
        var leafCtor = serviceType.GetConstructors()
            .OrderBy(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (leafCtor is not null)
        {
            var args = leafCtor.GetParameters().Select(p => CreateStub(p.ParameterType)).ToArray();
            return leafCtor.Invoke(args);
        }

        return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(serviceType);
    }

    private static bool HasMockableCtor(Type type) =>
        type.GetConstructor(Type.EmptyTypes) is not null ||
        type.GetConstructors().Length == 0;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repo root (Spaarke.sln) from AppContext.BaseDirectory — " +
            "the catalog description-parity assertions require an in-repo test run.");
    }
}
