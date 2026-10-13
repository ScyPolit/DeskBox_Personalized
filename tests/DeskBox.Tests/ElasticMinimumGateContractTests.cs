namespace DeskBox.Tests;

/// <summary>
/// Source contract for the elastic minimum-size gates (spec 5.2 rule 4):
/// the projection clamp follows the plan's adaptive minimum, and no
/// downstream gate re-inflates an elastically sized widget back to the
/// preferred minimum — any of the five gates clamping to 50 would push the
/// widget over its neighbours under extreme shrink.
/// </summary>
public sealed class ElasticMinimumGateContractTests
{
    [Fact]
    public void ElasticCore_DefinesAdaptiveMinimumAndAbsoluteFloor()
    {
        string layout = Read("src/DeskBox/Services/WidgetElasticLayout.cs");

        Assert.Contains("AbsoluteMinWidgetSizeDips = 20", layout, StringComparison.Ordinal);
        Assert.Contains("internal static double AdaptiveMinimumLength(", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionAndDownstreamGates_NeverReclampToThePreferredMinimum()
    {
        string service = Read("src/DeskBox/Services/WidgetTopologyLayoutService.cs");

        // The projection clamp follows the plan's adaptive minimum...
        Assert.Contains("plan.AdaptiveMinimumWidth", service, StringComparison.Ordinal);
        Assert.Contains("plan.AdaptiveMinimumHeight", service, StringComparison.Ordinal);
        // ...and no gate in the service re-inflates an elastically sized
        // widget back to the preferred minimum (seeding, physical cache,
        // sanitize all use the absolute floor instead).
        Assert.DoesNotContain("SettingsService.MinWidgetWidth", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsService.MinWidgetHeight", service, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowCreationGate_UsesTheAbsoluteElasticFloor()
    {
        string manager = Read("src/DeskBox/Services/WidgetManager.cs");

        Assert.Contains(
            "Math.Max(WidgetElasticLayout.AbsoluteMinWidgetSizeDips, config.Width)",
            manager,
            StringComparison.Ordinal);
        Assert.Contains(
            "Math.Max(WidgetElasticLayout.AbsoluteMinWidgetSizeDips, config.Height)",
            manager,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Math.Max(SettingsService.MinWidgetWidth, config.Width)",
            manager,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureTolerance_SwallowsPixelQuantizationNoise()
    {
        string service = Read("src/DeskBox/Services/WidgetTopologyLayoutService.cs");

        Assert.Contains(
            "private const double CaptureToleranceDips = 1.0;",
            service,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Math.Abs(left - right) < 0.01",
            service,
            StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "DeskBox", "DeskBox.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("DeskBox repository root was not found.");
    }
}
