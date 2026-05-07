using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using K4os.Hash.xxHash;
using NAudio.Wave;
using OpenUtau.Api;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using SharpCompress;
using ThirdParty;

namespace OpenUtau.Core.Neutrino {
    public class NeutrinoRenderer : IRenderer {
        const string NTYP = "ntyp";
        const string NMOD = "nmod";
        const string SMOC = "smoc";

        enum NeutrinoRenderType {
            WORLD,
            NSF,
        }

        enum NeutrinoRenderMode {
            Elements = 2,
            Standard = 3,
            Advanced = 4,
        }

        enum NsfModel {
            va,
            vs,
            ve,
        }

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.SHFT,
            Format.Ustx.GENC,
            Format.Ustx.TENC,
            Format.Ustx.BREC,
            Format.Ustx.VOIC,
            Format.Ustx.DIR,
            NTYP,
            NMOD,
            SMOC
        };

        static readonly object lockObj = new object();

        public USingerType SingerType => USingerType.Neutrino;

        public bool SupportsRenderPitch => true;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        private TimeAxis timeAxis;

        protected NeutrinoSinger singer;
        string NeutrinoExe = string.Empty;
        string Neutrino_ClientExe = string.Empty;
        string NeutrinoServerExe = string.Empty;
        string NsfExe = string.Empty;
        string WorldExe = string.Empty;
        string Vocoder_ClientExe = string.Empty;
        int sampleRate = 48000;
        //information used by HTS writer
        protected Dictionary<string, string[]> phoneDict = new Dictionary<string, string[]>();
        protected List<string> vowels = new List<string>();
        protected List<string> consonants = new List<string>();
        protected List<string> breaks = new List<string>();
        protected List<string> pauses = new List<string>();
        protected List<string> silences = new List<string>();
        protected List<string> unvoiced = new List<string>();
        protected List<string> macronLyrics = new List<string>();
        protected int startTick;
        protected int endTick;
        protected UTimeSignature sigStart;
        protected double bpmStart;
        protected double headMs;
        protected int barLenTicksStart;
        protected UTimeSignature sigEnd;
        protected double bpmEnd;
        protected double tailMs;
        protected int barLenTicksEnd;
        protected string lang = "";
        protected int key = 0;
        protected int resolution = 480;
        protected int framePeriod = 5;

        //information used by openutau phonemizer
        protected IG2p g2p;
        //result caching
        private Dictionary<int, List<Tuple<string, int>>> partResult = new Dictionary<int, List<Tuple<string, int>>>();
        protected string tablePath = string.Empty;
        protected string monoScorePath = string.Empty;
        protected string fullScorePath = string.Empty;
        protected string monoTimingPath = string.Empty;
        protected string fullTimingPath = string.Empty;

        public void setUp() {
            lang = "JPN";//TODO: use singer.language
            string confPath = "japanese.utf_8.conf";
            tablePath = "japanese.utf_8.table";
            string basePath = Path.Join(PathManager.Inst.DependencyPath, "NEUTRINO");
            //Load Dictionary
            try {
                phoneDict.Clear();
                LoadDict(Path.Join(Path.Join(basePath, @".\settings\dic"), confPath), singer.TextFileEncoding);
                LoadDict(Path.Join(Path.Join(basePath, @".\settings\dic"), tablePath), singer.TextFileEncoding);
                // Lyrics often handled in OpenUtau
                phoneDict.Add("R", new string[] { "pau" });
                phoneDict.Add("-", new string[] { "pau" });
                phoneDict.Add("SP", new string[] { "pau" });
                phoneDict.Add("AP", new string[] { "br" });
                g2p = this.LoadG2p();
            } catch (Exception e) {
                Log.Error(e, $"failed to load dictionary from {tablePath}");
                return;
            }
            LoadG2p();
            if (OS.IsWindows()) {
                NeutrinoExe = Path.Join(basePath, @".\bin", "NEUTRINO.exe");
                Neutrino_ClientExe = Path.Join(basePath, @".\bin", "neutrino_client.exe");
                NeutrinoServerExe = Path.Join(basePath, @".\bin", "neutrino_server.exe");
                NsfExe = Path.Join(basePath, @".\bin", "NSF.exe");
                WorldExe = Path.Join(basePath, @".\bin", "WORLD.exe");
                Vocoder_ClientExe = Path.Join(basePath, @".\bin", "vocoder_client.exe");
            } else if (OS.IsMacOS() || OS.IsLinux()) {
                NeutrinoExe = Path.Join(basePath, @".\bin", "NEUTRINO");
                NsfExe = Path.Join(basePath, @".\bin", "NSF");
                WorldExe = Path.Join(basePath, @".\bin", "WORLD");
            } else {
                throw new NotSupportedException("Platform not supported.");
            }
            NeutrinoServerLauncher.EnsureStarted(NeutrinoServerExe);
            NeutrinoServerLauncher.EnsureStarted(Vocoder_ClientExe);
        }

