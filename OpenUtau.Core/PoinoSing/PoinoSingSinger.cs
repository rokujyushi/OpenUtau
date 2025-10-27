using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using PoinoSing;
using Serilog;

namespace OpenUtau.Core.PoinoSing {
    public class PoinoSingSinger : USinger {
        public override string Id => voicebank.Id;
        public override string Name => voicebank.Name;
        public override Dictionary<string, string> LocalizedNames => voicebank.LocalizedNames;
        public override USingerType SingerType => voicebank.SingerType;
        public override string BasePath => voicebank.BasePath;
        public override string Author => voicebank.Author;
        public override string Voice => voicebank.Voice;
        public override string Location => Path.GetDirectoryName(voicebank.File);
        public override string Web => voicebank.Web;
        public override string Version => voicebank.Version;
        public override string OtherInfo => voicebank.OtherInfo;
        public override IList<string> Errors => errors;
        public override string Avatar => voicebank.Image == null ? null : Path.Combine(Location, voicebank.Image);
        public override byte[] AvatarData => avatarData;
        public override string Portrait => voicebank.Portrait == null ? null : Path.Combine(Location, voicebank.Portrait);
        public override float PortraitOpacity => voicebank.PortraitOpacity;
        public override int PortraitHeight => voicebank.PortraitHeight;
        public override string Sample => voicebank.Sample == null ? null : Path.Combine(Location, voicebank.Sample);
        public override string DefaultPhonemizer => voicebank.DefaultPhonemizer;
        public override Encoding TextFileEncoding => voicebank.TextFileEncoding;
        public override IList<USubbank> Subbanks => subbanks;
        public override IList<UOto> Otos => otos;

        Voicebank voicebank;
        List<string> errors = new List<string>();
        List<USubbank> subbanks = new List<USubbank>();
        List<UOto> otos = new List<UOto>();
        Dictionary<string, UOto> otoMap = new Dictionary<string, UOto>();

        HashSet<string> phonemes = new HashSet<string>();
        Dictionary<string, string[]> table = new Dictionary<string, string[]>();

        public PoinoSingSpeaker speaker;
        public Symbol symbol_;

        public byte[] avatarData;

        public PoinoSingSinger(Voicebank voicebank) {
            this.voicebank = voicebank;
            found = true;
        }

        public override void EnsureLoaded() {
            if (Loaded) {
                return;
            }
            Reload();
        }

        public override void Reload() {
            if (!Found) {
                return;
            }
            try {
                voicebank.Reload();
                UnloadSpeaker(); // スピーカーが null ならスキップ
                Load();
                loaded = true;
            } catch (Exception e) {
                Log.Error(e, $"Failed to load {voicebank.File}");
            }
        }

        // スピーカーのアンロード（speaker が null なら処理しない）
        private void UnloadSpeaker() {
            if (speaker == null || string.IsNullOrEmpty(speaker.Id)) {
                return;
            }
            try {
                var response = PoinoSingClient.Inst.SendRequest(new PoinoSingURL {
                    method = "DELETE",
                    path = $"/speakers/{speaker.Id}"
                });
                var jObj = JObject.Parse(response.Item1);
                if (jObj.ContainsKey("detail")) {
                    Log.Error($"Response was incorrect. : {jObj}");
                }
                Log.Information($"jObj after delete: {jObj}");
            } catch (Exception e) {
                Log.Error(e, "Failed to unload speaker.");
            }
        }

