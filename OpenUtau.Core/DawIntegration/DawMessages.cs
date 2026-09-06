using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// Control-plane message kinds. See PROTOCOL.md §6.
    /// </summary>
    public static class DawMessageKind {
        // OpenUtau -> plugin
        public const string Init = "init";
        public const string UpdateUstx = "updateUstx";
        public const string UpdatePartLayout = "updatePartLayout";
        public const string GetAudio = "getAudio";
        public const string UpdateTracks = "updateTracks";
        public const string UpdateProjectInfo = "updateProjectInfo";
        // plugin -> OpenUtau
        public const string Ping = "ping";
        public const string PlaybackStarted = "playbackStarted";
        public const string Playhead = "playhead";
        public const string Bpm = "bpm";
    }

    /// <summary>
    /// Protocol version carried by the discovery file and echoed in the init response
    /// (PROTOCOL.md §4). Major mismatch refuses the connection; minor skew connects.
    /// </summary>
    public readonly struct DawApiVersion : IEquatable<DawApiVersion> {
        public const string CurrentString = "1.2";
        public static DawApiVersion Current => new DawApiVersion(1, 2);

        public readonly int Major;
        public readonly int Minor;

        public DawApiVersion(int major, int minor) {
            Major = major;
            Minor = minor;
        }

        public static bool TryParse(string? text, out DawApiVersion version) {
            version = default;
            if (string.IsNullOrWhiteSpace(text)) {
                return false;
            }
            var parts = text.Split('.');
            if (parts.Length != 2) {
                return false;
            }
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)) {
                return false;
            }
            version = new DawApiVersion(major, minor);
            return true;
        }

        /// <summary>Only the major component gates compatibility (PROTOCOL.md §4, §10).</summary>
        public bool IsCompatibleWith(DawApiVersion other) => Major == other.Major;

        public bool Equals(DawApiVersion other) => Major == other.Major && Minor == other.Minor;
        public override bool Equals(object? obj) => obj is DawApiVersion other && Equals(other);
        public override int GetHashCode() => (Major * 397) ^ Minor;
        public override string ToString() => $"{Major}.{Minor}";
    }

    /// <summary>
    /// Response envelope for <c>response:&lt;uuid&gt;</c> lines (PROTOCOL.md §5.1):
    /// <c>{ "success": true, "data": { }, "error": null }</c>.
    /// </summary>
    public class DawResult {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("data")] public JsonElement? Data { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }

        public static DawResult Ok() => new DawResult { Success = true };

        /// <summary>
        /// Wraps a payload as the envelope's <c>data</c>. Round-tripping through
        /// <see cref="JsonElement"/> keeps <see cref="Data"/> a single type regardless of whether
        /// the envelope was built locally or read off the wire.
        /// </summary>
        public static DawResult Ok<T>(T data) => new DawResult {
            Success = true,
            Data = JsonSerializer.SerializeToElement(data, DawJson.Options),
        };

        public static DawResult Fail(string error) => new DawResult { Success = false, Error = error };

        /// <summary>
        /// Deserializes <see cref="Data"/> into <typeparamref name="T"/>. Throws
        /// <see cref="DawProtocolException"/> when the envelope reported failure or the
        /// payload is absent, so callers never silently observe a default value.
        /// </summary>
        public T Unwrap<T>() {
            if (!Success) {
                throw new DawProtocolException($"Plugin returned an error: {Error ?? "(none)"}");
            }
            if (Data == null) {
                throw new DawProtocolException("Plugin returned a successful response with no data.");
            }
            var value = Data.Value.Deserialize<T>(DawJson.Options);
            if (value == null) {
                throw new DawProtocolException($"Plugin response data could not be read as {typeof(T).Name}.");
            }
            return value;
        }
    }

    /// <summary>Raised on any framing / envelope violation that must drop the connection (PROTOCOL.md §8).</summary>
    public class DawProtocolException : Exception {
        public DawProtocolException(string message) : base(message) { }
        public DawProtocolException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Shared serializer settings. Keys are explicit on every DTO, so no naming policy is applied.</summary>
    public static class DawJson {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    }

    /// <summary>
    /// Produces the <c>ustx</c> string field carried by <c>init</c> and <c>updateUstx</c>.
    /// USTX is OpenUtau's native YAML document — byte-identical to what <c>Ustx.Save</c> writes
    /// to a <c>.ustx</c> file — so the plugin can persist it or re-parse it with any YAML reader.
    /// </summary>
    /// <remarks>
    /// Must run on the document thread: <c>UProject.BeforeSave</c> mutates the project's
    /// serialization views (<c>voiceParts</c>/<c>waveParts</c>), so concurrent edits would race.
    /// </remarks>
    public static class DawUstx {
        /// <summary>
        /// Serializes the open project to USTX YAML. An unsaved project has no
        /// <c>FilePath</c>, and <c>UWavePart.BeforeSave</c> builds paths relative to it, so
        /// serializing one dies inside <c>Path.GetRelativePath</c> with a null-argument
        /// exception that tells the user nothing. That case is reported here instead, in the
        /// same shape as the renderer's friendly errors.
        /// </summary>
        public static string Serialize(Ustx.UProject project) {
            if (string.IsNullOrEmpty(project.FilePath)) {
                throw new MessageCustomizableException(
                    "The project has not been saved. Save it before connecting a DAW plugin.",
                    "<translate:dawintegration.unsavedproject>", new InvalidOperationException(), false);
            }
            project.ustxVersion = Format.Ustx.kUstxVersion;
            project.BeforeSave();
            try {
                return Yaml.DefaultSerializer.Serialize(project);
            } finally {
                project.AfterSave();
            }
        }
    }

    /// <summary>Payload for kinds that carry no fields: <c>ping</c> and <c>playbackStarted</c>.</summary>
    public class DawEmptyPayload {
        public static readonly DawEmptyPayload Instance = new DawEmptyPayload();
    }

    /// <summary>
    /// <c>init</c> request payload (PROTOCOL.md §6.1): the full-project baseline, pushed once
    /// per connection. OpenUtau is the sole owner of the project, so the baseline only ever
    /// travels outward and is never echoed back.
    /// </summary>
    public class InitRequest {
        [JsonPropertyName("ustx")] public string Ustx { get; set; } = string.Empty;
    }

    /// <summary><c>init</c> response data: the plugin's protocol version, for the §4 major check.</summary>
    public class InitResponse {
        [JsonPropertyName("apiVersion")] public string ApiVersion { get; set; } = DawApiVersion.CurrentString;
    }

    /// <summary><c>updateUstx</c> notification payload (PROTOCOL.md §6.1).</summary>
    public class UpdateUstxNotification {
        [JsonPropertyName("ustx")] public string Ustx { get; set; } = string.Empty;
    }

    /// <summary>One entry of <c>updatePartLayout</c>. <see cref="AudioHash"/> is a decimal XXH64 string.</summary>
    public class DawPartLayout {
        [JsonPropertyName("trackNo")] public int TrackNo { get; set; }
        [JsonPropertyName("startMs")] public double StartMs { get; set; }
        [JsonPropertyName("endMs")] public double EndMs { get; set; }
        [JsonPropertyName("audioHash")] public string AudioHash { get; set; } = string.Empty;
    }

    /// <summary><c>updatePartLayout</c> request payload (PROTOCOL.md §6.1).</summary>
    public class UpdatePartLayoutRequest {
        [JsonPropertyName("parts")] public List<DawPartLayout> Parts { get; set; } = new List<DawPartLayout>();
    }

    /// <summary><c>updatePartLayout</c> response data: hashes the plugin does not hold yet.</summary>
    public class UpdatePartLayoutResponse {
        [JsonPropertyName("missingAudios")] public List<string> MissingAudios { get; set; } = new List<string>();
    }

    /// <summary><c>getAudio</c> request payload. The response is a data-plane frame, not an envelope.</summary>
    public class GetAudioRequest {
        [JsonPropertyName("hash")] public string Hash { get; set; } = string.Empty;
    }

    /// <summary>One entry of <c>updateTracks</c>, in OpenUtau's internal scale (<c>UTrack.Volume</c> dB, <c>UTrack.Pan</c>).</summary>
    public class DawTrackInfo {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("volume")] public double Volume { get; set; }
        [JsonPropertyName("pan")] public double Pan { get; set; }

        /// <summary>
        /// The effective mute, i.e. <see cref="Ustx.UTrack.Muted"/> — mute with solo already
        /// resolved against the rest of the project. Part audio travels pre-fader, so without
        /// this the plugin has no way to reproduce a mute or a solo.
        /// </summary>
        [JsonPropertyName("muted")] public bool Muted { get; set; }

        /// <summary>
        /// v1.2: the track's singer display name (<see cref="Ustx.UTrack.Singer"/>), or the
        /// empty string when the track has none yet. Informational — the plugin's GUI shows
        /// it next to the track name; it does not affect the audio. Omitted on the wire when
        /// the peer negotiated a minor below 2 (§10: the newer side omits fields the older
        /// minor does not know).
        /// </summary>
        [JsonPropertyName("singer")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Singer { get; set; } = string.Empty;

        /// <summary>
        /// v1.2: the track's render engine key (<see cref="Ustx.URenderSettings.renderer"/>,
        /// e.g. CLASSIC, WORLDLINE-R, DIFFSINGER), or the empty string when the track has no
        /// usable singer/renderer yet. Informational only; version-gated like <see cref="Singer"/>.
        /// </summary>
        [JsonPropertyName("engine")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Engine { get; set; } = string.Empty;
    }

    /// <summary><c>updateTracks</c> notification payload (PROTOCOL.md §6.1).</summary>
    public class UpdateTracksNotification {
        [JsonPropertyName("tracks")] public List<DawTrackInfo> Tracks { get; set; } = new List<DawTrackInfo>();
    }

    /// <summary>
    /// <c>updateProjectInfo</c> notification payload (v1.1): what a plugin's info window shows
    /// about the project. The name is the file stem; an unsaved project reports
    /// <see cref="Saved"/> false and an empty name.
    /// </summary>
    public class UpdateProjectInfoNotification {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("saved")] public bool Saved { get; set; }
    }

    /// <summary>
    /// <c>playhead</c> notification payload (v1.1): the DAW's transport position, one-way
    /// towards OpenUtau. <see cref="PositionMs"/> is absolute milliseconds on the shared
    /// timeline — the same coordinate <see cref="DawPartLayout.StartMs"/> uses.
    /// </summary>
    public class PlayheadNotification {
        [JsonPropertyName("positionMs")] public double PositionMs { get; set; }
        [JsonPropertyName("playing")] public bool Playing { get; set; }
    }

    /// <summary><c>bpm</c> notification payload (v1.1): the DAW project's tempo.</summary>
    public class BpmNotification {
        [JsonPropertyName("bpm")] public double Bpm { get; set; }
    }
}
