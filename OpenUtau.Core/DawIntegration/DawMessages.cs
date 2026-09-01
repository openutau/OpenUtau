using System.Collections.Generic;
using Newtonsoft.Json;

namespace OpenUtau.Core.DawIntegration {
    public abstract class DawMessage {
    }

    /// <summary>
    /// OpenUtau to DAW notification.
    /// Does not require a response.
    /// </summary>
    public abstract class DawDawNotification : DawMessage {
        [JsonIgnore]
        public abstract string kind { get; }
    }
    /// <summary>
    /// DAW to OpenUtau notification.
    /// </summary>
    public abstract class DawOuNotification : DawMessage {
    }
    public sealed class PingNotification : DawOuNotification {
    }
    public sealed class PlaybackStartedNotification : DawOuNotification {
    }
    /// <summary>
    /// OpenUtau to DAW request.
    /// </summary>
    public abstract class DawDawRequest : DawMessage {
        [JsonIgnore]
        public abstract string kind { get; }
    }
    /// <summary>
    /// OpenUtau to DAW response.
    /// </summary>
    public class DawDawResponse : DawMessage {
    }

    /// <summary>
    /// DAW to OpenUtau request.
    /// </summary>
    public abstract class DawOuRequest : DawMessage {
    }
    /// <summary>
    /// DAW to OpenUtau response.
    /// </summary>
    public class DawOuResponse : DawMessage {
    }

    public class DawResult<T> : DawMessage where T : DawDawResponse {
        public bool success;
        public T? data;
        public string? error;

        public DawResult(bool success, T? data, string? error) {
            this.success = success;
            this.data = data;
            this.error = error;
        }
    }


    public class InitRequest : DawDawRequest {
        public InitRequest() {
        }
        public override string kind => "init";
    }
    public class InitResponse : DawDawResponse {
        public string ustx;

        public InitResponse(string ustx) {
            this.ustx = ustx;
        }
    }
    public class UpdateUstxNotification : DawDawNotification {
        public string ustx;

        public UpdateUstxNotification(string ustx) {
            this.ustx = ustx;
        }

        public override string kind => "updateUstx";
    }
    public class UpdatePartLayoutRequest : DawDawRequest {
        public List<Part> parts;

        public override string kind => "updatePartLayout";

        public UpdatePartLayoutRequest(List<Part> parts) {
            this.parts = parts;
        }

        public class Part {
            public int trackNo;
            public double startMs;
            public double endMs;
            public uint audioHash;

            public Part(int trackNo, double startMs, double endMs, uint audioHash) {
                this.trackNo = trackNo;
                this.startMs = startMs;
                this.endMs = endMs;
                this.audioHash = audioHash;
            }
        }
    }

    public class UpdatePartLayoutResponse : DawDawResponse {
        public List<uint> missingAudios;

        public UpdatePartLayoutResponse(List<uint> missingAudios) {
            this.missingAudios = missingAudios;
        }

    }

    public class UpdateTracksNotification : DawDawNotification {
        public List<Track> tracks;

        public override string kind => "updateTracks";

        public UpdateTracksNotification(List<Track> tracks) {
            this.tracks = tracks;
        }

        public class Track {
            public string name;
            public double volume;
            public double pan;

            public Track(string name, double volume, double pan) {
                this.name = name;
                this.volume = volume;
                this.pan = pan;
            }
        }
    }

    public class UpdateAudioNotification : DawDawNotification {
        public Dictionary<uint, string> audios;

        public override string kind => "updateAudio";

        public UpdateAudioNotification(Dictionary<uint, string> audios) {
            this.audios = audios;
        }
    }
}
