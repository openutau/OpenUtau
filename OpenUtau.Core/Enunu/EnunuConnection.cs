using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Enunu {
    /// <summary>
    /// Command names, as the server expects them.
    /// </summary>
    static class EnunuCommand {
        public const string VerCheck = "ver_check";
        public const string Timing = "timing";
        public const string Acoustic = "acoustic";
        public const string Pitch = "pitch";
        public const string AcousticF0 = "acoustic_f0";
        public const string Synthe = "synthe";
        public const string Config = "config";
    }

    interface IEnunuResponse {
        string? Error { get; }
    }

    public struct VersionResult {
        public string name;
        public string version;
        public string author;
        /// <summary>Added by ENUNUServer 2. Null on older servers.</summary>
        public EnunuServerFeatures? features;
    }

    public class EnunuServerFeatures {
        public string[] commands;
        public bool style_shift;
        public bool pitch_n_frames;
        public Dictionary<string, EnunuDiffusionSetting>? diffusion;

        public bool Has(string command) => commands != null && commands.Contains(command);
        public bool SupportsPitch => Has(EnunuCommand.Pitch) && Has(EnunuCommand.AcousticF0);
    }

    public class EnunuDiffusionSetting {
        public string method;
        public int steps;
    }

    public struct VersionResponse : IEnunuResponse {
        public string? error;
        public VersionResult result;
        public readonly string? Error => error;
    }

    struct TimingResult {
        public string path_full_timing;
        public string path_mono_timing;
    }

    struct TimingResponse : IEnunuResponse {
        public string? error;
        public TimingResult result;
        public readonly string? Error => error;
    }

    struct AcousticResult {
        public string path_f0;
        public string path_spectrogram;
        public string path_aperiodicity;
        public string path_mel;
        public string path_vuv;
        /// <summary>Only in acoustic_f0 responses.</summary>
        public bool? lf0_conditioning;
    }

    struct AcousticResponse : IEnunuResponse {
        public string? error;
        public AcousticResult result;
        public readonly string? Error => error;
    }

    struct PitchResult {
        public string path_f0;
        public bool lf0_conditioning;
        public int n_frames;
    }

    struct PitchResponse : IEnunuResponse {
        public string? error;
        public PitchResult result;
        public readonly string? Error => error;
    }

    struct SyntheResult {
        public string path_wav;
    }

    struct SyntheResponse : IEnunuResponse {
        public string? error;
        public SyntheResult result;
        public readonly string? Error => error;
    }

    /// <summary>
    /// Talks to the ENUNU server. Holds only what belongs to the server (port and features);
    /// everything about a phrase is passed in as arguments, so one instance serves all tracks and threads.
    /// Each command method matches one server command.
    /// </summary>
    class EnunuConnection : Util.SingletonBase<EnunuConnection> {
        /// <summary>request[4]: how long the server keeps the voicebank's models loaded after the last use.</summary>
        const string EngineLifetimeSec = "600";
        /// <summary>
        /// request[5]: style shift in semitones. Required by ENUNUServer 2 and ignored by older servers.
        /// Always 0: style shifts are sent per note as S flags in the UST.
        /// </summary>
        const int StyleShift = 0;
        const int RequestTimeoutSec = 300;
        const int VerCheckTimeoutSec = 1;

        readonly object stateLock = new object();
        string? port;
        EnunuServerFeatures? features;
        readonly ConcurrentDictionary<string, bool> lf0Conditioning = new ConcurrentDictionary<string, bool>();

        /// <summary>Connects if needed and returns the server's features (null for older servers).</summary>
        public EnunuServerFeatures? GetFeatures() {
            EnsureConnected();
            return features;
        }

        /// <summary>
        /// Whether the voicebank's acoustic model takes the editor pitch (has an lf0_model).
        /// Null until a pitch or acoustic_f0 response has told us.
        /// </summary>
        public bool? Lf0Conditioning(string voicebankNameHash) {
            return lf0Conditioning.TryGetValue(voicebankNameHash, out var value) ? value : null;
        }

        public TimingResponse Timing(string ustPath, string voicebankNameHash) {
            return Request<TimingResponse>(CommandRequest(EnunuCommand.Timing, ustPath, "", voicebankNameHash));
        }

        public AcousticResponse Acoustic(string ustPath, string voicebankNameHash) {
            return Request<AcousticResponse>(CommandRequest(EnunuCommand.Acoustic, ustPath, "", voicebankNameHash));
        }

        public PitchResponse Pitch(string ustPath, string voicebankNameHash) {
            var response = Request<PitchResponse>(CommandRequest(EnunuCommand.Pitch, ustPath, "", voicebankNameHash));
            lf0Conditioning[voicebankNameHash] = response.result.lf0_conditioning;
            return response;
        }

        /// <param name="editorF0">Hz per frame. Frames with 0 use the model's own pitch.</param>
        public AcousticResponse AcousticF0(string ustPath, string voicebankNameHash, double[] editorF0) {
            var response = Request<AcousticResponse>(CommandRequest(EnunuCommand.AcousticF0, ustPath, "", voicebankNameHash, editorF0));
            if (response.result.lf0_conditioning is bool value) {
                lf0Conditioning[voicebankNameHash] = value;
            }
            return response;
        }

        public SyntheResponse Synthe(string ustPath, string wavPath, string voicebankNameHash) {
            return Request<SyntheResponse>(CommandRequest(EnunuCommand.Synthe, ustPath, wavPath, voicebankNameHash));
        }

        /// <summary>
        /// [command, ust, wav, voicebank, engine lifetime, style shift, ...extra].
        /// acoustic_f0 puts the editor f0 in extra, at request[6].
        /// </summary>
        internal static object[] CommandRequest(string command, string ustPath, string wavPath, string voicebankNameHash, params object[] extra) {
            var request = new List<object> { command, ustPath, wavPath, voicebankNameHash, EngineLifetimeSec, StyleShift };
            request.AddRange(extra);
            return request.ToArray();
        }

        T Request<T>(object[] request) where T : IEnunuResponse {
            string port = EnsureConnected();
            string? message = EnunuClient.Inst.Send(request, port, RequestTimeoutSec);
            if (string.IsNullOrEmpty(message)) {
                // TODO (stage 2): reset the connection and show a message that asks the user to (re)start the server.
                throw new Exception($"ENUNU server did not respond to {request[0]}.");
            }
            var response = Json.Deserialize<T>(message)!;
            if (response.Error != null) {
                throw new Exception(response.Error);
            }
            return response;
        }

        string EnsureConnected() {
            lock (stateLock) {
                if (port == null) {
                    (port, features) = Detect();
                }
                return port;
            }
        }

        // Same rule as the former EnunuUtils.SetPortNum. Stage 2 replaces this with the port setting.
        static (string port, EnunuServerFeatures? features) Detect() {
            string? message = EnunuClient.Inst.Send(new object[] { EnunuCommand.VerCheck }, "15556", VerCheckTimeoutSec);
            var response = string.IsNullOrEmpty(message) ? default : Json.Deserialize<VersionResponse>(message);
            if (response.Error != null) {
                Log.Error(response.Error);
            } else if (response.result.name != null) {
                Log.Information($"ENUNU server {response.result.name} {response.result.version} on 15556, new commands: {response.result.features?.SupportsPitch == true}");
                return ("15556", response.result.features);
            }
            return ("15555", null);
        }
    }
}
