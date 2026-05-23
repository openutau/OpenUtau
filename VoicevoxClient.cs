using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Serilog;

namespace OpenUtau.Core.Voicevox {
    class VoicevoxClient : Util.SingletonBase<VoicevoxClient> {
        internal Tuple<string, byte[]> SendRequest(VoicevoxURL voicevoxURL) {
            try {
                using (var client = new HttpClient()) {
                    using (var request = new HttpRequestMessage(new HttpMethod(voicevoxURL.method.ToUpper()), this.RequestURL(voicevoxURL))) {
                        request.Headers.TryAddWithoutValidation("accept", voicevoxURL.accept);

                        request.Content = new StringContent(voicevoxURL.body);
                        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

                        Log.Information($"VoicevoxProcess sending {request}");
                        var response = client.SendAsync(request);
                        Log.Information($"VoicevoxProcess received");
                        string str = response.Result.Content.ReadAsStringAsync().Result;
                        //May not fit json format
                        if (!str.StartsWith("{") || !str.EndsWith("}")) {
                            str = "{ \"json\":" + str + "}";
                        }
                        Log.Information($"VoicevoxResponse StatusCode :{response.Result.StatusCode}");
                        return new Tuple<string, byte[]>(str, response.Result.Content.ReadAsByteArrayAsync().Result);
                    }
                }
            } catch (Exception ex) {
                Log.Error($"{ex}");
            }
            return new Tuple<string, byte[]>("", new byte[0]);
        }

        public string RequestURL(VoicevoxURL voicevoxURL) {
            StringBuilder queryStringBuilder = new StringBuilder();
            foreach (var parameter in voicevoxURL.query) {
                queryStringBuilder.Append($"{parameter.Key}={parameter.Value}&");
            }

            // Remove extra "&" at the end
            string queryString = "?" + queryStringBuilder.ToString().TrimEnd('&');

            string str = $"{voicevoxURL.protocol}{voicevoxURL.host}{voicevoxURL.path}{queryString}";
            return str;
        }
    }
    public class VoicevoxURL {
        public string method = string.Empty;
        public string protocol = "http://";
        //Currently fixed port 50021 to connect to
        public string host = "127.0.0.1:50021";
        public string path = string.Empty;
        public Dictionary<string, string> query = new Dictionary<string, string>();
        public string body = string.Empty;
        public string accept = "application/json";
    }
}
