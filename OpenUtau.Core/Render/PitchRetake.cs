using System;
using System.Collections.Generic;

namespace OpenUtau.Core.Render {
    public static class PitchRetake {
        /// <summary>
        /// Splits <see cref="RenderPitchResult.retakeMask"/> into contiguous frame ranges to write back.
        /// A null mask means the whole phrase was retaken.
        /// </summary>
        public static IEnumerable<(int start, int end)> GetRetakeFrameRanges(
            IReadOnlyList<bool>? retakeMask, int frameCount) {
            if (retakeMask == null) {
                if (frameCount > 0) {
                    yield return (0, frameCount);
                }
                yield break;
            }
            int limit = Math.Min(retakeMask.Count, frameCount);
            int start = -1;
            for (int i = 0; i < limit; i++) {
                if (retakeMask[i]) {
                    if (start < 0) {
                        start = i;
                    }
                } else if (start >= 0) {
                    yield return (start, i);
                    start = -1;
                }
            }
            if (start >= 0) {
                yield return (start, limit);
            }
        }
    }
}
