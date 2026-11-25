using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Seam;

/// <summary>
/// spaarkeai-word-add-in-r1 task 138 (#2209), fix round 1b: the save's "which attachments are not yet saved" answer and the
/// upload-finalization worker's "which attachments to create" work are ONE selection path, so they cannot drift apart.
/// </summary>
/// <remarks>
/// A source-scan guard (the two sides are in different layers and the worker's side runs on a queue, so only one shared
/// method keeps them equal). It fails when the planner or the worker extracts, filters or selects attachments itself instead of
/// calling <c>EmailAttachmentAddPlanner.SelectAttachments</c>, which is the only place that does.
/// Must-fire control: the same scan over a copy of the old worker text finds the duplicate steps.
/// </remarks>
[Trait("status", "task-138-word-add-in-r1")]
public class OfficeEmailAttachmentSelectionIsSharedTests
{
    private static readonly string Planner = Path.Combine("src", "server", "api", "Sprk.Bff.Api", "Services", "Office", "EmailAttachmentAddPlanner.cs");
    private static readonly string Worker = Path.Combine("src", "server", "api", "Sprk.Bff.Api", "Workers", "Office", "UploadFinalizationWorker.cs");

    [Fact]
    public void ExtractAndFilterAndSelect_AreDoneOnce_InTheSharedMethod_AndNowhereElse()
    {
        var planner = Code(Planner);
        var worker = Code(Worker);

        Count(worker, ".ExtractAttachments(").Should().Be(0, "the worker selects through the shared method");
        Count(worker, ".FilterAttachments(").Should().Be(0);
        Count(worker, "ApplySelection(").Should().Be(0);
        Count(planner, ".ExtractAttachments(").Should().Be(1, "the shared method is the only extraction");
        Count(planner, ".FilterAttachments(").Should().Be(1);
        Count(planner, "ApplySelection(").Should().Be(2, "its definition and its one use inside the shared method");
    }

    [Fact]
    public void ThePlanner_AndTheWorker_BothCallTheSharedMethod()
    {
        Count(Code(Planner), "SelectAttachments(").Should().BeGreaterThanOrEqualTo(2, "its definition and the planner's call");
        Count(Code(Worker), "EmailAttachmentAddPlanner.SelectAttachments(").Should().Be(1);
    }

    [Fact]
    public void TheScan_FiresOnTheOldDuplicatedSteps()
    {
        const string old = "attachments = emlConverter.ExtractAttachments(emlStream);\nvar f = attachmentFilterService.FilterAttachments(attachments);";
        Count(old, ".ExtractAttachments(").Should().Be(1);
        Count(old, ".FilterAttachments(").Should().Be(1);
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>The file's code without comment lines.</summary>
    private static string Code(string relativePath) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(RepoRoot(), relativePath)).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
