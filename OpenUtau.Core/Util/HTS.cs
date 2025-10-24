using System;
using System.Collections.Generic;
using System.Linq;

//This file implement utaupy.hts python library's function
//https://github.com/oatsu-gh/utaupy/hts.py

//HTS labels use b instead of #
//In HTS labels, "xx" is a preserved keyword that means null
namespace OpenUtau.Core.Util {
    public static class HTS {
        public static readonly string[] KeysInOctave = {
            "C",
            "Db",
            "D",
            "Eb",
            "E",
            "F",
            "Gb",
            "G",
            "Ab",
            "A",
            "Bb",
            "B" ,
        };

        public static readonly Dictionary<string, int> NameInOctave = new Dictionary<string, int> {
            { "C", 0 }, { "C#", 1 }, { "Db", 1 },
            { "D", 2 }, { "D#", 3 }, { "Eb", 3 },
            { "E", 4 },
            { "F", 5 }, { "F#", 6 }, { "Gb", 6 },
            { "G", 7 }, { "G#", 8 }, { "Ab", 8 },
            { "A", 9 }, { "A#", 10 }, { "Bb", 10 },
            { "B", 11 },
        };

        public static string GetToneName(int noteNum) {
            return noteNum < 0 ? string.Empty : KeysInOctave[noteNum % 12] + (noteNum / 12 - 1).ToString();
        }

        public static string GetOctaveNum(int noteNum) {
            NameInOctave.TryGetValue(KeysInOctave[noteNum % 12].ToString(), out int num);
            return noteNum < 0 ? string.Empty : num.ToString();
        }

        //return -1 if error
        public static int NameToTone(string name) {
            if (name.Length < 2) {
                return -1;
            }
            var str = name.Substring(0, (name[1] == '#' || name[1] == 'b') ? 2 : 1);
            var num = name.Substring(str.Length);
            if (!int.TryParse(num, out int octave)) {
                return -1;
            }
            if (!NameInOctave.TryGetValue(str, out int inOctave)) {
                return -1;
            }
            return 12 * (octave + 1) + inOctave;
        }

        public static string WriteInt(int integer) {
            return (integer >= 0 ? "p" : "m") + Math.Abs(integer).ToString();
        }
    }

    public class HTSPhoneme {
        public string symbol;
        public string flag1 = "xx";
        public string flag2 = "xx";

        //Links to this phoneme's neighbors and parent
        public HTSPhoneme? prev;
        public HTSPhoneme? next;
        public HTSNote parent;

        //informations about this phoneme
        //v:vowel, c:consonant, p:pause, s:silence, b:break
        public string type = "xx";
        //(number of phonemes before this phoneme in this note) + 1
        public int position = 1;
        //(number of phonemes after this phoneme in this note) + 1
        public int position_backward = 1;
        //Here -1 means null
        //distances to vowels in this note, -1 for vowels themselves
        public int distance_from_previous_vowel = -1;
        public int distance_to_next_vowel = -1;

        public HTSPhoneme(string phoneme, HTSNote note) {
            this.symbol = phoneme;
            this.parent = note;
        }

        public HTSPhoneme? beforePrev {
            get {
                if (prev == null) { return null; } else { return prev.prev; }
            }
        }

        public HTSPhoneme? afterNext {
            get {
                if (next == null) { return null; } else { return next.next; }
            }
        }

        public string dump() {
            //Write phoneme as an HTS line
            // 100ns単位出力時にintオーバーフローを避けるためlongへ
            string result =
                $"{(long)parent.startMs * 10000} {(long)parent.endMs * 10000} "
                //Phoneme informations
                + string.Format("{0}@{1}^{2}-{3}+{4}={5}_{6}%{7}^{8}_{9}~{10}-{11}!{12}[{13}${14}]{15}", p())
                //Syllable informations
                + string.Format("/A:{0}-{1}-{2}@{3}~{4}", a())
                + string.Format("/B:{0}_{1}_{2}@{3}|{4}", b())
                + string.Format("/C:{0}+{1}+{2}@{3}&{4}", c())
                //Note informations
                + string.Format("/D:{0}!{1}#{2}${3}%{4}|{5}&{6};{7}-{8}", d())
                + string.Format(
                    "/E:{0}]{1}^{2}={3}~{4}!{5}@{6}#{7}+{8}]{9}${10}|{11}[{12}&{13}]{14}={15}^{16}~{17}#{18}_{19};{20}${21}&{22}%{23}[{24}|{25}]{26}-{27}^{28}+{29}~{30}={31}@{32}${33}!{34}%{35}#{36}|{37}|{38}-{39}&{40}&{41}+{42}[{43};{44}]{45};{46}~{47}~{48}^{49}^{50}@{51}[{52}#{53}={54}!{55}~{56}+{57}!{58}^{59}",
                    e())
                + string.Format("/F:{0}#{1}#{2}-{3}${4}${5}+{6}%{7};{8}", f())
                + string.Format("/G:{0}_{1}", g())
                + string.Format("/H:{0}_{1}", h())
                + string.Format("/I:{0}_{1}", i())
                + string.Format("/J:{0}~{1}@{2}", j())
                ;
            return result;
        }

