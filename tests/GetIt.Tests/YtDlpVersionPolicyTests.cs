using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public class YtDlpVersionPolicyTests
{
    [Theory]
    [InlineData("2025.12.31", "2026.01.01", -1)]
    [InlineData("2026.10.01", "2026.10.21", -1)]
    [InlineData("2026.10.21", "2026.10.21", 0)]
    [InlineData("v2026.10.21", "2026.10.21", 0)]
    [InlineData("2026.1.2", "2026.01.02", 0)]
    [InlineData("2026.10.21.0", "2026.10.21", 0)]
    [InlineData("2026.10.21.232757", "2026.10.21", 1)]
    [InlineData("2026.11.01", "2026.10.21", 1)]
    public void ComparesReleaseDatesAndNightlyRevisions(string installed, string latest, int expected)
    {
        Assert.Equal(expected, Math.Sign(YtDlpVersionPolicy.Compare(installed, latest)!.Value));
    }

    [Theory]
    [InlineData(null, "2026.10.21")]
    [InlineData("not-a-version", "2026.10.21")]
    [InlineData("2026.02.31", "2026.10.21")]
    [InlineData("2026.10.21", "<html>error</html>")]
    [InlineData("2026.10.21+custom", "2026.10.21")]
    public void UnknownVersionsAreNeverReportedAsCurrent(string? installed, string? latest)
    {
        var status = new YtDlpVersionStatus(installed, latest);
        Assert.False(status.IsUpToDate);
        Assert.False(status.UpdateAvailable);
        Assert.Null(status.Comparison);
    }

    [Fact]
    public void OutdatedMessageIncludesBothVersions()
    {
        var status = new YtDlpVersionStatus("2026.09.01", "2026.10.21");
        Assert.True(status.UpdateAvailable);
        Assert.Contains("2026.09.01", status.Message);
        Assert.Contains("2026.10.21", status.Message);
    }

    [Fact]
    public void AnUnchangedOldVersionDoesNotProduceAnUpdateSuccess()
    {
        var before = new YtDlpVersionStatus("2026.09.01", "2026.10.21");
        var result = YtDlpVersionPolicy.VerifyUpdate(before, "2026.09.01");
        Assert.False(result.Succeeded);
        Assert.Contains("não foi confirmada", result.Message);
    }

    [Fact]
    public void AVerifiedUpdateReportsTheOldAndNewVersions()
    {
        var before = new YtDlpVersionStatus("2026.09.01", "2026.10.21");
        var result = YtDlpVersionPolicy.VerifyUpdate(before, "2026.10.21");
        Assert.True(result.Succeeded);
        Assert.Contains("atualizado de 2026.09.01 para 2026.10.21", result.Message);
    }

    [Fact]
    public void AlreadyCurrentIsNotMisreportedAsANewlyAppliedUpdate()
    {
        var before = new YtDlpVersionStatus("2026.10.21", "2026.10.21");
        var result = YtDlpVersionPolicy.VerifyUpdate(before, "2026.10.21");
        Assert.True(result.Succeeded);
        Assert.Contains("já está atualizado", result.Message);
        Assert.DoesNotContain("atualizado de", result.Message);
    }

    [Fact]
    public void AFailedLatestCheckNeverShowsAFalseUpToDateResult()
    {
        var status = new YtDlpVersionStatus("2026.10.21", null, "Sem conexão");
        Assert.False(status.IsUpToDate);
        Assert.False(status.UpdateAvailable);
        Assert.Equal("Sem conexão", status.Message);
    }

    [Fact]
    public void NewerNightlyIsKeptInsteadOfOfferingADowngrade()
    {
        var status = new YtDlpVersionStatus("2026.10.21.232757", "2026.10.21");
        Assert.True(status.IsUpToDate);
        Assert.False(status.UpdateAvailable);
        Assert.Contains("mais recente", status.Message);
    }
}
