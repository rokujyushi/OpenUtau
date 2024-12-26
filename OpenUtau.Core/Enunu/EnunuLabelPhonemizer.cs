using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Hash.xxHash;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Enunu {
    [Phonemizer("Enunu Label Phonemizer", "ENUNU-LAB")]
    public class EnunuLabelPhonemizer : HTSLabelPhonemizer {
        readonly string PhonemizerType = "ENUNU";

        protected EnunuSinger singer;
        protected string port;

        struct TimingResult {
            public string path_full_timing;
            public string path_mono_timing;
        }

        struct TimingResponse {
            public string error;
            public TimingResult result;
        }

        public override void SetSinger(USinger singer) {
            base.SetSinger(singer);
            this.singer = singer as EnunuSinger;
            if (port == null) {
                port = EnunuUtils.SetPortNum();
            }
        }

        protected override void SendScore(Note[][] phrase) {
            if (File.Exists(fullScorePath) && !File.Exists(fullTimingPath)) {
                var voicebankNameHash = $"{this.singer.voicebankNameHash:x16}";
                var response = EnunuClient.Inst.SendRequest<TimingResponse>(new string[] { "timing", fullScorePath, "", voicebankNameHash, "600" }, port);
                if (response.error != null) {
                    throw new Exception(response.error);
                }
                fullTimingPath = response.result.path_full_timing;
                monoTimingPath = response.result.path_mono_timing;
            }
        }
    }
}