        public string[] p() {
            var result = Enumerable.Repeat("xx", 16).ToArray();
            result[0] = type;
            result[1] = (beforePrev == null) ? "xx" : beforePrev.symbol;
            result[2] = (prev == null) ? "xx" : prev.symbol;
            result[3] = symbol;
            result[4] = (next == null) ? "xx" : next.symbol;
            result[5] = (afterNext == null) ? "xx" : afterNext.symbol;
            result[6] = (beforePrev == null) ? "xx" : beforePrev.flag1;
            result[7] = (prev == null) ? "xx" : prev.flag1;
            result[8] = flag1;
            result[9] = (next == null) ? "xx" : next.flag1;
            result[10] = (afterNext == null) ? "xx" : afterNext.flag1;
            result[11] = position.ToString();
            result[12] = position_backward.ToString();
            result[13] = distance_from_previous_vowel < 0 ? "xx" : distance_from_previous_vowel.ToString();
            result[14] = distance_to_next_vowel < 0 ? "xx" : distance_to_next_vowel.ToString();
            result[15] = flag2;

            return result;
        }

        public string[] a() {
            return parent.a();
        }

        public string[] b() {
            return parent.b();
        }

        public string[] c() {
            return parent.c();
        }

        public string[] d() {
            return parent.d();
        }

        public string[] e() {
            return parent.e();
        }

        public string[] f() {
            return parent.f();
        }

        public string[] g() {
            var result = parent.g();
            if (type.Equals("p")) {
                result[0] = "xx";
                result[1] = "xx";
        }
            return result;
        }

        public string[] h() {
            var result = parent.h();
            if (type.Equals("p")) {
                result[0] = "xx";
                result[1] = "xx";
        }
            return result;
        }

        public string[] i() {
            var result = parent.i();
            if (type.Equals("p")) {
                result[0] = "xx";
                result[1] = "xx";
        }
            return result;
        }

        public string[] j() {
            return parent.j();
        }
    }

    //TODO
    public class HTSNote {
        public int startMs = 0;
        public int endMs = 0;
        public int positionTicks;
        public int durationTicks = 0;
        public int index = 0;//index of this note in sentence
        public int indexBackwards = 0;
        public int sentenceDurMs = 0;
        public int sentenceDurTicks = 0;
        public double startMsPercent = 0;

        //TimeSignatures
        public int beatPerBar = 0;
        public int beatUnit = 0;

        public int positionBar = 0; //bar number in the sentence, starting from 0
        public int positionBeat = 0; //unit number in the bar, starting from 0

        public double key = 0;
        public double bpm = 0;
        public int tone = 0;
        public bool isSlur = false;
        public bool isRest = true;
        public string[] symbols;
        public string lang = string.Empty;
        public string langDependent = "xx";
        public string accent = string.Empty;

        public HTSNote? prev;
        public HTSNote? next;
        public HTSPhrase parent;

        public HTSNote(string[] symbols, int beatPerBar, int beatUnit, int positionBar, int positionBeat, int key, double bpm, int tone, bool isSlur, bool isRest, string lang, string accent, int startms, int endms, int positionTicks, int durationTicks) {
            this.startMs = startms;
            this.endMs = endms;
            this.beatPerBar = beatPerBar;
            this.beatUnit = beatUnit;
            this.positionBar = positionBar;
            this.positionBeat = positionBeat;
            this.key = key;
            this.bpm = bpm;
            this.tone = tone;
            this.isSlur = isSlur;
            this.isRest = isRest;
            this.lang = lang;
            this.accent = accent;
            this.symbols = symbols;
            this.positionTicks = positionTicks;
            this.durationTicks = durationTicks;
        }

