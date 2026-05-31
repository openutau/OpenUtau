using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    class LyricBoxViewModel : ViewModelBase {
        public class SuggestionItem {
            public string Alias { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
        }

        [Reactive] public UVoicePart? Part { get; set; }
        [Reactive] public LyricBoxNoteOrPhoneme? NoteOrPhoneme { get; set; }
        [Reactive] public bool IsVisible { get; set; }
        [Reactive] public string? Text { get; set; }
        [Reactive] public SuggestionItem? SelectedSuggestion { get; set; }
        [Reactive] public ObservableCollectionExtended<SuggestionItem> Suggestions { get; set; }

        public bool IsAliasBox => isAliasBox.Value;
        private readonly ObservableAsPropertyHelper<bool> isAliasBox;

        public LyricBoxViewModel() {
            Text = string.Empty;
            Suggestions = new ObservableCollectionExtended<SuggestionItem>();

            this.WhenAnyValue(x => x.Text, x => x.IsVisible)
                .Subscribe(_ => UpdateSuggestion());
            this.WhenAnyValue(x => x.SelectedSuggestion)
                .WhereNotNull()
                .Subscribe(ss => Serilog.Log.Information(ss.Alias));

            isAliasBox = this.WhenAnyValue(x => x.NoteOrPhoneme)
                .Select(v => v is LyricBoxPhoneme)
                .ToProperty(this, x => x.IsAliasBox);
        }

        private void UpdateSuggestion() {
            if (Part == null || NoteOrPhoneme == null) {
                Suggestions.Clear();
                return;
            }
            var singer = DocManager.Inst.Project.tracks[Part.trackNo].Singer;
            if (singer == null || !singer.Found || !singer.Loaded) {
                Suggestions.Clear();
                Suggestions.Add(new SuggestionItem() {
                    Alias = "No Singer",
                });
                return;
            }
            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
            Task.Run(() => singer.GetSuggestions(Text ?? "").Select(oto => new SuggestionItem() {
                Alias = oto.Alias,
                Source = string.IsNullOrEmpty(oto.Set) ? singer.Id : $"{oto.Set}",
            }).Take(32).ToList()).ContinueWith(task => {
                Suggestions.Clear();
                if (!string.IsNullOrEmpty(Text) && Core.Util.ActiveLyricsHelper.Inst.Current != null) {
                    string text = Core.Util.ActiveLyricsHelper.Inst.Current.Convert(Text);
                    if (Core.Util.Preferences.Default.LyricsHelperBrackets) {
                        text = $"[{text}]";
                    }
                    Suggestions.Add(new SuggestionItem() {
                        Alias = text,
                        Source = Core.Util.ActiveLyricsHelper.Inst.Current.Source,
                    });
                }
                if (!task.IsFaulted) {
                    Suggestions.AddRange(task.Result);
                }
            }, scheduler);
        }

        public void Commit() {
            if (Part == null || NoteOrPhoneme == null || Text == null) {
                return;
            }
            if (!IsAliasBox) {
                var note = NoteOrPhoneme as LyricBoxNote;
                if (Text == note!.Unwrap().lyric) {
                    return;
                }
            } else {
                var phoneme = NoteOrPhoneme as LyricBoxPhoneme;
                if (Text == phoneme!.Unwrap().phoneme) {
                    return;
                }
            }
            if (IsAliasBox) {
                DocManager.Inst.StartUndoGroup("command.phoneme.edit");
                var phoneme = (NoteOrPhoneme as LyricBoxPhoneme)!.Unwrap();
                var note = phoneme.Parent;
                int index = phoneme.index;
                DocManager.Inst.ExecuteCmd(new ChangePhonemeAliasCommand(Part, note.Extends ?? note, index, Text));
            } else {
                DocManager.Inst.StartUndoGroup("command.note.lyric");
                DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(Part, (NoteOrPhoneme as LyricBoxNote)!.Unwrap(), Text));
            }
            DocManager.Inst.EndUndoGroup();
        }
    }

    public abstract class LyricBoxNoteOrPhoneme { }
    public class LyricBoxNote : LyricBoxNoteOrPhoneme {
        public UNote note;
        public LyricBoxNote(UNote note) { this.note = note; }
        public UNote Unwrap() => note;
    }
    public class LyricBoxPhoneme : LyricBoxNoteOrPhoneme {
        public UPhoneme phoneme;
        public LyricBoxPhoneme(UPhoneme phoneme) { this.phoneme = phoneme; }
        public UPhoneme Unwrap() => phoneme;
    }
}
