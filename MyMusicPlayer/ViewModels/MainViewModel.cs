using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MyMusicPlayer.Models;
using MyMusicPlayer.Services;

namespace MyMusicPlayer.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioPlayerService _player;
    private readonly Random _random = new();
    private List<int> _shuffleList = new();
    private int _shuffleIndex = -1;

    // 防止 PlaybackEnded 回调重入（单曲循环快速触发两次）
    private bool _handlingPlaybackEnded;
    // 构造完成前不写盘
    private bool _initialized;

    // ── playlist ─────────────────────────────────────────────────────────
    public ObservableCollection<MusicTrack> Playlist { get; } = new();

    private MusicTrack? _currentTrack;
    public MusicTrack? CurrentTrack
    {
        get => _currentTrack;
        set
        {
            _currentTrack = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTitle));
            OnPropertyChanged(nameof(CurrentArtist));
            OnPropertyChanged(nameof(CurrentAlbumArt));
        }
    }

    private int _selectedIndex = -1;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set { _selectedIndex = value; OnPropertyChanged(); }
    }

    // ── playback state ────────────────────────────────────────────────────
    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon)); }
    }

    public string PlayPauseIcon => IsPlaying ? "⏸" : "▶";

    private long _duration;
    public long Duration
    {
        get => _duration;
        set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); }
    }

    private long _position;
    public long Position
    {
        get => _position;
        set
        {
            if (Math.Abs(_position - value) < 200) return;
            _position = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PositionText));
        }
    }

    private bool _isSeeking;
    public bool IsSeeking
    {
        get => _isSeeking;
        set { _isSeeking = value; OnPropertyChanged(); }
    }

    public string PositionText => FormatTime(_position);
    public string DurationText => FormatTime(_duration);

    private float _volume = 0.8f;
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            _player.Volume = _volume;
            OnPropertyChanged();
            SavePlaylist();
        }
    }

    // ── play mode ─────────────────────────────────────────────────────────
    private PlayMode _playMode = PlayMode.ListLoop;
    public PlayMode PlayMode
    {
        get => _playMode;
        set
        {
            _playMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayModeIcon));
            OnPropertyChanged(nameof(PlayModeToolTip));
            SavePlaylist();
        }
    }

    public string PlayModeIcon => PlayMode switch
    {
        PlayMode.ListLoop   => "🔁",
        PlayMode.SingleLoop => "🔂",
        PlayMode.Shuffle    => "🔀",
        _ => "🔁"
    };

    public string PlayModeToolTip => PlayMode switch
    {
        PlayMode.ListLoop   => "列表循环",
        PlayMode.SingleLoop => "单曲循环",
        PlayMode.Shuffle    => "随机播放",
        _ => "列表循环"
    };

    // ── current track info ────────────────────────────────────────────────
    public string CurrentTitle     => CurrentTrack?.Title  ?? "暂无播放";
    public string CurrentArtist    => CurrentTrack?.Artist ?? "—";
    public Bitmap? CurrentAlbumArt => CurrentTrack?.AlbumArt;

    // ── search ────────────────────────────────────────────────────────────
    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); FilterPlaylist(); }
    }

    public ObservableCollection<MusicTrack> FilteredPlaylist { get; } = new();

    // ── loading ───────────────────────────────────────────────────────────
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    private string _statusText = "欢迎使用 MyMusicPlayer";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    // ── ctor ──────────────────────────────────────────────────────────────
    public MainViewModel()
    {
        _player = new AudioPlayerService();
        _player.PlaybackEnded   += OnPlaybackEnded;
        _player.PositionChanged += OnPositionChanged;
        _player.DurationChanged += OnDurationChanged;
        _player.Volume = _volume;

        // 启动时恢复上次的播放列表
        _ = RestorePlaylistAsync();
        _initialized = true;
    }

    // ── 持久化 ─────────────────────────────────────────────────────────────
    private void SavePlaylist()
    {
        if (!_initialized) return;
        PlaylistPersistenceService.Save(
            Playlist.Select(t => t.FilePath),
            CurrentTrack != null ? Playlist.IndexOf(CurrentTrack) : -1,
            _volume,
            (int)_playMode);
    }

    private async Task RestorePlaylistAsync()
    {
        var data = PlaylistPersistenceService.Load();
        if (data == null || data.FilePaths.Count == 0) return;

        // 过滤掉已不存在的文件
        var validPaths = data.FilePaths.Where(File.Exists).ToList();
        if (validPaths.Count == 0) return;

        IsLoading = true;
        StatusText = "正在恢复播放列表...";

        // 恢复播放模式和音量（不触发 SavePlaylist）
        _playMode = (PlayMode)Math.Clamp(data.PlayMode, 0, 2);
        OnPropertyChanged(nameof(PlayMode));
        OnPropertyChanged(nameof(PlayModeIcon));
        OnPropertyChanged(nameof(PlayModeToolTip));

        _volume = Math.Clamp(data.Volume, 0f, 1f);
        _player.Volume = _volume;
        OnPropertyChanged(nameof(Volume));

        await Task.Run(() =>
        {
            int count = 0;
            foreach (var file in validPaths)
            {
                var (title, artist, album, duration, art) = MetadataService.ReadMetadata(file);
                var track = new MusicTrack
                {
                    FilePath = file, Title = title,
                    Artist = artist, Album = album,
                    Duration = duration, AlbumArt = art
                };
                int snap = ++count;
                Dispatcher.UIThread.Post(() =>
                {
                    Playlist.Add(track);
                    FilterPlaylist();
                    StatusText = $"正在恢复 {snap}/{validPaths.Count} 首...";
                });
            }
        });

        BuildShuffleList();
        IsLoading = false;
        StatusText = $"共 {Playlist.Count} 首歌曲";

        // 恢复上次选中的曲目（仅高亮，不自动播放）
        if (data.LastIndex >= 0 && data.LastIndex < Playlist.Count)
        {
            SelectedIndex = data.LastIndex;
            CurrentTrack = Playlist[data.LastIndex];
        }
    }

    // ── commands ──────────────────────────────────────────────────────────
    public async Task ImportFolderAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;
        IsLoading = true;
        StatusText = "正在扫描文件夹...";

        // 在 UI 线程拿快照，避免后台线程访问 ObservableCollection
        var existingPaths = new HashSet<string>(Playlist.Select(t => t.FilePath));

        await Task.Run(() =>
        {
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(MetadataService.IsSupportedAudio)
                .Where(f => !existingPaths.Contains(f))
                .OrderBy(f => f)
                .ToList();

            int count = 0;
            foreach (var file in files)
            {
                var (title, artist, album, duration, art) = MetadataService.ReadMetadata(file);
                var track = new MusicTrack
                {
                    FilePath = file, Title = title,
                    Artist = artist, Album = album,
                    Duration = duration, AlbumArt = art
                };
                int snap = ++count;
                Dispatcher.UIThread.Post(() =>
                {
                    Playlist.Add(track);
                    FilterPlaylist();
                    StatusText = $"已加载 {snap}/{files.Count} 首...";
                });
            }
        });

        IsLoading = false;
        StatusText = $"共 {Playlist.Count} 首歌曲";
        BuildShuffleList();
        SavePlaylist();
    }

    public void AddFile(string filePath)
    {
        if (!MetadataService.IsSupportedAudio(filePath)) return;
        if (Playlist.Any(t => t.FilePath == filePath)) return;
        var (title, artist, album, duration, art) = MetadataService.ReadMetadata(filePath);
        Playlist.Add(new MusicTrack
        {
            FilePath = filePath, Title = title,
            Artist = artist, Album = album,
            Duration = duration, AlbumArt = art
        });
        FilterPlaylist();
        BuildShuffleList();
        StatusText = $"共 {Playlist.Count} 首歌曲";
        SavePlaylist();
    }

    public void PlayTrack(MusicTrack track)
    {
        CurrentTrack = track;
        SelectedIndex = Playlist.IndexOf(track);
        Duration = 0;
        Position = 0;
        _player.Play(track.FilePath);
        IsPlaying = true;
        StatusText = $"正在播放: {track.Title}";
        SavePlaylist();
    }

    public void TogglePlayPause()
    {
        if (CurrentTrack == null && Playlist.Count > 0)
        {
            PlayTrack(Playlist[0]);
            return;
        }
        _player.TogglePlayPause();
        IsPlaying = _player.IsPlaying;
    }

    public void Previous()
    {
        if (Playlist.Count == 0) return;
        PlayTrack(Playlist[GetPreviousIndex()]);
    }

    public void Next()
    {
        if (Playlist.Count == 0) return;
        PlayTrack(Playlist[GetNextIndex()]);
    }

    public void CyclePlayMode()
    {
        PlayMode = PlayMode switch
        {
            PlayMode.ListLoop   => PlayMode.SingleLoop,
            PlayMode.SingleLoop => PlayMode.Shuffle,
            PlayMode.Shuffle    => PlayMode.ListLoop,
            _ => PlayMode.ListLoop
        };
        if (PlayMode == PlayMode.Shuffle) BuildShuffleList();
    }

    public void SeekTo(long ms)
    {
        _player.Seek(ms);
        _position = ms;
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(PositionText));
    }

    public void RemoveTrack(MusicTrack track)
    {
        bool wasCurrent = CurrentTrack == track;
        Playlist.Remove(track);
        FilterPlaylist();
        BuildShuffleList();
        if (wasCurrent)
        {
            _player.Stop();
            CurrentTrack = null;
            IsPlaying = false;
            Duration = 0;
            Position = 0;
        }
        StatusText = $"共 {Playlist.Count} 首歌曲";
        SavePlaylist();
    }

    public void ClearPlaylist()
    {
        _player.Stop();
        CurrentTrack = null;
        IsPlaying = false;
        Duration = 0;
        Position = 0;
        Playlist.Clear();
        FilteredPlaylist.Clear();
        BuildShuffleList();
        StatusText = "播放列表已清空";
        SavePlaylist();
    }

    // ── private helpers ───────────────────────────────────────────────────
    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // 防止重入：BASS 有时在同一首歌快速结束时触发两次
            if (_handlingPlaybackEnded) return;
            _handlingPlaybackEnded = true;

            try
            {
                IsPlaying = false;
                if (Playlist.Count == 0) return;

                if (PlayMode == PlayMode.SingleLoop)
                {
                    // 单曲循环：直接重播当前曲目，不调用 Next()
                    if (CurrentTrack != null)
                        PlayTrack(CurrentTrack);
                }
                else
                {
                    Next();
                }
            }
            finally
            {
                _handlingPlaybackEnded = false;
            }
        });
    }

    private void OnPositionChanged(object? sender, long ms)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsSeeking) Position = ms;
        });
    }

    private void OnDurationChanged(object? sender, long ms)
    {
        Dispatcher.UIThread.Post(() => Duration = ms);
    }

    private int GetNextIndex()
    {
        if (Playlist.Count == 0) return 0;
        if (PlayMode == PlayMode.Shuffle) return GetNextShuffleIndex();
        int cur = CurrentTrack != null ? Playlist.IndexOf(CurrentTrack) : -1;
        return (cur + 1) % Playlist.Count;
    }

    private int GetPreviousIndex()
    {
        if (Playlist.Count == 0) return 0;
        int cur = CurrentTrack != null ? Playlist.IndexOf(CurrentTrack) : 0;
        return (cur - 1 + Playlist.Count) % Playlist.Count;
    }

    private void BuildShuffleList()
    {
        _shuffleList = Enumerable.Range(0, Playlist.Count).OrderBy(_ => _random.Next()).ToList();
        _shuffleIndex = -1;
    }

    private int GetNextShuffleIndex()
    {
        if (Playlist.Count == 0) return 0;

        _shuffleIndex++;
        if (_shuffleIndex >= _shuffleList.Count)
        {
            BuildShuffleList();
            _shuffleIndex = 0;
        }

        return _shuffleList[_shuffleIndex];
    }

    private void FilterPlaylist()
    {
        FilteredPlaylist.Clear();
        var q = _searchText.Trim().ToLowerInvariant();
        foreach (var t in Playlist)
        {
            if (string.IsNullOrEmpty(q) ||
                t.Title.ToLowerInvariant().Contains(q)  ||
                t.Artist.ToLowerInvariant().Contains(q) ||
                t.Album.ToLowerInvariant().Contains(q))
                FilteredPlaylist.Add(t);
        }
    }

    private static string FormatTime(long ms)
    {
        if (ms <= 0) return "0:00";
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    public void Dispose() => _player.Dispose();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
