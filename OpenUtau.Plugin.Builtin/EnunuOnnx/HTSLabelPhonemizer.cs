using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.Enunu;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin.EnunuOnnx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    public class HTSLabelPhonemizer : Phonemizer {
        protected USinger singer;
        Dictionary<Note[], Phoneme[]> partResult = new Dictionary<Note[], Phoneme[]>();
        //information used by HTS writer
        protected Dictionary<string, string[]> phoneDict;
        protected string[] vowels;
        protected string[] breaks;
        protected string[] pauses;
        protected string[] silences;
        protected string[] unvoiced;
        protected string[] macron;
        string defaultPause = "pau";

        //information used by openutau phonemizer
        protected IG2p g2p;
        int paddingMs = 500;//子音の持続時間

        protected string monoScorePath;
        protected string fullScorePath;
        protected string monoTimingPath;
        protected string fullTimingPath;

        public struct LabelNote {
            Note note;
            int index;
        }

        public HTSLabelPhonemizer() {
        }


        public override void SetSinger(USinger singer) {
            this.singer = singer;
            //Load g2p from enunux.yaml
            //g2p dict should be load after enunu dict
            try {
                this.g2p = LoadG2p(singer.Location);
            } catch (Exception e) {
                Log.Error(e, "failed to load g2p dictionary");
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

        public override void SetUp(Note[][] notes, UProject project, UTrack track) {
            base.SetUp(notes, project, track);
        }

        //make a HTS Note from given symbols and UNotes
        protected HTSNote makeHtsNote(string[] symbols, IList<Note> group, int startTick) {
            return new HTSNote(
                symbols: symbols,
                tone: group[0].tone,
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
            if (pauses.Contains(phoneme)) {
                return "p";
            }
            if (silences.Contains(phoneme)) {
                return "s";
            }
            if (breaks.Contains(phoneme)) {
                return "b";
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

        static int[] LabelToNoteIndex(string scorePath, EnunuNote[] enunuNotes) {
            var result = new List<int>();
            int lastPos = 0;
            int index = 0;
            var score = ParseLabel(scorePath);
            foreach (var p in score) {
                if (p.position != lastPos) {
                    index++;
                    lastPos = p.position;
                }
                result.Add(enunuNotes[index].noteIndex);
            }
            return result.ToArray();
        }

        static Phoneme[] ParseLabel(string path) {
            var phonemes = new List<Phoneme>();
            using (var reader = new StreamReader(path, Encoding.UTF8)) {
                while (!reader.EndOfStream) {
                    var line = reader.ReadLine();
                    var parts = line.Split();
                    if (parts.Length == 3 &&
                        long.TryParse(parts[0], out long pos) &&
                        long.TryParse(parts[1], out long end)) {
                        phonemes.Add(new Phoneme {
                            phoneme = parts[2],
                            position = (int)(pos / 1000L),
                        });
                    }
                }
            }
            return phonemes.ToArray();
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            if (partResult.TryGetValue(notes, out var phonemes)) {
                return new Result {
                    phonemes = phonemes.Select(p => {
                        double posMs = p.position * 0.1;
                        p.position = MsToTick(posMs) - notes[0].position;
                        return p;
                    }).ToArray(),
                };
            }
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme {
                        phoneme = "error",
                    }
                },
            };
        }

        public override void CleanUp() {
            partResult.Clear();
        }
    }
}
