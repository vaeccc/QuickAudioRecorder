using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace QuickAudioRecorder
{
    internal static class IconResources
    {
        private const string IdleName = "QuickAudioRecorder.Resources.MiniRec_idle.ico";
        private const string RecordingName = "QuickAudioRecorder.Resources.MiniRec_recording.ico";

        public static Icon LoadIdle() { return Load(IdleName); }
        public static Icon LoadRecording() { return Load(RecordingName); }

        private static Icon Load(string name)
        {
            var assembly = typeof(IconResources).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("Embedded icon resource was not found: " + name);
                using (var icon = new Icon(stream)) return (Icon)icon.Clone();
            }
        }
    }
}
