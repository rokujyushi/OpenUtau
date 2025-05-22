using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using K4os.Hash.xxHash;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using Serilog.Core;
using SharpCompress.Common;

namespace OpenUtau.Core.Neutrino {
    [Phonemizer("Neutrino Label Phonemizer", "NEUTRINO")]
    public class Neutrino : HTSLabelPhonemizer {
        readonly string PhonemizerType = "NEUTRINO";
        string NeutrinoExe = string.Empty;
        string NsfExe = string.Empty;
        string WorldExe = string.Empty;

        protected NeutrinoSinger singer;

        struct TimingResult {
            public string path_full_timing;
            public string path_mono_timing;
        }

        struct TimingResponse {
            public string error;
            public TimingResult result;
        }

        public override void SetSinger(USinger singer) {
            string confPath = "japanese.utf_8.conf";
            tablePath = "japanese.utf_8.table";
            this.singer = singer as NeutrinoSinger;
            if (this.singer == null) {
                return;
            }
            string basePath = Path.Join(PathManager.Inst.DependencyPath, "NEUTRINO");
            //Load Dictionary
            try {
                phoneDict.Clear();
                LoadDict(Path.Join(Path.Join(basePath, @".\settings\dic"), confPath), singer.TextFileEncoding);
                LoadDict(Path.Join(Path.Join(basePath, @".\settings\dic"), tablePath), singer.TextFileEncoding);
                g2p = this.LoadG2p();
            } catch (Exception e) {
                Log.Error(e, $"failed to load dictionary from {tablePath}");
                return;
            }
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
        protected IG2p LoadG2p() {
            var g2ps = new List<IG2p>();
            var builder = G2pDictionary.NewBuilder();
            vowels.AddRange(phoneDict["VOWELS"]);
            breaks.AddRange(phoneDict["BREAK"]);
            pauses.AddRange(phoneDict["PAUSES"]);
            silences.AddRange(phoneDict["SILENCES"]);
            macron.AddRange(phoneDict["MACRON"]);
            consonants.AddRange(phoneDict["PHONEME_CL"]);
            foreach (var dict in phoneDict.Values) {
                foreach (var phoneme in dict) {
                    if (!consonants.Contains(phoneme) && !vowels.Contains(phoneme) &&
                        !breaks.Contains(phoneme) && !pauses.Contains(phoneme) &&
                        !silences.Contains(phoneme) && !macron.Contains(phoneme)) {
                        consonants.Add(phoneme);
                    }
                    if (!consonants.Contains(phoneme)) {
                        builder.AddSymbol(phoneme, true);
                    }else {
                        builder.AddSymbol(phoneme, false);
                    }
                }
            }
            foreach (var entry in phoneDict.Keys) {
                builder.AddEntry(entry, phoneDict[entry]);
            }
            g2ps.Add(builder.Build());
            return new G2pFallbacks(g2ps.ToArray());
        }

        protected override void SendScore(Note[][] phrase) {
            if (File.Exists(fullScorePath) && !File.Exists(fullTimingPath)) {
                var voicebankNameHash = $"{this.singer.voicebankNameHash:x16}";
                string f0Path = Path.Join(htstmpPath, $"{voicebankNameHash}_tmp.f0");
                string melspecPath = Path.Join(htstmpPath, $"{voicebankNameHash}_tmp.melspec");
                string modelDir = this.singer.Location;
                var attr = phrase[0][0].phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
                int toneShift = attr.toneShift;
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
                string ArgParam = $"{fullScorePath} {fullTimingPath} {f0Path} {melspecPath} {modelDir} -n 1 -o {numThreads} -k {toneShift} -d 2 {gpuMode}";
                ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                fullTimingPath = "";
                monoTimingPath = "";
            }
        }
    }
}
