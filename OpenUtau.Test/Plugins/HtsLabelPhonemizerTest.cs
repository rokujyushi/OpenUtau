using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Xunit;
using Xunit.Abstractions;

namespace OpenUtau.Plugins {
    // Minimal concrete HTSLabelPhonemizer for testing without external aligners.
    class DummyHtsLabelPhonemizer : HTSLabelPhonemizer {
        public DummyHtsLabelPhonemizer() {
            // Minimal language and symbol classes
            lang = "JPN";
            vowels = new List<string> { "a", "i", "u", "e", "o" };
            pauses = new List<string> { "pau" };
            silences = new List<string> { "sil" };
            breaks = new List<string> { "br" };
        }

        protected override IG2p LoadG2p(string rootPath) {
            // Provide a tiny JP-like dictionary: simple CV mapping.
            var builder = G2pDictionary.NewBuilder();
            // vowels
            builder.AddSymbol("a", true);
            builder.AddSymbol("i", true);
            builder.AddSymbol("u", true);
            builder.AddSymbol("e", true);
            builder.AddSymbol("o", true);
            // consonants
            var cons = new[] { "k", "s", "t", "n", "h", "m", "y", "r", "w" };
            foreach (var c in cons) builder.AddSymbol(c, false);
            // pauses etc
            builder.AddSymbol("pau", false);
            builder.AddSymbol("sil", false);
            builder.AddSymbol("br", false);
            // single vowels
            builder.AddEntry("a", new[] { "a" });
            builder.AddEntry("i", new[] { "i" });
            builder.AddEntry("u", new[] { "u" });
            builder.AddEntry("e", new[] { "e" });
            builder.AddEntry("o", new[] { "o" });
            // CV (subset)
            builder.AddEntry("ka", new[] { "k", "a" });
            builder.AddEntry("ki", new[] { "k", "i" });
            builder.AddEntry("ku", new[] { "k", "u" });
            builder.AddEntry("ke", new[] { "k", "e" });
            builder.AddEntry("ko", new[] { "k", "o" });
            builder.AddEntry("ta", new[] { "t", "a" });
            builder.AddEntry("ti", new[] { "t", "i" });
            builder.AddEntry("to", new[] { "t", "o" });
            builder.AddEntry("na", new[] { "n", "a" });
            builder.AddEntry("ni", new[] { "n", "i" });
            builder.AddEntry("no", new[] { "n", "o" });
            builder.AddEntry("ma", new[] { "m", "a" });
            builder.AddEntry("mi", new[] { "m", "i" });
            builder.AddEntry("mo", new[] { "m", "o" });
            builder.AddEntry("ra", new[] { "r", "a" });
            builder.AddEntry("ri", new[] { "r", "i" });
            builder.AddEntry("ro", new[] { "r", "o" });
            return builder.Build();
        }

        protected override HTSNote CustomHTSNoteContext(HTSNote htsNote, Phonemizer.Note note) {
            return htsNote; // no-op
        }

        protected override HTSPhoneme[] CustomHTSPhonemeContext(HTSPhoneme[] htsPhonemes, Phonemizer.Note[] notes) {
            return htsPhonemes; // no-op
        }

        protected override Phonemizer.Note[][] PhraseAdjustments(Phonemizer.Note[][] phrese) {
            return phrese; // no-op
        }

        protected override void SendScore(Phonemizer.Note[][] phrase) {
            // Create a fake mono_timing.lab with uniform 100ms durations for each phoneme in full_score.lab
            if (!Directory.Exists(htstmpPath)) {
                Directory.CreateDirectory(htstmpPath);
            }
            int count = 0;
            if (File.Exists(fullScorePath)) {
                count = File.ReadLines(fullScorePath).Count();
            }
            long start = 0;
            var lines = new List<string>(count);
            for (int i = 0; i < count; i++) {
                long end = start + 1_000_000; // 100ms in 100ns units
                lines.Add($"{start} {end} a");
                start = end;
            }
            File.WriteAllLines(monoTimingPath, lines);
        }
    }

    public class HtsLabelPhonemizerTest : PhonemizerTestBase {
        public HtsLabelPhonemizerTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() {
            return new DummyHtsLabelPhonemizer();
        }

        [Theory]
        [InlineData(new string[] { "a" }, new string[] { "a" })]
        [InlineData(new string[] { "a", "i" }, new string[] { "a", "i" })]
        [InlineData(new string[] { "a", "+~a", "i" }, new string[] { "a", "i" })] // extension note should not duplicate symbols
        // JP CV
        [InlineData(new string[] { "ka" }, new string[] { "k", "a" })]
        [InlineData(new string[] { "ka", "ki" }, new string[] { "k", "a", "k", "i" })]
        [InlineData(new string[] { "ka", "+~a", "ki" }, new string[] { "k", "a", "k", "i" })]
        public void BasicHtsPipelineTest(string[] lyrics, string[] aliases) {
            SameAltsTonesColorsTest("en_delta0", lyrics, aliases, "", "C4", "");
        }
    }
}
