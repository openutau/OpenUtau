using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.Core.Voicevox {
    class VoicevoxClient : Util.SingletonBase<VoicevoxClient> {
        // Give up quickly when the engine is not running.
        static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
        // Synthesis of a long phrase can take a while, so the whole request gets more time.
        static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);
        // After a connection failure, fail immediately for this long instead of connecting again.
        static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

        static readonly HttpClient client = new HttpClient(new SocketsHttpHandler() {
            ConnectTimeout = ConnectTimeout,
        }) {
            Timeout = RequestTimeout,
        };

        long lastConnectFailureTicks = DateTime.MinValue.Ticks;

        public bool IsInCooldown =>
            DateTime.UtcNow.Ticks - Interlocked.Read(ref lastConnectFailureTicks) < Cooldown.Ticks;

        internal Tuple<string, byte[]> SendRequest(VoicevoxURL voicevoxURL) {
            if (IsInCooldown) {
                throw new VoicevoxException("VOICEVOX engine is not reachable.");
            }
            try {
                using (var request = new HttpRequestMessage(new HttpMethod(voicevoxURL.method.ToUpper()), this.RequestURL(voicevoxURL))) {
                    request.Headers.TryAddWithoutValidation("accept", voicevoxURL.accept);

                    request.Content = new StringContent(voicevoxURL.body);
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

                    Log.Information($"VoicevoxProcess sending {request}");
                    using (var response = client.SendAsync(request).GetAwaiter().GetResult()) {
                        Log.Information($"VoicevoxProcess received");
                        string str = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        //May not fit json format
                        if (!str.StartsWith("{") || !str.EndsWith("}")) {
                            str = "{ \"json\":" + str + "}";
                        }
                        Log.Information($"VoicevoxResponse StatusCode :{response.StatusCode}");
                        return new Tuple<string, byte[]>(str, response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
                    }
                }
            } catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException) {
                // Connection refused or timed out: the engine is probably not running.
                Interlocked.Exchange(ref lastConnectFailureTicks, DateTime.UtcNow.Ticks);
                Log.Error($"Could not connect to the VOICEVOX engine: {ex.Message}");
                throw new VoicevoxException("Could not connect to the VOICEVOX engine.", ex);
            } catch (Exception ex) {
                Log.Error($"{ex}");
                throw new VoicevoxException("VOICEVOX request failed.", ex);
            }
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
