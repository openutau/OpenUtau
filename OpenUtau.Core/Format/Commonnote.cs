using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Newtonsoft.Json;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection.Emit;
using TextCopy;
using Serilog;

//Commonnote format definition: https://github.com/ExpressiveLabs/commonnote
namespace OpenUtau.Core.Format {
    public struct CommonnoteNote {
        public long start;
        public long length;
        public string label;
        public int pitch;
    }

    public struct CommonnoteHeader {
        public long resolution;
        public string origin;
    }

    public struct CommonnoteData {
        public string identifier;
        public CommonnoteHeader header;
        public List<CommonnoteNote> notes;
    }

    public static class Commonnote {
        static CommonnoteNote DumpNote(UNote uNote) {
            return new CommonnoteNote {
                start = uNote.position,
                length = uNote.duration,
                label = uNote.lyric,
                pitch = uNote.tone,
            };
        }

        static UNote LoadNote(CommonnoteNote cNote, int resolution, UProject project) {
            int position = (int)(cNote.start * project.resolution / resolution);
            int duration = (int)((cNote.start + cNote.length) * project.resolution / resolution - position);
            string lyric = cNote.label;
            if (string.IsNullOrEmpty(cNote.label)) {
                lyric = NotePresets.Default.DefaultLyric;
            }
            var note = project.CreateNote(cNote.pitch, position, duration);
            note.lyric = lyric;
            return note;
        }

        public static string Dumps(List<UNote> uNotes, UProject project) {
            var data = new CommonnoteData {
                identifier = "commonnote",
                header = new CommonnoteHeader {
                    resolution = project.resolution,
                    origin = "openutau",
                },
                notes = uNotes.Select(DumpNote).ToList(),
            };
            return JsonConvert.SerializeObject(data);
        }

        public static List<UNote> Loads(string text, UProject project) {
            var data = JsonConvert.DeserializeObject<CommonnoteData>(text);
            if (data.identifier != "commonnote") {
                Log.Error($"Clipboard is missing commonnote header");
                return null;
            }
            int resolution = (int)(data.header.resolution > 0 ? data.header.resolution : project.resolution);
            return data.notes.Select(n => LoadNote(n, resolution, project)).ToList();
        }

        public static void CopyToClipboard(List<UNote> uNotes, UProject project) {
            var text = Dumps(uNotes, project);
            ClipboardService.SetText(text);
        }

        public static List<UNote>? LoadFromClipboard(UProject project) {
            var text = ClipboardService.GetText();
            if (String.IsNullOrEmpty(text)) {
                return null;
            }
            return Loads(text, project);
        }
    }
}
