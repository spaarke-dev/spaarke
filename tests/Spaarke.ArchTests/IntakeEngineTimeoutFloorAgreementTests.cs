using System.Text.RegularExpressions;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// spaarke-intake-pipeline-r1 task 014 (review K2): the BFF's engine timeout floor is built from the engine job's hard stop per
/// execution. That stop lives in <c>infrastructure/bicep/modules/intake-engine-job.bicep</c> (<c>replicaTimeoutSeconds</c>), so the
/// two must agree: a longer replica timeout with an unchanged floor would hold runs whose engine is still working.
/// Enforced by: this source scan (must-fire control below).
/// </summary>
public class IntakeEngineTimeoutFloorAgreementTests
{
    private static readonly Regex ReplicaTimeout = new(@"^\s*param\s+replicaTimeoutSeconds\s+int\s*=\s*(\d+)\s*$", RegexOptions.Multiline);

    [Fact]
    public void EngineReplicaTimeoutConstant_EqualsTheBicepDefault()
    {
        var bicep = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "infrastructure", "bicep", "modules", "intake-engine-job.bicep"));

        var seconds = ParseReplicaTimeoutSeconds(bicep);

        Assert.Equal(TimeSpan.FromSeconds(seconds), IntakeEngineOptions.EngineReplicaTimeout);
    }

    [Fact]
    public void Detector_FiresOnAChangedDefault_AndFindsNothingInAFileWithoutTheParameter()
    {
        Assert.Equal(900, ParseReplicaTimeoutSeconds("param replicaTimeoutSeconds int = 900\n"));
        Assert.Throws<InvalidOperationException>(() => ParseReplicaTimeoutSeconds("param other int = 600\n"));
    }

    private static int ParseReplicaTimeoutSeconds(string bicep)
    {
        var match = ReplicaTimeout.Match(bicep);
        return match.Success
            ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : throw new InvalidOperationException("param replicaTimeoutSeconds not found in intake-engine-job.bicep");
    }
}
