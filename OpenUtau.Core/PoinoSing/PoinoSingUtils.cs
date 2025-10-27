using System;
using System.Collections.Generic;

namespace PoinoSing {

    public class PoinoSingSpeaker {
        public string Id { get; set; }

        public string Name { get; set; }

        public int Fs { get; set; }

        public int SegLen { get; set; }

        public int ShiftLen { get; set; }

        public int ShiftNum { get; set; }

        public Dictionary<string, double[][]> Envelopes { get; set; }

        public Dictionary<string, Entry> Kanas { get; set; }
    }
    public partial class Entry {
        public string EnvKey { get; set; }

        public double? Len { get; set; }

        public double Vol { get; set; }
    }
    public class Symbol {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> Kanas { get; set; }
        public List<string> Phonemes { get; set; }
    }
    public partial class TimingsRequest {
        public List<string> Phrase { get; set; } = new List<string>();

        public double Bpm { get; set; }

        public string SpeakerId { get; set; }

        public string Mode { get; set; }

        public List<int> Anchors { get; set; } = new List<int>();
    }
    public partial class TimingsResponse {
        public string SpeakerId { get; set; }

        public int Bpm { get; set; }

        public Token[] Tokens { get; set; }

        public double[] PhonemeTimeline { get; set; }

        public string Unit { get; set; }
    }

    public partial class Token {
        public string Lyric { get; set; }

        public int AnchorTick { get; set; }

        public double[] PhonemeTimings { get; set; }

        public double[] AbsoluteTimings { get; set; }

        public string[] Phonemes { get; set; }
    }// リクエストモデル
    public sealed class SynthesizeRenderPhraseRequest {
        public string SpeakerId { get; set; } = "";
        public double BeginSec { get; set; }
        public double EndSec { get; set; }
        public List<Phone> Phones { get; set; } = new List<Phone>();

        public List<double>? PitchCents { get; set; }
        public List<double>? PitchTimesSec { get; set; }

        public List<double>? Dynamics { get; set; }
        public List<double>? DynamicsTimesSec { get; set; }

        public void Validate() {
            if (string.IsNullOrWhiteSpace(SpeakerId)) throw new ArgumentException("speakerId is required.");
            if (EndSec < BeginSec) throw new ArgumentException("endSec must be >= beginSec.");
            bool hasB = PitchCents != null && PitchCents.Count > 0
                && PitchTimesSec != null && PitchTimesSec.Count > 0
                && PitchCents.Count == PitchTimesSec.Count;
            if (!hasB) throw new ArgumentException("Either (baseMidiPitch + f0Seg) or (pitchCents + pitchTimesSec) is required.");
            if (Dynamics != null || DynamicsTimesSec != null) {
                if (Dynamics == null || Dynamics.Count == 0
                    || DynamicsTimesSec == null || DynamicsTimesSec.Count == 0
                    || Dynamics.Count != DynamicsTimesSec.Count) {
                    throw new ArgumentException("dynamics and dynamicsTimesSec must be provided with equal length.");
                }
            }
            if (Phones.Count == 0) throw new ArgumentException("phones must be non-empty.");
        }
    }

    public sealed class Phone {
        public string Phoneme { get; set; } = ""; // EnvKey or "k:1,s:0.5"
        public double PositionSec { get; set; }   // 絶対秒
        public double DurationSec { get; set; }
        public double? Volume { get; set; }       // 0..1（省略可・既定=1）
    }
    public static class PoinoSingUtils {
        public static bool IsSyllableVowelExtensionNote(string lyric) {
            return lyric.StartsWith("+~") || lyric.StartsWith("+*") || lyric.StartsWith("+") || lyric.StartsWith("-");
        }
        public static bool IsPau(string lyric) {
            return lyric.StartsWith("R") || lyric.StartsWith("SP") || lyric.StartsWith("AP") || lyric.StartsWith("pau") || string.IsNullOrEmpty(lyric);
        }
    }
}
