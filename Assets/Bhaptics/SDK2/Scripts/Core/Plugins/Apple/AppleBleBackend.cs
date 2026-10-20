namespace Bhaptics.SDK2
{
    /// <summary>
    /// iOS/visionOS backend: a thin adapter from <see cref="IBhapticsBackend"/> to the native
    /// boundary <see cref="BhapticsPluginNative"/> (which no-ops off-device). No external bHaptics
    /// Player is required on Apple — playback runs over CoreBluetooth in the native framework.
    /// </summary>
    internal class AppleBleBackend : IBhapticsBackend
    {
        public bool Initialize(string appId, string apiKey, string json)
        {
            return BhapticsPluginNative.Initialize(apiKey, appId);
        }

        public int Play(string eventId, int requestId, int startMillis, float intensity, float duration, float angleX, float offsetY)
        {
            if (startMillis > 0)
            {
                return BhapticsPluginNative.PlayWithStartTime(eventId, startMillis, intensity, duration, angleX, offsetY);
            }

            return BhapticsPluginNative.PlayParam(eventId, requestId, intensity, duration, angleX, offsetY);
        }

        public int PlayLoop(string eventId, float intensity, float duration, float angleX, float offsetY, int interval, int maxCount)
        {
            return BhapticsPluginNative.PlayLoop(eventId, intensity, duration, angleX, offsetY, interval, maxCount);
        }

        public int PlayPath(int position, float[] xValues, float[] yValues, int[] intensityValues, int durationMillis)
        {
            return BhapticsPluginNative.PlayPath(position, durationMillis, xValues, yValues, intensityValues);
        }

        public int PlayWaveform(int position, int[] motorValues, int[] playTimeValues, int[] shapeValues, int motorCount, int frequency, int repeatCount)
        {
            var index = new int[motorCount];
            var intensity = new int[motorCount];
            var shape = new int[motorCount];
            var playTime = new int[motorCount];
            for (int i = 0; i < motorCount; i++)
            {
                index[i] = i;
                intensity[i] = motorValues[i];
                shape[i] = shapeValues[i];
                playTime[i] = playTimeValues[i];
            }

            return BhapticsPluginNative.PlayGlove(position, index, intensity, shape, playTime, frequency, repeatCount);
        }

        public int PlayMotors(int position, int[] motors, int durationMillis)
        {
            var bytes = new byte[motors.Length];
            for (int i = 0; i < motors.Length; i++)
            {
                int v = motors[i];
                bytes[i] = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
            }
            return BhapticsPluginNative.PlayMotors(position, durationMillis, bytes);
        }

        public void PauseByEventId(string eventId)
        {
            BhapticsPluginNative.Pause(eventId);
        }

        public void ResumeByEventId(string eventId)
        {
            BhapticsPluginNative.Resume(eventId);
        }

        public bool StopByEventId(string eventId)
        {
            BhapticsPluginNative.StopByEventId(eventId);
            return true;
        }

        public bool StopByRequestId(int requestId)
        {
            BhapticsPluginNative.StopByRequestId(requestId);
            return true;
        }

        public bool StopAll()
        {
            BhapticsPluginNative.StopAll();
            return true;
        }

        public bool IsPlaying() => BhapticsPluginNative.IsPlaying();

        public bool IsPlayingByEventId(string eventId) => BhapticsPluginNative.IsPlayingByEventId(eventId);

        public bool IsPlayingByRequestId(int requestId) => BhapticsPluginNative.IsPlayingByRequestId(requestId);

        public void Ping(string deviceId)
        {
            BhapticsPluginNative.Ping(deviceId);
        }

        public void PingAll()
        {
            BhapticsPluginNative.PingAll();
        }
    }
}
