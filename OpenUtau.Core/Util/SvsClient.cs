using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using Serilog;

namespace OpenUtau.Core.Util {
    public abstract class ApiRequest {
        [JsonPropertyName("op")]
        public string Op { get; init; } = "";
    }

    public abstract class SingerRequest : ApiRequest {
        [JsonPropertyName("singer_id")]
        public string? SingerId { get; init; }
    }

    public sealed class OpsRequest : ApiRequest {
        public OpsRequest() {
            Op = "ops";
        }
    }

    public sealed class CapabilitiesRequest : ApiRequest {
        public CapabilitiesRequest() {
            Op = "capabilities";
        }
    }

    public sealed class ListSingersRequest : ApiRequest {
        public ListSingersRequest() {
            Op = "list_singers";
        }
    }

    public sealed class GetSingerInfoRequest : SingerRequest {
        public GetSingerInfoRequest() {
            Op = "get_singer_info";
        }
    }

    public sealed class GetSuggestedExpressionsRequest : SingerRequest {
        public GetSuggestedExpressionsRequest() {
            Op = "get_suggested_expressions";
        }
    }

    public sealed class RenderPitchRequest : SingerRequest {
        public RenderPitchRequest() {
            Op = "render_pitch";
        }

        [JsonPropertyName("note_sequence")]
        public NoteSequence? NoteSequence { get; init; }
    }

    public sealed class PhonemizeRequest : SingerRequest {
        public PhonemizeRequest() {
            Op = "phonemize";
        }

        [JsonPropertyName("note_sequence")]
        public NoteSequence? NoteSequence { get; init; }
    }

    public sealed class SynthAudioSamplesRequest : SingerRequest {
        public SynthAudioSamplesRequest() {
            Op = "synth_audio_samples";
        }

        [JsonPropertyName("phoneme_sequence")]
        public PhonemeSequence? PhonemeSequence { get; init; }

        [JsonPropertyName("sample_rate")]
        public int? SampleRate { get; init; }

        [JsonPropertyName("sample_format")]
        public string? SampleFormat { get; init; }
    }
    public sealed class NoteSequence {
        [JsonPropertyName("time_unit")]
        public string TimeUnit { get; init; } = "ms";

        [JsonPropertyName("notes")]
        public List<NoteItem> Notes { get; init; } = new();
    }

    public sealed class NoteItem {
        [JsonPropertyName("lyric")]
        public string Lyric { get; init; } = "";

        [JsonPropertyName("duration")]
        public double Duration { get; init; }

