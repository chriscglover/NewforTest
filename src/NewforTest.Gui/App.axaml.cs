// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NewforTest.Gui.ViewModels;

namespace NewforTest.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel(GuiSettings.Load(GuiSettings.DefaultPath));
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            desktop.Exit += (_, _) =>
            {
                viewModel.CaptureSettings().Save(GuiSettings.DefaultPath);
                viewModel.Dispose(); // clears the screen and sends DISCONNECT
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
