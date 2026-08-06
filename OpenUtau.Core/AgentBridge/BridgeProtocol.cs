using System;
using System.Collections.Generic;

namespace OpenUtau.Core.AgentBridge {
    /// <summary>Compact v2 envelope shared by the local MCP coordinator and OpenUtau.</summary>
    public static class BridgeProtocol {
        public const int Version = 2;
        public const string BridgeVersion = "0.2.0";
        public const string RequestFileName = "openutau-agent-bridge.request.json";
        public const string ProcessingFileName = "openutau-agent-bridge.processing.json";
        public const string ResponseFileName = "openutau-agent-bridge.response.json";
        public const string StatusFileName = "openutau-agent-bridge.status.json";
        private static readonly HashSet<string> Actions = new(StringComparer.Ordinal) {
            "ping", "get_project_info", "get_state_snapshot", "get_state_events", "get_bridge_diagnostics", "set_track_config", "set_track_singer", "create_part", "add_notes_simple",
            "edit_note_simple", "delete_notes_simple", "playback", "get_editor_state", "save_file", "load_file",
            "navigate_editor", "open_piano_roll",
        };

        public static bool IsSupportedAction(string? action) => action != null && Actions.Contains(action);
    }
}