        [JsonPropertyName("key")]
        public int Key { get; init; }

        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; init; }
    }

    public sealed class PhonemeSequence {
        [JsonPropertyName("time_unit")]
        public string TimeUnit { get; init; } = "ms";

        [JsonPropertyName("phonemes")]
        public List<PhonemeItem> Phonemes { get; init; } = new();
    }

    public sealed class PhonemeItem {
        [JsonPropertyName("note_index")]
        public int? NoteIndex { get; init; }

        [JsonPropertyName("phoneme")]
        public string Phoneme { get; init; } = "";

        [JsonPropertyName("duration")]
        public double Duration { get; init; }

        [JsonPropertyName("key")]
        public int Key { get; init; }

        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; init; }
    }
    public sealed class ApiResponse<T> {
        [JsonProperty("error")]
        public ApiError? Error { get; set; }

        [JsonProperty("response")]
        public T? Response { get; set; }
    }

    public sealed class ApiError {
        [JsonProperty("code")]
        public string Code { get; set; } = "";

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("details")]
        public Dictionary<string, object>? Details { get; set; }
    }
    public sealed class OpsResponse {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("version")]
        public string Version { get; set; } = "";

        [JsonProperty("ops")]
        public List<OpDefinition> Ops { get; set; } = new();
    }

    public sealed class OpDefinition {
        [JsonProperty("op")]
        public string Op { get; set; } = "";

        [JsonProperty("inputs")]
        public Dictionary<string, FieldSpec> Inputs { get; set; } = new();

        [JsonProperty("outputs")]
        public Dictionary<string, FieldSpec> Outputs { get; set; } = new();
    }

    public sealed class FieldSpec {
        [JsonProperty("required", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Required { get; set; }
    }
    public sealed class CapabilitiesResponse {
        [JsonProperty("render_pitch")]
        public RenderPitchCapability? RenderPitch { get; set; }

        [JsonProperty("expressions")]
        public ExpressionsCapability? Expressions { get; set; }
    }

    public sealed class RenderPitchCapability {
        [JsonProperty("supported")]
        public bool Supported { get; set; }

        [JsonProperty("input")]
        public string Input { get; set; } = "";

        [JsonProperty("output")]
        public string Output { get; set; } = "";
    }

    public sealed class ExpressionsCapability {
        [JsonProperty("supported")]
        public bool Supported { get; set; }

        [JsonProperty("custom")]
        public bool Custom { get; set; }

        [JsonProperty("definitions")]
        public List<ExpressionDefinition> Definitions { get; set; } = new();
    }

    public sealed class ExpressionDefinition {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("type")]
        public string Type { get; set; } = "";

        [JsonProperty("range")]
        public List<float> Range { get; set; } = new();

        [JsonProperty("scale", NullValueHandling = NullValueHandling.Ignore)]
        public string? Scale { get; set; }

        [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
        public float? Default { get; set; }
    }
    public sealed class ListSingersResponse {
        [JsonProperty("singers")]
        public List<SingerSummary> Singers { get; set; } = new();
    }

    public sealed class SingerSummary {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("languages")]
        public List<string> Languages { get; set; } = new();

        [JsonProperty("backend")]
        public string Backend { get; set; } = "";
    }

    public sealed class GetSingerInfoResponse {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("languages")]
        public List<string> Languages { get; set; } = new();

        [JsonProperty("phonemes")]
        public List<string> Phonemes { get; set; } = new();

        [JsonProperty("dictionary")]
        public Dictionary<string, string[]> Dict { get; set; } = new();

        [JsonProperty("phoneme_format")]
        public string? PhonemeFormat { get; set; }

        [JsonProperty("ranges", NullValueHandling = NullValueHandling.Ignore)]
        public SingerRanges? Ranges { get; set; }

        [JsonProperty("styles")]
        public List<string> Styles { get; set; } = new();

        [JsonProperty("expressions")]
        public List<string> Expressions { get; set; } = new();

        [JsonProperty("recipe", NullValueHandling = NullValueHandling.Ignore)]
        public SingerRecipeResponse? Recipe { get; set; }
    }

    public sealed class SingerRanges {
        [JsonProperty("pitch")]
        public List<int> Pitch { get; set; } = new();
    }

    public sealed class SingerRecipeResponse {
        [JsonProperty("base")]
        public SingerRecipeBaseResponse? Base { get; set; }
    }

    public sealed class SingerRecipeBaseResponse {
        [JsonProperty("scale")]
        public double Scale { get; set; }

        [JsonProperty("vibratoDepth")]
        public double VibratoDepth { get; set; }

        [JsonProperty("vibratoRate")]
        public double VibratoRate { get; set; }

        [JsonProperty("aspiration")]
        public double Aspiration { get; set; }

        [JsonProperty("tilt")]
        public double Tilt { get; set; }

        [JsonProperty("effort")]
        public double Effort { get; set; }

        public sealed class GetSuggestedExpressionsResponse {
            [JsonProperty("expressions")]
            public List<ExpressionDefinition> Expressions { get; set; } = new();
        }

        public sealed class RenderPitchResponse {
            [JsonProperty("f0")]
            public F0Data? F0 { get; set; }
        }

        public sealed class F0Data {
            [JsonProperty("time_unit")]
            public string TimeUnit { get; set; } = "ms";

            [JsonProperty("frame_duration")]
            public double FrameDuration { get; set; }

            [JsonProperty("f0")]
            public List<double> Values { get; set; } = new();
        }
        public sealed class PhonemizeResponse {
            [JsonProperty("phoneme_sequence")]
            public PhonemeSequence? PhonemeSequence { get; set; }
        }

        public sealed class PhonemeSequence {
            [JsonProperty("time_unit")]
            public string TimeUnit { get; set; } = "ms";

            [JsonProperty("phonemes")]
            public List<PhonemeItem> Phonemes { get; set; } = new();
        }

        public sealed class PhonemeItem {
            [JsonProperty("note_index", NullValueHandling = NullValueHandling.Ignore)]
            public int? NoteIndex { get; set; }

            [JsonProperty("phoneme")]
            public string Phoneme { get; set; } = "";

            [JsonProperty("duration")]
            public double Duration { get; set; }

            [JsonProperty("key")]
            public int Key { get; set; }
        }
        public sealed class SynthAudioSamplesResponse {
            [JsonProperty("channel_count")]
            public int ChannelCount { get; set; }

            [JsonProperty("sample_rate")]
            public int SampleRate { get; set; }

            [JsonProperty("sample_format")]
            public string SampleFormat { get; set; } = "";

            [JsonProperty("channels")]
            public List<AudioChannel> Channels { get; set; } = new();
        }

        public sealed class AudioChannel {
            [JsonProperty("samples")]
            public List<double> Samples { get; set; } = new();
        }
        public sealed class CurveData {
            [JsonProperty("time_unit")]
            public string TimeUnit { get; set; } = "ms";

            [JsonProperty("frame_duration")]
            public double FrameDuration { get; set; }

            [JsonProperty("range", NullValueHandling = NullValueHandling.Ignore)]
            public List<double>? Range { get; set; }

            [JsonProperty("scale")]
            public string Scale { get; set; } = "linear";

            [JsonProperty("curve")]
            public List<double> Curve { get; set; } = new();
        }

        public sealed class WorldFeatureData {
            [JsonProperty("time_unit")]
            public string TimeUnit { get; set; } = "ms";

            [JsonProperty("frame_duration")]
            public double FrameDuration { get; set; }

            [JsonProperty("width")]
            public int Width { get; set; }

            [JsonProperty("world_mgc", NullValueHandling = NullValueHandling.Ignore)]
            public List<List<double>>? WorldMgc { get; set; }

            [JsonProperty("world_sp", NullValueHandling = NullValueHandling.Ignore)]
            public List<List<double>>? WorldSp { get; set; }

            [JsonProperty("world_bap", NullValueHandling = NullValueHandling.Ignore)]
            public List<List<double>>? WorldBap { get; set; }

            [JsonProperty("world_ap", NullValueHandling = NullValueHandling.Ignore)]
            public List<List<double>>? WorldAp { get; set; }
        }
    }

    class SvsClient : SingletonBase<SvsClient> {
        internal ApiResponse<T> SendRequest<T>(string[] args) {
            return SendRequest<T>(args, "22222");
        }

        internal ApiResponse<T> SendRequest<T>(ApiRequest request) {
            return SendRequest<T>(request, "22222");
        }

        internal ApiResponse<T> SendRequest<T>(string[] args, string port, int second = 300) {
            ArgumentNullException.ThrowIfNull(args);

            return SendRequestCore<T>(JsonConvert.SerializeObject(args), port, second);
        }

        internal ApiResponse<T> SendRequest<T>(ApiRequest args, string port, int second = 300) {
            ArgumentNullException.ThrowIfNull(args);

            return SendRequestCore<T>(System.Text.Json.JsonSerializer.Serialize(args), port, second);
        }

        private static ApiResponse<T> SendRequestCore<T>(string request, string port, int second) {
            using (var client = new RequestSocket()) {
                client.Connect($"tcp://localhost:{port}");
                Log.Information($"Process sending {request}");
                client.SendFrame(request);
                client.TryReceiveFrameString(TimeSpan.FromSeconds(second), out string? message);
                Log.Information($"Process received {message}");
                if (string.IsNullOrEmpty(message)) {
                    return new ApiResponse<T>();
                }

                var response = JsonConvert.DeserializeObject<ApiResponse<T>>(message);
                return response ?? new ApiResponse<T>();
            }
        }

    }
}
