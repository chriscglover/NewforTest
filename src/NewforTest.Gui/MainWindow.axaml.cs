// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using NewforTest.Core;
using NewforTest.Gui.ViewModels;

namespace NewforTest.Gui;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        CueList.DoubleTapped += (_, _) => _viewModel?.GoToCueCommand.Execute(CueList.SelectedItem as CueItem);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.Log.CollectionChanged -= OnLogChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is null)
            return;

        _viewModel.PickFile = PickFileAsync;
        _viewModel.CopyToClipboard = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
        _viewModel.Log.CollectionChanged += OnLogChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async Task<string?> PickFileAsync()
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Open subtitle file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Subtitle files") { Patterns = SubtitleFile.Extensions.Select(e => "*" + e).ToArray() },
                FilePickerFileTypes.All,
            },
        };

        if (_viewModel?.LastFolder is { } folder && Directory.Exists(folder))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(folder);

        var files = await StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    // The IDataObject drag-and-drop API is marked obsolete in Avalonia 11.3 in favour of
    // DataTransfer, but still works; switch over when moving to Avalonia 12.
#pragma warning disable CS0618
    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath() is { } path)
            _viewModel?.LoadFile(path);
    }
#pragma warning restore CS0618

    // Keep the newest log line in view
    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _viewModel!.Log.Count > 0)
            LogList.ScrollIntoView(_viewModel.Log.Count - 1);
    }

    // Keep the cue on screen in view while playing
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedCue) && _viewModel?.SelectedCue is { } cue)
            CueList.ScrollIntoView(cue);
    }
}
