using System.Text.RegularExpressions;

namespace DeskBox.Tests;

/// <summary>
/// WeatherWidgetContent keeps its established runtime {Binding} surface, and
/// Native AOT builds resolve those bindings only for properties listed in
/// <c>WeatherViewModels.AotBindableProperties.cs</c> — a binding on an
/// unlisted member fails silently (empty text, and Visibility stays at its
/// Visible default). The v3.1 visual line added the error-overlay bindings
/// without extending that manifest, shipping an always-on, empty error
/// overlay on every retail build while JIT Debug and the x64 test suite
/// stayed green. This contract pins the manifest against the XAML so the
/// next new binding cannot reach packaging unrooted.
/// </summary>
public sealed class WeatherAotBindingSurfaceContractTests
{
    [Fact]
    public void WeatherManifest_CoversEveryRuntimeBindingPath()
    {
        string manifestText = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/ViewModels/WeatherViewModels.AotBindableProperties.cs"));
        string xamlText = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Controls/WidgetContents/WeatherWidgetContent.xaml"));

        HashSet<string> manifestMembers = ManifestMembers(manifestText);
        List<string> unrooted = ViewModelBindingPaths(xamlText)
            .Where(path => !manifestMembers.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unrooted.Count == 0,
            "WeatherWidgetContent binds these view-model members at runtime, but " +
            "they are missing from WeatherViewModels.AotBindableProperties.cs — " +
            "on Native AOT retail builds each of them renders empty (and a " +
            "Visibility binding stays Visible). Add them to the manifest:\n" +
            string.Join('\n', unrooted));
    }

    [Fact]
    public void WeatherManifest_PinsTheErrorOverlaySurface()
    {
        // The regression this file was born from: the error overlay's four
        // bindings were the ones that shipped unrooted.
        string manifestText = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/ViewModels/WeatherViewModels.AotBindableProperties.cs"));
        HashSet<string> members = ManifestMembers(manifestText);

        Assert.Contains("ErrorVisibility", members);
        Assert.Contains("ErrorTitle", members);
        Assert.Contains("ErrorBody", members);
        Assert.Contains("ErrorRetryText", members);
    }

    /// <summary>
    /// Extracts the view-model paths from runtime bindings: the first path
    /// segment of every {Binding} that targets the DataContext. Bindings with
    /// an ElementName that does not go through DataContext bind control to
    /// control and never touch the view model, so they are skipped.
    /// </summary>
    private static IEnumerable<string> ViewModelBindingPaths(string xamlText)
    {
        foreach (Match binding in new Regex(@"\{Binding\s+([^}]*)\}").Matches(xamlText))
        {
            string options = binding.Groups[1].Value;
            string path = options.Split(',')[0].Trim();
            bool viaDataContext = path.StartsWith("DataContext.", StringComparison.Ordinal);
            bool hasElementName = options.Contains("ElementName=", StringComparison.Ordinal);
            if (viaDataContext)
            {
                path = path["DataContext.".Length..];
            }
            else if (hasElementName)
            {
                continue;
            }

            if (path.Length == 0 ||
                !Regex.IsMatch(path, "^[A-Z][A-Za-z0-9_]*$"))
            {
                continue;
            }

            yield return path;
        }
    }

    private static HashSet<string> ManifestMembers(string manifestText)
    {
        HashSet<string> members = new(StringComparer.Ordinal);
        foreach (Match member in new Regex(@"nameof\(([A-Za-z0-9_]+)\)").Matches(manifestText))
        {
            members.Add(member.Groups[1].Value);
        }

        return members;
    }
}
