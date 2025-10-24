using System;
using System.Linq;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.Util {
    public class HtsSpecTests {
        private HTSNote MakeNote(int startMs, int endMs, int positionTicks, int durationTicks, int positionBar, string accent = "") {
            var symbols = new[] { "a" };
            var beatPerBar = 4;
            var beatUnit = 4;
            var key = 0;
            double bpm = 120;
            var tone = 60; // C4
            var isSlur = false;
            var isRest = false;
            var lang = "JPN";
            var accentStr = accent;
            var note = new HTSNote(symbols, beatPerBar, beatUnit, positionBar, 0, key, bpm, tone, isSlur, isRest, lang, accentStr, startMs, endMs, positionTicks, durationTicks);
            return note;
        }

        private HTSPhrase BuildPhrase(HTSNote[] notes, int resolution) {
            var phrase = new HTSPhrase(notes) { resolution = resolution };
            var sentenceDurMs = notes.Sum(n => n.durationMs);
            var sentenceDurTicks = notes.Sum(n => n.durationTicks);
            for (var i = 0; i < notes.Length; i++) {
                var n = notes[i];
                n.parent = phrase;
                n.index = i + 1;
                n.indexBackwards = notes.Length - i;
                n.sentenceDurMs = sentenceDurMs;
                n.sentenceDurTicks = sentenceDurTicks;
                if (i > 0) {
                    notes[i - 1].next = n;
                    n.prev = notes[i - 1];
                }
            }
            return phrase;
        }

        [Fact]
        public void MeasureForwardBackwardAreComputedPerBar() {
            var res = 480; // ticks per quarter
            var ticksPer96 = res / 24; // 20
            var n0 = MakeNote(0, 1000, 0, 480, 0);
            var n1 = MakeNote(1000, 2000, 480, 480, 0);
            var n2 = MakeNote(2000, 3000, 960, 480, 0);
            var phrase = BuildPhrase(new[] { n0, n1, n2 }, res);

            var e0 = n0.e();
            var e1 = n1.e();
            var e2 = n2.e();

            // forward index (e10)
            Assert.Equal("0", e0[9]);
            Assert.Equal("1", e1[9]);
            Assert.Equal("2", e2[9]);
            // backward index (e11)
            Assert.Equal("2", e0[10]);
            Assert.Equal("1", e1[10]);
            Assert.Equal("0", e2[10]);

            // forward ms in centiseconds (e12)
            Assert.Equal("0", e0[11]);
            Assert.Equal("100", e1[11]);
            Assert.Equal("200", e2[11]);
            // backward ms in centiseconds (e13)
            Assert.Equal("200", e0[12]);
            Assert.Equal("100", e1[12]);
            Assert.Equal("0", e2[12]);

            // forward 96th (e14)
            Assert.Equal("0", e0[13]);
            Assert.Equal((480 / ticksPer96).ToString(), e1[13]);
            Assert.Equal((960 / ticksPer96).ToString(), e2[13]);
            // backward 96th (e15)
            Assert.Equal((960 / ticksPer96).ToString(), e0[14]);
            Assert.Equal((480 / ticksPer96).ToString(), e1[14]);
            Assert.Equal("0", e2[14]);

            // forward percent (e16)
            Assert.Equal("0", e0[15]);
            Assert.Equal("33", e1[15]);
            Assert.Equal("66", e2[15]);
            // backward percent (e17)
            Assert.Equal("66", e0[16]);
            Assert.Equal("33", e1[16]);
            Assert.Equal("0", e2[16]);
        }

        [Fact]
        public void AccentDistancesForwardBackward() {
            var res = 480;
            var ticksPer96 = res / 24; // 20
            var n0 = MakeNote(0, 1000, 0, 480, 0, accent: "");
            var n1 = MakeNote(1000, 2000, 480, 480, 0, accent: "A");
            var n2 = MakeNote(2000, 3000, 960, 480, 0, accent: "");
            var n3 = MakeNote(3000, 4000, 1440, 480, 0, accent: "A");
            var phrase = BuildPhrase(new[] { n0, n1, n2, n3 }, res);

            var e0 = n0.e();
            var e1 = n1.e();
            var e2 = n2.e();
            var e3 = n3.e();

            // For n2 (between accents): distances should be 1 note, 100 cs, 24 (96th)
            Assert.Equal("1", e2[28]); // next accent (notes)
            Assert.Equal("1", e2[29]); // prev accent (notes)
            Assert.Equal("100", e2[30]); // next accent (cs)
            Assert.Equal("100", e2[31]); // prev accent (cs)
            Assert.Equal((480 / ticksPer96).ToString(), e2[32]); // next (96th)
            Assert.Equal((480 / ticksPer96).ToString(), e2[33]); // prev (96th)

            // For n1 (accent): prev distance is 0, next accent is one note away (n2)
            Assert.Equal("1", e1[28]); // next accent (n3 via one note n2)
            Assert.Equal("0", e1[29]); // prev accent (itself)
            Assert.Equal("100", e1[30]); // next accent (cs)
            Assert.Equal("0", e1[31]); // prev accent (cs)
        }
    }
}
