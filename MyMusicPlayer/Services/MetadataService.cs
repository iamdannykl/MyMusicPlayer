using System;
using System.IO;
using Avalonia.Media.Imaging;
using TagLib;

namespace MyMusicPlayer.Services;

public static class MetadataService
{
    private static readonly string[] SupportedExtensions =
    {
        ".mp3", ".ogg", ".flac", ".wav", ".aac", ".m4a", ".wma", ".opus", ".ape", ".aiff"
    };

    public static bool IsSupportedAudio(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return Array.IndexOf(SupportedExtensions, ext) >= 0;
    }

    public static (string Title, string Artist, string Album, TimeSpan Duration, Bitmap? AlbumArt) ReadMetadata(string filePath)
    {
        var title = Path.GetFileNameWithoutExtension(filePath);
        var artist = "未知艺术家";
        var album = "未知专辑";
        var duration = TimeSpan.Zero;
        Bitmap? albumArt = null;

        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            if (!string.IsNullOrWhiteSpace(tagFile.Tag.Title))
                title = tagFile.Tag.Title;
            if (!string.IsNullOrWhiteSpace(tagFile.Tag.FirstPerformer))
                artist = tagFile.Tag.FirstPerformer;
            if (!string.IsNullOrWhiteSpace(tagFile.Tag.Album))
                album = tagFile.Tag.Album;
            duration = tagFile.Properties.Duration;

            if (tagFile.Tag.Pictures.Length > 0)
            {
                var pictureData = tagFile.Tag.Pictures[0].Data.Data;
                using var ms = new MemoryStream(pictureData);
                albumArt = new Bitmap(ms);
            }
        }
        catch
        {
            // ignore metadata errors
        }

        return (title, artist, album, duration, albumArt);
    }
}

