using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.AgentBridge {
    public enum McpStartupMode {
        Manual,
        OnOpenUtauStartup,
    }

    public sealed record McpServiceOptions(string BindAddress, int Port) {
        public static bool TryCreate(string? bindAddress, int port, out McpServiceOptions options, out string? error) {
            options = null!;
            error = null;
            if (!IPAddress.TryParse(bindAddress, out var address) || !IPAddress.IsLoopback(address)) {
                error = "MCP binding must use a loopback IP address.";
                return false;
            }
            if (port is < 1 or > 65535) {
                error = "MCP port must be within 1..65535.";
                return false;
            }
            options = new McpServiceOptions(address.ToString(), port);
            return true;
        }
    }

    public sealed record McpServiceStatus(bool Running, string BindAddress, int Port, string? Error);

    /// <summary>Owns the loopback HTTP listener used by the native MCP transport.</summary>
    public static class McpService {
        private static readonly object Gate = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private static HttpListener? listener;
        private static CancellationTokenSource? cancellation;
        private static Task? worker;
        private static McpServiceOptions? options;
        private static string? sessionToken;
        private static string? lastError;
        // Streamable HTTP uses the bearer token as the local trust boundary;
        // current MCP clients do not require a transport session header.
        private static McpSession? requestContext;
        private static readonly SemaphoreSlim bridgeRequests = new(1, 1);
        private const int MaxRequestBytes = 1024 * 1024;
        private static readonly HashSet<string> ReadActions = new(StringComparer.Ordinal) {
            "ping", "get_project_info", "get_state_snapshot", "get_state_events", "get_bridge_diagnostics", "get_editor_state",
        };
        private static readonly HashSet<string> WriteActions = new(StringComparer.Ordinal) {
            "set_track_config", "set_track_singer", "create_part", "add_notes_simple", "edit_note_simple", "delete_notes_simple", "playback",
        };

        public static McpServiceStatus Status {
            get {
                lock (Gate) {
                    return new McpServiceStatus(listener?.IsListening == true, options?.BindAddress ?? string.Empty, options?.Port ?? 0, lastError);
                }
            }
        }

        public static bool Start(McpServiceOptions requestedOptions, out string? error) {
            lock (Gate) {
                if (listener?.IsListening == true && options == requestedOptions) {
                    error = null;
                    return true;
                }
                StopLocked();
                try {
                    var created = new HttpListener();
                    created.Prefixes.Add(BuildPrefix(requestedOptions));
                    created.Start();
                    options = requestedOptions;
                    sessionToken = GetOrCreatePersistentToken();
                    requestContext = new McpSession(Guid.NewGuid().ToString("N"));
                    cancellation = new CancellationTokenSource();
                    listener = created;
                    worker = Task.Run(() => RunAsync(created, cancellation.Token));
                    lastError = null;
                    error = null;
                    Log.Information("OpenUtau MCP service listening on loopback address {McpBindAddress}:{McpPort}.", requestedOptions.BindAddress, requestedOptions.Port);
                    return true;
                } catch (Exception ex) when (ex is HttpListenerException or ArgumentException) {
                    lastError = ex.Message;
                    options = null;
                    sessionToken = null;
                    error = lastError;
                    Log.Warning(ex, "OpenUtau MCP service could not start.");
                    return false;
                }
            }
        }

        public static void Stop() {
            lock (Gate) {
                StopLocked();
            }
        }

        internal static bool HasCurrentSessionToken(string? token) {
            lock (Gate) {
                return sessionToken != null && token != null && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(sessionToken), Encoding.UTF8.GetBytes(token));
            }
        }

        public static bool TryGetBearerToken(out string token) {
            lock (Gate) {
                if (listener?.IsListening != true || sessionToken == null) {
                    token = string.Empty;
                    return false;
                }
                token = sessionToken;
                return true;
            }
        }

        public static void RefreshBearerToken() {
            lock (Gate) {
                var token = CreateSessionToken();
                Preferences.Default.McpToken = token;
                Preferences.Save();
                sessionToken = token;
            }
        }

        public static bool TryGetConnectionConfiguration(out string configuration) {
            lock (Gate) {
                if (listener?.IsListening != true || options == null || sessionToken == null) {
                    configuration = string.Empty;
                    return false;
                }
                var host = options.BindAddress.Contains(':') ? $"[{options.BindAddress}]" : options.BindAddress;
                configuration = JsonSerializer.Serialize(new {
                    mcpServers = new {
                        openutau = new {
                            url = $"http://{host}:{options.Port}/mcp",
                            headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {sessionToken}" },
                        },
                    },
                }, JsonOptions);
                return true;
            }
        }

        private static async Task RunAsync(HttpListener activeListener, CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try {
                    var context = await activeListener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleRequestAsync(context), token);
                } catch (HttpListenerException) when (token.IsCancellationRequested || !activeListener.IsListening) {
                    break;
                } catch (ObjectDisposedException) when (token.IsCancellationRequested) {
                    break;
                } catch (Exception ex) {
                    Log.Warning(ex, "OpenUtau MCP listener failure.");
                }
            }
        }

        private static async Task HandleRequestAsync(HttpListenerContext context) {
            try {
                if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/healthz") {
                    var status = Status;
                    await WriteJsonAsync(context.Response, 200, new { status = "ready", bindAddress = status.BindAddress, port = status.Port }).ConfigureAwait(false);
                    return;
                }
                if (context.Request.Url?.AbsolutePath == "/mcp") {
                    await HandleMcpRequestAsync(context).ConfigureAwait(false);
                    return;
                }
                await WriteJsonAsync(context.Response, 404, new { error = "NOT_FOUND" }).ConfigureAwait(false);
            } catch (Exception ex) {
                Log.Warning(ex, "OpenUtau MCP request failed.");
                try { context.Response.Close(); } catch { }
            }
        }

        private static async Task HandleMcpRequestAsync(HttpListenerContext context) {
            if (!HasCurrentSessionToken(ReadBearerToken(context.Request))) {
                await WriteJsonAsync(context.Response, 401, new { error = "UNAUTHORIZED" }).ConfigureAwait(false);
                return;
            }
            if (context.Request.HttpMethod is "DELETE" or "GET") {
                await WriteJsonAsync(context.Response, 405, new { error = "METHOD_NOT_ALLOWED" }).ConfigureAwait(false);
                return;
            }
            if (context.Request.HttpMethod != "POST") {
                await WriteJsonAsync(context.Response, 405, new { error = "METHOD_NOT_ALLOWED" }).ConfigureAwait(false);
                return;
            }
            if (!IsJsonRequest(context.Request)) {
                await WriteJsonAsync(context.Response, 415, new { error = "INVALID_CONTENT_TYPE" }).ConfigureAwait(false);
                return;
            }
            if (!AcceptsMcpResponse(context.Request)) {
                await WriteJsonAsync(context.Response, 406, new { error = "INVALID_ACCEPT" }).ConfigureAwait(false);
                return;
            }
            JsonDocument document;
            try {
                document = JsonDocument.Parse(await ReadBodyAsync(context.Request).ConfigureAwait(false));
            } catch (BridgeException ex) {
                await WriteJsonAsync(context.Response, 413, new { error = ex.Code }).ConfigureAwait(false);
                return;
            } catch (JsonException) {
                await WriteJsonAsync(context.Response, 400, new { error = "INVALID_JSON" }).ConfigureAwait(false);
                return;
            }
            using (document) {
                var request = document.RootElement;
                if (request.ValueKind != JsonValueKind.Object || request.GetPropertyOrDefault("jsonrpc") != "2.0" || !request.TryGetProperty("method", out var methodValue)) {
                    await WriteJsonAsync(context.Response, 400, new { error = "INVALID_JSON_RPC" }).ConfigureAwait(false);
                    return;
                }
                var requestId = request.TryGetProperty("id", out var id) ? id.Clone() : JsonSerializer.SerializeToElement<object?>(null);
                var method = methodValue.GetString();
                var session = GetRequestContext();
                if (session == null) {
                    await WriteJsonAsync(context.Response, 503, RpcError(requestId, -32000, "SERVICE_NOT_READY")).ConfigureAwait(false);
                    return;
                }
                if (!ConsumeRequestQuota(session)) {
                    await WriteJsonAsync(context.Response, 429, RpcError(requestId, -32002, "RATE_LIMITED")).ConfigureAwait(false);
                    return;
                }
                var response = await DispatchMcpMethodAsync(method, request, session).ConfigureAwait(false);
                Log.Information("OpenUtau MCP request {McpSession} {McpMethod} {McpResult}", session.Id, method, response.ErrorCode?.ToString() ?? "ok");
                await WriteJsonAsync(context.Response, response.ErrorCode == null ? 200 : 400, response.ToRpc(requestId)).ConfigureAwait(false);
            }
        }

        private static async Task<McpMethodResponse> DispatchMcpMethodAsync(string? method, JsonElement request, McpSession session) {
            if (method == "initialize") {
                return McpMethodResponse.Success(new {
                    protocolVersion = "2025-03-26",
                    capabilities = new { tools = new { }, resources = new { subscribe = false } },
                    serverInfo = new { name = "openutau", version = BridgeProtocol.BridgeVersion },
                });
            }
            if (method == "notifications/initialized") return McpMethodResponse.Success(new { });
            if (method == "tools/list") return McpMethodResponse.Success(new {
                tools = new[] {
                    Tool("openutau_read", "Read authoritative OpenUtau state.", new[] { "action" }, "action", "payload"),
                    Tool("openutau_plan", "Create a guarded write plan that expires after confirmation timeout.", new[] { "action" }, "action", "payload", "expiresInSeconds"),
                    Tool("openutau_apply", "Apply one pending guarded write plan.", new[] { "planId" }, "planId"),
                    Tool("openutau_diagnostics", "Read Bridge and HTTP diagnostics.", Array.Empty<string>()),
                },
            });
            if (method != "tools/call" || !request.TryGetProperty("params", out var parameters)) return McpMethodResponse.Error(-32601, "METHOD_NOT_FOUND");
            var name = parameters.GetPropertyOrDefault("name");
            var arguments = parameters.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object ? args : default;
            if (name == "openutau_diagnostics") return await CallBridgeAsync("get_bridge_diagnostics", default).ConfigureAwait(false);
            if (name == "openutau_read") {
                var action = arguments.GetPropertyOrDefault("action");
                if (action == null || !ReadActions.Contains(action)) return McpMethodResponse.Error(-32602, "UNSUPPORTED_READ_ACTION");
                return await CallBridgeAsync(action, GetPayload(arguments)).ConfigureAwait(false);
            }
            if (name == "openutau_plan") {
                var action = arguments.GetPropertyOrDefault("action");
                if (action == null || !WriteActions.Contains(action)) return McpMethodResponse.Error(-32602, "UNSUPPORTED_WRITE_ACTION");
                var seconds = arguments.TryGetProperty("expiresInSeconds", out var expiry) && expiry.TryGetInt32(out var requested) ? requested : 120;
                if (seconds is < 1 or > 600) return McpMethodResponse.Error(-32602, "INVALID_PLAN_EXPIRY");
                var plan = new McpPlan(Guid.NewGuid().ToString("N"), action, GetPayload(arguments), DateTimeOffset.UtcNow.AddSeconds(seconds));
                session.Plans[plan.Id] = plan;
                return ToolContent(new { planId = plan.Id, action, expiresAt = plan.ExpiresAt, status = "pending" });
            }
            if (name == "openutau_apply") {
                var planId = arguments.GetPropertyOrDefault("planId");
                if (planId == null || !session.Plans.Remove(planId, out var plan)) return McpMethodResponse.Error(-32602, "PLAN_NOT_FOUND");
                if (plan.ExpiresAt < DateTimeOffset.UtcNow) return McpMethodResponse.Error(-32602, "PLAN_EXPIRED");
                return await CallBridgeAsync(plan.Action, plan.Payload).ConfigureAwait(false);
            }
            return McpMethodResponse.Error(-32602, "UNKNOWN_TOOL");
        }

        private static async Task<McpMethodResponse> CallBridgeAsync(string action, JsonElement payload) {
            await bridgeRequests.WaitAsync().ConfigureAwait(false);
            try {
                var requestPayload = payload.ValueKind == JsonValueKind.Object ? payload : JsonSerializer.SerializeToElement(new { });
                var envelope = JsonSerializer.SerializeToElement(new { v = BridgeProtocol.Version, id = Guid.NewGuid().ToString("N"), a = action, p = requestPayload }, JsonOptions);
                var result = BridgeCore.DispatchRequest(envelope);
                return ToolContent(result);
            } catch (Exception ex) {
                Log.Warning(ex, "OpenUtau MCP bridge dispatch failed for {McpAction}", action);
                return McpMethodResponse.Error(-32000, "HOST_ERROR");
            } finally {
                bridgeRequests.Release();
            }
        }

        private static McpMethodResponse ToolContent(object result) => McpMethodResponse.Success(new {
            content = new[] { new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) } },
        });
        private static object Tool(string name, string description, string[] required, params string[] properties) => new {
            name, description,
            inputSchema = new {
                type = "object",
                properties = properties.ToDictionary(property => property, property => property switch {
                    "payload" => (object)new { type = "object" },
                    "expiresInSeconds" => new { type = "integer", minimum = 1, maximum = 600 },
                    _ => new { type = "string" },
                }),
                required,
                additionalProperties = false,
            },
        };

        private static JsonElement GetPayload(JsonElement arguments) => arguments.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object ? payload.Clone() : JsonSerializer.SerializeToElement(new { });
        private static string? ReadBearerToken(HttpListenerRequest request) {
            var value = request.Headers["Authorization"];
            return value?.StartsWith("Bearer ", StringComparison.Ordinal) == true ? value[7..] : null;
        }
        private static bool IsJsonRequest(HttpListenerRequest request) => request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true;
        private static bool AcceptsMcpResponse(HttpListenerRequest request) {
            var accept = request.Headers["Accept"];
            return !string.IsNullOrWhiteSpace(accept) && (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) || accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase));
        }
        private static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request) {
            if (request.ContentLength64 > MaxRequestBytes) throw new BridgeException("REQUEST_TOO_LARGE", "body too large");
            await using var body = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await request.InputStream.ReadAsync(buffer).ConfigureAwait(false)) > 0) {
                if (body.Length + read > MaxRequestBytes) throw new BridgeException("REQUEST_TOO_LARGE", "body too large");
                await body.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            }
            return body.ToArray();
        }
        private static McpSession? GetRequestContext() {
            lock (Gate) return requestContext;
        }
        private static bool ConsumeRequestQuota(McpSession session) {
            lock (Gate) {
                var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
                session.Requests.RemoveAll(time => time < cutoff);
                if (session.Requests.Count >= 60) return false;
                session.Requests.Add(DateTimeOffset.UtcNow);
                return true;
            }
        }
        private static object RpcError(JsonElement id, int code, string message) => new { jsonrpc = "2.0", id, error = new { code, message } };

        private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload) {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = bytes.Length;
            response.Headers[HttpResponseHeader.CacheControl] = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            response.Close();
        }

        private static void StopLocked() {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            listener?.Close();
            listener = null;
            worker = null;
            options = null;
            sessionToken = null;
            requestContext = null;
        }

        private static string BuildPrefix(McpServiceOptions serviceOptions) {
            var host = serviceOptions.BindAddress.Contains(':') ? $"[{serviceOptions.BindAddress}]" : serviceOptions.BindAddress;
            return $"http://{host}:{serviceOptions.Port}/";
        }

        private static string CreateSessionToken() {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        private static string GetOrCreatePersistentToken() {
            if (!string.IsNullOrWhiteSpace(Preferences.Default.McpToken)) {
                return Preferences.Default.McpToken;
            }
            var token = CreateSessionToken();
            Preferences.Default.McpToken = token;
            Preferences.Save();
            return token;
        }

        private sealed class McpSession {
            public McpSession(string id) { Id = id; }
            public string Id { get; }
            public List<DateTimeOffset> Requests { get; } = new();
            public Dictionary<string, McpPlan> Plans { get; } = new();
        }
        private sealed record McpPlan(string Id, string Action, JsonElement Payload, DateTimeOffset ExpiresAt);
        private sealed record McpMethodResponse(object? Result, int? ErrorCode, string? ErrorMessage) {
            public static McpMethodResponse Success(object result) => new(result, null, null);
            public static McpMethodResponse Error(int code, string message) => new(null, code, message);
            public object ToRpc(JsonElement id) => ErrorCode == null
                ? new { jsonrpc = "2.0", id, result = Result }
                : RpcError(id, ErrorCode.Value, ErrorMessage!);
        }
        private sealed class BridgeException : Exception {
            public BridgeException(string code, string message) : base(message) { Code = code; }
            public string Code { get; }
        }
        private static string? GetPropertyOrDefault(this JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
