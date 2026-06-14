using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NWaves.Transforms;
using OpenUtau.Classic;
using OpenUtau.Core.Enunu;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using static OpenUtau.Core.Util.SingerRecipeBaseResponse;

namespace OpenUtau.Core.Custom {
    public class CustomSinger : USinger {
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
        public override string Avatar => voicebank.Image == null ?  null : Path.Combine(Location, voicebank.Image);
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

        public Dictionary<string, OpDefinition> ops;

        public byte[] avatarData;

        public CustomSinger(Voicebank voicebank) {
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
                Load();
                loaded = true;
            } catch (Exception e) {
                Log.Error(e, $"Failed to load {voicebank.File}");
            }
        }

        void Load() {
            ApiResponse<OpsResponse> response = SvsClient.Inst.SendRequest<OpsResponse>(new OpsRequest());
            if (response.Error != null) {
                Log.Error(response.Error.Message);
            } else if(response.Response != null) {
                for (int i = 0; i < response.Response.Ops.Count; i++) {
                    ops.Add(response.Response.Ops[i].Op, response.Response.Ops[i]);
                }
            } else {
                Log.Error("It differs from the supported version.");
                return;
            }
            phonemes.Clear();
            table.Clear();
            otos.Clear();
            subbanks.Clear();
            try {
                GetSingerInfoRequest getSingerInfo = new GetSingerInfoRequest();
                if (ops.ContainsKey(getSingerInfo.Op)) {
                    ApiResponse<GetSingerInfoResponse> response_ = SvsClient.Inst.SendRequest<GetSingerInfoResponse>(getSingerInfo);
                    if (response_.Error != null) {
                        Log.Error(response_.Error.Message);
                    } else if (response.Response != null) {
                        foreach (var phone in response_.Response.Phonemes) {
                            phonemes.Add(phone);
                        }
                        foreach (var item in response_.Response.Dict) {
                            table = response_.Response.Dict;
                        }
                        if (voicebank.Subbanks == null || voicebank.Subbanks.Count == 0 ||
                            voicebank.Subbanks.Count == 1 && string.IsNullOrEmpty(voicebank.Subbanks[0].Color)) {
                            subbanks.Add(new USubbank(new Subbank() {
                                Prefix = string.Empty,
                                Suffix = string.Empty,
                                ToneRanges = new[] { "C1-B7" },
                            }));
                            subbanks.AddRange(response_.Response.Styles.Select(flag => new USubbank(new Subbank() {
                                Color = flag,
                                Prefix = string.Empty,
                                Suffix = flag,
                                ToneRanges = new[] { "C1-B7" },
                            })));
                        } else {
                            subbanks.AddRange(voicebank.Subbanks
                                .Select(subbank => new USubbank(subbank)));
                        }
                    } else {
                        Log.Error("It differs from the supported version.");
                        return;
                    }
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load phonemes for {Name}");
                return;
            }

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
