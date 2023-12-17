using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.MusicTheory;
using OpenUtau.Core.Ustx;
using SharpCompress.Common;

namespace OpenUtau.Core.Enunu {
    public struct EnunuNote {
        public string lyric;
        public int length;
        public int noteNum;
        public int noteIndex;
        public string timbre;
    }

    internal static class EnunuUtils {
        static readonly Encoding ShiftJIS = Encoding.GetEncoding("shift_jis");
        static readonly Encoding UTF8 = Encoding.GetEncoding("utf8");

        internal static void WriteUst(IList<EnunuNote> notes, double tempo, USinger singer, string ustPath) {
            using (var writer = new StreamWriter(ustPath, false, ShiftJIS)) {
                writer.WriteLine("[#SETTING]");
                writer.WriteLine($"Tempo={tempo}");
                writer.WriteLine("Tracks=1");
                writer.WriteLine($"Project={ustPath}");
                writer.WriteLine($"VoiceDir={singer.Location}");
                writer.WriteLine($"CacheDir={PathManager.Inst.CachePath}");
                writer.WriteLine("Mode2=True");
                for (int i = 0; i < notes.Count; ++i) {
                    writer.WriteLine($"[#{i}]");
                    writer.WriteLine($"Lyric={notes[i].lyric}");
                    writer.WriteLine($"Length={notes[i].length}");
                    writer.WriteLine($"NoteNum={notes[i].noteNum}");
                    if (!string.IsNullOrEmpty(notes[i].timbre)) {
                        writer.WriteLine($"Flags={notes[i].timbre}");
                    }
                }
                writer.WriteLine("[#TRACKEND]");
            }
        }
    }
}
