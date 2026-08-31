using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;

namespace SpotifyFifinePlugin;

/// <summary>
/// NativeAOT-compatible C ABI wrapper around the prebuilt <c>libspotifyctl.dll</c>.
///
/// <para><b>ABI notes (validated by inspection of the Node binding).</b></para>
/// <list type="bullet">
///   <item>koffi loads the DLL with its default (cdecl) calling convention and the
///     callback prototypes are declared without a <c>__stdcall</c> modifier, so every
///     export is <see cref="CallConvCdecl"/>. Native callbacks therefore use a
///     <c>delegate* unmanaged[Cdecl]</c> function pointer.</item>
///   <item>The managed mirror struct below uses <see cref="LayoutKind.Sequential"/>
///     with <see cref="StructLayoutAttribute.Pack"/> = 8. On Windows x64 the C
///     compiler also uses 8-byte packing, yielding identical field offsets:
///     status@0, artist@8, title@16, album@24, position_ms@32, duration_ms@40,
///     album_art@48, album_art_len@56 (size_t=8), can_seek@64, can_skip_next@68,
///     can_skip_prev@72, is_ad@76, audible@80, app_muted@84, app_volume@88
///     (total 92, padded to 96).</item>
///   <item>All string and album-art pointers are copied out <i>immediately</i> inside
///     the native callback (UTF-8 strings via <see cref="Marshal.PtrToStringUTF8"/>
///     and art via <see cref="Marshal.Copy(System.IntPtr,byte[],int,int)"/>); no
///     native memory is retained past the copy.</item>
///   <item>Callback tokens are retained and disconnected <i>before</i> the native
///     handle is freed. The function pointer itself is a stable static method, so it
///     never needs GC rooting; the per-instance <see cref="GCHandle"/> only keeps the
///     managed controller alive for the duration of the native subscription.</item>
/// </list>
/// </summary>
internal sealed class SpotifyController : ISpotifyController, IDisposable
{
    private const string DllName = "libspotifyctl";

    // Mirror of spotifyctl_playback_state. All fields are blittable so it can be
    // passed by ref to spotifyctl_latest_state with zero marshaling.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct PlaybackStateRaw
    {
        public int Status;
        public IntPtr Artist;
        public IntPtr Title;
        public IntPtr Album;
        public long PositionMs;
        public long DurationMs;
        public IntPtr AlbumArt;
        public UIntPtr AlbumArtLen; // size_t
        public int CanSeek;
        public int CanSkipNext;
        public int CanSkipPrev;
        public int IsAd;
        public int Audible;
        public int AppMuted;
        public float AppVolume;
    }