        public void LoadDict(string path, Encoding encoding) {
            if (path.EndsWith(".conf")) {
                LoadConf(path, encoding);
            } else {
                LoadTable(path, encoding);
            }
        }

        public void LoadTable(string path, Encoding encoding) {
            var lines = File.ReadLines(path, encoding);
            foreach (var line in lines) {
                var lineSplit = line.Split();
                phoneDict[lineSplit[0]] = lineSplit[1..];
            }
        }

        public void LoadConf(string path, Encoding encoding) {
            phoneDict["SILENCES"] = new string[] { "sil" };
            phoneDict["PAUSES"] = new string[] { "pau" };
            phoneDict["BREAK"] = new string[] { "br" };
            var lines = File.ReadLines(path, encoding);
            foreach (var line in lines) {
                if (line.Contains('=')) {
                    var lineSplit = line.Split("=");
                    var key = lineSplit[0];
                    var value = lineSplit[1];
                    var phonemes = value.Trim(new char[] { '\"' }).Split(",");
                    phoneDict[key] = phonemes;
                }
            }
        }
        protected IG2p LoadG2p() {
            var g2ps = new List<IG2p>();
            var builder = G2pDictionary.NewBuilder();
            vowels.AddRange(phoneDict["VOWELS"]);
            breaks.AddRange(phoneDict["BREAK"]);
            pauses.AddRange(phoneDict["PAUSES"]);
            silences.AddRange(phoneDict["SILENCES"]);
            consonants.AddRange(phoneDict["PHONEME_CL"]);
            macronLyrics.AddRange(phoneDict["MACRON"]);
            foreach (var dict in phoneDict.Values) {
                foreach (var phoneme in dict) {
                    if (!consonants.Contains(phoneme) && !vowels.Contains(phoneme) &&
                        !breaks.Contains(phoneme) && !pauses.Contains(phoneme) &&
                        !silences.Contains(phoneme)) {
                        consonants.Add(phoneme);
                    }
                    if (!consonants.Contains(phoneme)) {
                        builder.AddSymbol(phoneme, true);
                    } else {
                        builder.AddSymbol(phoneme, false);
                    }
                }
            }
            foreach (var entry in phoneDict.Keys) {
                builder.AddEntry(entry, phoneDict[entry]);
                foreach (var reduction in phoneDict["VOWEL_REDUCTION"]) {
                    var phonemes = phoneDict[entry].Except(vowels).ToList();
                    if (phonemes.Count == 0) continue;
                    builder.AddEntry(entry + reduction, phonemes);
                }
                foreach (var macron in phoneDict["MACRON"]) {
                    var addPhonemes = phoneDict[entry].Where(x => vowels.Contains(x)).ToList();
                    if (addPhonemes.Count == 0) continue;
                    var phonemes = phoneDict[entry].ToList();
                    phonemes.AddRange(addPhonemes);
                    builder.AddEntry(entry + macron, phonemes);
                    macronLyrics.Add(entry + macron);
                }
            }
            g2ps.Add(builder.Build());
            return new G2pFallbacks(g2ps.ToArray());
        }



        protected (string prefix, string suffix) GetPrefixAndSuffix(RenderNote note) {
            string prefix = string.Empty;
            string suffix = string.Empty;

            var textList = note.lyric.Split().ToList();
            bool splitFlag = true;
            foreach (var text in textList) {
                var existSymbol = g2p.IsValidSymbol(text);
                if (existSymbol) {
                    splitFlag = false;
                    continue;
                } else if (existSymbol && !splitFlag) {
                    splitFlag = true;
                    continue;
                }
                if (splitFlag) {
                    prefix += text;
                } else {
                    suffix += text;
                }
            }

            return (prefix, suffix);
        }

        private string FindLastVowelOrLastPhoneme(string[] phonemes) {
            if (phonemes == null || phonemes.Length == 0) {
                return string.Empty;
            }
            for (int i = phonemes.Length - 1; i >= 0; --i) {
                if (g2p.IsVowel(phonemes[i])) {
                    return phonemes[i];
                }
            }
            return phonemes[^1];
        }

