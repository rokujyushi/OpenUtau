using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using PoinoSing;
using Serilog;

namespace OpenUtau.Core.PoinoSing {
    public class PoinoSingRenderer : IRenderer {
        const string PITD = Format.Ustx.PITD;
        const string VOLC = "volc";
        const string RATI = "rati";

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            PITD,
            RATI,
        };

        static readonly object lockObj = new object();

        public USingerType SingerType => USingerType.PoinoSing;

        public bool SupportsRenderPitch => false;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        public RenderResult Layout(RenderPhrase phrase) {
            return new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,
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
                    var wavPath = Path.Join(PathManager.Inst.CachePath, $"po-{phrase.hash:x16}.wav");
                    phrase.AddCacheFile(wavPath);
                    var result = Layout(phrase);
                    if (!File.Exists(wavPath)) {
                        var singer = phrase.singer as PoinoSingSinger;
                        if (singer != null) {
                            try {
                                Log.Information($"Starting PoinoSing synthesis");

                                // 1) 相対化の基準（beginSec）
                                double beginSec = phrase.positionMs / 1000.0;
                                double endSec = phrase.endMs / 1000.0;

                                // 2) phones（絶対ms -> 相対秒）
                                var phones = new List<Phone>(phrase.phones.Length);
                                foreach (var ph in phrase.phones) {
                                    phones.Add(new Phone {
                                        Phoneme = ph.phoneme,
                                        PositionSec = ph.positionMs / 1000.0 - beginSec, // 相対化
                                        DurationSec = ph.durationMs / 1000.0,
                                        Volume = ph.volume
                                    });
                                }

                                // 3) ピッチ（5tickグリッドの時間列を生成）
                                //    起点tick = phrase.position - phrase.leading（RenderPhrase内部と同じ原点）
                                const int pitchIntervalTick = 5;
                                int baseTick = phrase.position - phrase.leading;

                                var pitchCents = new List<double>(phrase.pitches.Length);
                                var pitchTimesSec = new List<double>(phrase.pitches.Length);
                                for (int i = 0; i < phrase.pitches.Length; i++) {
                                    int tTick = baseTick + i * pitchIntervalTick; // 絶対tick
                                    double tSec = phrase.timeAxis.TickPosToMsPos(tTick) / 1000.0;
                                    pitchCents.Add(phrase.pitches[i]);            // セント値そのまま
                                    pitchTimesSec.Add(tSec - beginSec);           // 相対秒にする
                                }

                                // 4) ダイナミクス（存在する場合。サンプリングも5tickなので上と同じ時間列を再計算）
                                List<double>? dynamics = null;
                                List<double>? dynamicsTimes = null;
                                if (phrase.dynamics != null) {
                                    dynamics = new List<double>(phrase.dynamics.Length);
                                    dynamicsTimes = new List<double>(phrase.dynamics.Length);
                                    for (int i = 0; i < phrase.dynamics.Length; i++) {
                                        int tTick = baseTick + i * pitchIntervalTick;
                                        double tSec = phrase.timeAxis.TickPosToMsPos(tTick) / 1000.0;
                                        dynamics.Add(phrase.dynamics[i]);          // 0..1 を想定（dB系なら事前変換済み）
                                        dynamicsTimes.Add(tSec - beginSec);
                                    }
                                }

                                var request = new SynthesizeRenderPhraseRequest {
                                    SpeakerId = singer.speaker.Id,
                                    BeginSec = beginSec,
                                    EndSec = endSec,
                                    Phones = phones,
                                    PitchCents = pitchCents,
                                    PitchTimesSec = pitchTimesSec,
                                    Dynamics = dynamics,
                                    DynamicsTimesSec = dynamicsTimes
                                };

                                var queryurl = new PoinoSingURL() { method = "POST", path = "/synthesize-phonemes", body = JsonConvert.SerializeObject(request), accept = "audio/wav" };
                                var response = PoinoSingClient.Inst.SendRequest(queryurl);
                                byte[] bytes = null;
                                if (!response.Item2.Equals(null)) {
                                    bytes = response.Item2;
                                } else if (!string.IsNullOrEmpty(response.Item1)) {
                                    var jObj = JObject.Parse(response.Item1);
                                    if (jObj.ContainsKey("detail")) {
                                        Log.Error($"Failed to create a voice base. : {jObj}");
                                    }
                                }
                                if (bytes != null) {
                                    File.WriteAllBytes(wavPath, bytes);
                                }
                            } catch (Exception e) {
                                Log.Error($"Failed to create a voice base.:{e}");
                            }
                            if (cancellation.IsCancellationRequested) {
                                return new RenderResult();
                            }
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
        public override string ToString() => Renderers.POINOSHING;

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            throw new NotImplementedException();
        }

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            //under development
            var result = new List<UExpressionDescriptor> {
                //volumes
                new UExpressionDescriptor{
                    name="input volume (curve)",
                    abbr=VOLC,
                    type=UExpressionType.Curve,
                    min=0,
                    max=200,
                    defaultValue=100,
                    isFlag = false,
                },
            };

            return result.ToArray();
        }

        // 1) noteIndex でグルーピング
        private static ILookup<int, RenderPhone> GroupByNoteIndex(RenderPhone[] phones) {
            return phones.ToLookup(p => p.noteIndex);
        }

        // 2) phoneme を分割して配列に（"k,s" -> ["k","s"]）
        private static string[] ParsePhonemeParts(string phoneme) {
            if (string.IsNullOrEmpty(phoneme)) return Array.Empty<string>();
            return phoneme
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
        }

        // 3) flags から比率を取得（なければ 0.5）。0–1 に正規化
        private static double ResolveRatio(RenderPhone phone, string abbr) {
            var flag = phone.flags?.FirstOrDefault(f =>
                string.Equals(f.Item3, abbr, StringComparison.OrdinalIgnoreCase));
            if (flag != null && flag.Item2.HasValue) {
                return Math.Clamp(flag.Item2.Value / 100.0, 0.0, 1.0);
            }
            return 0.5;
        }

        // 4) 分割した音素へ比率を付与して "k:1,s:0.5" 形式に
        private static string BuildPhonemeWithRatios(string[] parts, double ratioForRest) {
            if (parts.Length == 0) return string.Empty;
            if (parts.Length == 1) return parts[0]; // 単一ならそのまま
            return string.Join(",", parts.Select((ph, i) => $"{ph}:{(i == 0 ? "1" : ratioForRest.ToString("0.####"))}"));
        }

        // 5) まとめて noteIndex => string[] に変換
        private static Dictionary<int, string[]> BuildPhonemesByNote(RenderPhone[] phones, string ratioAbbr) {
            var groups = GroupByNoteIndex(phones);
            var dict = new Dictionary<int, string[]>();
            foreach (var g in groups) {
                var list = new List<string>();
                foreach (var phone in g) {
                    var parts = ParsePhonemeParts(phone.phoneme);
                    var ratio = ResolveRatio(phone, ratioAbbr);
                    list.Add(BuildPhonemeWithRatios(parts, ratio));
                }
                dict[g.Key] = list.ToArray();
            }
            return dict;
        }

        // ヘルパーを既存ヘルパー群の下に追加

        // noteIndex に対応する最初の音素の toneShift を返す（なければ 0）
        private static int GetNoteToneShift(ILookup<int, RenderPhone> groups, int noteIndex) {
            var firstPhone = groups[noteIndex].FirstOrDefault();
            return firstPhone == null ? 0 : firstPhone.toneShift;
        }
    }
}