        public int durationMs {
            get { return endMs - startMs; }
        }

        private int startMsBackwards {
            get { return sentenceDurMs - startMs; }
        }

        private int positionTickBackwards {
            get { return sentenceDurTicks - positionTicks; }
        }


        public int? measureIndexForward;
        public int? measureMsForward;
        public int? measureTickForward;
        public int? measurePercentForward;
        public int? measureIndexBackward;
        public int? measureMsBackward;
        public int? measureTickBackward;
        public int? measurePercentBackward;

        public int? accentIndexForward;
        public int? accentMsForward;
        public int? accentTickForward;
        public int? accentIndexBackward;
        public int? accentMsBackward;
        public int? accentTickBackward;

        public string[] b() {
            return new string[] {
                symbols.Length.ToString(),
                "1",
                "1",
                lang != string.Empty ? lang : "xx",
                langDependent,
            };
        }

        public string[] a() {
            if (prev == null) {
                return Enumerable.Repeat("xx", 5).ToArray();
            } else {
                return prev.b();
            }
        }

        public string[] c() {
            if (next == null) {
                return Enumerable.Repeat("xx", 5).ToArray();
            } else {
                return next.b();
            }
        }

        public string[] d() {
            if (prev == null) {
                return Enumerable.Repeat("xx", 60).ToArray();
            } else {
                return prev.e();
            }
        }

        public string[] e() {
            var result = Enumerable.Repeat("xx", 60).ToArray();
            result[0] = HTS.GetToneName(tone);
            result[1] = HTS.GetOctaveNum(tone);
            result[2] = key.ToString();
            result[3] = $"{beatPerBar}/{beatUnit}";
            result[4] = bpm.ToString();
            result[5] = "1";

            int lengthCs = Math.Max(0, (int)Math.Round(durationMs / 10.0));
            int ticksPer96th = (parent != null && parent.resolution > 0) ? parent.resolution / 24 : 0;
            int length96 = (ticksPer96th > 0) ? (int)Math.Round((double)durationTicks / ticksPer96th) : 0;
            result[6] = lengthCs.ToString();
            result[7] = length96.ToString();

            result[9]  = measureIndexForward != null ? measureIndexForward.ToString() : "xx";   // e10
            result[10] = measureIndexBackward != null ? measureIndexBackward.ToString() : "xx"; // e11
            result[11] = measureMsForward != null ? measureMsForward.ToString() : "xx";         // e12 (centisecond already)
            result[12] = measureMsBackward != null ? measureMsBackward.ToString() : "xx";       // e13
            result[13] = measureTickForward != null ? measureTickForward.ToString() : "xx";     // e14 (96th already)
            result[14] = measureTickBackward != null ? measureTickBackward.ToString() : "xx";   // e15
            result[15] = measurePercentForward != null ? measurePercentForward.ToString() : "xx"; // e16
            result[16] = measurePercentBackward != null ? measurePercentBackward.ToString() : "xx"; // e17

            result[17] = index <= 0 ? "xx" : index.ToString();
            result[18] = indexBackwards <= 0 ? "xx" : indexBackwards.ToString();
            result[19] = ((startMs + 50) / 100).ToString(); // 100ms単位
            result[20] = ((startMsBackwards + 50) / 100).ToString();

            // e22/e23: phrase-level position by 96th note, resolution independent
            if (ticksPer96th > 0 && parent != null && parent.notes != null && index > 0) {
                int forwardTicks = 0;
                int idx = Math.Min(index - 1, parent.notes.Length - 1);
                for (int i = 0; i < idx; i++) {
                    forwardTicks += parent.notes[i].durationTicks;
                }
                int backwardTicks = Math.Max(0, sentenceDurTicks - forwardTicks);
                result[21] = ((forwardTicks + ticksPer96th / 2) / ticksPer96th).ToString();
                result[22] = ((backwardTicks + ticksPer96th / 2) / ticksPer96th).ToString();
            } else {
                result[21] = "xx";
                result[22] = "xx";
            }

            if (sentenceDurMs > 0) {
                result[23] = ((startMs * 100) / sentenceDurMs).ToString();
                result[24] = (100 - ((startMs * 100) / sentenceDurMs)).ToString();
            } else {
                result[23] = "xx";
                result[24] = "xx";
            }

            if (prev == null) {
                result[25] = "0";
            } else if (!prev.isRest) {
                result[25] = prev.isSlur ? "1" : "0";
            }
            if (next == null) {
                result[26] = "0";
            } else if (!next.isRest) {
                result[26] = next.isSlur ? "1" : "0";
            }
            result[27] = "n";
            result[28] = accentIndexBackward.HasValue ? accentIndexBackward.Value.ToString() : "xx";
            result[29] = accentIndexForward.HasValue ? accentIndexForward.Value.ToString() : "xx";
            result[30] = accentMsBackward.HasValue ? ((int)Math.Round(accentMsBackward.Value / 10.0)).ToString() : "xx";
            result[31] = accentMsForward.HasValue ? ((int)Math.Round(accentMsForward.Value / 10.0)).ToString() : "xx";
            result[32] = (accentTickBackward.HasValue && ticksPer96th > 0) ? ((int)Math.Round((double)accentTickBackward.Value / ticksPer96th)).ToString() : "xx";
            result[33] = (accentTickForward.HasValue && ticksPer96th > 0) ? ((int)Math.Round((double)accentTickForward.Value / ticksPer96th)).ToString() : "xx";

            if (this.tone > 0) {
                result[56] = (prev == null || prev.tone <= 0) ? "p0" : HTS.WriteInt(prev.tone - tone);
                result[57] = (next == null || next.tone <= 0) ? "p0" : HTS.WriteInt(next.tone - tone);
            } else {
                result[56] = "p0";
                result[57] = "p0";
            }
            return result;
        }