        protected HTSNote CustomHTSNoteContext(HTSNote htsNote, RenderNote note) {
            var fixs = GetPrefixAndSuffix(note);
            if (!htsNote.isRest && !htsNote.isSlur) {
                htsNote.langDependent = "0"; // no macron
                if (macronLyrics.Contains(note.lyric)) {
                    htsNote.langDependent = "1"; // macron
                }
            }
            return htsNote;
        }

        //make a HTS Note from given symbols and UNotes
        protected HTSNote makeHtsNote(string[] symbols, RenderNote note, int startTick, int leadingMs) {
            var positiontick = startTick + note.position;
            var endTick = positiontick + note.duration;
            UTimeSignature sig = timeAxis.TimeSignatureAtTick(positiontick);
            timeAxis.TickPosToBarBeat(positiontick, out int bar, out int beat, out int remainingTicks);
            var isRest = symbols.Select(x => x.ToLowerInvariant()).Any(x => pauses.Contains(x) || silences.Contains(x) || breaks.Contains(x));
            var htsNote = new HTSNote(
                            symbols: symbols,
                            tone: note.tone,
                            isSlur: IsSyllableVowelExtensionNote(note),
                            isRest: isRest,
                            lang: isRest ? string.Empty : lang,
                            accent: string.Empty,
                            beatPerBar: sig.beatPerBar,
                            beatUnit: sig.beatUnit,
                            positionBar: bar,
                            positionBeat: beat,
                            key: key,
                            bpm: timeAxis.GetBpmAtTick(positiontick),
                            startms: (int)timeAxis.MsBetweenTickPos(startTick, positiontick) + leadingMs,
                            endms: (int)timeAxis.MsBetweenTickPos(startTick, endTick) + leadingMs,
                            positionTicks: positiontick,
                            durationTicks: note.duration
                            );
            return CustomHTSNoteContext(htsNote, note) ?? htsNote;
        }
        protected HTSNote makeHtsNote(string symbol, RenderNote note, int startTick, int leadingMs) {
            return makeHtsNote(new string[] { symbol }, note, startTick, leadingMs);
        }

        protected bool IsSyllableVowelExtensionNote(RenderNote note) {
            return note.lyric.StartsWith("+~") || note.lyric.StartsWith("+*");
        }

        public string GetPhonemeType(string phoneme) {
            if (phoneme == "xx") {
                return "xx";
            }
            if (vowels.Contains(phoneme)) {
                return "v";
            }
            if (pauses.Contains(phoneme)) {
                return "p";
            }
            if (silences.Contains(phoneme)) {
                return "s";
            }
            if (breaks.Contains(phoneme)) {
                return "b";
            }
            //if (unvoiced.Contains(phoneme)) {
            //    return "c";
            //}
            return "c";
        }

        HTSPhoneme[] HTSNoteToPhonemes(HTSNote htsNote) {
            var htsPhonemes = htsNote.symbols.Select(x => new HTSPhoneme(x, htsNote)).ToArray();
            foreach (int i in Enumerable.Range(0, htsPhonemes.Length)) {
                htsPhonemes[i].type = GetPhonemeType(htsPhonemes[i].symbol);
                htsPhonemes[i].position = i + 1;
                htsPhonemes[i].position_backward = htsPhonemes.Length - i;
                if (htsPhonemes[i].type.Equals("c")) {
                    int prev = i - 1;
                    if (prev >= 0) {
                        if (htsPhonemes[prev].type.Equals("v")) {
                            htsPhonemes[i].prev_vowel_distance = 1;
                        } else {
                            htsPhonemes[i].prev_vowel_distance = htsPhonemes[prev].prev_vowel_distance + 1;
                        }
                    }
                }
            }
            for (int i = htsPhonemes.Length - 1; i > 0; --i) {
                if (htsPhonemes[i].type.Equals("c")) {
                    int next = i + 1;
                    if (next < htsPhonemes.Length) {
                        if (htsPhonemes[next].type.Equals("v")) {
                            htsPhonemes[i].next_vowel_distance = 1;
                        } else {
                            htsPhonemes[i].next_vowel_distance = htsPhonemes[next].next_vowel_distance + 1;
                        }
                    }
                }
            }
            return htsPhonemes;
        }

        protected HTSPhoneme[] CustomHTSPhonemeContext(HTSPhoneme[] htsPhonemes, RenderNote notes) {
            var fixs = GetPrefixAndSuffix(notes);
            foreach (var htsPhoneme in htsPhonemes) {
                htsPhoneme.flag1 = "00"; // NEUTRINO Default.
            }
            return htsPhonemes;
        }

        protected struct monoLabel {
            public string symbol;
            public int startMs;
            public int endMs;
            public override string ToString() {
                return $"{startMs * 1000} {endMs * 1000} {symbol}";
            }
        }

