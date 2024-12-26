﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Core.Util.nnmnkwii.io.hts;
using Serilog;

namespace OpenUtau.Core.Enunu {
    public abstract class HTSLabelPhonemizer : Phonemizer {
        protected USinger singer;
        //information used by HTS writer
        protected Dictionary<string, string[]> phoneDict;
        protected string[] vowels;
        protected string[] breaks;
        protected string[] pauses;
        protected string[] silences;
        protected string[] unvoiced;
        protected string[] macron;
        string defaultPause = "pau";
        protected string lang = "";
        int key = 0;

        //information used by openutau phonemizer
        protected IG2p g2p;
        //result caching
        private Dictionary<int, List<Tuple<string, int>>> partResult = new Dictionary<int, List<Tuple<string, int>>>();
        int paddingMs = 500;//子音の持続時間

        protected string tablePath;
        protected string questionPath;
        protected string htstmpPath;
        protected string monoScorePath;
        protected string fullScorePath;
        protected string monoTimingPath;
        protected string fullTimingPath;

        public HTSLabelPhonemizer() {

        }

        public override void SetSinger(USinger singer) {
            this.singer = singer;
            if (singer == null) {
                return;
            }
            //Load enuconfig
            string rootPath;
            if (File.Exists(Path.Join(singer.Location, "enunux", "enuconfig.yaml"))) {
                rootPath = Path.Combine(singer.Location, "enunux");
            }
            if (File.Exists(Path.Join(singer.Location, "enuconfig.yaml"))) {
                rootPath = Path.Combine(singer.Location, "enunux");
            } else {
                rootPath = singer.Location;
            }
            //Load g2p from enunux.yaml
            //g2p dict should be load after enunu dict
            try {
                this.g2p = LoadG2p(singer.Location);
            } catch (Exception e) {
                Log.Error(e, "failed to load g2p dictionary");
                return;
            }
            //Load Dictionary
            var enunuDictPath = Path.Join(rootPath, tablePath);
            try {
                phoneDict.Clear();
                LoadDict(Path.Join(rootPath, tablePath), singer.TextFileEncoding);
            } catch (Exception e) {
                Log.Error(e, $"failed to load dictionary from {enunuDictPath}");
                return;
            }
        }

        protected virtual IG2p LoadG2p(string rootPath) {
            var g2ps = new List<IG2p>();

            string enunuxPath = Path.Combine(rootPath, "enunux.yaml");
            var builder = G2pDictionary.NewBuilder();
            // Load dictionary from enunux.yaml and nnsvs dict
            if (File.Exists(enunuxPath)) {
                try {
                    var input = File.ReadAllText(enunuxPath, singer.TextFileEncoding);
                    var data = Core.Yaml.DefaultDeserializer.Deserialize<G2pDictionaryData>(input);
                    if (data.symbols != null) {
                        foreach (var symbolData in data.symbols) {
                            builder.AddSymbol(symbolData.symbol, symbolData.type);
                        }
                    }
                    foreach (var grapheme in phoneDict.Keys) {
                        builder.AddEntry(grapheme, phoneDict[grapheme]);
                    }
                    if (data.entries != null) {
                        foreach (var entry in data.entries) {
                            builder.AddEntry(entry.grapheme, entry.phonemes);
                        }
                    }
                } catch (Exception e) {
                    Log.Error(e, $"Failed to load Dictionary");
                }
            }
            foreach (var entry in phoneDict.Keys) {
                builder.AddEntry(entry, phoneDict[entry]);
            }
            g2ps.Add(builder.Build());
            return new G2pFallbacks(g2ps.ToArray());
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

        public override void SetUp(Note[][] notes, UProject project, UTrack track) {
            key = project.key;
            //将全曲拆分为句子
            var phrase = new List<Note[]> { notes[0] };
            for (int i = 1; i < notes.Length; ++i) {
                //如果上下音符相互衔接，则不分句
                if (notes[i - 1][^1].position + notes[i - 1][^1].duration == notes[i][0].position) {
                    phrase.Add(notes[i]);
                } else {
                    //如果断开了，则处理当前句子，并开启下一句
                    ProcessPart(phrase.ToArray());
                    phrase.Clear();
                    phrase.Add(notes[i]);
                }
            }
            if (phrase.Count > 0) {
                ProcessPart(phrase.ToArray());
            }
        }

        //make a HTS Note from given symbols and UNotes
        protected HTSNote makeHtsNote(string[] symbols, IList<Note> group, int startTick) {
            timeAxis.TickPosToBarBeat(group[0].position, out int bar, out int beat, out int remainingTicks);
            return new HTSNote(
                symbols: symbols,
                tone: group[0].tone,
                lang: lang,
                accent: string.Empty,
                beatPerBar: bar,
                beatUnit: beat,
                key: key,
                bpm: timeAxis.GetBpmAtTick(group[0].position),
                startms: (int)timeAxis.MsBetweenTickPos(startTick, group[0].position) + paddingMs,
                endms: (int)timeAxis.MsBetweenTickPos(startTick, group[^1].position + group[^1].duration) + paddingMs,
                positionTicks: group[0].position,
                durationTicks: group[^1].position + group[^1].duration - group[0].position
                );
        }

        protected HTSNote makeHtsNote(string symbol, Note[] group, int startTick) {
            return makeHtsNote(new string[] { symbol }, group, startTick);
        }

        protected bool IsSyllableVowelExtensionNote(Note note) {
            return note.lyric.StartsWith("+~") || note.lyric.StartsWith("+*");
        }

        private string[] ApplyExtensions(string[] symbols, Note[] notes) {
            var newSymbols = new List<string>();
            var vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
                // no syllables or all consonants, the last phoneme will be interpreted as vowel
                vowelIds.Add(symbols.Length - 1);
            }
            var lastVowelI = 0;
            newSymbols.AddRange(symbols.Take(vowelIds[lastVowelI] + 1));
            for (var i = 1; i < notes.Length && lastVowelI + 1 < vowelIds.Count; i++) {
                if (!IsSyllableVowelExtensionNote(notes[i])) {
                    var prevVowel = vowelIds[lastVowelI];
                    lastVowelI++;
                    var vowel = vowelIds[lastVowelI];
                    newSymbols.AddRange(symbols.Skip(prevVowel + 1).Take(vowel - prevVowel));
                } else {
                    newSymbols.Add(symbols[vowelIds[lastVowelI]]);
                }
            }
            newSymbols.AddRange(symbols.Skip(vowelIds[lastVowelI] + 1));
            return newSymbols.ToArray();
        }

