using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MyMusicPlayer.Models;
using MyMusicPlayer.ViewModels;

namespace MyMusicPlayer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _isSeeking;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Wire buttons
        this.FindControl<Button>("ImportFolderBtn")!.Click += ImportFolder_Click;
        this.FindControl<Button>("ImportFileBtn")!.Click += ImportFile_Click;
        this.FindControl<Button>("ClearBtn")!.Click += (_, _) => _vm.ClearPlaylist();
        this.FindControl<Button>("PrevBtn")!.Click += (_, _) => _vm.Previous();
        this.FindControl<Button>("PlayPauseBtn")!.Click += (_, _) => _vm.TogglePlayPause();
        this.FindControl<Button>("NextBtn")!.Click += (_, _) => _vm.Next();
        this.FindControl<Button>("PlayModeBtn")!.Click += (_, _) => _vm.CyclePlayMode();

        // Progress slider seeking
        var progressSlider = this.FindControl<Slider>("ProgressSlider")!;
        progressSlider.AddHandler(PointerPressedEvent, ProgressSlider_PointerPressed, RoutingStrategies.Tunnel);
        progressSlider.AddHandler(PointerReleasedEvent, ProgressSlider_PointerReleased, RoutingStrategies.Tunnel);

        // Double-click list item to play
        var listBox = this.FindControl<ListBox>("TrackListBox")!;
        listBox.DoubleTapped += TrackList_DoubleTapped;

        // Drag & drop
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        // Remove track button – event bubbles up from DataTemplate
        listBox.AddHandler(Button.ClickEvent, TrackList_ButtonClick, RoutingStrategies.Bubble);

        Closed += (_, _) => _vm.Dispose();
    }

    // ── Import ─────────────────────────────────────────────────────────────
    private async void ImportFolder_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择音乐文件夹",
            AllowMultiple = false
        });
        if (result.Count == 0) return;
        var path = result[0].TryGetLocalPath();
        if (path != null)
            await _vm.ImportFolderAsync(path);
    }

    private async void ImportFile_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择音频文件",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("音频文件")
                {
                    Patterns = new[] { "*.mp3","*.ogg","*.flac","*.wav","*.aac","*.m4a","*.wma","*.opus","*.ape","*.aiff" }
                }
            }
        });
        foreach (var file in result)
        {
            var path = file.TryGetLocalPath();
            if (path != null) _vm.AddFile(path);
        }
    }

    // ── Drag & Drop ────────────────────────────────────────────────────────
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        var files = e.Data.GetFiles();
        if (files == null) return;
        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (path == null) continue;
            if (System.IO.Directory.Exists(path))
                await _vm.ImportFolderAsync(path);
            else
                _vm.AddFile(path);
        }
    }

    // ── List interactions ──────────────────────────────────────────────────
    private void TrackList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.SelectedIndex >= 0 && _vm.SelectedIndex < _vm.FilteredPlaylist.Count)
            _vm.PlayTrack(_vm.FilteredPlaylist[_vm.SelectedIndex]);
    }

    private void TrackList_ButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Name: "RemoveTrackBtn", Tag: MusicTrack track })
            _vm.RemoveTrack(track);
    }

    // ── Progress seeking ───────────────────────────────────────────────────
    private void ProgressSlider_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isSeeking = true;
        _vm.IsSeeking = true;
    }

    private void ProgressSlider_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isSeeking)
        {
            var slider = this.FindControl<Slider>("ProgressSlider")!;
            _vm.SeekTo((long)slider.Value);
            _isSeeking = false;
            _vm.IsSeeking = false;
        }
    }
}