        void Load() {
            phonemes.Clear();
            table.Clear();
            otos.Clear();
            try {
                var speakerYamlPath = Path.Combine(Location, "speaker.yaml");
                if (File.Exists(speakerYamlPath)) {
                    var speakerData = File.ReadAllText(speakerYamlPath);
                    speaker = Yaml.DefaultDeserializer.Deserialize<PoinoSingSpeaker>(speakerData);
                    foreach (KeyValuePair<string, double[][]> envelope in speaker.Envelopes) {
                        phonemes.Add(envelope.Key);
                    }
                    var response = PoinoSingClient.Inst.SendRequest(new PoinoSingURL() { method = "POST", path = $"/speakers/load", body = $"{{ \"path\": \"{speakerYamlPath}\" }}" });
                    var jObj = JObject.Parse(response.Item1);
                    if (jObj.ContainsKey("detail")) {
                        Log.Error($"Response was incorrect. : {jObj}");
                    }
                } else {
                    var response = PoinoSingClient.Inst.SendRequest(new PoinoSingURL() { method = "GET", path = $"/health" });
                    var jObj = JObject.Parse(response.Item1);
                    if (response.Item3.Equals(HttpStatusCode.OK)) {
                        response = PoinoSingClient.Inst.SendRequest(new PoinoSingURL() { method = "GET", path = $"/speakers" });
                        jObj = JObject.Parse(response.Item1);
                        if (jObj.ContainsKey("detail")) {
                            Log.Error($"Response was incorrect. : {jObj}");
                        }
                        speaker = jObj["speakers"].ToObject<List<PoinoSingSpeaker>>().FirstOrDefault(s => s.Name == voicebank.Name);

                        response = PoinoSingClient.Inst.SendRequest(new PoinoSingURL() { method = "GET", path = $"/symbols/{speaker.Id}" });
                        jObj = JObject.Parse(response.Item1);
                        if (jObj.ContainsKey("detail")) {
                            Log.Error($"Response was incorrect. : {jObj}");
                        } else {
                            symbol_ = jObj.ToObject<Symbol>();
                            foreach (var phoneme in symbol_.Phonemes) {
                                phonemes.Add(phoneme);
                            }
                        }
                    } else if (response.Item3.Equals(HttpStatusCode.BadRequest)) {
                        Log.Error($"Response was incorrect. : {jObj}");
                    }
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load phonemes.yaml for {Name}");
            }

            subbanks.Clear();
            subbanks.Add(new USubbank(new Subbank() {
                Prefix = string.Empty,
                Suffix = string.Empty,
                ToneRanges = new[] { "C1-B7" },
                Color = ""
            })); ;

            var dummyOtoSet = new UOtoSet(new OtoSet(), Location);
            foreach (var phone in phonemes) {
                foreach (var subbank in subbanks) {
                    var uOto = UOto.OfDummy(phone);
                    if (!otoMap.ContainsKey(uOto.Alias)) {
                        otos.Add(uOto);
                        otoMap.Add(uOto.Alias, uOto);
                    } else {
                        //Errors.Add($"oto conflict {Otos[oto.Alias].Set}/{oto.Alias} and {otoSet.Name}/{oto.Alias}");
                    }
                }
            }

            if (Avatar != null && File.Exists(Avatar)) {
                try {
                    using (var stream = new FileStream(Avatar, FileMode.Open)) {
                        using (var memoryStream = new MemoryStream()) {
                            stream.CopyTo(memoryStream);
                            avatarData = memoryStream.ToArray();
                        }
                    }
                } catch (Exception e) {
                    avatarData = null;
                    Log.Error(e, "Failed to load avatar data.");
                }
            } else {
                avatarData = null;
                Log.Error("Avatar can't be found");
            }
        }

        public override bool TryGetOto(string phoneme, out UOto oto) {
            if(phoneme != null) {
                var parts = phoneme.Split();
                if (parts.All(p => phonemes.Contains(p))) {
                    oto = UOto.OfDummy(phoneme);
                    return true;
                }
            }
            oto = null;
            return false;
        }

        public override IEnumerable<UOto> GetSuggestions(string text) {
            if (text != null) {
                text = text.ToLowerInvariant().Replace(" ", "");
            }
            bool all = string.IsNullOrEmpty(text);
            return table.Keys
                .Where(key => all || key.Contains(text))
                .Select(key => UOto.OfDummy(key));
        }

        public override byte[] LoadPortrait() {
            return string.IsNullOrEmpty(Portrait)
                ? null
                : File.ReadAllBytes(Portrait);
        }

        public override byte[] LoadSample() {
            return string.IsNullOrEmpty(Sample)
                ? null
                : File.ReadAllBytes(Sample);
        }
    }
}