        private List<int> ExtractVowels(string[] symbols) {
            var vowelIds = new List<int>();
            for (var i = 0; i < symbols.Length; i++) {
                if (g2p.IsVowel(symbols[i])) {
                    vowelIds.Add(i);
                }
            }
            return vowelIds;
        }

        protected virtual Note[] HandleNotEnoughNotes(Note[] notes, List<int> vowelIds) {
            var newNotes = new List<Note>();
            newNotes.AddRange(notes.SkipLast(1));
            var lastNote = notes.Last();
            var position = lastNote.position;
            var notesToSplit = vowelIds.Count - newNotes.Count;
            var duration = lastNote.duration / notesToSplit / 15 * 15;
            for (var i = 0; i < notesToSplit; i++) {
                var durationFinal = i != notesToSplit - 1 ? duration : lastNote.duration - duration * (notesToSplit - 1);
                newNotes.Add(new Note() {
                    position = position,
                    duration = durationFinal,
                    tone = lastNote.tone,
                    phonemeAttributes = lastNote.phonemeAttributes
                });
                position += durationFinal;
            }

            return newNotes.ToArray();
        }

        protected virtual Note[] HandleExcessNotes(Note[] notes, List<int> vowelIds) {
            var newNotes = new List<Note>();
            var SyllableCount = vowelIds.Count;
            newNotes.AddRange(notes.Take(SyllableCount - 1));
            var lastNote = notes[SyllableCount - 1];
            newNotes.Add(new Note() {
                position = lastNote.position,
                duration = notes[(SyllableCount - 1)..].Select(note => note.duration).Sum(),
                tone = lastNote.tone,
                phonemeAttributes = lastNote.phonemeAttributes
            });
            return newNotes.ToArray();
        }

        public string GetPhonemeType(string phoneme) {
            if (phoneme == "xx") {
                return "xx";
            }
            if (vowels.Contains(phoneme)) {
                return "v";
            }
            if (macron.Contains(phoneme)) {
                return "m";
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
            if (unvoiced.Contains(phoneme)) {
                return "u";
            }
            return "c";
        }

        string[] GetSymbols(Note note) {
            //priority:
            //1. phonetic hint
            //2. query from g2p dictionary
            //3. treat lyric as phonetic hint, including single phoneme
            //4. default pause
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                // Split space-separated symbols into an array.
                return note.phoneticHint.Split()
                    .Where(s => g2p.IsValidSymbol(s)) // skip the invalid symbols.
                    .ToArray();
            }
            // User has not provided hint, query g2p dictionary.
            var g2presult = g2p.Query(note.lyric.ToLowerInvariant());
            if (g2presult != null) {
                return g2presult;
            }
            //not founded in g2p dictionary, treat lyric as phonetic hint
            var lyricSplited = note.lyric.Split()
                    .Where(s => g2p.IsValidSymbol(s)) // skip the invalid symbols.
                    .ToArray();
            if (lyricSplited.Length > 0) {
                return lyricSplited;
            }
            return new string[] { defaultPause };
        }


