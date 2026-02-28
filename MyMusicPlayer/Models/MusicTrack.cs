using System;
using Avalonia.Media.Imaging;

namespace MyMusicPlayer.Models;

public class MusicTrack
{
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = "未知艺术家";
    public string Album { get; set; } = "未知专辑";
    public TimeSpan Duration { get; set; }
    public Bitmap? AlbumArt { get; set; }

    public string DurationText => Duration == TimeSpan.Zero
        ? "--:--"
        : Duration.TotalHours >= 1
            ? Duration.ToString(@"h\:mm\:ss")
            : Duration.ToString(@"m\:ss");
}

