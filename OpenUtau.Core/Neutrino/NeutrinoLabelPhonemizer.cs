using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using K4os.Hash.xxHash;
using OpenUtau.Api;
using OpenUtau.Core.Enunu;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using Serilog.Core;
using SharpCompress.Common;

namespace OpenUtau.Core.Neutrino {
    [Phonemizer("Neutrino Label Phonemizer", "NEUTRINO-LAB")]
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
            string basePath = Path.Join(PathManager.Inst.DependencyPath, "NEUTRINO");
            //Load Dictionary
            try {
                phoneDict.Clear();
                LoadDict(Path.Join(Path.Join(basePath, @".\settings\dic"), confPath), singer.TextFileEncoding);
                LoadDict(Path.Join(Path.Join(basePath, @".\settings\dic"), tablePath), singer.TextFileEncoding);
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

        protected override void SendScore(Note[][] phrase) {
            if (File.Exists(fullScorePath) && !File.Exists(fullTimingPath)) {
                var voicebankNameHash = $"{this.singer.voicebankNameHash:x16}";
                string f0Path = Path.Join(htstmpPath ,$"{voicebankNameHash}_tmp.f0");
                string melspecPath = Path.Join(htstmpPath, $"{voicebankNameHash}_tmp.melspec");
                string mgcPath = Path.Join(htstmpPath, $"{voicebankNameHash}_tmp.mgc");
                string bapPath = Path.Join(htstmpPath, $"{voicebankNameHash}_tmp.bap");
                string modelDir = this.singer.Location;
                int toneShift = phrase[0][0].phonemeAttributes != null ? phrase[0][0].phonemeAttributes[0].toneShift:0;
                int numThreads = Preferences.Default.NumRenderThreads;
                string gpuMode = string.Empty;
                switch (Preferences.Default.OnnxRunner) {
                    case "":
                        gpuMode = string.Empty ;
                        break;
                    default:
                        gpuMode = string.Empty;
                        break;
                }
                string ArgParam = $"{fullScorePath} {fullTimingPath} {f0Path} {melspecPath} {modelDir} -n 1 -o ${numThreads} -k ${toneShift} -d 2 {gpuMode}";
                ProcessRunner.Run(NeutrinoExe, ArgParam, Log.Logger);
                fullTimingPath = "";
                monoTimingPath = "";
            }
        }
    }
}