        protected void ProcessPart(RenderPhrase phrase) {
            if (timeAxis == null) {
                timeAxis = phrase.timeAxis;
            }

            int startTick = phrase.position;
            int endTick = phrase.position + phrase.duration;

            // 文全体の長さ（開始1小節 + 本体 + 終了1小節）
            int sentenceDurMs = barLenMsStart + (int)(phrase.endMs - phrase.positionMs) + barLenMsEnd;
            int sentenceDurTicks = barLenTicksStart + (endTick - startTick) + barLenTicksEnd;

            int noteIndexAnchorOffset = 1; // 先頭パディング分
            var notePhIndex = new List<int> { noteIndexAnchorOffset };
            var phAlignPoints = new List<Tuple<int, double>>();

            // 先頭パディング pau
            timeAxis.TickPosToBarBeat(startTick - barLenTicksStart, out int barStart, out int beatStart, out int _);
            var sigForPadStart = timeAxis.TimeSignatureAtTick(startTick - barLenTicksStart);
            HTSNote PaddingNoteStart = new HTSNote(
                symbols: new string[] { "pau" },
                beatPerBar: sigForPadStart.beatPerBar,
                beatUnit: sigForPadStart.beatUnit,
                positionBar: barStart,
                positionBeat: beatStart,
                key: key,
                bpm: timeAxis.GetBpmAtTick(startTick - barLenTicksStart),
                tone: 0,
                isSlur: false,
                isRest: true,
                lang: string.Empty,
                accent: string.Empty,
                startms: 0,
                endms: barLenMsStart,
                positionTicks: startTick - barLenTicksStart,
                durationTicks: barLenTicksStart
            );
            var htsNotes = new List<HTSNote> { PaddingNoteStart };
            var htsPhonemes = new List<HTSPhoneme>();
            htsPhonemes.AddRange(HTSNoteToPhonemes(PaddingNoteStart));

            //Alignment
            var phonemesByNoteIndex = phrase.phones
                .GroupBy(phone => phone.noteIndex)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(phone => phone.phoneme).ToArray());
            var lastBasePhonemes = Array.Empty<string>();
            var tuples = new List<Tuple<HTSNote, int>>();
            for (int noteIndex = 0; noteIndex < phrase.notes.Length; noteIndex++) {
                var note = phrase.notes[noteIndex];
                if (!IsSyllableVowelExtensionNote(note)) {
                    if (!phonemesByNoteIndex.TryGetValue(noteIndex, out var phonemes)) {
                        continue;
                    }
                    lastBasePhonemes = phonemes;
                    HTSNote htsNote = makeHtsNote(phonemes, note, startTick, barLenMsStart);
                    tuples.Add(Tuple.Create(htsNote, noteIndex));
                } else {
                    // 拍点延長ノートは、直前の通常ノートの最後の母音を引き延ばす
                    var extensionPhoneme = FindLastVowelOrLastPhoneme(lastBasePhonemes);
                    if (!string.IsNullOrEmpty(extensionPhoneme)) {
                        HTSNote htsNote = makeHtsNote(extensionPhoneme, note, startTick, barLenMsStart);
                        tuples.Add(Tuple.Create(htsNote, noteIndex));
                    }
                }
            }
            for (int i = 0; i < tuples.Count; i++) {
                var htsNote = tuples[i].Item1;
                htsNotes.Add(htsNote);
                htsNote.index = i;
                htsNote.indexBackwards = htsNotes.Count - i;
                htsNote.sentenceDurMs = sentenceDurMs;
                htsNote.sentenceDurTicks = sentenceDurTicks;
                var tmpPhonemes = HTSNoteToPhonemes(htsNote);
                var notePhonemes = CustomHTSPhonemeContext(tmpPhonemes, phrase.notes[tuples[i].Item2]) ?? tmpPhonemes;
                //分析第几个音素与音符对齐
                int firstVowelIndex = HTSContextBuilder.FindFirstVowelIndex(htsNote.symbols, g2p.IsVowel);
                phAlignPoints.Add(new Tuple<int, double>(
                    htsPhonemes.Count + firstVowelIndex,//TODO
                    timeAxis.MsBetweenTickPos(startTick, htsNote.positionTicks) + barLenMsStart
                    ));
                htsPhonemes.AddRange(notePhonemes);
                notePhIndex.Add(htsPhonemes.Count);
            }
            // 終端パディング pau（位置は「本当の曲末」tick）
            timeAxis.TickPosToBarBeat(endTick, out int barEnd, out int beatEnd, out int _);
            HTSNote PaddingNoteEnd = new HTSNote(
                symbols: new string[] { "pau" },
                beatPerBar: sigEnd.beatPerBar,
                beatUnit: sigEnd.beatUnit,
                positionBar: barEnd,
                positionBeat: beatEnd,
                key: key,
                bpm: bpmEnd,
                tone: 0,
                isSlur: false,
                isRest: true,
                lang: string.Empty,
                accent: string.Empty,
                // 絶対msで末尾に配置
                startms: sentenceDurMs - barLenMsEnd,
                endms: sentenceDurMs,
                positionTicks: endTick,
                durationTicks: barLenTicksEnd
            );
            htsNotes.Add(PaddingNoteEnd);
            htsPhonemes.AddRange(HTSNoteToPhonemes(PaddingNoteEnd));

