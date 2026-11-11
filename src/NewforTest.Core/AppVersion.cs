// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Reflection;

namespace NewforTest.Core;

/// <summary>
/// The release version shown by the CLI and the GUI. It comes from &lt;Version&gt; in
/// Directory.Build.props, so every app and exe reports the same number.
/// </summary>
public static class AppVersion
{
    /// <summary>The version as released, e.g. "4.0".</summary>
    public static string Display { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(2)
        ?? "?";
}
