// TS origin: ./utils/path.ts, ./utils/windowsPaths.ts
using ClawSharp.Core;
using System.Text;

namespace ClawSharp.UnitTests;

public sealed class PathUtilitiesTests
{
    [Fact]
    public void ExpandPath_Resolves_Relative_Path_Against_Base_Directory()
    {
        var expanded = PathUtilities.ExpandPath(@" .\src ", @"D:\repo");

        Assert.Equal(@"D:\repo\src", expanded);
    }

    [Fact]
    public void ExpandPath_Expands_Home_Directory_Notation()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(expected, PathUtilities.ExpandPath("~"));
    }

    [Fact]
    public void ExpandPath_Converts_Msys_Drive_Paths_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var expanded = PathUtilities.ExpandPath("/c/Users/test/project");

        Assert.Equal(@"C:\Users\test\project", expanded);
    }

    [Fact]
    public void NormalizePathInputForCurrentPlatform_Converts_Cygdrive_Paths_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var normalized = PathUtilities.NormalizePathInputForCurrentPlatform("/cygdrive/c/Users/test/project");

        Assert.Equal(@"C:\Users\test\project", normalized);
    }

    [Fact]
    public void NormalizePathForConfigKey_Uses_Forward_Slashes()
    {
        var normalized = PathUtilities.NormalizePathForConfigKey(@"D:\repo\child");

        Assert.Equal("D:/repo/child", normalized);
    }

    [Fact]
    public void NormalizePathForConfigKey_Normalizes_Unc_Paths_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var normalized = PathUtilities.NormalizePathForConfigKey(@"\\server\share\repo\child");

        Assert.Equal("//server/share/repo/child", normalized);
    }

    [Fact]
    public void ResolveRealPathLikeNode_Resolves_Symlinked_Ancestor_Segments_When_Supported()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-realpath", Guid.NewGuid().ToString("N"));
        var actualRoot = Path.Combine(tempRoot, "actual");
        var actualChild = Path.Combine(actualRoot, "repo");
        var linkRoot = Path.Combine(tempRoot, "link");
        Directory.CreateDirectory(actualChild);

        try
        {
            try
            {
                Directory.CreateSymbolicLink(linkRoot, actualRoot);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (PlatformNotSupportedException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            var resolved = PathUtilities.ResolveRealPathLikeNode(Path.Combine(linkRoot, "repo"));

            Assert.Equal(Path.GetFullPath(actualChild).Normalize(NormalizationForm.FormC), resolved);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
