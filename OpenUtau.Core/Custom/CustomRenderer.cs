using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Neo.IronLua;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using ThirdParty;
using static OpenUtau.Core.Util.SingerRecipeBaseResponse;
namespace OpenUtau.Core.Custom {
    public class CustomRenderer : IRenderer {

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.CLR,
            Format.Ustx.VOL,
        };

        static readonly object lockObj = new object();

        public USingerType SingerType => USingerType.Custom;
        public CustomSinger singer;

        public bool SupportsRenderPitch => singer != null ? singer.ops.ContainsKey(new RenderPitchRequest().Op) : false;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            ApiResponse<GetSuggestedExpressionsResponse> response = SvsClient.Inst.SendRequest<GetSuggestedExpressionsResponse>(new GetSuggestedExpressionsRequest());
            if (response.Error != null) {
                Log.Error(response.Error.Message);
            } else if (response.Response != null) {
                foreach (var item in response.Response.Expressions) {
                    string name = item.Name.ToLower();
                    if (name.Count() > 4) {
                        name = name.Substring(0, 4);
                    }
                    if (!supportedExp.Add(name)) {
                        Log.Warning($"{name} was skipped.");
                    }
                }
            }
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
                    var wavPath = Path.Join(PathManager.Inst.CachePath, $"vv-{phrase.hash:x16}.wav");
                    phrase.AddCacheFile(wavPath);
                    var result = Layout(phrase);
                    if (!File.Exists(wavPath)) {
                        singer = phrase.singer as CustomSinger;
                        if (singer != null) {
                            try {
                                ApiResponse<SynthAudioSamplesResponse> response = SvsClient.Inst.SendRequest<SynthAudioSamplesResponse>(new SynthAudioSamplesRequest());
                                if (response.Error != null) {
                                    Log.Error(response.Error.Message);
                                }
                                List<byte> bytes = null;
                                if (response.Response != null && response.Response.Channels != null) {
                                    // AudioChannelのSamples(double)を16bit PCMに変換してバイト配列化
                                    var channels = response.Response.Channels;
                                    var sampleRate = response.Response.SampleRate;
                                    var channelCount = response.Response.ChannelCount;
                                    var sampleFormat = response.Response.SampleFormat;
                                    // ここでは16bit PCM, 1chのみ対応（必要に応じて拡張）
                                    if (channels.Count > 0) {
                                        var samples = channels[0].Samples;
                                        bytes = new List<byte>(samples.Count * 2);
                                        foreach (var sample in samples) {
                                            // -1.0～1.0のdoubleを16bit PCMに変換
                                            var intSample = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, sample * short.MaxValue));
                                            bytes.Add((byte)(intSample & 0xFF));
                                            bytes.Add((byte)((intSample >> 8) & 0xFF));
                                        }
                                        File.WriteAllBytes(wavPath, bytes.ToArray());
                                    }
                                }
                            } catch (Exception e) {
                                Log.Error(e, "Failed to create a voice base.");
                            }
                            if (cancellation.IsCancellationRequested) {
                                return new RenderResult();
                            }
                        }
                    }
                    progress.Complete(phrase.phones.Length, progressInfo);
                    if (File.Exists(wavPath)) {
                        using (var waveStream = new WaveFileReader(wavPath)) {

                            result.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                        }
                        if (result.samples != null) {
                            Renderers.ApplyDynamics(phrase, result);
                        }
                    }
                    return result;
                }
            });
            return task;
        }
        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            //under development
            var result = new List<UExpressionDescriptor>();
            var singer_ = singer as CustomSinger;
            if (singer_.ops.ContainsKey(new GetSuggestedExpressionsRequest().Op)) {
                ApiResponse<GetSuggestedExpressionsResponse> response = SvsClient.Inst.SendRequest<GetSuggestedExpressionsResponse>(new GetSuggestedExpressionsRequest());
                if (response.Error != null) {
                    return null;
                }else if (response.Response != null) {
                    foreach (var exp in response.Response.Expressions) {
                        result.Add(new UExpressionDescriptor(exp.Name, exp.Name, exp.Range[0], exp.Range[1], exp.Default ?? exp.Range[0]));
                    }
                }
            }
            return result.ToArray();
        }

        public override string ToString() => Renderers.CUSTOM;

        RenderPitchResult IRenderer.LoadRenderedPitch(RenderPhrase phrase) {
            return null;
        }
    }
}
