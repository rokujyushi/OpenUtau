using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Serilog;

namespace OpenUtau.Core.PoinoSing {
    class PoinoSingClient : Util.SingletonBase<PoinoSingClient> {
        internal Tuple<string, byte[], HttpStatusCode> SendRequest(PoinoSingURL poinoSingURL) {
            try {
                using (var client = new HttpClient()) {
                    using (var request = new HttpRequestMessage(new HttpMethod(poinoSingURL.method.ToUpper()), this.RequestURL(poinoSingURL))) {
                        request.Headers.TryAddWithoutValidation("accept", poinoSingURL.accept);

                        request.Content = new StringContent(poinoSingURL.body);
                        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

                        Log.Information($"PoinoSingProcess sending {request}");
                        var response = client.SendAsync(request);
                        Log.Information($"PoinoSingProcess received");
                        string str = response.Result.Content.ReadAsStringAsync().Result;
                        //May not fit json format
                        if (!str.StartsWith("{") || !str.EndsWith("}")) {
                            str = "{ \"json\":" + str + "}";
                        }
                        Log.Information($"PoinoSingResponse StatusCode :{response.Result.StatusCode}");
                        return new Tuple<string, byte[], HttpStatusCode>(str, response.Result.Content.ReadAsByteArrayAsync().Result, response.Result.StatusCode);
                    }
                }
            } catch (Exception ex) {
                Log.Error($"{ex}");
            }
            return new Tuple<string, byte[], HttpStatusCode>("{ detail : \"\" }", new byte[0], HttpStatusCode.BadRequest);
        }

        private string RequestURL(PoinoSingURL poinoSingURL) {
            StringBuilder queryStringBuilder = new StringBuilder();
            foreach (var parameter in poinoSingURL.query) {
                queryStringBuilder.Append($"{parameter.Key}={parameter.Value}&");
            }

            // Remove extra "&" at the end
            string queryString = "?" + queryStringBuilder.ToString().TrimEnd('&');

            string str = $"{poinoSingURL.protocol}{poinoSingURL.host}{poinoSingURL.path}{queryString}";
            return str;
        }
    }
    public class PoinoSingURL {
        public string method = string.Empty;
        public string protocol = "http://";
        //Currently fixed port 5179 to connect to
        public string host = "127.0.0.1:5179";
        public string path = string.Empty;
        public Dictionary<string, string> query = new Dictionary<string, string>();
        public string body = string.Empty;
        public string accept = "application/json";
    }
}
