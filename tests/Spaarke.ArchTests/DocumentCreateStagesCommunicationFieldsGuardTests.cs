using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// spaarkeai-word-add-in-r1 task 130 (#2126): <c>DataverseServiceClientImpl.CreateDocumentAsync</c> must call
/// <c>StageDocumentCommunicationFields(document, request)</c>, the call that puts <c>sprk_relatedcommunication</c> and
/// <c>sprk_accesspermission</c> on the created <c>sprk_document</c>.
/// </summary>
/// <remarks>
/// <para><b>Why a source scan.</b> Every Office test mocks <c>IDataverseService.CreateDocumentAsync</c>, and the real
/// method creates through a <c>ServiceClient</c> that only connects to a live org (no seam to hand it an in-memory
/// service), so deleting the one staging line breaks no test: the stager's own unit test calls it directly. Without the
/// call every email-derived document is created with neither the link nor Restricted, the exact open-by-default state task
/// 130 removes — and nothing fails. Enforced by: this guard (ArchTests, CI and the Stop hook), per CLAUDE.md §16.</para>
/// <para><b>What it checks.</b> The body of <c>CreateDocumentAsync</c> (code text: comments stripped, so a commented-out call
/// does not count) contains a call to <c>StageDocumentCommunicationFields</c> with the <c>request</c> parameter.
/// <b>Stated limit:</b> it proves the call is present, not that it runs on every path or that the stager maps correctly —
/// <c>DocumentCommunicationFieldsStagingTests</c> pins the mapping.</para>
/// </remarks>
public class DocumentCreateStagesCommunicationFieldsGuardTests
{
    private const string File_ = "src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs";
    private const string Method = "CreateDocumentAsync";

    private static readonly Regex StagerCall = new(
        @"\bStageDocumentCommunicationFields\s*\(\s*\w+\s*,\s*request\s*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The code text of the body of the first method named <paramref name="method"/> in <paramref name="source"/>,
    /// or null when there is none.</summary>
    internal static string? MethodBody(string source, string method)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        var declaration = Regex.Match(code, @"\b" + Regex.Escape(method) + @"\s*\([^)]*\)\s*\{", RegexOptions.CultureInvariant);
        if (!declaration.Success)
            return null;

        var depth = 0;
        for (var i = declaration.Index + declaration.Length - 1; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0)
                return code[declaration.Index..(i + 1)];
        }

        return null;
    }

    /// <summary>True when <paramref name="source"/>'s <c>CreateDocumentAsync</c> body calls the stager.</summary>
    internal static bool StagesCommunicationFields(string source)
        => MethodBody(source, Method) is { } body && StagerCall.IsMatch(body);

    [Fact(DisplayName = "Task 130: DataverseServiceClientImpl.CreateDocumentAsync stages the communication link and access permission")]
    public void CreateDocumentAsync_CallsStageDocumentCommunicationFields()
    {
        var path = Path.Combine(SourceScan.RepoRoot, File_.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(System.IO.File.Exists(path), $"{File_} not found; update this guard if the file moved.");

        Assert.True(
            StagesCommunicationFields(System.IO.File.ReadAllText(path)),
            $"{File_}: {Method} no longer calls StageDocumentCommunicationFields(document, request). Without it an "
            + "email-derived sprk_document is created with neither sprk_relatedcommunication nor Restricted "
            + "sprk_accesspermission (task 130, #2126), and no Office test notices because they all mock the create.");
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 130 guard control: a CreateDocumentAsync body without the stager call fails the scan")]
    public void Control_BodyWithoutTheCall_IsAViolation()
    {
        const string source = """
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                document["sprk_documentname"] = request.Name;
                // StageDocumentCommunicationFields(document, request);   (commented out: does not count)
                document["statuscode"] = new OptionSetValue(1);
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }

            internal static void StageDocumentCommunicationFields(Entity document, CreateDocumentRequest request) { }
            """;

        Assert.False(StagesCommunicationFields(source));
    }

    [Fact(DisplayName = "Task 130 guard control: a stager call in another method does not satisfy CreateDocumentAsync")]
    public void Control_CallInAnotherMethod_IsAViolation()
    {
        const string source = """
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }

            public void Other(Entity document, CreateDocumentRequest request)
            {
                StageDocumentCommunicationFields(document, request);
            }
            """;

        Assert.False(StagesCommunicationFields(source));
    }

    [Fact(DisplayName = "Task 130 guard control: a CreateDocumentAsync body that calls the stager passes")]
    public void Control_BodyWithTheCall_IsNotAViolation()
    {
        const string source = """
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                if (request.Id is { } id) { document.Id = id; }
                StageDocumentCommunicationFields(document, request);
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }
            """;

        Assert.True(StagesCommunicationFields(source));
    }
}
