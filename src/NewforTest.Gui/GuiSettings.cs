// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Text.Json;
using System.Text.Json.Serialization;
using NewforTest.Core;

namespace NewforTest.Gui;

/// <summary>Settings remembered between runs, stored as JSON in the user's application data folder.</summary>
public sealed record GuiSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NewforTest", "gui-settings.json");

    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 1234;
    public string Page { get; init; } = "888";
    public TeletextColor Color { get; init; } = TeletextColor.White;
    public bool Boxed { get; init; } = true;
    public TeletextColor BoxColor { get; init; } = TeletextColor.Black;
    public bool DoubleHeight { get; init; }
    public VerticalPosition Position { get; init; } = VerticalPosition.Lower;
    public Justification Justification { get; init; } = Justification.Left;
    public string CharacterSet { get; init; } = TeletextCharacterSet.English.Name;
    public decimal SoakSeconds { get; init; } = 5;
    public bool HexDump { get; init; }
    public string CustomText { get; init; } = "";
    public string? LastFolder { get; init; }

    /// <summary>Loads settings, or returns defaults if the file is missing or unreadable.</summary>
    public static GuiSettings Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(path), JsonOptions) ?? new GuiSettings()
                : new GuiSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new GuiSettings();
        }
    }

    /// <summary>Saves settings; failures are ignored (settings are a convenience).</summary>
    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not worth bothering the user about
        }
    }
}