        private (string[], int[], Note[]) GetSymbolsAndVowels(Note[] notes) {
            var mainNote = notes[0];
            var symbols = GetSymbols(mainNote);
            if (symbols == null) {
                return (null, null, null);
            }
            if (symbols.Length == 0) {
                symbols = new string[] { "" };
            }
            symbols = ApplyExtensions(symbols, notes);
            List<int> vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
                // no syllables or all consonants, the last phoneme will be interpreted as vowel
                vowelIds.Add(symbols.Length - 1);
            }
            if (notes.Length < vowelIds.Count) {
                notes = HandleNotEnoughNotes(notes, vowelIds);
            } else if (notes.Length > vowelIds.Count) {
                notes = HandleExcessNotes(notes, vowelIds);
            }
            return (symbols, vowelIds.ToArray(), notes);
        }

        protected struct Syllable {
            public List<string> symbols;
            public List<Note> notes;
        }

        protected virtual HTSNote[] MakeSyllables(Note[] inputNotes, int startTick) {
            (var symbols, var vowelIds, var notes) = GetSymbolsAndVowels(inputNotes);
            if (symbols == null || vowelIds == null || notes == null) {
                return null;
            }
            var firstVowelId = vowelIds[0];
            if (notes.Length < vowelIds.Length) {
                //error = $"Not enough extension notes, {vowelIds.Length - notes.Length} more expected";
                return null;
            }

            var syllables = new Syllable[vowelIds.Length];

            // Making the first syllable

            // there is only empty space before us
            syllables[0] = new Syllable() {
                symbols = symbols.Take(firstVowelId + 1).ToList(),
                notes = notes[0..1].ToList()
            };

            // normal syllables after the first one
            var noteI = 1;
            var ccs = new List<string>();
            var position = 0;
            var lastSymbolI = firstVowelId + 1;
            for (; lastSymbolI < symbols.Length; lastSymbolI++) {
                if (!vowelIds.Contains(lastSymbolI)) {
                    ccs.Add(symbols[lastSymbolI]);
                } else {
                    position += notes[noteI - 1].duration;
                    syllables[noteI] = new Syllable() {
                        symbols = ccs.Append(symbols[lastSymbolI]).ToList(),
                        notes = new List<Note>() { notes[noteI] }
                    };
                    ccs = new List<string>();
                    noteI++;
                }
            }
            syllables[^1].symbols.AddRange(ccs);
            return syllables.Select(x => makeHtsNote(x.symbols.ToArray(), x.notes, startTick)).ToArray();
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

        protected abstract void SendScore(Note[][] phrase);

        protected void ProcessPart(Note[][] phrase) {
            var tmpPath = Path.Join(PathManager.Inst.CachePath, $"lab-{phrase:x16}-{this.singer.Name:x16}");
            htstmpPath = tmpPath + "_htstemp";
            fullScorePath = Path.Join(htstmpPath, $"full_score.lab");
            fullTimingPath = Path.Join(htstmpPath, $"full_timing.lab");

            int offsetTick = phrase[0][0].position;
            int sentenceDurMs = paddingMs + (int)timeAxis.MsBetweenTickPos(
                phrase[0][0].position, phrase[^1][^1].position + phrase[^1][^1].duration);
            int paddingTicks = timeAxis.MsPosToTickPos(paddingMs);
            var notePhIndex = new List<int> { 1 };//每个音符的第一个音素在音素列表上对应的位置
            var phAlignPoints = new List<Tuple<int, double>>();//音素对齐的位置，Ms，绝对时间
            timeAxis.TickPosToBarBeat(phrase[0][0].position, out int bar, out int beat, out int remainingTicks);
            HTSNote PaddingNote = new HTSNote(
                symbols: new string[] { defaultPause },
                beatPerBar: bar,
                beatUnit: beat,
                key: key,
                bpm: 0,
                tone: 0,
                lang: lang,
                accent: string.Empty,
                startms: 0,
                endms: paddingMs,
                positionTicks: phrase[0][0].position - paddingTicks,
                durationTicks: paddingTicks
            );
            //convert OpenUtau notes to HTS Labels
            var htsNotes = new List<HTSNote> { PaddingNote };
            var htsPhonemes = new List<HTSPhoneme>();
            htsPhonemes.AddRange(HTSNoteToPhonemes(PaddingNote));

            //Alignment
            for (int noteIndex = 0; noteIndex < phrase.Length; ++noteIndex) {
                HTSNote[] Syllables = MakeSyllables(phrase[noteIndex], offsetTick);
                htsNotes.AddRange(Syllables);
                foreach (var htsNote in Syllables) {
                    var notePhonemes = HTSNoteToPhonemes(htsNote);
                    //分析第几个音素与音符对齐
                    int firstVowelIndex = 0;//The index of the first vowel in the note
                    for (int phIndex = 0; phIndex < htsNote.symbols.Length; phIndex++) {
                        if (g2p.IsVowel(htsNote.symbols[phIndex])) {
                            firstVowelIndex = phIndex;
                            break;
                        }
                    }
                    phAlignPoints.Add(new Tuple<int, double>(
                        htsPhonemes.Count + (firstVowelIndex),//TODO
                        timeAxis.TickPosToMsPos(htsNote.positionTicks)
                        ));
                    htsPhonemes.AddRange(notePhonemes);
                }
                notePhIndex.Add(htsPhonemes.Count);
            }

            var lastNote = htsNotes[^1];
            phAlignPoints.Add(new Tuple<int, double>(
                htsPhonemes.Count,
                timeAxis.TickPosToMsPos(lastNote.positionTicks + lastNote.durationTicks)));

            //make neighborhood links between htsNotes and between htsPhonemes
            foreach (int i in Enumerable.Range(0, htsNotes.Count)) {
                htsNotes[i].index = i;
                htsNotes[i].indexBackwards = htsNotes.Count - i;
                htsNotes[i].sentenceDurMs = sentenceDurMs;
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
                return;
            }
            SendScore(phrase);
            if (!File.Exists(fullTimingPath)) {
                Log.Error(@"File not found.:{timingPath}");
                return;
            }
            HTSLabelFile hTSLabels = hts.load(fullTimingPath, Encoding.UTF8);
            List<Tuple<int, double, double>> timing = hTSLabels
                .Select((label, index) => Tuple.Create(index, timeAxis.TickPosToMsPos((double)label.start_time), timeAxis.TickPosToMsPos((double)label.end_time)))
                .ToList();


            //アライメント、時系列を位置系列に変換、単位ms
            var positions = new List<double>();
            List<double> alignGroup = (List<double>)timing.Select(x => timeAxis.TickPosToMsPos(x.Item2));
            //子音の開始位置がずれている
            positions.AddRange(stretch(alignGroup, 1, phAlignPoints[0].Item2));
            //セグメントごとの他の音素の線形スケーリング
            foreach (var pair in phAlignPoints.Zip(phAlignPoints.Skip(1), (a, b) => Tuple.Create(a, b))) {
                var currAlignPoint = pair.Item1;
                var nextAlignPoint = pair.Item2;
                alignGroup = alignGroup.GetRange(currAlignPoint.Item1, nextAlignPoint.Item1 - currAlignPoint.Item1);
                double ratio = (nextAlignPoint.Item2 - currAlignPoint.Item2) / alignGroup.Sum();
                positions.AddRange(stretch(alignGroup, ratio, nextAlignPoint.Item2));
            }
            //一連のポジションを刻みに変換し、結果リストに入力する。
            int index = 1;
            foreach (int groupIndex in Enumerable.Range(0, phrase.Length)) {
                string[] phonemesRedirected = (string[])htsPhonemes.Select(x => x.symbol);
                Note[] group = phrase[groupIndex];
                var noteResult = new List<Tuple<string, int>>();
                if (group[0].lyric.StartsWith("+")) {
                    continue;
                }
                double notePos = timeAxis.TickPosToMsPos(group[0].position);//音符起点位置，单位ms
                for (int phIndex = notePhIndex[groupIndex]; phIndex < notePhIndex[groupIndex + 1]; ++phIndex) {
                    if (!String.IsNullOrEmpty(phonemesRedirected[phIndex])) {
                        noteResult.Add(Tuple.Create(phonemesRedirected[phIndex], timeAxis.TicksBetweenMsPos(
                           notePos, positions[phIndex - 1])));
                    }
                }
                partResult[group[0].position] = noteResult;
            }
        }

        //缩放音素时长序列
        public List<double> stretch(IList<double> source, double ratio, double endPos) {
            //source：音素时长序列，单位ms
            //ratio：缩放比例
            //endPos：目标终点时刻，单位ms
            //输出：缩放后的音素位置，单位ms
            double startPos = endPos - source.Sum() * ratio;
            var result = CumulativeSum(source.Select(x => x * ratio).Prepend(0), startPos).ToList();
            result.RemoveAt(result.Count - 1);
            return result;
        }

        //计算累加
        public static IEnumerable<double> CumulativeSum(IEnumerable<double> sequence, double start = 0) {
            double sum = start;
            foreach (var item in sequence) {
                sum += item;
                yield return sum;
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            if (!partResult.TryGetValue(notes[0].position, out var phonemes)) {
                throw new Exception("error");
            }
            return new Result {
                phonemes = phonemes
                    .Select((tu) => new Phoneme() {
                        phoneme = tu.Item1,
                        position = tu.Item2,
                    })
                    .ToArray(),
            };
        }
    }
}
