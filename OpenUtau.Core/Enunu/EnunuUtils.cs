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

        internal static void WriteHed(EnunuConfig config, Dictionary<string, int> enu_singing_style, string hedPath) {
            List<string> hedContents = new List<string>();
            int lineCount = 0;
            List<string> styleNames = config.styles.styles;
            string[] lines = File.ReadAllLines(config.questionPath);
            foreach (string styleName in styleNames) {
                int styleLineCount = 0;
                foreach (string hedLine in lines) {
                    if (hedLine.StartsWith($"Q5 \"{styleName}\"")) {
                        if (enu_singing_style.TryGetValue(styleName, out int styleLevel)) {
                            if (styleLineCount < styleLevel) {
                                hedContents.Add($"Q5 \"{styleName}\" {{*]xx/*}}");
                            } else {
                                hedContents.Add($"Q5 \"{styleName}\" {{*]{styleName}/*}}");
                            }
                            styleLineCount++;
                        } else {
                            hedContents.Add(hedLine);
                        }
                    } else {
                        hedContents.Add(hedLine);
                    }
                }
            }
            if (hedContents.Count != 0) {

                using (var writer = new StreamWriter(hedPath, false, UTF8)) {

                    foreach (string line in hedContents) {
                        writer.WriteLine(line);
                    }
                }
            }
        }
    }
}