            // 末尾アンカーは「曲末＋終端パディング」位置
            var lastNote = htsNotes[^1];
            phAlignPoints.Add(Tuple.Create(
                htsPhonemes.Count,
                (double)sentenceDurMs
            ));
            var htsPhrase = new HTSPhrase(htsNotes.ToArray());
            htsPhrase.UpdateResolution(resolution);
            htsPhrase.totalNotes = htsNotes.Count - 1;
            htsPhrase.totalPhonemes = htsPhonemes.Count - 1;
            htsPhrase.totalPhrases = 1;
            //make neighborhood links between htsNotes and between htsPhonemes
            foreach (int i in Enumerable.Range(0, htsNotes.Count)) {
                htsNotes[i].parent = htsPhrase;
                if (i > 0) {
                    htsNotes[i].prev = htsNotes[i - 1];
                    htsNotes[i - 1].next = htsNotes[i];
                }
            }
            for (int i = 1; i < htsPhonemes.Count; ++i) {
                htsPhonemes[i].prev = htsPhonemes[i - 1];
                htsPhonemes[i - 1].next = htsPhonemes[i];
            }

            List<monoLabel> monoLabels = new List<monoLabel>();
            monoLabels.Add(new monoLabel() {
                symbol = htsPhonemes[0].symbol,
                startMs = htsPhonemes[0].parent.startMs,
                endMs = htsPhonemes[0].parent.endMs
            });
            foreach (var phneme in phrase.phones) {
                monoLabels.Add(new monoLabel() {
                    symbol = phneme.phoneme,
                    startMs = (int)(phneme.positionMs - phrase.positionMs) + barLenMsStart,
                    endMs = (int)(phneme.endMs - phrase.positionMs) + barLenMsStart
                });
            }

