using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using K4os.Hash.xxHash;
using NAudio.Wave;
using NumSharp;
using OpenUtau.Api;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using SharpCompress;

namespace OpenUtau.Core.Neutrino {
    public class NeutrinoRenderer : IRenderer {
        const string ENG = "eng";

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.SHFT,
            Format.Ustx.GENC,
            Format.Ustx.TENC,
            Format.Ustx.BREC,
            Format.Ustx.VOIC,
            ENG
        };

        static readonly object lockObj = new object();

        public USingerType SingerType => USingerType.Neutrino;

        public bool SupportsRenderPitch => true;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        TimeAxis timeAxis;

        protected NeutrinoSinger singer;
        string NeutrinoExe = string.Empty;
        string NsfExe = string.Empty;
        string WorldExe = string.Empty;
        string[] Eng = new string[] { "NSF", "WORLD" };
        const int fs = 48000;
        public const int headTicks = 240;
        public const int tailTicks = 240;
        //information used by HTS writer
        protected Dictionary<string, string[]> phoneDict = new Dictionary<string, string[]>();
        protected List<string> vowels = new List<string>();
        protected List<string> consonants = new List<string>();
        protected List<string> breaks = new List<string>();
        protected List<string> pauses = new List<string>();
        protected List<string> silences = new List<string>();
        protected List<string> unvoiced = new List<string>();
        List<string> macronLyrics = new List<string>();
        protected string lang = "";
        int key = 0;
        int resolution = 480;

