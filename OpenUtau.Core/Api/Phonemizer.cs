using System;
using System.Collections.Generic;
using System.Globalization;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Api {
    /// <summary>
    /// Mark your Phonemizer class with this attribute for OpenUtau to load it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class PhonemizerAttribute : Attribute {
        public string Name { get; private set; }
        public string Tag { get; private set; }
        public string Author { get; private set; }
        public string Language { get; private set; }

        /// <param name="name">Name of phonemizer. Required.</param>
        /// <param name="tag">Use IETF language code + phonetic type as tag, e.g., "EN ARPA", "JA VCV", etc. Required.</param>
        /// <param name="author">Author of this phonemizer.</param>
        /// <param name="language">IETF language code of this phonemizer's singing language, e.g., "EN", "JA"</param>
        public PhonemizerAttribute(string name, string tag, string author = null, string language = null) {
            Name = name;
            Tag = tag;
            Author = author;
            Language = language;
        }
    }

    /// <summary>
    /// Phonemizer plugin interface.
    /// </summary>
    public abstract class Phonemizer {
        /// <summary>
        /// The input struct that represents a note.
        /// </summary>
        public struct Note {
            /// <summary>
            /// Lyric of the note, the part of lyric that is not enclosed in "[]".
            /// Example: if lyric on note is "read". The lyric is "read".
            /// Example: if lyric on note is "read[r iy d]". The lyric is "read".
            /// </summary>
            public string lyric;

            /// <summary>
            /// Phonetic hint,
            /// Example: if lyric on note is "read". The hint is null.
            /// Example: if lyric on note is "read[r iy d]". The hint is "r iy d".
            /// </summary>
            public string phoneticHint;

            /// <summary>
            /// Music tone of note. C4 = 60.
            /// </summary>
            public int tone;

            /// <summary>
            /// Position of note in project, measured in ticks.
            /// Use timeAxis to convert between ticks and milliseconds .
            /// </summary>
            public int position;

            /// <summary>
            /// Duration of note measured in ticks.
            /// Use timeAxis to convert between ticks and milliseconds .
            /// </summary>
            public int duration;

            /// <summary>
            /// Phoneme overrides. Not guaranteed to exist or be ordered.
            /// </summary>
            public PhonemeAttributes[] phonemeAttributes;

            public override string ToString() => $"\"{lyric}\" pos:{position}";
        }

        public struct PhonemeAttributes {
            /// <summary>
            /// Index of phoneme.
            /// </summary>
            public int index;
            /// <summary>
            /// Consonant stretch ratio computed from velocity.
            /// </summary>
            public double? consonantStretchRatio;
            /// <summary>
            /// Tone shift. Shifts the note tone used for oto lookup.
            /// </summary>
            public int toneShift;
            /// <summary>
            /// Alternate index. The number suffix of duplicate aliases.
            /// </summary>
            public int? alternate;
            /// <summary>
            /// Voice color.
            /// </summary>
            public string voiceColor;
        }

        public struct PhonemeExpression {
            public string abbr;
            public float value;
        }

        /// <summary>
        /// The output struct that represents a phoneme.
        /// </summary>
        public struct Phoneme {
            /// <summary>
            /// Number to manage phonemes in note.
            /// Optional. Whether to specify an index or not should be consistent within Phonemizer (All phonemes should be indexed, or all should be unindexed).
            /// </summary>
            public int? index;

            /// <summary>
            /// Phoneme name. Should match one of oto alias.
            /// Note that you don't have to return tone-mapped phonemes. OpenUtau will do it afterwards.
            /// I.e., you can simply return "あ" even when best match is actually "あC5".
            /// OpenUtau will try to find the most suitable tone for this phoneme, and tone-map based on it.
            /// </summary>
            public string phoneme;

            /// <summary>
            /// Position of phoneme in note. Measured in ticks.
            /// Use TickToMs() and MsToTick() to convert between ticks and milliseconds.
            /// </summary>
            public int position;

            /// <summary>
            /// Error from the phonemizer, if this phoneme represents an error.
            /// </summary>
            public Exception? error;

            /// <summary>
            /// Suggested attributes. It may later be overwritten with a user-specified value.
            /// </summary>
            public List<PhonemeExpression> expressions;

            public override string ToString() => $"\"{phoneme}\" pos:{position}";
        }

        /// <summary>
        /// Result returned by Process().
        /// </summary>
        public struct Result {
            /// <summary>
            /// An array of phonemes that are corresponding to input notes.
            /// </summary>
            public Phoneme[] phonemes;
        }

        public string Name { get; set; }
        public string Tag { get; set; }
        public string Language { get; set; }
        internal Exception? SetUpException { get; set; }

        protected double bpm;
        protected TimeAxis timeAxis;

        /// <summary>
        /// Sets the current singer. Called by OpenUtau when user changes the singer.
        ///
        /// In addition to using data in the USinger class,
        /// a phonemizer can also use this method to load singer-specific resource,
        /// such as a custom dictionary file in the singer directory.
        /// Use singer.Location to access the singer directory.
        ///
        /// Do not modify the singer.
        /// </summary>
        /// <param name="singer"></param>
        public abstract void SetSinger(USinger singer);

        /// <summary>
        /// Uses the legacy bahaviour of further mapping phonemizer outputs.
        /// Do not override for new phonemizers.
        /// </summary>
        public virtual bool LegacyMapping => false;

        public virtual void SetUp(Note[][] notes, UProject project, UTrack track) { }

        /// <summary>
        /// Phonemize a consecutive sequence of notes. This is the main logic of a phonemizer.
        /// </summary>
        /// <param name="notes">A note and its extender notes. Always one or more.</param>
        /// <param name="prev">The note before the leading note, if exists.</param>
        /// <param name="next">The note after the last extender note, if exists.</param>
        /// <param name="prevNeighbour">Same as prev if is immediate neighbour, otherwise null.</param>
        /// <param name="nextNeighbour">Same as next if is immediate neighbour, otherwise null.</param>
        /// <param name="prevs">Prev note neighbour with all extended notes. May be emtpy, not null</param>
        public abstract Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs);

        public virtual void CleanUp() { }

        public override string ToString() => $"[{Tag}] {Name}";

        /// <summary>
        /// Used by OpenUtau to set timing info for TickToMs() and MsToTick().
        /// Not need to call this method from within a phonemizer.
        /// </summary>
        public void SetTiming(TimeAxis timeAxis) {
            this.timeAxis = timeAxis;
            bpm = timeAxis.GetBpmAtTick(0);
        }

        public string DictionariesPath => PathManager.Inst.DictionariesPath;
        public string PluginDir => PathManager.Inst.PluginsPath;

        /// <summary>
        /// Utility method to convert tick position to millisecond position.
        /// </summary>
        [Obsolete] // TODO: update usages
        protected double TickToMs(int tick) {
            return timeAxis.TickPosToMsPos(tick);
        }

        /// <summary>
        /// Utility method to convert millisecond position to tick position.
        /// </summary>
        [Obsolete] // TODO: update usages
        protected int MsToTick(double ms) {
            return timeAxis.MsPosToTickPos(ms);
        }

        /// <summary>
        /// Utility method to convert plain string to Unicode elements.
        /// </summary>
        public static IList<string> ToUnicodeElements(string lyric) {
            var result = new List<string>();
            var etor = StringInfo.GetTextElementEnumerator(lyric);
            while (etor.MoveNext()) {
                result.Add(etor.GetTextElement());
            }
            return result;
        }

        public bool Testing { get; set; } = false;

        protected void OnAsyncInitStarted() {
            if (!Testing) {
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, "Initializing phonemizer..."));
            }
        }

        protected void OnAsyncInitFinished() {
            if (!Testing) {
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ""));
                DocManager.Inst.ExecuteCmd(new ValidateProjectNotification());
                DocManager.Inst.ExecuteCmd(new PreRenderNotification());
            }
        }

        protected Result MakeSimpleResult(string phoneme) {
            return new Result() {
                phonemes = new Phoneme[] {
                    new Phoneme() {
                        phoneme = phoneme
                    }
                }
            };
        }

        /// <summary>
        /// Utility method to map a phoneme alias to proper pitch using prefixmap.
        /// For example, MapPhoneme("あ", 72, singer) may return "あC5".
        /// </summary>
        /// <param name="phoneme">Alias before pitch mapping.</param>
        /// <param name="tone">Music tone of note. C4 = 60.</param>
        /// <param name="singer">The singer.</param>
        /// <returns>Mapped alias.</returns>
        public static string MapPhoneme(string phoneme, int tone, string color, string alt, USinger singer) {
            if (singer.TryGetMappedOto(phoneme + alt, tone, color, out var otoAlt)) {
                return otoAlt.Alias;
            }
            if (singer.TryGetMappedOto(phoneme, tone, color, out var oto)) {
                return oto.Alias;
            }
            return phoneme;
        }
    }
}
