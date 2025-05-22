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
        public string flags = "xx";
        public bool isRest = true;

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
        public HTSPhoneme(string phoneme, string flags, HTSNote note) {
            this.symbol = phoneme;
            this.flags = flags;
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
            result[6] = (beforePrev == null) ? "xx" : beforePrev.flags;
            result[7] = (prev == null) ? "xx" : prev.flags;
            result[8] = flags;
            result[9] = (next == null) ? "xx" : next.flags;
            result[10] = (afterNext == null) ? "xx" : afterNext.flags;
            result[11] = position.ToString();
            result[12] = position_backward.ToString();
            result[13] = distance_from_previous_vowel < 0 ? "xx" : distance_from_previous_vowel.ToString();
            result[14] = distance_to_next_vowel < 0 ? "xx" : distance_to_next_vowel.ToString();
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

    //TODO
    public class HTSNote {
        public int startMs = 0;
        public int endMs = 0;
        public int positionTicks;
        public int durationTicks = 0;
        public int index = 0;//index of this note in sentence
        public int indexBackwards = 0;
        public int sentenceDurMs = 0;

        //TimeSignatures
        public int beatPerBar = 0;
        public int beatUnit = 0;

        public double key = 0;
        public double bpm = 0;
        public int tone = 0;
        public bool isSlur = false;
        public bool isRest = true;
        public string[] symbols;
        public string lang;
        public string accent;

        public HTSNote? prev;
        public HTSNote? next;
        public HTSPhrase parent;

        public HTSNote(string[] symbols, int beatPerBar, int beatUnit, int key, double bpm, int tone, bool isSlur, bool isRest, string lang, string accent, int startms, int endms, int positionTicks, int durationTicks) {
            this.startMs = startms;
            this.endMs = endms;
            this.beatPerBar = beatPerBar;
            this.beatUnit = beatUnit;
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

        public int startMsBackwards {
            get { return sentenceDurMs - startMs; }
        }

        public string[] b() {
            return new string[] {
                symbols.Length.ToString(),
                "1",
                "1",
                lang != string.Empty ? lang : "xx",
                "0"//"xx"
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

        public string[] e() {
            var result = parent.e();
            result[0] = HTS.GetToneName(tone);
            result[1] = HTS.GetOctaveNum(tone);
            result[2] = key.ToString();
            result[3] = $"{beatPerBar}/{beatUnit}";//beat
            result[4] = bpm.ToString();//tempo
            result[5] = "1";//number_of_syllables
            result[6] = ((durationMs + 5) / 10).ToString();//duration in 10ms
            result[7] = ((durationTicks + 10) / 20).ToString(); //length in 96th note, or 20 ticks
            result[17] = index <= 0 ? "xx" : index.ToString();//index of note in sentence
            result[18] = indexBackwards <= 0 ? "xx" : indexBackwards.ToString();
            result[19] = ((startMs + 50) / 100).ToString();//position in 100ms
            result[20] = ((startMsBackwards + 50) / 100).ToString();
            if (prev == null) {
                result[26] = "0";
            } else {
                result[26] = prev.isSlur ? "1" : "0";
            }
            if (next == null) {
                result[26] = "0";
            } else {
                result[26] = next.isSlur ? "1" : "0";
            }
            result[28] = "n";
            if (this.tone > 0) {
                result[56] = (prev == null || prev.tone <= 0) ? "p0" : HTS.WriteInt(prev.tone - tone);
                result[57] = (next == null || next.tone <= 0) ? "p0" : HTS.WriteInt(next.tone - tone);
            } else {
                result[56] = "p0";
                result[57] = "p0";
            }
            return result;
        }

        public string[] d() {
            if (prev == null) {
                return Enumerable.Repeat("xx", 60).ToArray();
            } else {
                return prev.e();
            }
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
        public HTSPhrase[]? phrases;
        public HTSPhrase? prev;
        public HTSPhrase? next;
        public HTSNote[] notes;

        public HTSPhrase(HTSNote[] notes) {
            this.notes = notes;
        }

        public static (int measure, string beat) MeasureLength(string flag, int currentMeasure) {
            int measure;
            string beat;

            switch (flag) {
                case "4/4":
                    beat = "4/4";
                    measure = 1920;
                    break;
                case "3/4":
                    beat = "3/4";
                    measure = 1440;
                    break;
                case "2/4":
                    beat = "2/4";
                    measure = 960;
                    break;
                case "2/2":
                    beat = "2/2";
                    measure = 1920;
                    break;
                case "6/8":
                    beat = "6/8";
                    measure = 1440;
                    break;
                default:
                    beat = null;  // ここでbeatを初期化する必要があります
                    measure = currentMeasure;
                    break;
            }

            return (measure, beat);
        }

        public string[] e() {
            var result = Enumerable.Repeat("xx", 60).ToArray();
            int measure = 1920;
            string beat = "4/4"; // 仮の値
            int lastMeasure = 0;
            int tickSum = 0;
            int forwardDurTickSum = 0;
            int backwardDurTickSum = 0;
            List<string> startEndCheck = new List<string>();
            List<int> noteCountArray = new List<int>();
            List<double> timeLength = new List<double>();
            List<int> tickInMeasure = new List<int>();
            for (int i = 0; i < notes.Length; i++) {
                HTSNote note = notes[i];
                lastMeasure = measure;
                (measure, beat) = MeasureLength($"{note.beatPerBar}/{note.beatUnit}", measure);
                int overMeasure = tickSum / measure;
                tickSum -= measure * overMeasure;
                if (overMeasure != 0) {
                    startEndCheck.Add("start");
                    if (i - 1 >= 0) {
                        startEndCheck[i - 1] = "end"; 
                    }
                } else {
                    startEndCheck.Add("middle");
                }
                int noteCount;
                if (i == 0 || startEndCheck[i] == "start") {
                    noteCount = 0;
                } else {
                    noteCount = noteCountArray[i - 1] + 1;
                } noteCountArray.Add(noteCount);
                int currentTick = tickSum % measure; timeLength.Add((60000.0 / note.bpm) * (note.durationTicks / 480.0));
                tickInMeasure.Add(currentTick);
                tickSum += note.durationTicks;

                if (note.isRest) {
                    result[21] = "xx";
                    forwardDurTickSum = 0;
                } else if (note.index == 1) {
                    result[21] = "0";
                    forwardDurTickSum += note.durationTicks;
                } else {
                    result[21] = forwardDurTickSum.ToString();
                    forwardDurTickSum += note.durationTicks;
                }
            }
            for (int i = notes.Length-1; i >= 0 ; i--) {
                HTSNote note = notes[i];
                if (note.isRest) {
                    result[21] = "xx";
                    backwardDurTickSum = 0;
                } else if (note.index == 1) {
                    backwardDurTickSum = note.durationTicks;
                    result[21] = backwardDurTickSum.ToString();
                } else {
                    backwardDurTickSum += note.durationTicks;
                    result[21] = backwardDurTickSum.ToString();
                }
            }
            result[8] = "1";//number_of_syllables
            result[9] = "1";//number_of_syllables
            result[10] = "1";//number_of_syllables
            result[11] = "1";//number_of_syllables
            result[12] = "1";//number_of_syllables
            result[13] = "1";//number_of_syllables
            result[14] = "1";//number_of_syllables
            result[15] = "1";//number_of_syllables
            result[16] = "1";//number_of_syllables


            result[29] = "1";//number_of_syllables
            result[30] = "1";//number_of_syllables
            result[31] = "1";//number_of_syllables
            result[32] = "1";//number_of_syllables
            result[33] = "1";//number_of_syllables
            result[34] = "1";//number_of_syllables
            result[30] = "1";//number_of_syllables
            result[30] = "1";//number_of_syllables
            result[30] = "1";//number_of_syllables
            return result;
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
            if (phrases != null) {
                int firstBar = phrases[0].notes[0].beatPerBar;
                int lastBar = phrases[^1].notes[^1].beatPerBar;
                int barCount = lastBar - firstBar + 1;

                int totalNotes = phrases.Sum(phrase => phrase.notes.Length);
                int totalSymbols = phrases.Sum(phrase => phrase.notes.Sum(note => note.symbols.Length));

                result[0] = (barCount > 0 ? (totalNotes / barCount).ToString() : "xx");
                result[1] = (barCount > 0 ? (totalSymbols / barCount).ToString() : "xx");
                result[2] = phrases.Length.ToString();
            }
            return result;
        }

    }
}