        //information used by openutau phonemizer
        protected IG2p g2p;
        //result caching
        private Dictionary<int, List<Tuple<string, int>>> partResult = new Dictionary<int, List<Tuple<string, int>>>();
        int paddingMs = 500;//子音の持続時間

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
                NsfExe = Path.Join(basePath, @".\bin", "NSF.exe");
                WorldExe = Path.Join(basePath, @".\bin", "WORLD.exe");
            } else if (OS.IsMacOS() || OS.IsLinux()) {
                NeutrinoExe = Path.Join(basePath, @".\bin", "NEUTRINO");
                NsfExe = Path.Join(basePath, @".\bin", "NSF");
                WorldExe = Path.Join(basePath, @".\bin", "WORLD");
            } else {
                throw new NotSupportedException("Platform not supported.");
            }
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
        protected HTSNote makeHtsNote(string[] symbols, RenderNote note, int startTick) {
            UTimeSignature sig = timeAxis.TimeSignatureAtTick(note.position);
            timeAxis.TickPosToBarBeat(note.position, out int bar, out int beat, out int remainingTicks);
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
                            bpm: timeAxis.GetBpmAtTick(note.position),
                            startms: (int)timeAxis.MsBetweenTickPos(startTick, note.position) + paddingMs,
                            endms: (int)timeAxis.MsBetweenTickPos(startTick, note.end) + paddingMs,
                            positionTicks: note.position,
                            durationTicks: note.duration
                            );
            return CustomHTSNoteContext(htsNote, note) ?? htsNote;
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
            int prevVowelPos = -1;
            foreach (int i in Enumerable.Range(0, htsPhonemes.Length)) {
                htsPhonemes[i].position = i + 1;
                htsPhonemes[i].position_backward = htsPhonemes.Length - i;
                htsPhonemes[i].type = GetPhonemeType(htsPhonemes[i].symbol);
                if (htsPhonemes[i].type == "v") {
                    prevVowelPos = i;
                } else {
                    if (prevVowelPos > 0) {
                        htsPhonemes[i].distance_from_previous_vowel = i - prevVowelPos;
                    }
                }
            }
            int nextVowelPos = -1;
            for (int i = htsPhonemes.Length - 1; i > 0; --i) {
                if (htsPhonemes[i].type == "v") {
                    nextVowelPos = i;
                } else {
                    if (nextVowelPos > 0) {
                        htsPhonemes[i].distance_to_next_vowel = nextVowelPos - i;
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

        protected void ProcessPart(RenderPhrase phrase) {
            timeAxis = phrase.timeAxis;

            int offsetTick = phrase.position;
            int sentenceDurMs = paddingMs + (int)timeAxis.MsBetweenTickPos(
                phrase.position, phrase.end);
            int sentenceDurTicks = phrase.end - phrase.position;
            int paddingTicks = timeAxis.MsPosToTickPos(paddingMs);
            var notePhIndex = new List<int> { 1 };//每个音符的第一个音素在音素列表上对应的位置
            var phAlignPoints = new List<Tuple<int, double>>();//音素对齐的位置，Ms，绝对时间
            UTimeSignature sig = timeAxis.TimeSignatureAtTick(phrase.position - paddingTicks);
            timeAxis.TickPosToBarBeat(phrase.position - paddingTicks, out int bar, out int beat, out int remainingTicks);
            HTSNote PaddingNote = new HTSNote(
                symbols: new string[] { "pau" },
                beatPerBar: sig.beatPerBar,
                beatUnit: sig.beatUnit,
                positionBar: bar,
                positionBeat: beat,
                key: key,
                bpm: 0,
                tone: 0,
                isSlur: false,
                isRest: true,
                lang: string.Empty,//TODO:Does the pau not have language information?
                accent: string.Empty,
                startms: 0,
                endms: paddingMs,
                positionTicks: phrase.position - paddingTicks,
                durationTicks: paddingTicks
            );
            //convert OpenUtau notes to HTS Labels
            var htsNotes = new List<HTSNote> { PaddingNote };
            var htsPhonemes = new List<HTSPhoneme>();
            htsPhonemes.AddRange(HTSNoteToPhonemes(PaddingNote));

            //Alignment
            for (int noteIndex = 0; noteIndex < phrase.notes.Length; ++noteIndex) {
                var phenes = phrase.phones.Where(ph => ph.noteIndex == noteIndex).ToList();
                var phonemes = phenes.Select(ph => ph.phoneme).ToArray();
                HTSNote htsNote = makeHtsNote(phonemes, phrase.notes[noteIndex], offsetTick);
                htsNotes.Add(htsNote);
                var tmpPhonemes = HTSNoteToPhonemes(htsNote);
                var notePhonemes = CustomHTSPhonemeContext(tmpPhonemes, phrase.notes[noteIndex]) ?? tmpPhonemes;
                for(int i = 0; i < phenes.Count; ++i) {
                    //Log.Debug($"Note {noteIndex} Phoneme {i}: {phenes[i].phoneme} at {phenes[i].positionMs}ms, duration {phenes[i].endMs}ms");
                    //Log.Debug($"HTSNote {noteIndex} HTSPhonemes {i}: {notePhonemes[i].symbol} at {notePhonemes[i].parent.startMs}ms, duration {notePhonemes[i].parent.endMs}ms");
                    notePhonemes[i].parent.startMs = (int)phenes[i].positionMs;
                    notePhonemes[i].parent.endMs = (int)phenes[i].endMs;
                }
            //分析第几个音素与音符对齐
            int firstVowelIndex = 0;//The index of the first vowel in the note
                for (int phIndex = 0; phIndex < htsNote.symbols.Length; phIndex++) {
                    if (g2p.IsVowel(htsNote.symbols[phIndex])) {
                        firstVowelIndex = phIndex;
                        break;
                    }
                }
                phAlignPoints.Add(new Tuple<int, double>(
                    htsPhonemes.Count + firstVowelIndex,//TODO
                    timeAxis.TickPosToMsPos(htsNote.positionTicks)
                    ));
                htsPhonemes.AddRange(notePhonemes);
                notePhIndex.Add(htsPhonemes.Count);
            }

            htsNotes.Add(PaddingNote);//add padding note to the end of htsNotes
            htsPhonemes.AddRange(HTSNoteToPhonemes(PaddingNote));

            var lastNote = htsNotes[^1];
            phAlignPoints.Add(new Tuple<int, double>(
                htsPhonemes.Count,
                timeAxis.TickPosToMsPos(lastNote.positionTicks + lastNote.durationTicks)));


            var htsPhrase = new HTSPhrase(htsNotes.ToArray());
            htsPhrase.resolution = resolution;
            htsPhrase.totalNotes = htsNotes.Count;
            htsPhrase.totalPhonemes = htsPhonemes.Count;
            //make neighborhood links between htsNotes and between htsPhonemes
            foreach (int i in Enumerable.Range(0, htsNotes.Count)) {
                htsNotes[i].parent = htsPhrase;
                htsNotes[i].index = i;
                htsNotes[i].indexBackwards = htsNotes.Count - i;
                htsNotes[i].sentenceDurMs = sentenceDurMs;
                htsNotes[i].sentenceDurTicks = sentenceDurTicks;
                if (i > 0) {
                    htsNotes[i].prev = htsNotes[i - 1];
                    htsNotes[i - 1].next = htsNotes[i];
                }
            }
            for (int i = 1; i < htsPhonemes.Count; ++i) {
                htsPhonemes[i].prev = htsPhonemes[i - 1];
                htsPhonemes[i - 1].next = htsPhonemes[i];
            }


            try {
                File.WriteAllLines(fullScorePath, htsPhonemes.Select(x => x.dump()));
            } catch (Exception e) {
                Log.Error(e.ToString());
                throw e;
            }
        }

        public RenderResult Layout(RenderPhrase phrase) {
            return new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,//- ((VoicevoxUtils.headS * 1000) + 10),
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
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
                    if(g2p == null || string.IsNullOrEmpty(NeutrinoExe)) {
                        setUp();
                    }
                    var result = Layout(phrase);
                    var hash = HashPhraseGroups(phrase);
                    var tmpPath = Path.Join(PathManager.Inst.CachePath, $"ne-{hash:x16}", "_entemp");
                    if (!Directory.Exists(tmpPath)) {
                        Directory.CreateDirectory(tmpPath);
                    }

                    var wavPath = Path.Join(tmpPath, $"ne-{phrase.preEffectHash:x16}.wav");
                    string f0Path = Path.Join(tmpPath, $"ne-{phrase.preEffectHash}.f0");
                    string melspecPath = Path.Join(tmpPath, $"ne-{phrase.preEffectHash}.melspec");
                    string mgcPath = Path.Join(tmpPath, $"ne-{phrase.preEffectHash}.mgc");
                    string bapPath = Path.Join(tmpPath, $"ne-{phrase.preEffectHash}.bap");
                    fullScorePath = Path.Join(tmpPath, $"ne-{phrase.preEffectHash}_full_score.lab");
                    monoTimingPath = Path.Join(tmpPath, $"ne-{phrase.preEffectHash}_mono_timing.lab");

                    string modelDir = this.singer.Location + "\\";
                    int toneShift = phrase.phones[0] != null ? phrase.phones[0].toneShift : 0;
                    int numThreads = Preferences.Default.NumRenderThreads;
                    int gpuMode = -1;
                    switch (Preferences.Default.OnnxRunner) {
                        case "directml":
                            gpuMode = Preferences.Default.OnnxGpu;
                            break;
                        default:
                            gpuMode = -1;
                            break;
                    }

                    if (File.Exists(wavPath)) {
                        if (!File.Exists(fullScorePath)) {
                            ProcessPart(phrase);
                        }
                        string ArgParam = $"{fullScorePath} {monoTimingPath} {f0Path} {melspecPath} {modelDir} -n 1 -o ${numThreads} -k ${toneShift} -d 2 {gpuMode}";
                        ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                        var flag = phrase.phones[0].flags.FirstOrDefault(f => f.Item1.Equals(ENG));
                        string eng = string.Empty;
                        if(flag != null) {
                            eng = flag.Item1;
                        }
                        if (eng.Equals(Eng[0])) {
                            ArgParam = $"{f0Path} {melspecPath} {modelDir} {wavPath} -l {monoTimingPath} -n 1 -o ${numThreads} -k ${toneShift} -d 2 {gpuMode}";
                            ProcessRunner.Run(NsfExe, ArgParam, Log.Logger);
                        } else {
                            if (!File.Exists(f0Path) || !File.Exists(mgcPath) || !File.Exists(bapPath)) {
                                ArgParam = $"{fullScorePath} {monoTimingPath} {f0Path} {melspecPath} {modelDir} -n 1 -o ${numThreads} -k ${toneShift} -d 2 {gpuMode}";
                                ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                            }
                            if (cancellation.IsCancellationRequested) {
                                return new RenderResult();
                            }
                            var f0 = np.Load<double[]>(f0Path);
                            var mgc = np.Load<double[]>(mgcPath);
                            var bap = np.Load<double[]>(bapPath);
                            int fftSize = (mgc.Length - 1) * 2;
                            var sp = Worldline.DecodeMgc(f0.Length, mgc, fftSize, fs);
                            var ap = Worldline.DecodeBap(f0.Length, bap, fftSize, fs);

                            var framePeriod = 5;
                            int totalFrames = f0.Length;
                            var headMs = phrase.positionMs - phrase.timeAxis.TickPosToMsPos(phrase.position - headTicks);
                            var tailMs = phrase.timeAxis.TickPosToMsPos(phrase.end + tailTicks) - phrase.endMs;
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
                                sp, false, sp.GetLength(1),
                                ap, false, fftSize,
                                framePeriod, fs,
                                gender, tension, breathiness, voicing);
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
            return null;
        }

        public override string ToString() => Renderers.NEUTRINO;

        RenderPitchResult IRenderer.LoadRenderedPitch(RenderPhrase phrase) {
            try {
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
