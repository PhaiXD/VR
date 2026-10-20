namespace Bhaptics.SDK2
{
    /// <summary>
    /// Backend seam for haptic playback so the public <see cref="BhapticsLibrary"/> facade can stay
    /// a single API across platforms. <see cref="AppleBleBackend"/> implements the iOS/visionOS path
    /// against the native Swift BhapticsPlugin (CoreBluetooth).
    /// </summary>
    internal interface IBhapticsBackend
    {
        bool Initialize(string appId, string apiKey, string json);

        int Play(string eventId, int requestId, int startMillis, float intensity, float duration, float angleX, float offsetY);
        int PlayLoop(string eventId, float intensity, float duration, float angleX, float offsetY, int interval, int maxCount);
        int PlayPath(int position, float[] xValues, float[] yValues, int[] intensityValues, int durationMillis);
        int PlayWaveform(int position, int[] motorValues, int[] playTimeValues, int[] shapeValues, int motorCount, int frequency, int repeatCount);
        int PlayMotors(int position, int[] motors, int durationMillis);

        void PauseByEventId(string eventId);
        void ResumeByEventId(string eventId);

        bool StopByEventId(string eventId);
        bool StopByRequestId(int requestId);
        bool StopAll();

        bool IsPlaying();
        bool IsPlayingByEventId(string eventId);
        bool IsPlayingByRequestId(int requestId);

        void Ping(string deviceId);
        void PingAll();
    }
}
