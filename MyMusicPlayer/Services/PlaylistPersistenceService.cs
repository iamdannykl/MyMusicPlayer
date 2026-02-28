using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyMusicPlayer.Services;

/// <summary>
/// 把播放列表（文件路径列表）和上次播放状态以 JSON 形式持久化到用户数据目录。
/// </summary>
public static class PlaylistPersistenceService
{
    private static readonly string SaveDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MyMusicPlayer");

    private static readonly string SavePath = Path.Combine(SaveDir, "playlist.json");

    public static void Save(IEnumerable<string> filePaths, int lastIndex, float volume, int playMode)
    {
        try
        {
            Directory.CreateDirectory(SaveDir);
            var data = new SaveData
            {
                FilePaths = new List<string>(filePaths),
                LastIndex = lastIndex,
                Volume    = volume,
                PlayMode  = playMode
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SavePath, json);
        }
        catch
        {
            // ignore write errors
        }
    }

    public static SaveData? Load()
    {
        try
        {
            if (!File.Exists(SavePath)) return null;
            var json = File.ReadAllText(SavePath);
            return JsonSerializer.Deserialize<SaveData>(json);
        }
        catch
        {
            return null;
        }
    }

    public class SaveData
    {
        [JsonPropertyName("filePaths")]
        public List<string> FilePaths { get; set; } = new();

        [JsonPropertyName("lastIndex")]
        public int LastIndex { get; set; } = -1;

        [JsonPropertyName("volume")]
        public float Volume { get; set; } = 0.8f;

        [JsonPropertyName("playMode")]
        public int PlayMode { get; set; } = 0;
    }
}