        public string[] f() {
            if (next == null) {
                return Enumerable.Repeat("xx", 60).ToArray();
            } else {
                return next.e();
            }
        }

        public string[] g() {
            return parent.g();
        }

        public string[] h() {
            return parent.h();
        }

        public string[] i() {
            return parent.i();
        }

        public string[] j() {
            return parent.j();
        }
    }

    public class HTSPhrase {
        public int resolution = 480;
        public int totalPhrases;
        public int totalNotes;
        public int totalPhonemes;

        public HTSPhrase? prev;
        public HTSPhrase? next;
        public HTSNote[] notes;

        public HTSPhrase(HTSNote[] notes) {
            this.notes = notes;

            // アクセント（forward）
            int accentIndexForwardSum = 0;
            int accentMsForwardSum = 0;
            int accentTickForwardSum = 0;
            for (int i = 0; i < notes.Length; i++) {
                var note = notes[i];
                if (note.isRest) {
                    accentIndexForwardSum = 0;
                    accentMsForwardSum = 0;
                    accentTickForwardSum = 0;
                } else if (!string.IsNullOrEmpty(note.accent)) {
                    accentIndexForwardSum = 0;
                    note.accentIndexForward = accentIndexForwardSum;
                    accentIndexForwardSum += 1;

                    accentMsForwardSum = 0;
                    note.accentMsForward = accentMsForwardSum;
                    accentMsForwardSum += note.durationMs;

                    accentTickForwardSum = 0;
                    note.accentTickForward = accentTickForwardSum;
                    accentTickForwardSum += note.durationTicks; // ticks で累積
                } else {
                    if (accentIndexForwardSum != 0) {
                        note.accentIndexForward = accentIndexForwardSum;
                        accentIndexForwardSum += 1;
                    }
                    if (accentMsForwardSum != 0) {
                        note.accentMsForward = accentMsForwardSum;
                        accentMsForwardSum += note.durationMs;
                    }
                    if (accentTickForwardSum != 0) {
                        note.accentTickForward = accentTickForwardSum;
                        accentTickForwardSum += note.durationTicks; // ticks で累積
                    }
                }
            }

            // アクセント（backward）
            int accentIndexBackwardSum = 0;
            int accentMsBackwardSum = 0;
            int accentTickBackwardSum = 0;
            int lastAccentIndexContribution = 0;
            int lastAccentMs = 0;
            int lastAccentTicks = 0;
            for (int i = notes.Length - 1; i >= 0; i--) {
                var note = notes[i];
                if (note.isRest) {
                    accentIndexBackwardSum = 0;
                    accentMsBackwardSum = 0;
                    accentTickBackwardSum = 0;
                    lastAccentIndexContribution = 0;
                    lastAccentMs = 0;
                    lastAccentTicks = 0;
                } else if (!string.IsNullOrEmpty(note.accent)) {
                    note.accentIndexBackward = Math.Max(0, accentIndexBackwardSum - lastAccentIndexContribution);
                    note.accentMsBackward = Math.Max(0, accentMsBackwardSum - lastAccentMs);
                    note.accentTickBackward = Math.Max(0, accentTickBackwardSum - lastAccentTicks);

                    lastAccentIndexContribution = 1;
                    lastAccentMs = note.durationMs;
                    lastAccentTicks = note.durationTicks;

                    accentIndexBackwardSum = 1;
                    accentMsBackwardSum = note.durationMs;
                    accentTickBackwardSum = note.durationTicks;
                } else {
                    note.accentIndexBackward = accentIndexBackwardSum;
                    note.accentMsBackward = accentMsBackwardSum;
                    note.accentTickBackward = accentTickBackwardSum;

                    accentIndexBackwardSum += 1;
                    accentMsBackwardSum += note.durationMs;
                    accentTickBackwardSum += note.durationTicks;
                }
            }

            // 小節ごとのグルーピング（positionBar 基準）
            var groups = notes
                .GroupBy(n => n.positionBar)
                .OrderBy(g => g.Key)
                .Select(g => g.OrderBy(n => n.positionTicks).ToList())
                .ToList();

            int ticksPer96th = (resolution > 0) ? (resolution / 24) : 0;

            foreach (var group in groups) {
                int totalDurationMs = group.Sum(n => n.durationMs);
                int totalDurationTicks = group.Sum(n => n.durationTicks);

                // forward（小節先頭からの位置）
                int idxF = 1;
                int accMsF = 0;
                int accTicksF = 0;
                foreach (var note in group) {
                    note.measureIndexForward = idxF;
                    note.measureMsForward = (int)Math.Round(accMsF / 100.0);
                    note.measureTickForward = ticksPer96th > 0 ? (int)Math.Round((double)accTicksF / ticksPer96th / 10) : 0;
                    note.measurePercentForward = totalDurationMs > 0 ? (accMsF * 100) / totalDurationMs : 0;

                    idxF += 1;
                    accMsF += note.durationMs;
                    accTicksF += note.durationTicks;
                }

                // backward
                int idxB = 1;
                int accMsB = 0;
                int accTicksB = 0;
                for (int i = group.Count - 1; i >= 0; --i) {
                    var note = group[i];
                    note.measureIndexBackward = idxB;
                    note.measureMsBackward = (int)Math.Round(accMsB / 100.0);
                    note.measureTickBackward = ticksPer96th > 0 ? (int)Math.Round((double)accTicksB / ticksPer96th / 10) : 0;
                    note.measurePercentBackward = totalDurationMs > 0 ? (accMsB * 100) / totalDurationMs : 0;

                    idxB += 1;
                    accMsB += note.durationMs;
                    accTicksB += note.durationTicks;
                }
            }
        }
        private int barCount {
            get { return notes[^1].positionBar - notes[0].positionBar + 1; }
        }

        public string[] g() {
            var result = Enumerable.Repeat("xx", 2).ToArray();
            if (prev == null) {
                return result;
            } else {
                return prev.h();
            }
        }

        public string[] h() {
            var result = Enumerable.Repeat("xx", 2).ToArray();
            result[0] = notes.Length.ToString();
            result[1] = notes.Select(note => note.symbols.Length).Sum().ToString();
            return result;
        }

        public string[] i() {
            var result = Enumerable.Repeat("xx", 2).ToArray();
            if (next == null) {
                return result;
            } else {
                return next.h();
            }
        }

        public string[] j() {
            var result = Enumerable.Repeat("xx", 3).ToArray();
            result[0] = (barCount > 0 ? (totalNotes / barCount).ToString() : "xx");
            result[1] = (barCount > 0 ? (totalPhonemes / barCount).ToString() : "xx");
            result[2] = totalPhrases.ToString();
            return result;
        }

    }
}
