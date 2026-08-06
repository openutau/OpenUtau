using OpenUtau.Core.AgentBridge;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace OpenUtau.Test.AgentBridge {
    public class BridgeProtocolTest {
        [Fact]
        public void V2ProtocolExposesOnlyBuiltInActions() {
            Assert.Equal(2, BridgeProtocol.Version);
            Assert.True(BridgeProtocol.IsSupportedAction("add_notes_simple"));
            Assert.True(BridgeProtocol.IsSupportedAction("get_state_snapshot"));
            Assert.True(BridgeProtocol.IsSupportedAction("get_state_events"));
            Assert.True(BridgeProtocol.IsSupportedAction("get_bridge_diagnostics"));
            Assert.False(BridgeProtocol.IsSupportedAction("apply_transaction"));
            Assert.True(BridgeProtocol.IsSupportedAction("save_file"));
            Assert.True(BridgeProtocol.IsSupportedAction("load_file"));
            Assert.True(BridgeProtocol.IsSupportedAction("navigate_editor"));
            Assert.True(BridgeProtocol.IsSupportedAction("open_piano_roll"));
            Assert.False(BridgeProtocol.IsSupportedAction("ui_click"));
        }

        [Fact]
        public void StateSnapshotIncludesEditableNoteDetails() {
            var note = UNote.Create();
            note.position = 120;
            note.duration = 480;
            note.tone = 60;
            note.lyric = "a";
            note.tuning = 12;
            note.pitch.AddPoint(new PitchPoint(25, 15, PitchPointShape.l));
            note.vibrato.length = 65;
            note.phonemeExpressions.Add(new UExpression("vel") { index = 0, value = 80 });
            note.phonemeOverrides.Add(new UPhonemeOverride { index = 0, phoneme = "ka", offset = 10 });

            using var document = JsonDocument.Parse(JsonSerializer.Serialize(BridgeCore.SnapshotNote(note, 3)));
            var snapshot = document.RootElement;

            Assert.Equal(3, snapshot.GetProperty("index").GetInt32());
            Assert.Equal(25, snapshot.GetProperty("pitch").GetProperty("points")[0].GetProperty("x").GetSingle());
            Assert.Equal("l", snapshot.GetProperty("pitch").GetProperty("points")[0].GetProperty("shape").GetString());
            Assert.Equal(65, snapshot.GetProperty("vibrato").GetProperty("length").GetSingle());
            Assert.Equal("vel", snapshot.GetProperty("phonemeExpressions")[0].GetProperty("abbr").GetString());
            Assert.Equal("ka", snapshot.GetProperty("phonemeOverrides")[0].GetProperty("phoneme").GetString());
            Assert.Equal(10, snapshot.GetProperty("phonemeOverrides")[0].GetProperty("offset").GetInt32());
        }

        [Fact]
        public async Task HttpMcpRequiresTokenAndIgnoresLegacySessionHeaders() {
            var port = GetAvailablePort();
            var savedToken = Preferences.Default.McpToken;
            Preferences.Default.McpToken = string.Empty;
            Assert.True(McpServiceOptions.TryCreate("127.0.0.1", port, out var options, out _));
            Assert.True(McpService.Start(options, out _));
            try {
                using var client = new HttpClient();
                var endpoint = $"http://127.0.0.1:{port}/mcp";
                var anonymous = await client.GetAsync(endpoint);
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

                Assert.True(McpService.TryGetBearerToken(out var token));
                Assert.NotEmpty(token);
                McpService.Stop();
                Assert.True(McpService.Start(options, out _));
                Assert.True(McpService.TryGetBearerToken(out var restartedToken));
                Assert.Equal(token, restartedToken);
                McpService.RefreshBearerToken();
                Assert.True(McpService.TryGetBearerToken(out var refreshedToken));
                Assert.NotEqual(token, refreshedToken);
                Assert.True(McpService.TryGetConnectionConfiguration(out var configuration));
                using var configDocument = JsonDocument.Parse(configuration);
                var authorization = configDocument.RootElement
                    .GetProperty("mcpServers").GetProperty("openutau").GetProperty("headers")
                    .GetProperty("Authorization").GetString();
                Assert.Equal($"Bearer {refreshedToken}", authorization);

                using var expiredRequest = new HttpRequestMessage(HttpMethod.Post, endpoint) {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\"}", Encoding.UTF8, "application/json"),
                };
                expiredRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                expiredRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
                var expiredResponse = await client.SendAsync(expiredRequest);
                Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}", Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.False(response.Headers.Contains("mcp-session-id"));

                using var toolsRequest = new HttpRequestMessage(HttpMethod.Post, endpoint) {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}", Encoding.UTF8, "application/json"),
                };
                toolsRequest.Headers.TryAddWithoutValidation("Authorization", authorization);
                toolsRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
                toolsRequest.Headers.TryAddWithoutValidation("mcp-session-id", "legacy-client-session");
                var toolsResponse = await client.SendAsync(toolsRequest);
                Assert.Equal(HttpStatusCode.OK, toolsResponse.StatusCode);

                using var planRequest = new HttpRequestMessage(HttpMethod.Post, endpoint) {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"openutau_plan\",\"arguments\":{\"action\":\"create_part\"}}}", Encoding.UTF8, "application/json"),
                };
                planRequest.Headers.TryAddWithoutValidation("Authorization", authorization);
                planRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
                var planResponse = await client.SendAsync(planRequest);
                Assert.Equal(HttpStatusCode.OK, planResponse.StatusCode);
                using var planDocument = JsonDocument.Parse(await planResponse.Content.ReadAsStringAsync());
                var planText = planDocument.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
                using var planPayload = JsonDocument.Parse(planText!);
                Assert.Equal("pending", planPayload.RootElement.GetProperty("status").GetString());
                Assert.False(string.IsNullOrWhiteSpace(planPayload.RootElement.GetProperty("planId").GetString()));

                using var callRequest = new HttpRequestMessage(HttpMethod.Post, endpoint) {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"unknown\"}}", Encoding.UTF8, "application/json"),
                };
                callRequest.Headers.TryAddWithoutValidation("Authorization", authorization);
                callRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
                callRequest.Headers.TryAddWithoutValidation("mcp-session-id", "legacy-client-session");
                var callResponse = await client.SendAsync(callRequest);
                Assert.Equal(HttpStatusCode.BadRequest, callResponse.StatusCode);
                using var callDocument = JsonDocument.Parse(await callResponse.Content.ReadAsStringAsync());
                Assert.Equal(-32602, callDocument.RootElement.GetProperty("error").GetProperty("code").GetInt32());
            } finally {
                McpService.Stop();
                Preferences.Default.McpToken = savedToken;
                Preferences.Save();
            }
        }

        [Fact]
        public void McpServiceOptionsAcceptOnlyLoopbackAddressesAndValidPorts() {
            Assert.True(McpServiceOptions.TryCreate("::1", 43102, out _, out _));
            Assert.False(McpServiceOptions.TryCreate("0.0.0.0", 43102, out _, out _));
            Assert.False(McpServiceOptions.TryCreate("127.0.0.1", 0, out _, out _));
            Assert.False(McpServiceOptions.TryCreate("127.0.0.1", 65536, out _, out _));
        }

        private static int GetAvailablePort() {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
