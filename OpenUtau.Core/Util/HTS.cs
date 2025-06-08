using System;
using System.Collections.Generic;
using System.Linq;

//This file implement utaupy.hts python library's function
//https://github.com/oatsu-gh/utaupy/blob/master/utaupy/hts.py

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

        //write integer with "p" as positive and "n" as negative. 0 is "p0"
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

            string result =
                $"{parent.startMs * 100000} {parent.endMs * 100000} "
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
            if (!type.Equals("p")) {
                result[0] = "xx";
                result[1] = "xx";
            }
            return result;
        }

        public string[] h() {
            var result = parent.h();
            if (!type.Equals("p")) {
                result[0] = "xx";
                result[1] = "xx";
            }
            return result;
        }

        public string[] i() {
            var result = parent.i();
            if (!type.Equals("p")) {
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
            result[3] = $"{beatPerBar}/{beatUnit}";//beat
            result[4] = bpm.ToString();//tempo
            result[5] = "1";//number_of_syllables
            result[6] = ((durationMs + 5) / 10).ToString();//duration in 10ms
            result[7] = ((durationTicks + 10) / 20).ToString(); //length in 96th note, or 20 ticks

            result[9] = measureIndexForward != null ? measureIndexForward.ToString() : "xx";
            result[10] = measureIndexBackward != null ? measureIndexBackward.ToString() : "xx";
            result[11] = measureMsForward != null ? measureMsForward.ToString() : "xx";
            result[12] = measureMsBackward != null ? measureMsBackward.ToString() : "xx";
            result[13] = measureTickForward != null ? measureTickForward.ToString() : "xx";
            result[14] = measureTickBackward != null ? measureTickBackward.ToString() : "xx";
            result[15] = measurePercentForward != null ? measurePercentForward.ToString() : "xx";
            result[16] = measurePercentBackward != null ? measurePercentBackward.ToString() : "xx";

            result[17] = index <= 0 ? "xx" : index.ToString();//index of note in sentence
            result[18] = indexBackwards <= 0 ? "xx" : indexBackwards.ToString();
            result[19] = ((startMs + 50) / 100).ToString();//position in 100ms
            result[20] = ((startMsBackwards + 50) / 100).ToString();
            result[21] = ((positionTicks + 10) / 20).ToString();
            result[22] = ((positionTickBackwards + 10) / 20).ToString();
            result[23] = ((startMs / sentenceDurMs) * 100).ToString();
            result[24] = (100 - ((startMs / sentenceDurMs) * 100)).ToString();
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
            result[28] = accentIndexForward != null && !string.IsNullOrEmpty(accent) ? accentIndexForward.ToString() : "xx";
            result[29] = accentIndexBackward != null && !string.IsNullOrEmpty(accent) ? accentIndexForward.ToString() : "xx";
            result[30] = accentMsForward != null && !string.IsNullOrEmpty(accent) ? accentMsForward.ToString() : "xx";
            result[31] = accentMsBackward != null && !string.IsNullOrEmpty(accent) ? accentMsBackward.ToString() : "xx";
            result[32] = accentTickForward != null && !string.IsNullOrEmpty(accent) ? accentTickForward.ToString() : "xx";
            result[33] = accentTickBackward != null && !string.IsNullOrEmpty(accent) ? accentTickBackward.ToString() : "xx";
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
            //int forwardDurTickSum = 0;
            //int forwardDurMsSum = 0;
            int accentIndexForwardSum = 0;
            int accentMsForwardSum = 0;
            int accentTickForwardSum = 0;
            int tempBarForward = 0;
            int tempBeatForward = 0;
            int tempPositionBarForward = 0;
            List<List<HTSNote>> noteGroupsForward = new List<List<HTSNote>>();
            List<HTSNote> currentGroupForward = new List<HTSNote>();
            for (int i = 0; i < notes.Length; i++) {
                HTSNote note = notes[i];

                //if (note.isRest) {
                //    result[21] = "xx";
                //    forwardDurTickSum = 0;
                //} else if (note.index == 1) {
                //    result[21] = "0";
                //    forwardDurTickSum += note.durationTicks;
                //} else {
                //    result[21] = forwardDurTickSum.ToString();
                //    forwardDurTickSum += note.durationTicks;
                //}
                //if (note.isRest) {
                //    forwardDurMsSum = 0;
                //} else if (note.index == 1) {
                //    forwardDurMsSum = 0;
                //    note.startMsPercent = ((forwardDurMsSum / note.sentenceDurMs) * 100);
                //    result[23] = note.startMsPercent.ToString();
                //    forwardDurMsSum += note.durationMs;
                //} else {
                //    note.startMsPercent = ((forwardDurMsSum / note.sentenceDurMs) * 100);
                //    result[23] = note.startMsPercent.ToString();
                //    forwardDurMsSum += note.durationMs;
                //}

                if (note.isRest) {
                    accentIndexForwardSum = 0;
                } else if (!string.IsNullOrEmpty(note.accent)) {
                    accentIndexForwardSum = 0;
                    note.accentIndexForward = accentIndexForwardSum;
                    accentIndexForwardSum += 1;
                } else if (accentIndexForwardSum != 0) {
                    note.accentIndexForward = accentIndexForwardSum;
                    accentIndexForwardSum += 1;
                }
                if (note.isRest) {
                    accentMsForwardSum = 0;
                } else if (!string.IsNullOrEmpty(note.accent)) {
                    accentMsForwardSum = 0;
                    note.accentMsForward = accentMsForwardSum;
                    accentMsForwardSum += note.durationMs;
                } else if (accentMsForwardSum != 0) {
                    note.accentMsForward = accentMsForwardSum;
                    accentMsForwardSum += note.durationMs;
                }
                if (note.isRest) {
                    accentTickForwardSum = 0;
                } else if (!string.IsNullOrEmpty(note.accent)) {
                    accentTickForwardSum = 0;
                    note.accentTickForward = accentTickForwardSum;
                    accentTickForwardSum += note.durationMs;
                } else if (accentTickForwardSum != 0) {
                    note.accentTickForward = accentTickForwardSum;
                    accentTickForwardSum += note.durationMs;
                }
                if (note.beatPerBar != tempBarForward || note.beatPerBar != tempBarForward || note.beatUnit != tempBeatForward) {
                    tempBarForward = note.beatPerBar;
                    tempBeatForward = note.beatUnit;
                    tempPositionBarForward = note.positionBar;
                    if (currentGroupForward.Count > 0) {
                        noteGroupsForward.Add(currentGroupForward);
                    }
                    currentGroupForward = new List<HTSNote>();
                }
                if (note.positionBar == tempBarForward) {
                    currentGroupForward.Add(note);
                }
            }
            int indexForwardSum = 0;
            int msForwardsSum = 0;
            int tickForwardSum = 0;
            foreach (var noteGs in noteGroupsForward) {
                int totalDurationMs = noteGs.Sum(note => note.durationMs);
                indexForwardSum = 0;
                msForwardsSum = 0;
                tickForwardSum = 0;
                foreach (var note in noteGs) {
                    note.measureIndexForward = indexForwardSum;
                    indexForwardSum += 1;
                    note.measureMsForward = msForwardsSum;
                    msForwardsSum += note.durationMs;
                    note.measureTickForward = tickForwardSum;
                    tickForwardSum += note.durationTicks;
                    note.measurePercentForward = (note.startMs / totalDurationMs) * 100;
                }
            }
            //int backwardDurTickSum = 0;
            int accentIndexBackwardSum = 0;
            int accentMsBackwardSum = 0;
            int accentTickBackwardSum = 0;
            int tempBarBackward = 0;
            int tempBeatBackward = 0;
            int tempPositionBarBackward = 0;
            List<List<HTSNote>> noteGroupsBackward = new List<List<HTSNote>>();
            List<HTSNote> currentGroupBackward = new List<HTSNote>();
            for (int i = notes.Length - 1; i >= 0; i--) {
                HTSNote note = notes[i];
                //if (note.isRest) {
                //    backwardDurTickSum = 0;
                //} else if (note.indexBackwards == 1) {
                //    backwardDurTickSum = ((note.durationTicks + 10) / 20);
                //    result[22] = backwardDurTickSum.ToString();
                //} else {
                //    backwardDurTickSum += ((note.durationTicks + 10) / 20);
                //    result[22] = backwardDurTickSum.ToString();
                //}

                //if (note.isRest) {
                //    forwardDurMsSum = 0;
                //} else {
                //    result[24] = (100 - note.startMsPercent).ToString();
                //}

                if (note.isRest) {
                    accentIndexBackwardSum = 0;
                } else if (note.indexBackwards == 1) {
                    accentIndexBackwardSum = 0;
                    note.accentIndexForward = accentIndexBackwardSum;
                    accentIndexBackwardSum += 1;
                } else {
                    note.accentIndexForward = accentIndexBackwardSum;
                    accentIndexBackwardSum += 1;
                }
                if (note.isRest) {
                    accentMsBackwardSum = 0;
                } else if (!string.IsNullOrEmpty(note.accent)) {
                    accentMsBackwardSum = 0;
                    note.accentMsBackward = accentMsBackwardSum;
                    accentMsBackwardSum += note.durationMs;
                } else if (accentMsBackwardSum != 0) {
                    note.accentMsBackward = accentMsBackwardSum;
                    accentMsBackwardSum += note.durationMs;
                }
                if (note.isRest) {
                    accentTickBackwardSum = 0;
                } else if (!string.IsNullOrEmpty(note.accent)) {
                    accentTickBackwardSum = 0;
                    note.accentTickBackward = accentTickBackwardSum;
                    accentTickBackwardSum += note.durationMs;
                } else if (accentTickBackwardSum != 0) {
                    note.accentTickBackward = accentTickBackwardSum;
                    accentTickBackwardSum += note.durationMs;
                }
                if (note.beatPerBar != tempBarBackward || note.beatPerBar != tempBarBackward || note.beatUnit != tempBeatBackward) {
                    tempBarBackward = note.beatPerBar;
                    tempBeatBackward = note.beatUnit;
                    tempPositionBarBackward = note.positionBar;
                    if (currentGroupBackward.Count > 0) {
                        noteGroupsBackward.Add(currentGroupBackward);
                    }
                    currentGroupBackward = new List<HTSNote>();
                }
                if (note.positionBar == tempBarBackward) {
                    currentGroupBackward.Add(note);
                }
            }
            int indexBackwardSum = 0;
            int msBackwardsSum = 0;
            int tickBackwardSum = 0;
            foreach (var noteGs in noteGroupsForward) {
                int totalDurationMs = noteGs.Sum(note => note.durationMs);
                indexBackwardSum = 0;
                msBackwardsSum = 0;
                tickBackwardSum = 0;
                foreach (var note in noteGs) {
                    note.measureIndexBackward = indexBackwardSum;
                    indexBackwardSum += 1;
                    note.measureMsBackward = msBackwardsSum;
                    msBackwardsSum += note.durationMs;
                    note.measureTickBackward = tickBackwardSum;
                    tickBackwardSum += note.durationTicks;
                    note.measurePercentBackward = (note.startMs / totalDurationMs) * 100;
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
