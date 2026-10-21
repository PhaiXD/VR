using System;
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Bhaptics.SDK2
{
    /// <summary>
    /// The single boundary to the native Apple Swift framework (BhapticsPlugin). On iOS/visionOS
    /// device builds the wrappers call the statically-linked <c>__Internal</c> C exports; everywhere
    /// else (including the Editor) they are safe no-ops returning defaults. The externs MUST stay
    /// compiled out of non-Apple builds — an uncompiled-out <c>__Internal</c> import fails at
    /// IL2CPP link time. Swift.Int is 64-bit on all shipped slices and marshals as <c>long</c>;
    /// strings = LPUTF8Str, Swift.Bool = U1.
    /// </summary>
    internal static class BhapticsPluginNative
    {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
        private const string Lib = "__Internal";

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_initialize")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool NativeInitialize(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string apiKey,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string appId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_play")]
        private static extern long NativePlay([MarshalAs(UnmanagedType.LPUTF8Str)] string eventId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_playParam")]
        private static extern long NativePlayParam(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string eventId,
            long requestId, float intensity, float duration, float angleX, float offsetY);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_playLoop")]
        private static extern long NativePlayLoop(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string eventId,
            float intensity, float duration, float angleX, float offsetY, long interval, long maxCount);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_playWithStartTime")]
        private static extern long NativePlayWithStartTime(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string eventId,
            long startMillis, float intensity, float duration, float angleX, float offsetY);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_playPath")]
        private static extern long NativePlayPath(
            long position, long durationMillis, long count, float[] x, float[] y, int[] intensity);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_playGlove")]
        private static extern long NativePlayGlove(
            long position, long count, int[] index, int[] intensity, int[] shape, int[] playTime, long frequency, long repeatCount);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_playMotors")]
        private static extern long NativePlayMotors(long position, long durationMillis, long length, byte[] motors);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_stopByRequestId")]
        private static extern void NativeStopByRequestId(long requestId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_stopByEventId")]
        private static extern void NativeStopByEventId([MarshalAs(UnmanagedType.LPUTF8Str)] string eventId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_stopAll")]
        private static extern void NativeStopAll();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_stop")]
        private static extern void NativeTurnOffMotors();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_isPlaying")]
        private static extern long NativeIsPlaying();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_isPlayingByRequestId")]
        private static extern long NativeIsPlayingByRequestId(long requestId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_isPlayingByEventId")]
        private static extern long NativeIsPlayingByEventId([MarshalAs(UnmanagedType.LPUTF8Str)] string eventId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_pause")]
        private static extern void NativePause([MarshalAs(UnmanagedType.LPUTF8Str)] string eventId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_resume")]
        private static extern void NativeResume([MarshalAs(UnmanagedType.LPUTF8Str)] string eventId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_ping")]
        private static extern void NativePing([MarshalAs(UnmanagedType.LPUTF8Str)] string deviceId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_pingAll")]
        private static extern void NativePingAll();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_scan")]
        private static extern void NativeScan();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_isScanning")]
        private static extern long NativeIsScanning();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_getBluetoothState")]
        private static extern long NativeGetBluetoothState();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_openAppSettings")]
        private static extern void NativeOpenAppSettings();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_stopScan")]
        private static extern void NativeStopScan();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_pair")]
        private static extern void NativePair([MarshalAs(UnmanagedType.LPUTF8Str)] string deviceId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_unpair")]
        private static extern void NativeUnpair([MarshalAs(UnmanagedType.LPUTF8Str)] string deviceId);

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_getDevices")]
        private static extern IntPtr NativeGetDevices();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_getEventList")]
        private static extern IntPtr NativeGetEventList();

        [DllImport(Lib, EntryPoint = "BhapticsPlugin_freeString")]
        private static extern void NativeFreeString(IntPtr ptr);
#endif

        internal static bool Initialize(string apiKey, string appId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return NativeInitialize(apiKey, appId);
#else
            return false;
#endif
        }

        internal static int Play(string eventId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return (int)NativePlay(eventId);
#else
            return -1;
#endif
        }

        internal static int PlayParam(string eventId, int requestId, float intensity, float duration, float angleX, float offsetY)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return (int)NativePlayParam(eventId, requestId, intensity, duration, angleX, offsetY);
#else
            return -1;
#endif
        }

        internal static int PlayLoop(string eventId, float intensity, float duration, float angleX, float offsetY, int interval, int maxCount)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return (int)NativePlayLoop(eventId, intensity, duration, angleX, offsetY, interval, maxCount);
#else
            return -1;
#endif
        }

        internal static int PlayWithStartTime(string eventId, int startMillis, float intensity, float duration, float angleX, float offsetY)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return (int)NativePlayWithStartTime(eventId, startMillis, intensity, duration, angleX, offsetY);
#else
            return -1;
#endif
        }

        internal static int PlayPath(int position, int durationMillis, float[] x, float[] y, int[] intensity)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return (int)NativePlayPath(position, durationMillis, x.Length, x, y, intensity);
#else
            return -1;
#endif
        }

        internal static int PlayGlove(int position, int[] index, int[] intensity, int[] shape, int[] playTime, int frequency, int repeatCount)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return (int)NativePlayGlove(position, index.Length, index, intensity, shape, playTime, frequency, repeatCount);
#else
            return -1;
#endif
        }

        internal static int PlayMotors(int position, int durationMillis, byte[] motors)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return (int)NativePlayMotors(position, durationMillis, motors.Length, motors);
#else
            return -1;
#endif
        }

        internal static void StopByEventId(string eventId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativeStopByEventId(eventId);
#endif
        }

        internal static void StopByRequestId(int requestId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativeStopByRequestId(requestId);
#endif
        }

        internal static void StopAll()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativeStopAll();
            NativeTurnOffMotors();
#endif
        }

        internal static bool IsPlaying()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return NativeIsPlaying() != 0;
#else
            return false;
#endif
        }

        internal static bool IsPlayingByEventId(string eventId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return NativeIsPlayingByEventId(eventId) != 0;
#else
            return false;
#endif
        }

        internal static bool IsPlayingByRequestId(int requestId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return NativeIsPlayingByRequestId(requestId) != 0;
#else
            return false;
#endif
        }

        internal static void Pause(string eventId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativePause(eventId);
#endif
        }

        internal static void Resume(string eventId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativeResume(eventId);
#endif
        }

        internal static void Ping(string deviceId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativePing(deviceId);
#endif
        }

        internal static void PingAll()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativePingAll();
#endif
        }

        internal static void Scan()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativeScan();