            try {
                File.WriteAllLines(fullScorePath, htsPhonemes.Select(x => x.dump()));
                File.WriteAllLines(monoTimingPath, monoLabels.Select(x => x.ToString()));
            } catch (Exception e) {
                Log.Error(e.ToString());
                throw e;
            }
        }

        public double[] LoadFile(string filePath) {
            if (File.Exists(filePath)) {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read)) {
                    using (BinaryReader reader = new BinaryReader(fs)) {
                        long fileSize = fs.Length;
                        int Count = (int)(fileSize / sizeof(float));
                        double[] pitchData = new double[Count];
                        for (int i = 0; i < Count; i++) {
                            pitchData[i] = reader.ReadSingle();
                        }
                        return pitchData;
                    }
                }
            }
            return new double[0];
        }
        private static double[,] Array2DArray(double[] elements, int columns) {
            int rows = elements.Length / columns;
            double[,] result = new double[rows, columns];
            for (int i = 0; i < rows; i++) {
                for (int j = 0; j < columns; j++) {
                    result[i, j] = elements[i * columns + j];
                }
            }
            return result;
        }

        public void SaveFile(string filePath, double[] doubles) {
            try {
                using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write)) {
                    using (BinaryWriter writer = new BinaryWriter(fs)) {
                        foreach (double pitch in doubles) {
                            writer.Write(pitch);
                        }
                    }
                }
            } catch (Exception ex) {
                Log.Error($"Error: {ex.Message}");
            }
        }

        public RenderResult Layout(RenderPhrase phrase) {
            if (timeAxis == null) {
                timeAxis = phrase.timeAxis;
            }
            startTick = phrase.position;
            endTick = phrase.position + phrase.duration;

            // パディングを小節長で設定（開始・終了ともに1小節）
            sigStart = timeAxis.TimeSignatureAtTick(startTick);
            bpmStart = timeAxis.GetBpmAtTick(startTick);
            barLenMsStart = (int)Math.Round((60000.0 / bpmStart) * sigStart.beatPerBar);

            sigEnd = timeAxis.TimeSignatureAtTick(endTick);
            bpmEnd = timeAxis.GetBpmAtTick(endTick);
            barLenMsEnd = (int)Math.Round((60000.0 / bpmEnd) * sigEnd.beatPerBar);

            headMs = phrase.positionMs - phrase.timeAxis.TickPosToMsPos(phrase.position) - barLenMsStart;
            tailMs = phrase.timeAxis.TickPosToMsPos(phrase.end) - barLenMsEnd - phrase.endMs;
            return new RenderResult() {
                leadingMs = headMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = headMs + phrase.durationMs + tailMs,
            };
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender) {
            var task = Task.Run(() => {
                lock (lockObj) {
                    if (cancellation.IsCancellationRequested) {
                        return new RenderResult();
                    }
                    string progressInfo = $"Track {trackNo + 1}: {this} \"{string.Join(" ", phrase.phones.Select(p => p.phoneme))}\"";
                    progress.Complete(0, progressInfo);
                    this.singer = phrase.singer as NeutrinoSinger;
                    if (g2p == null || string.IsNullOrEmpty(NeutrinoExe)) {
                        setUp();
                    }
                    var result = Layout(phrase);
                    var hash = HashPhraseGroups(phrase);
                    string tmpPath = Path.Join(PathManager.Inst.CachePath, $"ne-{hash:x16}_temp");
                    if (!Directory.Exists(tmpPath)) {
                        Directory.CreateDirectory(tmpPath);
                    }
                    string wavPath = Path.Join(tmpPath, $"ne.wav");
                    string f0Path = Path.Join(tmpPath, $"ne.f0");
                    string editorf0Path = Path.Join(tmpPath, $"ne-edit.f0");
                    string melspecPath = Path.Join(tmpPath, $"ne.melspec");
                    string mgcPath = Path.Join(tmpPath, $"ne.mgc");
                    string bapPath = Path.Join(tmpPath, $"ne.bap");
                    fullScorePath = Path.Join(tmpPath, $"ne_full_score.lab");
                    monoTimingPath = Path.Join(tmpPath, $"ne_mono_timing.lab");
                    string modelDir = this.singer.Location + "\\";
                    int toneShift = phrase.phones[0] != null ? phrase.phones[0].toneShift : 0;
                    int numThreads = Preferences.Default.NumRenderThreads;
                    if (!File.Exists(fullScorePath) && !File.Exists(monoTimingPath)) {
                        ProcessPart(phrase);
                    }
                    var flag1 = phrase.phones[0].flags.FirstOrDefault(f => f.Item1.Equals(NTYP));
                    string eng = string.Empty;
                    if (flag1 != null) {
                        eng = flag1.Item1;
                    }
                    string ArgParam = string.Empty;
                    if (eng.Equals(NeutrinoRenderType.NSF.ToString())) {
                        var flag2 = phrase.phones[0].flags.FirstOrDefault(f => f.Item1.Equals(NMOD));
                        string nsf = "ve";
                        if (flag2 != null) {
                            if (flag2.Item2 == 4) {
                                nsf = NsfModel.va.ToString();
                                sampleRate = 48000;
                            } else if (flag2.Item2 == 3) {
                                nsf = NsfModel.vs.ToString();
                                sampleRate = 48000;
                            } else if (flag2.Item2 == 2) {
                                nsf = NsfModel.ve.ToString();
                                sampleRate = 24000;
                            }
                        }
                        if (!File.Exists(f0Path) || !File.Exists(melspecPath)) {
                            ArgParam = $"{fullScorePath} {monoTimingPath} {f0Path} {melspecPath} {modelDir} -s -n 1 -o {numThreads} -k {toneShift} -m -t";
                            if (File.Exists(Neutrino_ClientExe)) {
                                ProcessRunner.Run(Neutrino_ClientExe, ArgParam, Log.Logger);
                            } else {
                            ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                        }
                        if (phrase.phones[0].direct) {
                            double[] f0 = LoadFile(f0Path);
                            int totalFrames = f0.Length;
                            int headFrames = (int)Math.Round(headMs / framePeriod);
                            int tailFrames = (int)Math.Round(tailMs / framePeriod);
                            double[] editorF0 = SampleCurve(phrase, phrase.pitches, 0, framePeriod, totalFrames, headFrames, tailFrames, x => MusicMath.ToneToFreq(x * 0.01));
                            SaveFile(editorf0Path, editorF0);
                            ArgParam = $"{editorf0Path} {melspecPath} {modelDir}{nsf}.bin {wavPath} -l {monoTimingPath} -n 1 -p {numThreads} -s{sampleRate / 1000} -f {toneShift} -m -t";
                        } else {
                            ArgParam = $"{f0Path} {melspecPath} {modelDir}{nsf}.bin {wavPath} -l {monoTimingPath} -n 1 -p {numThreads} -s{sampleRate / 1000} -f {toneShift} -m -t";
                        }
                            if (File.Exists(Vocoder_ClientExe)) {
                                ProcessRunner.Run(Vocoder_ClientExe, ArgParam, Log.Logger);
                            } else {
                        ProcessRunner.Run(NsfExe, ArgParam, Log.Logger);
                            }
                        using (var waveStream = new WaveFileReader(wavPath)) {
                            result.samples = Wave.GetSamples(waveStream.ToSampleProvider());
                            Wave.CorrectSampleScale(result.samples);
                            var signal = new NWaves.Signals.DiscreteSignal(sampleRate, result.samples);
                            signal = NWaves.Operations.Operation.Resample(signal, 44100);
                            var source = new WaveSource(0, 0, 0, 1);
                            source.SetSamples(result.samples);
                            WaveFileWriter.CreateWaveFile16(wavPath, new ExportAdapter(source).ToMono(1, 0));
                        }
                        }
                    } else {
                        if (!File.Exists(f0Path) || !File.Exists(mgcPath) || !File.Exists(bapPath)) {
                            ArgParam = $"{fullScorePath} {monoTimingPath} {f0Path} {melspecPath} {modelDir} -w {mgcPath} {bapPath} -n 1 -o {numThreads} -k {toneShift} -m -t";
                            if (File.Exists(Neutrino_ClientExe)) {
                                ProcessRunner.Run(Neutrino_ClientExe, ArgParam, Log.Logger);
                            } else {
                            ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                        }
                        if (cancellation.IsCancellationRequested) {
                            return new RenderResult();
                        }
                        if (!File.Exists(wavPath)) {
                            if (phrase.phones[0].direct) {
                            float gender = 1f + (phrase.phones[0].flags.FirstOrDefault(f => f.Item3.Equals(Format.Ustx.GEN)).Item2 / 100) ?? 1f;
                            float breathiness = phrase.phones[0].flags.FirstOrDefault(f => f.Item3.Equals(Format.Ustx.BRE)).Item2 ?? 0f;
                                ArgParam = $"{f0Path} {mgcPath} {bapPath} {wavPath} -n 1 -m {gender} -b {breathiness} -t";
                            ProcessRunner.Run(WorldExe, ArgParam, Log.Logger);

                        } else {
                            double[] f0 = LoadFile(f0Path);
                            double[,] mgc = Array2DArray(LoadFile(mgcPath), 60);
                            double[,] bap = Array2DArray(LoadFile(bapPath), 5);
                            int totalFrames = f0.Length;
                            int headFrames = (int)Math.Round(headMs / framePeriod);
                            int tailFrames = (int)Math.Round(tailMs / framePeriod);

                            var editorF0 = SampleCurve(phrase, phrase.pitches, 0, framePeriod, totalFrames, headFrames, tailFrames, x => MusicMath.ToneToFreq(x * 0.01));
                            var gender = SampleCurve(phrase, phrase.gender, 0.5, framePeriod, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
                            var tension = SampleCurve(phrase, phrase.tension, 0.5, framePeriod, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
                            var breathiness = SampleCurve(phrase, phrase.breathiness, 0.5, framePeriod, totalFrames, headFrames, tailFrames, x => 0.5 + 0.005 * x);
                            var voicing = SampleCurve(phrase, phrase.voicing, 1.0, framePeriod, totalFrames, headFrames, tailFrames, x => 0.01 * x);

                            for (int i = 0; i < f0.Length; i++) {
                                if (f0[i] < 50) {
                                    editorF0[i] = 0;
                                }
                            }
                            var samples = Worldline.WorldSynthesis(
                                editorF0,
                                mgc, true, mgc.Length - 1,
                                bap, true, bap.Length - 1,
                                framePeriod, sampleRate,
                                gender, tension, breathiness, voicing);
                            result.samples = samples.Select(d => (float)d).ToArray();
                            Wave.CorrectSampleScale(result.samples);
                            var signal = new NWaves.Signals.DiscreteSignal(sampleRate, result.samples);
                            signal = NWaves.Operations.Operation.Resample(signal, 44100);
                            result.samples = signal.Samples;
                            var source = new WaveSource(0, 0, 0, 1);
                            source.SetSamples(result.samples);
                            WaveFileWriter.CreateWaveFile16(wavPath, new ExportAdapter(source).ToMono(1, 0));
                        }
                    }
                    }
                    progress.Complete(phrase.phones.Length, progressInfo);
                    try {
                        if (File.Exists(wavPath)) {
                            using (var waveStream = new WaveFileReader(wavPath)) {

                                result.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                            }
                            if (result.samples != null) {
                                Renderers.ApplyDynamics(phrase, result);
                            }
                        }
                    } catch (Exception e) {
                        Log.Error(e.Message);
                        result.samples = new float[0];
                    }
                    return result;
                }
            });
            return task;
        }

        double[] SampleCurve(RenderPhrase phrase, float[] curve, double defaultValue, double frameMs, int length, int headFrames, int tailFrames, Func<double, double> convert) {
            const int interval = 5;
            var result = new double[length];
            if (curve == null) {
                Array.Fill(result, defaultValue);
                return result;
            }
            for (int i = 0; i < length - headFrames - tailFrames; i++) {
                double posMs = phrase.positionMs - phrase.leadingMs + i * frameMs;
                int ticks = phrase.timeAxis.MsPosToTickPos(posMs) - (phrase.position - phrase.leading);
                int index = Math.Max(0, (int)((double)ticks / interval));
                if (index < curve.Length) {
                    result[i + headFrames] = convert(curve[index]);
                }
            }
            Array.Fill(result, defaultValue, 0, headFrames);
            Array.Fill(result, defaultValue, length - tailFrames, tailFrames);
            return result;
        }


        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            var result = new List<UExpressionDescriptor> {
                //energy
                //new UExpressionDescriptor{
                //    name="energy (curve)",
                //    abbr=ENE,
                //    type=UExpressionType.Curve,
                //    min=-100,
                //    max=100,
                //    defaultValue=0,
                //    isFlag=false,
                //},
                ////engine
                //new UExpressionDescriptor {
                //    name = "NEUTRINO engine type (~2.x)",
                //    abbr = NTYP,
                //    type = UExpressionType.Options,
                //    options = Enum.GetNames<NeutrinoRenderType>(),
                //    isFlag = false
                //},
                ////engine mode
                //new UExpressionDescriptor {
                //    name = "NEUTRINO engine mode (~2.x)",
                //    abbr = NMOD,
                //    type = UExpressionType.Options,
                //    options = Enum.GetNames<NeutrinoRenderMode>(),
                //    isFlag = false
                //},
                //expressiveness
                new UExpressionDescriptor {
                    name = "pitch smoothened (curve)",
                    abbr = SMOC,
                    type = UExpressionType.Curve,
                    min = 0,
                    max = 10,
                    defaultValue = 0,
                    isFlag = false
                },
            };
            return result.ToArray();
        }

        public override string ToString() => Renderers.NEUTRINO;

        RenderPitchResult IRenderer.LoadRenderedPitch(RenderPhrase phrase) {
            try {
                var hash = HashPhraseGroups(phrase);
                var tmpPath = Path.Join(PathManager.Inst.CachePath, $"ne-{hash:x16}", "_temp");
                string f0Path = Path.Join(tmpPath, $"ne.f0");
                if (!File.Exists(f0Path)) {
                    return null;
                }
                double[] f0 = LoadFile(f0Path);

                int totalFrames = f0.Length;
                int headFrames = (int)Math.Round(headMs / framePeriod);
                int tailFrames = (int)Math.Round(tailMs / framePeriod);
                var exprCurve = phrase.curves.FirstOrDefault(curve => curve.Item1.Equals(SMOC));
                if (exprCurve != null) {

                    List<int> exprs = SampleCurve(phrase, exprCurve.Item2, 0, framePeriod, totalFrames, headFrames, tailFrames, x => x).Select(x => (int)x).ToList();
                    var f0S = new F0Smoother(f0.ToList());
                    f0S.SmoothenWidthList = exprs;
                    f0 = f0S.GetSmoothenedF0List(f0.ToList()).ToArray();
                }

                var result = new RenderPitchResult() {
                    tones = f0.Select(f => (float)MusicMath.FreqToTone(f)).ToArray(),
                };
                result.ticks = new float[result.tones.Length];
                var layout = Layout(phrase);
                var t = layout.positionMs - layout.leadingMs;
                for (int i = 0; i < result.tones.Length; i++) {
                    t += framePeriod;
                    result.ticks[i] = phrase.timeAxis.MsPosToTickPos(t) - phrase.position;
                }
            } catch {
            }
            return null;
        }


        ulong HashPhraseGroups(RenderPhrase phrase) {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(phrase.preEffectHash);
                    writer.Write(phrase.phones[0].toneShift);
                    phrase.phones.ForEach(x => writer.Write(x.tone));
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }
    }
}
