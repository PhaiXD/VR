using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Bhaptics.SDK2
{
    [Serializable]
    internal class MappingMessage
    {
        public bool status;
        public List<MappingMetaData> message;

        internal static MappingMessage CreateFromJSON(string jsonString)
        {
            return JsonUtility.FromJson<MappingMessage>(jsonString);
        }
    }

    // Editor workspace queries. Routed through bhaptics_library (the core library the
    // runtime already ships) instead of the separate bhaptics_editor native library.
    public class bhaptics_editor
    {
        public static string GetHapticMessages(string app_id, string api_key, int version, out int status)
        {
            // bhaptics_library takes the api key first, then the workspace id.
            IntPtr resultPtr = bhaptics_library.bHapticsGetHapticMessage(api_key, app_id, version, out status);
            return ReadResult(resultPtr, ref status);
        }

        public static string GetHapticMappings(string app_id, string api_key, int version, out int status)
        {
            IntPtr resultPtr = bhaptics_library.bHapticsGetHapticMappings(api_key, app_id, version, out status);
            return ReadResult(resultPtr, ref status);
        }

        // The returned buffer is owned by the library and reused by the next call on the
        // same thread: copy it immediately and never free it.
        private static string ReadResult(IntPtr resultPtr, ref int status)
        {
            if (resultPtr == IntPtr.Zero)
            {
                status = 999;
                return null;
            }

            var str = Marshal.PtrToStringAnsi(resultPtr);
            if (status == 0 && string.IsNullOrEmpty(str))
            {
                // A request timeout reports status 0 with an empty body.
                status = 999;
            }
            return str;
        }

        public static List<MappingMetaData> EditorGetEventList(string appId, string apiKey, int lastVersion, out int status)
        {
            var str = GetHapticMappings(appId, apiKey, lastVersion, out int code);

            status = code;
            if (code == 0)
            {
                var mappingMessage = MappingMessage.CreateFromJSON(str);
                return mappingMessage.message;
            }
            BhapticsLogManager.Log($"EditorGetEventList: {code}");
            return new List<MappingMetaData>();
        }

        public static string EditorGetSettings(string appId, string apiKey, int lastVersion, out int status2)
        {
            var str = GetHapticMessages(appId, apiKey, lastVersion, out int status);

            status2 = status;
            if (status == 0)
            {
                return str;
            }

            return "";
        }
    }

}
