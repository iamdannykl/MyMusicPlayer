using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;

namespace MyMusicPlayer.Services;

public class AudioPlayerService : IDisposable
{
    private int _channel;
    private bool _disposed;
    private Timer? _positionTimer;

    public event EventHandler? PlaybackEnded;
    public event EventHandler<long>? PositionChanged;   // ms
    public event EventHandler<long>? DurationChanged;   // ms

    public bool IsPlaying { get; private set; }

    private float _volume = 0.8f;
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_channel != 0)
                Bass.ChannelSetAttribute(_channel, ChannelAttribute.Volume, _volume);
        }
    }

    public AudioPlayerService()
    {
        var exeDir = AppContext.BaseDirectory;

        // Pre-load the native library from our output directory so ManagedBass finds it
        var bassLib = Path.Combine(exeDir, "libbass.dylib");
        if (File.Exists(bassLib))
            NativeLibrary.Load(bassLib);

        if (!Bass.Init())
            Bass.Init(0);   // init without a device (silent fallback)

        // Load optional format plugins
        TryLoadPlugin(Path.Combine(exeDir, "libbassflac.dylib"));
        TryLoadPlugin(Path.Combine(exeDir, "libbassopus.dylib"));

        // Poll position every 200 ms
        _positionTimer = new Timer(_ => PollPosition(), null,
            TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
    }

    private static void TryLoadPlugin(string path)
    {
        if (File.Exists(path))
            Bass.PluginLoad(path);
    }

    public void Play(string filePath)
    {
        StopAndFreeChannel();

        _channel = Bass.CreateStream(filePath, Flags: BassFlags.Prescan);
        if (_channel == 0)
        {
            // fallback: try pushing through FX decoder
            _channel = Bass.CreateStream(filePath);
        }
        if (_channel == 0) return;

        Bass.ChannelSetAttribute(_channel, ChannelAttribute.Volume, _volume);

        // Notify duration
        var lengthBytes = Bass.ChannelGetLength(_channel);
        var lengthSec = Bass.ChannelBytes2Seconds(_channel, lengthBytes);
        DurationChanged?.Invoke(this, (long)(lengthSec * 1000));

        // End-of-stream callback
        Bass.ChannelSetSync(_channel, SyncFlags.End, 0, (handle, channel, data, user) =>
        {
            IsPlaying = false;
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        });

        Bass.ChannelPlay(_channel);
        IsPlaying = true;
    }

    public void TogglePlayPause()
    {
        if (_channel == 0) return;
        if (IsPlaying)
        {
            Bass.ChannelPause(_channel);
            IsPlaying = false;
        }
        else
        {
            Bass.ChannelPlay(_channel, false);
            IsPlaying = true;
        }
    }

    public void Stop()
    {
        StopAndFreeChannel();
        IsPlaying = false;
    }

    public void Seek(long milliseconds)
    {
        if (_channel == 0) return;
        var bytes = Bass.ChannelSeconds2Bytes(_channel, milliseconds / 1000.0);
        Bass.ChannelSetPosition(_channel, bytes);
    }

    public long GetPosition()
    {
        if (_channel == 0) return 0;
        var bytes = Bass.ChannelGetPosition(_channel);
        return (long)(Bass.ChannelBytes2Seconds(_channel, bytes) * 1000);
    }

    private void PollPosition()
    {
        if (_channel == 0 || !IsPlaying) return;
        PositionChanged?.Invoke(this, GetPosition());
    }

    private void StopAndFreeChannel()
    {
        if (_channel != 0)
        {
            Bass.ChannelStop(_channel);
            Bass.StreamFree(_channel);
            _channel = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _positionTimer?.Dispose();
        StopAndFreeChannel();
        Bass.Free();
    }
}
