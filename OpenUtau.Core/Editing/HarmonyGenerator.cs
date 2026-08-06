using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Editing {
    public static class HarmonyGenerator {
        public static IReadOnlyList<int> CreateHarmonyTracks(
            UProject project,
            UVoicePart sourcePart,
            MusicalKey key,
            IReadOnlyList<HarmonyInterval> intervals) {
            if (intervals == null || intervals.Count == 0) {
                return Array.Empty<int>();
            }
            var sourceTrack = project.tracks[sourcePart.trackNo];
            var createdTrackNos = new List<int>();
            int insertAt = sourcePart.trackNo + 1;
            foreach (var interval in intervals) {
                var track = new UTrack($"{sourceTrack.TrackName} ({IntervalSuffix(interval)})") {
                    TrackNo = insertAt,
                    Singer = sourceTrack.Singer,
                    Phonemizer = sourceTrack.Phonemizer,
                    RendererSettings = sourceTrack.RendererSettings.Clone(),
                    Mute = sourceTrack.Mute,
                    Muted = sourceTrack.Muted,
                    Solo = false,
                    Volume = sourceTrack.Volume,
                    Pan = sourceTrack.Pan,
                    TrackColor = sourceTrack.TrackColor,
                    TrackExpressions = sourceTrack.TrackExpressions.Select(exp => exp.Clone()).ToList(),
                };
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, track));
                var part = (UVoicePart)sourcePart.Clone();
                part.name = sourcePart.name;
                part.trackNo = track.TrackNo;
                foreach (var note in part.notes) {
                    note.tone = ClampTone(KeySignatureHelper.TransposeHarmony(note.tone, key, interval));
                }
                DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
                createdTrackNos.Add(track.TrackNo);
                insertAt++;
            }
            return createdTrackNos;
        }

        static int ClampTone(int tone) {
            const int minTone = 1;
            const int maxTone = 12 * 11 - 1;
            return Math.Clamp(tone, minTone, maxTone);
        }

        static string IntervalSuffix(HarmonyInterval interval) => interval switch {
            HarmonyInterval.ThirdAbove => "3+",
            HarmonyInterval.ThirdBelow => "3-",
            HarmonyInterval.FifthAbove => "5+",
            HarmonyInterval.FifthBelow => "5-",
            _ => "Harmony",
        };
    }
}
