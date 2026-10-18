using Homestead.Assets;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

/// <summary>The check of the original installation; the parts with a real one pass unchecked without it.</summary>
public class InstallationCheckTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("homestead-install-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void An_empty_or_missing_folder_is_no_installation()
    {
        var empty = InstallationCheck.Check(_directory);
        Assert.True(empty.Exists);
        Assert.False(empty.IsValid);
        Assert.False(empty.HasGfx);
        Assert.Equal(InstallationCheck.RequiredSounds.Count, empty.MissingSounds.Count);
        Assert.False(InstallationCheck.Check(Path.Combine(_directory, "nothing")).Exists);
        Assert.Null(InstallationCheck.FindRoot(_directory));
    }

    [Fact]
    public void Folders_without_the_files_are_reported_as_such()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "gfx"));
        Directory.CreateDirectory(Path.Combine(_directory, "Snd"));
        Directory.CreateDirectory(Path.Combine(_directory, "MAP", "Multi"));
        File.WriteAllText(Path.Combine(_directory, "Snd", "Siedler3_00.dat"), "");
        File.WriteAllText(Path.Combine(_directory, "MAP", "Multi", "test.map"), "");

        var report = InstallationCheck.Check(_directory);
        Assert.True(report.HasGfx);
        Assert.Equal(InstallationCheck.RequiredGfx.Count, report.MissingGfx.Count);
        Assert.Equal(["siedler3_01.dat"], report.MissingSounds);
        Assert.True(report.MapsOk);
        Assert.False(report.IsValid);
    }

    [Fact]
    public void The_installation_is_found_from_its_own_subfolder()
    {
        if (GameInstallation.TryLocate(out _) is not { } installation)
        {
            output.WriteLine("No installation found, skipped.");
            return;
        }
        Assert.True(InstallationCheck.Check(installation.Root).IsValid);
        Assert.Equal(Path.GetFullPath(installation.Root).TrimEnd(Path.DirectorySeparatorChar),
            InstallationCheck.FindRoot(installation.GfxDirectory));
        Assert.Equal(Path.GetFullPath(installation.Root).TrimEnd(Path.DirectorySeparatorChar),
            InstallationCheck.FindRoot(Path.GetDirectoryName(installation.Root)!, depth: 1));
    }

    [Fact]
    public void The_search_in_the_usual_places_finds_the_local_installation()
    {
        if (GameInstallation.TryLocate(out _) is not { } installation)
        {
            output.WriteLine("No installation found, skipped.");
            return;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var found = InstallationCheck.Detect().ToList();
        output.WriteLine($"{watch.ElapsedMilliseconds} ms: {string.Join(", ", found)}");
        // Only where it lies in one of the usual places (as the local GOG/Wine one does).
        if (found.Count > 0)
            Assert.All(found, root => Assert.True(InstallationCheck.Check(root).IsValid));
    }
}