    private static class Lib
    {
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr spotifyctl_version();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr spotifyctl_new();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void spotifyctl_free(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void spotifyctl_start(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void spotifyctl_stop(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_is_running(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_play(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_pause(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_next(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_previous(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_send_command(IntPtr c, int cmd);

        [DllImport(DllName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_open_uri(IntPtr c, string uri);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_send_key(IntPtr c, uint vk);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern float spotifyctl_get_app_volume(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_set_app_volume(IntPtr c, float v);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_is_app_muted(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_set_app_muted(IntPtr c, int muted);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern float spotifyctl_get_peak_amplitude(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int spotifyctl_latest_state(IntPtr c, ref PlaybackStateRaw outState);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern UIntPtr spotifyctl_latest_state_json(IntPtr c, IntPtr buf, UIntPtr cap);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern long spotifyctl_latest_position_smooth_ms(IntPtr c);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern UIntPtr spotifyctl_on_state_changed_with_replay(
            IntPtr c,
            delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void> cb,
            IntPtr user);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void spotifyctl_disconnect(IntPtr c, UIntPtr token);
    }

    static SpotifyController()
    {
        NativeLibrary.SetDllImportResolver(typeof(SpotifyController).Assembly, ResolveDll);
    }

    private static IntPtr ResolveDll(string name, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != DllName)
            return IntPtr.Zero;

        var candidate = Path.Combine(AppContext.BaseDirectory, DllName + ".dll");
        if (File.Exists(candidate))
            return NativeLibrary.Load(candidate);

        // Fall back to default loader (DLL beside the exe / PATH).
        return IntPtr.Zero;
    }

    // Stable native callback. The function pointer is taken from this static
    // method, so it never moves; the GCHandle carries the instance.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void StateChangedThunk(IntPtr statePtr, IntPtr userPtr)
    {
        try
        {
            if (statePtr == IntPtr.Zero)
                return;

            var handle = GCHandle.FromIntPtr(userPtr);
            if (handle.Target is not SpotifyController self)
                return;

            var raw = Marshal.PtrToStructure<PlaybackStateRaw>(statePtr);
            var snapshot = ToManaged(raw);
            self._stateChannel.Writer.TryWrite(snapshot);
        }
        catch
        {
            // Native callbacks must never throw back into unmanaged code.
        }
    }

    private static PlaybackState ToManaged(PlaybackStateRaw raw)
    {
        return new PlaybackState
        {
            Status = raw.Status,
            Artist = Marshal.PtrToStringUTF8(raw.Artist) ?? "",
            Title = Marshal.PtrToStringUTF8(raw.Title) ?? "",
            Album = Marshal.PtrToStringUTF8(raw.Album) ?? "",
            PositionMs = raw.PositionMs,
            DurationMs = raw.DurationMs,
            AlbumArt = CopyArt(raw.AlbumArt, raw.AlbumArtLen),
            CanSeek = raw.CanSeek != 0,
            CanSkipNext = raw.CanSkipNext != 0,
            CanSkipPrev = raw.CanSkipPrev != 0,
            IsAd = raw.IsAd != 0,
            Audible = raw.Audible != 0,
            AppMuted = raw.AppMuted != 0,
            AppVolume = raw.AppVolume,
        };
    }

    private static byte[] CopyArt(IntPtr ptr, UIntPtr len)
    {
        int n = (int)len;
        if (ptr == IntPtr.Zero || n <= 0)
            return Array.Empty<byte>();

        var buf = new byte[n];
        Marshal.Copy(ptr, buf, 0, n);
        return buf;
    }

    private readonly Channel<PlaybackState> _stateChannel =
        Channel.CreateBounded<PlaybackState>(new BoundedChannelOptions(128)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    private IntPtr _handle;
    private GCHandle _self;
    private bool _started;
    private bool _disposed;
    private readonly System.Collections.Generic.List<ulong> _tokens = new();

    public ChannelReader<PlaybackState> StateReader => _stateChannel.Reader;

    public static string Version => Marshal.PtrToStringUTF8(Lib.spotifyctl_version()) ?? "";

    public SpotifyController()
    {
        _handle = Lib.spotifyctl_new();
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("spotifyctl_new() returned NULL");

        _self = GCHandle.Alloc(this);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            return;

        // Register the replay variant so any current state is also delivered once
        // the managed consumer starts draining the channel. Token is retained.
        // The function pointer is taken in an unsafe context (no GC root needed —
        // it is a stable static method).
        unsafe
        {
            var token = Lib.spotifyctl_on_state_changed_with_replay(
                _handle,
                &StateChangedThunk,
                GCHandle.ToIntPtr(_self));
            _tokens.Add(token.ToUInt64());
        }

        Lib.spotifyctl_start(_handle);
        _started = true;
    }

    public bool IsRunning
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Lib.spotifyctl_is_running(_handle) != 0;
        }
    }

    public bool TryRecover()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Lib.spotifyctl_is_running(_handle) != 0)
            return true;

        try
        {
            // libspotifyctl finds an already-open Spotify window during Start().
            // Rebuilding the monitor covers a missed window-create event during
            // Windows startup, without restarting the Fifine host/plugin.
            Lib.spotifyctl_stop(_handle);
            Lib.spotifyctl_start(_handle);
            _started = true;
            return Lib.spotifyctl_is_running(_handle) != 0;
        }
        catch
        {
            return false;
        }
    }

    public PlaybackState? LatestState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == IntPtr.Zero)
            return null;

        var raw = new PlaybackStateRaw();
        int ok = Lib.spotifyctl_latest_state(_handle, ref raw);
        if (ok == 0)
            return null;

        return ToManaged(raw);
    }

    public bool Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Lib.spotifyctl_play(_handle) != 0;
    }

    public bool Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Lib.spotifyctl_pause(_handle) != 0;
    }

    public bool Next()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Lib.spotifyctl_next(_handle) != 0;
    }

    public bool Previous()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Lib.spotifyctl_previous(_handle) != 0;
    }

    public double GetAppVolume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Lib.spotifyctl_get_app_volume(_handle);
    }

    public bool TrySetAppVolume(double value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Lib.spotifyctl_set_app_volume(_handle, (float)value) != 0;
    }

    public bool IsAppMuted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Lib.spotifyctl_is_app_muted(_handle) != 0;
    }

    public void SetAppMuted(bool muted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Lib.spotifyctl_set_app_muted(_handle, muted ? 1 : 0);
    }

    public void OpenUri(string uri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Lib.spotifyctl_open_uri(_handle, uri);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_handle != IntPtr.Zero)
        {
            // Disconnect every retained callback token BEFORE freeing the handle.
            foreach (var t in _tokens)
            {
                try
                {
                    Lib.spotifyctl_disconnect(_handle, (UIntPtr)t);
                }
                catch
                {
                    // best-effort
                }
            }

            _tokens.Clear();

            try
            {
                Lib.spotifyctl_stop(_handle);
            }
            catch
            {
                // best-effort
            }

            try
            {
                Lib.spotifyctl_free(_handle);
            }
            catch
            {
                // best-effort
            }

            _handle = IntPtr.Zero;
        }

        if (_self.IsAllocated)
            _self.Free();

        _stateChannel.Writer.TryComplete();
    }
}
