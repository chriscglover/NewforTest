// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Text.RegularExpressions;
using NewforTest.Core;

namespace NewforTest.Tests;

public class AppVersionTests
{
    [Fact]
    public void Is_major_dot_minor_with_no_build_metadata() =>
        Assert.Matches(new Regex(@"^\d+\.\d+$"), AppVersion.Display);

    [Fact]
    public void Matches_the_version_in_Directory_Build_props()
    {
        // Walk up from the test output folder to the repository root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        string props = File.ReadAllText(Path.Combine(dir!.FullName, "Directory.Build.props"));
        string version = Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value;

        Assert.Equal(version, AppVersion.Display);
    }
}