#endif
        }

        internal static bool IsScanning()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return NativeIsScanning() != 0;
#else
            return false;
#endif
        }

        internal static void StopScan()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativeStopScan();
#endif
        }

        /// <summary>CBManagerState.rawValue (0 unknown .. 5 poweredOn); 0 off-device.</summary>
        internal static int GetBluetoothState()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return (int)NativeGetBluetoothState();
#else
            return 0;
#endif
        }

        internal static void OpenAppSettings()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativeOpenAppSettings();
#endif
        }

        internal static void Pair(string deviceId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativePair(deviceId);
#endif
        }

        internal static void Unpair(string deviceId)
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            NativeUnpair(deviceId);
#endif
        }

        internal static string GetDevicesJson()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return ReadAndFreeNativeString(NativeGetDevices());
#else
            return "";
#endif
        }

        internal static string GetEventListJson()
        {
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
            return ReadAndFreeNativeString(NativeGetEventList());
#else
            return "";
#endif
        }

#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
        private static string ReadAndFreeNativeString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return "";
            }

            try
            {
                return PtrToStringUtf8(ptr);
            }
            finally
            {
                NativeFreeString(ptr);
            }
        }

        private static string PtrToStringUtf8(IntPtr ptr)
        {
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0)
            {
                len++;
            }

            if (len == 0)
            {
                return "";
            }

            var bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
#endif
    }
}
