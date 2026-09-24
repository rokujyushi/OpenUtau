using System.Linq;
using Xunit;

namespace OpenUtau.Core.Render {
    public class PitchRetakeTest {
        [Fact]
        public void GetRetakeFrameRanges_NullMaskReturnsFullRange() {
            var ranges = PitchRetake.GetRetakeFrameRanges(null, 5).ToArray();

            Assert.Equal(new[] { (0, 5) }, ranges);
        }

        [Fact]
        public void GetRetakeFrameRanges_SeparatesNonAdjacentRetakeRegions() {
            var mask = new[] { false, true, true, false, false, true, true, false };

            var ranges = PitchRetake.GetRetakeFrameRanges(mask, mask.Length).ToArray();

            Assert.Equal(new[] { (1, 3), (5, 7) }, ranges);
        }

        [Fact]
        public void GetRetakeFrameRanges_TreatsFramesPastMaskAsUnselected() {
            var mask = new[] { false, true, true };

            var ranges = PitchRetake.GetRetakeFrameRanges(mask, 6).ToArray();

            Assert.Equal(new[] { (1, 3) }, ranges);
        }

        [Fact]
        public void GetRetakeFrameRanges_EmptySelectionReturnsNoRanges() {
            var ranges = PitchRetake.GetRetakeFrameRanges(
                new[] { false, false, false }, 3).ToArray();

            Assert.Empty(ranges);
        }
    }
}
