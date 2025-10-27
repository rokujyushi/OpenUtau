using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using PoinoSing;
using Serilog;

namespace OpenUtau.Core.PoinoSing {
    [Phonemizer("PoinoSing Japanese Phonemizer", "POINOSHING JA", language: "JA")]
    public class PoinoSingPhonemizer : Phonemizer {

        protected PoinoSingSinger singer;
        protected string speakerId = string.Empty;
        Dictionary<Note[], Phoneme[]> partResult = new Dictionary<Note[], Phoneme[]>();

        public override void SetSinger(USinger singer) {
            this.singer = singer as PoinoSingSinger;
            speakerId = this.singer.speaker.Id;
        }

        public override void SetUp(Note[][] notes, UProject project, UTrack track) {
            partResult.Clear();
            var timings = new TimingsRequest() {Bpm = project.timeAxis.GetBpmAtTick(notes[0][0].position), Mode = "anchors", SpeakerId = speakerId };
            for (int i = 0; i < notes.Length; i++) {
                var currentLyric = notes[i][0].lyric.Normalize();
                var lyricList = currentLyric.Split(" ");
                if (lyricList.Length > 1) {
                    currentLyric = lyricList[1];
                }
                if (!PoinoSingUtils.IsSyllableVowelExtensionNote(currentLyric)) {
                    if (this.singer.symbol_.Kanas.Contains(currentLyric)) {
                        timings.Phrase.Add(currentLyric);
                        timings.Anchors.Add(notes[i][0].position);
                    } else if(PoinoSingUtils.IsPau(currentLyric)) {
                        timings.Phrase.Add("q");
                        timings.Anchors.Add(notes[i][0].position);
                    }
                }
            }

            var response = PoinoSingClient.Inst.SendRequest(new PoinoSingURL() { method = "POST", path = "/generate-phoneme-timings", body = JsonConvert.SerializeObject(timings) });
            var jObj = JObject.Parse(response.Item1);
            if (jObj.ContainsKey("detail")) {
                Log.Error($"Response was incorrect. : {jObj}");
            }
            var payload = jObj["json"] ?? jObj;
            var timingsResponse = payload?.ToObject<TimingsResponse>() ?? new TimingsResponse();

            for (int i = 0; i < notes.Length; i++) {
                var noteGroup = notes[i];
                var phoneme = new List<Phoneme>();
                var token = timingsResponse.Tokens.FirstOrDefault(t => t.Lyric == noteGroup[0].lyric || (PoinoSingUtils.IsPau(noteGroup[0].lyric) && t.Lyric == "q") && t.AnchorTick == noteGroup[0].position);
                for (int i2 = 0; i2 < token.Phonemes.Length; i2++) {
                    phoneme.Add(new Phoneme() { index = i2, phoneme = token.Phonemes[i2], position = (int)Math.Round(token.PhonemeTimings[i2]) });
                }
                partResult[noteGroup] = phoneme.ToArray();
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            var ps = new List<Phoneme>();
            if (partResult.TryGetValue(notes, out var phonemes)) {
                return new Result {
                    phonemes = phonemes.Select(p => {
                        p.position = p.position - notes[0].position;
                        return p;
                    }).ToArray(),
                };
            }
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme {
                        phoneme = "error",
                    }
                },
            };

        }

        public override void CleanUp() {
            partResult.Clear();
        }
    }
}
