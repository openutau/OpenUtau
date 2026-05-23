using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SharpCompress;
using OpenUtau.Api;

namespace OpenUtau.App.ViewModels {
    public class NotePropertiesViewModel : ViewModelBase, ICmdSubscriber {
        public string Title { get => ThemeManager.GetString("noteproperty") + " (" + selectedNotes.Count + " notes)"; }
        [Reactive] public string Lyric { get; set; } = string.Empty;
        [Reactive] public string Tone { get; set; } = string.Empty;
        [Reactive] public int Tuning { get; set; }
        [Reactive] public FontWeight TuningFontWeight { get; set; } = FontWeight.Normal;
        [Reactive] public float PortamentoLength { get; set; }
        [Reactive] public float PortamentoStart { get; set; }
        [Reactive] public int PitchCurveShape { get; set; } = -1;
        [Reactive] public bool VibratoEnable { get; set; }
        [Reactive] public float VibratoLength { get; set; }
        [Reactive] public float VibratoPeriod { get; set; }
        [Reactive] public float VibratoDepth { get; set; }
        [Reactive] public float VibratoIn { get; set; }
        [Reactive] public float VibratoOut { get; set; }
        [Reactive] public float VibratoShift { get; set; }
        [Reactive] public float VibratoDrift { get; set; }
        [Reactive] public float VibratoVolLink { get; set; }
        [Reactive] public float AutoVibratoNoteLength { get; set; }
        [Reactive] public bool AutoVibratoToggle { get; set; }
        [Reactive] public bool IsNoteSelected { get; set; } = false;
        [Reactive] public IReadOnlyList<MenuItemViewModel>? PhonemizerMenuItems { get; set; }
        public ReactiveCommand<string?, System.Reactive.Unit> SelectPhonemizerCommand { get; }
        [Reactive] public bool IsPhonemizerEnabled { get; set; } = true;
        public string PhonemizerOverrideText {
            get {
                string? targetId = PhonemizerOverride;
                bool isDefault = string.IsNullOrEmpty(targetId);
                if (isDefault) {
                    if (Part == null) return "Default";
                    var track = DocManager.Inst.Project.tracks[Part.trackNo];
                    string trackId = track.Phonemizer.GetType().FullName ?? "";
                    return $"Default ({GetPhonemizerDisplayName(trackId)})";
                }
                var factory = OpenUtau.Api.PhonemizerFactory.GetAll().FirstOrDefault(f => 
                    f.name == targetId || f.type.FullName == targetId || f.type.Name == targetId);
                if (factory != null) {
                    string displayName = !string.IsNullOrEmpty(factory.tag) ? factory.tag : factory.language;
                    return !string.IsNullOrEmpty(displayName) ? displayName : factory.ToString();
                }
                return targetId ?? "Default";
            }
        }

        private string? _phonemizerOverride;
        public string? PhonemizerOverride {
            get => _phonemizerOverride;
            set {
                this.RaiseAndSetIfChanged(ref _phonemizerOverride, value);
                this.RaisePropertyChanged(nameof(PhonemizerOverrideText));
            }
        }
        [Reactive] public ObservableCollection<NotePresets.PortamentoPreset>? PortamentoPresets { get; private set; }
        public NotePresets.PortamentoPreset? ApplyPortamentoPreset {
            get => appliedPortamentoPreset;
            set => this.RaiseAndSetIfChanged(ref appliedPortamentoPreset, value);
        }
        [Reactive] public ObservableCollection<NotePresets.VibratoPreset>? VibratoPresets { get; private set; }
        public NotePresets.VibratoPreset? ApplyVibratoPreset {
            get => appliedVibratoPreset;
            set => this.RaiseAndSetIfChanged(ref appliedVibratoPreset, value);
        }
        private NotePresets.PortamentoPreset? appliedPortamentoPreset = NotePresets.Default.DefaultPortamento;
        private NotePresets.VibratoPreset? appliedVibratoPreset = NotePresets.Default.DefaultVibrato;

        public UVoicePart? Part;
        private HashSet<UNote> selectedNotes = new HashSet<UNote>();
        public List<NotePropertyExpViewModel> Expressions = new List<NotePropertyExpViewModel>();
        public static bool PanelControlPressed { get; set; } = false;
        public static bool NoteLoading { get; set; } = false;
        private static bool AllowNoteEdit { get => PanelControlPressed && !NoteLoading; }

        public NotePropertiesViewModel() {
            PortamentoPresets = new ObservableCollection<NotePresets.PortamentoPreset>(NotePresets.Default.PortamentoPresets);
            VibratoPresets = new ObservableCollection<NotePresets.VibratoPreset>(NotePresets.Default.VibratoPresets);

            this.WhenAnyValue(vm => vm.ApplyPortamentoPreset)
                .WhereNotNull()
                .Subscribe(portamentoPreset => {
                    if (portamentoPreset != null) {
                        PortamentoLength = portamentoPreset.PortamentoLength;
                        PortamentoStart = portamentoPreset.PortamentoStart;

                        DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                        PanelControlPressed = true;
                        SetNoteParams("PortamentoLength", portamentoPreset.PortamentoLength);
                        SetNoteParams("PortamentoStart", portamentoPreset.PortamentoStart);
                        PanelControlPressed = false;
                        DocManager.Inst.EndUndoGroup();
                    }
                });
            this.WhenAnyValue(vm => vm.ApplyVibratoPreset)
                .WhereNotNull()
                .Subscribe(vibratoPreset => {
                    if (vibratoPreset != null) {
                        DocManager.Inst.StartUndoGroup("command.vibrato.edit");
                        PanelControlPressed = true;
                        SetNoteParams("VibratoLength", Math.Max(0, Math.Min(100, vibratoPreset.VibratoLength)));
                        SetNoteParams("VibratoPeriod", Math.Max(5, Math.Min(500, vibratoPreset.VibratoPeriod)));
                        SetNoteParams("VibratoDepth", Math.Max(5, Math.Min(200, vibratoPreset.VibratoDepth)));
                        SetNoteParams("VibratoIn", Math.Max(0, Math.Min(100, vibratoPreset.VibratoIn)));
                        SetNoteParams("VibratoOut", Math.Max(0, Math.Min(100, vibratoPreset.VibratoOut)));
                        SetNoteParams("VibratoShift", Math.Max(0, Math.Min(100, vibratoPreset.VibratoShift)));
                        SetNoteParams("VibratoDrift", Math.Max(-100, Math.Min(100, vibratoPreset.VibratoDrift)));
                        SetNoteParams("VibratoVolLink", Math.Max(0, Math.Min(100, vibratoPreset.VibratoVolLink)));
                        PanelControlPressed = false;
                        DocManager.Inst.EndUndoGroup();
                    }
                });
            this.WhenAnyValue(vm => vm.PitchCurveShape)
                .WhereNotNull()
                .Subscribe(shape => {
                    if (shape >= 0) {
                        DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                        PanelControlPressed = true;
                        SetNoteParams("PitchCurveShape", shape);
                        PanelControlPressed = false;
                        DocManager.Inst.EndUndoGroup();
                    }
                });
            SelectPhonemizerCommand = ReactiveCommand.Create<string?>(name => {
                DocManager.Inst.StartUndoGroup("command.property.edit");
                PanelControlPressed = true;
                SetNoteParams("PhonemizerOverride", name);
                PanelControlPressed = false;
                DocManager.Inst.EndUndoGroup();
            });

            MessageBus.Current.Listen<NotesSelectionEvent>()
                .Subscribe(e => {
                    if (PanelControlPressed) {
                        PanelControlPressed = false;
                        DocManager.Inst.EndUndoGroup();
                    }
                    NoteLoading = true;

                    selectedNotes.Clear();
                    selectedNotes.UnionWith(e.selectedNotes);
                    selectedNotes.UnionWith(e.tempSelectedNotes);
                    OnSelectNotes();

                    NoteLoading = false;
                });

            DocManager.Inst.AddSubscriber(this);
        }

        // note -> panel
        private void OnSelectNotes() {
            this.RaisePropertyChanged(nameof(Title));
            ApplyPortamentoPreset = null;
            ApplyVibratoPreset = null;
            PitchCurveShape = -1;

            if (selectedNotes.Count > 0) {
                IsNoteSelected = true;
                var note = selectedNotes.First();
                IsPhonemizerEnabled = !note.lyric.StartsWith("+"); 
                if (!IsPhonemizerEnabled) {
                    PhonemizerOverride = ThemeManager.GetString("noteproperty.parent.phonemizer");
                } else {
                    PhonemizerOverride = note.PhonemizerOverride ?? "";
                }

                Lyric = note.lyric;
                Tone = MusicMath.GetToneName(note.tone);
                Tuning = note.tuning;
                SetTuningFontWeight();
                if (note.pitch.data.Count >= 2) {
                    PortamentoLength = note.pitch.data.Last().X - note.pitch.data.First().X;
                    PortamentoStart = note.pitch.data.First().X;
                } else {
                    PortamentoLength = NotePresets.Default.DefaultPortamento.PortamentoLength;
                    PortamentoStart = NotePresets.Default.DefaultPortamento.PortamentoStart;
                }
                VibratoEnable = note.vibrato.length == 0 ? false : true;
                VibratoLength = note.vibrato.length;
                VibratoPeriod = note.vibrato.period;
                VibratoDepth = note.vibrato.depth;
                VibratoIn = note.vibrato.@in;
                VibratoOut = note.vibrato.@out;
                VibratoShift = note.vibrato.shift;
                VibratoDrift = note.vibrato.drift;
                VibratoVolLink = note.vibrato.volLink;
            } else {
                IsNoteSelected = false;
                Lyric = string.Empty;
                Tone = string.Empty;
                Tuning = 0;
                PhonemizerOverride = "";
                SetTuningFontWeight();
                PortamentoLength = NotePresets.Default.DefaultPortamento.PortamentoLength;
                PortamentoStart = NotePresets.Default.DefaultPortamento.PortamentoStart;
                VibratoEnable = false;
                VibratoLength = NotePresets.Default.DefaultVibrato.VibratoLength;
                VibratoPeriod = NotePresets.Default.DefaultVibrato.VibratoPeriod;
                VibratoDepth = NotePresets.Default.DefaultVibrato.VibratoDepth;
                VibratoIn = NotePresets.Default.DefaultVibrato.VibratoIn;
                VibratoOut = NotePresets.Default.DefaultVibrato.VibratoOut;
                VibratoShift = NotePresets.Default.DefaultVibrato.VibratoShift;
                VibratoDrift = NotePresets.Default.DefaultVibrato.VibratoDrift;
                VibratoVolLink = NotePresets.Default.DefaultVibrato.VibratoVolLink;
            }
            AutoVibratoNoteLength = NotePresets.Default.AutoVibratoNoteDuration;
            AutoVibratoToggle = NotePresets.Default.AutoVibratoToggle;

            AttachExpressions();
        }

        public void LoadPart(UPart? part) {
            Expressions.Clear();
            if (part != null && part is UVoicePart) {
                this.Part = part as UVoicePart;
                var track = DocManager.Inst.Project.tracks[part.trackNo];
                foreach (var descriptor in track.GetSupportedExps(DocManager.Inst.Project)) {
                    if (descriptor.type != UExpressionType.Curve) {
                        var viewModel = new NotePropertyExpViewModel(descriptor, this);
                        if (descriptor.abbr == Ustx.CLR) {
                            if (track.VoiceColorExp != null && track.VoiceColorExp.options.Length > 0) {
                                viewModel.Options.Clear();
                                track.VoiceColorExp.options.ForEach(opt => viewModel.Options.Add(opt));
                            }
                        }
                        Expressions.Add(viewModel);
                    }
                }
                AttachExpressions();
                RefreshPhonemizers();
            } else {
                this.Part = null;
            }
        }

        private string GetPhonemizerDisplayName(string? targetId) {
            if (string.IsNullOrEmpty(targetId)) return "Default";
            var factory = OpenUtau.Api.PhonemizerFactory.GetAll().FirstOrDefault(f => 
                f.name == targetId || f.type.FullName == targetId || f.type.Name == targetId);

            if (factory == null) return targetId;
            string displayName = !string.IsNullOrEmpty(factory.tag) ? factory.tag : factory.language;
            return !string.IsNullOrEmpty(displayName) ? displayName : factory.ToString();
        }

        public void RefreshPhonemizers() {
            var items = new List<MenuItemViewModel>();

            string defaultHeader = "Default";
            if (Part != null) {
                var track = DocManager.Inst.Project.tracks[Part.trackNo];
                string trackId = track.Phonemizer.GetType().FullName ?? "";
                defaultHeader = $"Default ({GetPhonemizerDisplayName(trackId)})";
            }
            items.Add(new MenuItemViewModel() {
                Header = defaultHeader,
                Command = SelectPhonemizerCommand,
                CommandParameter = null
            });

            items.Add(new MenuItemViewModel() { Header = "-", Height = 1 });
            items.AddRange(Preferences.Default.RecentPhonemizers
                .Select(name => PhonemizerFactory.Get(name))
                .OfType<PhonemizerFactory>()
                .OrderBy(factory => factory.tag)
                .Select(factory => new MenuItemViewModel() {
                    Header = factory.ToString(),
                    Command = SelectPhonemizerCommand,
                    CommandParameter = factory.name,
                }));

            items.Add(new MenuItemViewModel() {
                Header = $"{ThemeManager.GetString("tracks.more")} ...",
                Items = PhonemizerFactory.GetAll().GroupBy(factory => factory.language)
                .OrderBy(group => group.Key)
                .Select(group => new MenuItemViewModel() {
                    Header = GetPhonemizerGroupHeader(group.Key),
                    Items = group.Select(factory => new MenuItemViewModel() {
                        Header = factory.ToString(),
                        Command = SelectPhonemizerCommand,
                        CommandParameter = factory.name,
                    }).ToArray(),
                }).ToArray()
            });

            PhonemizerMenuItems = items;
        }

        public string GetPhonemizerGroupHeader(string key) {
            if (string.IsNullOrEmpty(key)) return "General";
            if (ThemeManager.TryGetString($"languages.{key.ToLowerInvariant()}", out var value)) {
                return $"{key}: {value}";
            }
            return key;
        }

        private void AttachExpressions() {
            if (Expressions.Count > 0) {
                if (selectedNotes.Count > 0) {
                    var note = selectedNotes.First();

                    foreach (NotePropertyExpViewModel exp in Expressions) {
                        exp.IsNoteSelected = true;
                        var phonemeExpression = note.phonemeExpressions.FirstOrDefault(e => e.abbr == exp.abbr && e.index == 0);
                        if (phonemeExpression != null) {
                            if (exp.IsNumerical) {
                                exp.Value = phonemeExpression.value;
                            } else if (exp.IsOptions) {
                                exp.SelectedOption = (int)phonemeExpression.value;
                            }
                            exp.HasValue = true;
                        } else {
                            if (exp.IsNumerical) {
                                exp.Value = exp.defaultValue;
                            } else if (exp.IsOptions) {
                                exp.SelectedOption = (int)exp.defaultValue;
                            }

                            if (selectedNotes.Any(note => note.phonemeExpressions.FirstOrDefault(e => e.abbr == exp.abbr) != null)) {
                                exp.HasValue = true;
                            } else {
                                exp.HasValue = false;
                            }
                        }
                    }
                } else {
                    foreach (NotePropertyExpViewModel exp in Expressions) {
                        exp.IsNoteSelected = false;
                        exp.HasValue = false;
                        if (exp.IsNumerical) {
                            exp.Value = exp.defaultValue;
                        } else if (exp.IsOptions) {
                            exp.SelectedOption = (int)exp.defaultValue;
                        }
                    }
                }
            }
        }

        #region ICmdSubscriber
        public void OnNext(UCommand cmd, bool isUndo) {
            var note = selectedNotes.FirstOrDefault();
            if (cmd is TrackChangePhonemizerCommand) {
                RefreshPhonemizers();
                this.RaisePropertyChanged(nameof(PhonemizerOverrideText));
            }
            if (note == null) { return; }

            if (cmd is NoteCommand) {
                if (cmd is ChangeNoteLyricCommand) {
                    Lyric = note.lyric;
                    this.RaisePropertyChanged(nameof(Lyric));
                } else if (cmd is MoveNoteCommand) {
                    Tone = MusicMath.GetToneName(note.tone);
                    this.RaisePropertyChanged(nameof(Tone));
                } else if (cmd is ChangeNoteTuningCommand) {
                    Tuning = note.tuning;
                    SetTuningFontWeight();
                    this.RaisePropertyChanged(nameof(Tuning));
                    this.RaisePropertyChanged(nameof(TuningFontWeight));
                } else if (cmd is VibratoCommand) {
                    if (cmd is VibratoLengthCommand || cmd is SetVibratoCommand) {
                        if (note.vibrato.length > 0) {
                            VibratoEnable = true;
                        } else {
                            VibratoEnable = false;
                        }
                        VibratoLength = note.vibrato.length;
                        this.RaisePropertyChanged(nameof(VibratoEnable));
                        this.RaisePropertyChanged(nameof(VibratoLength));
                    }
                    if (cmd is VibratoFadeInCommand || cmd is SetVibratoCommand) {
                        VibratoIn = note.vibrato.@in;
                        this.RaisePropertyChanged(nameof(VibratoIn));
                    }
                    if (cmd is VibratoFadeOutCommand || cmd is SetVibratoCommand) {
                        VibratoOut = note.vibrato.@out;
                        this.RaisePropertyChanged(nameof(VibratoOut));
                    }
                    if (cmd is VibratoDepthCommand || cmd is SetVibratoCommand) {
                        VibratoDepth = note.vibrato.depth;
                        this.RaisePropertyChanged(nameof(VibratoDepth));
                    }
                    if (cmd is VibratoPeriodCommand || cmd is SetVibratoCommand) {
                        VibratoPeriod = note.vibrato.period;
                        this.RaisePropertyChanged(nameof(VibratoPeriod));
                    }
                    if (cmd is VibratoShiftCommand || cmd is SetVibratoCommand) {
                        VibratoShift = note.vibrato.shift;
                        this.RaisePropertyChanged(nameof(VibratoShift));
                    }
                    if (cmd is VibratoDriftCommand || cmd is SetVibratoCommand) {
                        VibratoDrift = note.vibrato.drift;
                        this.RaisePropertyChanged(nameof(VibratoDrift));
                    }
                    if (cmd is VibratoVolumeLinkCommand || cmd is SetVibratoCommand) {
                        VibratoVolLink = note.vibrato.volLink;
                        this.RaisePropertyChanged(nameof(VibratoVolLink));
                    }
                } else if (cmd is ChangeNotePhonemizerCommand) {
                    PhonemizerOverride = note.PhonemizerOverride ?? "";
                    if (Part != null) {
                        DocManager.Inst.Project.Validate(new ValidateOptions { Part = Part });
                    }
                }
            } else if (cmd is ExpCommand) {
                if (cmd is PitchExpCommand) {
                    if (note.pitch.data.Count >= 2) {
                        PortamentoLength = note.pitch.data.Last().X - note.pitch.data.First().X;
                        PortamentoStart = note.pitch.data.First().X;
                    } else {
                        PortamentoLength = NotePresets.Default.DefaultPortamento.PortamentoLength;
                        PortamentoStart = NotePresets.Default.DefaultPortamento.PortamentoStart;
                    }
                    this.RaisePropertyChanged(nameof(PortamentoLength));
                    this.RaisePropertyChanged(nameof(PortamentoStart));
                } else if (cmd is SetNoteExpressionCommand || cmd is SetNotesSameExpressionCommand || cmd is SetPhonemeExpressionCommand || cmd is ResetExpressionsCommand) {
                    AttachExpressions();
                }
            } else if (cmd is NotePresetChangedNotification) {
                PortamentoPresets = new ObservableCollection<NotePresets.PortamentoPreset>(NotePresets.Default.PortamentoPresets);
                VibratoPresets = new ObservableCollection<NotePresets.VibratoPreset>(NotePresets.Default.VibratoPresets);
            }             
        }
        #endregion

        private void SetTuningFontWeight() {
            if (selectedNotes.Any(note => note.tuning != 0)) {
                TuningFontWeight = FontWeight.Bold;
            } else {
                TuningFontWeight = FontWeight.Normal;
            }
        }

        private float ParseValue(object? obj, string tag, float min, float max, float defaultValue) {
            if (obj != null && (obj is float f || float.TryParse(obj.ToString(), out f))) {
                if (tag.EndsWith("_TextBox")) {
                    return f;
                }
                return Math.Clamp(f, min, max);
            }
            return defaultValue;
        }

        // panel -> note
        public void SetNoteParams(string tag, object? obj) {
            if (AllowNoteEdit && Part != null && selectedNotes.Count > 0) {
                if (tag == "Lyric") {
                    if (obj is string s && !string.IsNullOrEmpty(s)) {
                        foreach (UNote note in selectedNotes) {
                            DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(Part, note, s));
                        }
                    } else {
                        var note = selectedNotes.FirstOrDefault();
                        Lyric = note != null ? note.lyric : string.Empty;
                        this.RaisePropertyChanged(nameof(Lyric));
                    }
                } else if (tag == "PhonemizerOverride") {
                    string? newOverride = obj as string;
                    if (string.IsNullOrEmpty(newOverride)) {
                        newOverride = null;
                    }
                    
                    DocManager.Inst.StartUndoGroup("command.property.edit");
                    foreach (UNote note in selectedNotes) {
                        if (note.PhonemizerOverride != newOverride) {
                            DocManager.Inst.ExecuteCmd(new ChangeNotePhonemizerCommand(Part, note, newOverride)); 
                        }
                    }
                    DocManager.Inst.EndUndoGroup();
                } else if (tag == "Tone") {
                    try {
                        if (obj is string s && !string.IsNullOrEmpty(s)) {
                            int tone = MusicMath.NameToTone(s);

                            if ((s.StartsWith("+") || s.StartsWith("-")) && int.TryParse(s, out int i) && i != 0) {
                                foreach (UNote note in selectedNotes) {
                                    DocManager.Inst.ExecuteCmd(new MoveNoteCommand(Part, note, 0, i));
                                }
                            } else if (tone >= 0) {
                                foreach (UNote note in selectedNotes) {
                                    DocManager.Inst.ExecuteCmd(new MoveNoteCommand(Part, note, 0, tone - note.tone));
                                }
                            } else {
                                throw new FormatException();
                            }
                        } else {
                            throw new FormatException();
                        }
                    } catch {
                        var note = selectedNotes.FirstOrDefault();
                        Tone = note != null ? MusicMath.GetToneName(note.tone) : string.Empty;
                        this.RaisePropertyChanged(nameof(Tone));
                    }
                } else if (tag == "Tuning") {
                    int value;
                    if (obj != null && (obj is int i || int.TryParse(obj.ToString(), out i)) && i >= -100 && i <= 100) {
                        value = i;
                    } else {
                        value = 0;
                    }
                    foreach (UNote note in selectedNotes) {
                        DocManager.Inst.ExecuteCmd(new ChangeNoteTuningCommand(Part, note, value));
                    }
                } else if (tag == "PortamentoLength" || tag == "PortamentoLength_TextBox") {
                    PortamentoLength = ParseValue(obj, tag, 2, 320, NotePresets.Default.DefaultPortamento.PortamentoLength);
                    foreach (var note in selectedNotes) {
                        if (note.pitch.data.Count < 2) {
                            continue;
                        }
                        var firstPoint = note.pitch.data.First();
                        var oldPortamentoLength = note.pitch.data.Last().X - firstPoint.X;
                        if (oldPortamentoLength <= 0) {
                            continue;
                        }
                        var scale = PortamentoLength / oldPortamentoLength;
                        foreach (var pitchPoint in note.pitch.data.Skip(1)) {
                            var newX = firstPoint.X + (pitchPoint.X - firstPoint.X) * scale;
                            var deltaX = newX - pitchPoint.X;
                            if (deltaX != 0) {
                                DocManager.Inst.ExecuteCmd(new MovePitchPointCommand(Part, pitchPoint, deltaX, 0));
                            }
                        }
                    }
                } else if (tag == "PortamentoStart" || tag == "PortamentoStart_TextBox") {
                    var oldPortamentoStart = selectedNotes.FirstOrDefault()?.pitch.data.FirstOrDefault()?.X
                        ?? NotePresets.Default.DefaultPortamento.PortamentoStart;
                    PortamentoStart = ParseValue(obj, tag, -200, 200, NotePresets.Default.DefaultPortamento.PortamentoStart);
                    var deltaX = PortamentoStart - oldPortamentoStart;
                    if (deltaX != 0) {
                        foreach (var note in selectedNotes) {
                            foreach (var pitchPoint in note.pitch.data) {
                                DocManager.Inst.ExecuteCmd(new MovePitchPointCommand(Part, pitchPoint, deltaX, 0));
                            }
                        }
                    }
                } else if (tag == "PitchCurveShape") {
                    if (obj != null && (obj is int value || int.TryParse(obj.ToString(), out value)) && Enum.IsDefined(typeof(PitchPointShape), value)) {
                        DocManager.Inst.ExecuteCmd(new SetPitchPointShapeCommand(Part, selectedNotes, (PitchPointShape)value));
                    }
                } else if (tag == "VibratoLength") {
                    float value;
                    if (obj != null && (obj is float f || float.TryParse(obj.ToString(), out f)) && f >= 0 && f <= 100) {
                        value = f;
                    } else {
                        value = NotePresets.Default.DefaultVibrato.VibratoLength;
                    }
                    UNote first = selectedNotes.First();
                    foreach (UNote note in selectedNotes) {
                        if (note != first && AutoVibratoToggle && note.duration < AutoVibratoNoteLength) {
                            if (note.vibrato.length != 0) {
                                DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(Part, note, 0));
                            }
                        } else {
                            DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(Part, note, value));
                        }
                    }
                    if (first.vibrato.length > 0) {
                        VibratoEnable = true;
                    } else {
                        VibratoEnable = false;
                    }
                } else if (tag == "VibratoPeriod") {
                    float value;
                    if (obj != null && (obj is float f || float.TryParse(obj.ToString(), out f)) && f >= 5 && f <= 500) {
                        value = f;
                    } else {
                        value = NotePresets.Default.DefaultVibrato.VibratoPeriod;
                    }
                    foreach (UNote note in selectedNotes) {
                        DocManager.Inst.ExecuteCmd(new VibratoPeriodCommand(Part, note, value));
                    }
                } else if (tag == "VibratoDepth") {
                    float value;
                    if (obj != null && (obj is float f || float.TryParse(obj.ToString(), out f)) && f >= 5 && f <= 200) {
                        value = f;
                    } else {
                        value = NotePresets.Default.DefaultVibrato.VibratoDepth;
                    }
                    foreach (UNote note in selectedNotes) {
                        DocManager.Inst.ExecuteCmd(new VibratoDepthCommand(Part, note, value));
                    }
                } else if (tag == "VibratoIn") {
                    float value;
                    if (obj != null && (obj is float f || float.TryParse(obj.ToString(), out f)) && f >= 0 && f <= 100) {
                        value = f;
                    } else {
                        value = NotePresets.Default.DefaultVibrato.VibratoIn;
                    }
                    foreach (UNote note in selectedNotes) {
                        DocManager.Inst.ExecuteCmd(new VibratoFadeInCommand(Part, note, value));
                    }
                } else if (tag == "VibratoOut") {
                    float value;
                    if (obj != null && (obj is float f || float.TryParse(obj.ToString(), out f)) && f >= 0 && f <= 100) {
                        value = f;
                    } else {
                        value = NotePresets.Default.DefaultVibrato.VibratoOut;
                    }
                    foreach (UNote note in selectedNotes) {
                        DocManager.Inst.ExecuteCmd(new VibratoFadeOutCommand(Part, note, value));
                    }
                } else if (tag == "VibratoShift") {
                    float value;
                    if (obj != null && (obj is float f || float.TryParse(obj.ToString(), out f)) && f >= 0 && f <= 100) {
                        value = f;
                    } else {
                        value = NotePresets.Default.DefaultVibrato.VibratoShift;
                    }
                    foreach (UNote note in selectedNotes) {
                        DocManager.Inst.ExecuteCmd(new VibratoShiftCommand(Part, note, value));
                    }
                } else if (tag == "VibratoDrift") {
                    float value;
                    if (obj != null && (obj is float f || float.TryParse(obj.ToString(), out f)) && f >= -100 && f <= 100) {
                        value = f;
                    } else {
                        value = NotePresets.Default.DefaultVibrato.VibratoDrift;
                    }
                    foreach (UNote note in selectedNotes) {
                        DocManager.Inst.ExecuteCmd(new VibratoDriftCommand(Part, note, value));
                    }
                } else if (tag == "VibratoVolLink") {
                    float value;
                    if (obj != null && (obj is float f || float.TryParse(obj.ToString(), out f)) && f >= -100 && f <= 100) {
                        value = f;
                    } else {
                        value = NotePresets.Default.DefaultVibrato.VibratoVolLink;
                    }
                    foreach (UNote note in selectedNotes) {
                        DocManager.Inst.ExecuteCmd(new VibratoVolumeLinkCommand(Part, note, value));
                    }
                }
            }
        }
        public void SetVibratoEnable() {
            if (Part != null && selectedNotes.Count > 0) {
                DocManager.Inst.StartUndoGroup("command.vibrato.edit");
                bool enable = VibratoEnable;
                UNote first = selectedNotes.First();

                foreach (UNote note in selectedNotes) {
                    if (enable) {
                        if (note.vibrato.length != 0) {
                            DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(Part, note, 0));
                        }
                    } else {
                        if (note != first && AutoVibratoToggle && note.duration < AutoVibratoNoteLength) {
                            if (note.vibrato.length != 0) {
                                DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(Part, note, 0));
                            }
                        } else {
                            if (note.vibrato.length != NotePresets.Default.DefaultVibrato.VibratoLength) {
                                DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(Part, note, NotePresets.Default.DefaultVibrato.VibratoLength));
                            }
                        }
                    }
                }
                DocManager.Inst.EndUndoGroup();
            }
        }
        public void SetNumericalExpressionsChanges(string abbr, float? value) {
            if (AllowNoteEdit && Part != null && selectedNotes.Count > 0) {
                var track = DocManager.Inst.Project.tracks[Part.trackNo];
                if (track.TryGetExpDescriptor(DocManager.Inst.Project, abbr, out UExpressionDescriptor descriptor) && descriptor.CustomDefaultValue == value) {
                    value = null;
                }
                DocManager.Inst.ExecuteCmd(new SetNotesSameExpressionCommand(DocManager.Inst.Project, track, Part, selectedNotes, abbr, value));
            }
        }
        public void SetOptionalExpressionsChanges(string abbr, int? value) {
            if (!NoteLoading && Part != null && selectedNotes.Count > 0) {
                var track = DocManager.Inst.Project.tracks[Part.trackNo];
                if (track.TryGetExpDescriptor(DocManager.Inst.Project, abbr, out UExpressionDescriptor descriptor) && descriptor.defaultValue == value) {
                    value = null;
                }
                DocManager.Inst.StartUndoGroup("command.exp.edit");
                DocManager.Inst.ExecuteCmd(new SetNotesSameExpressionCommand(DocManager.Inst.Project, track, Part, selectedNotes, abbr, value));
                DocManager.Inst.EndUndoGroup();
            }
        }

        // presets
        public void SavePortamentoPreset(string name) {
            if (string.IsNullOrEmpty(name)) {
                return;
            }
            NotePresets.Default.PortamentoPresets.Add(new NotePresets.PortamentoPreset(name, (int)PortamentoLength, (int)PortamentoStart));
            NotePresets.Save();
            PortamentoPresets = new ObservableCollection<NotePresets.PortamentoPreset>(NotePresets.Default.PortamentoPresets);
        }
        public void RemoveAppliedPortamentoPreset() {
            if (appliedPortamentoPreset == null) {
                return;
            }
            NotePresets.Default.PortamentoPresets.Remove(appliedPortamentoPreset);
            NotePresets.Save();
            PortamentoPresets = new ObservableCollection<NotePresets.PortamentoPreset>(NotePresets.Default.PortamentoPresets);
        }
        public void SaveVibratoPreset(string name) {
            if (string.IsNullOrEmpty(name)) {
                return;
            }
            NotePresets.Default.VibratoPresets.Add(new NotePresets.VibratoPreset(name, VibratoLength, VibratoPeriod, VibratoDepth, VibratoIn, VibratoOut, VibratoShift, VibratoDrift, VibratoVolLink));
            NotePresets.Save();
            VibratoPresets = new ObservableCollection<NotePresets.VibratoPreset>(NotePresets.Default.VibratoPresets);
        }
        public void RemoveAppliedVibratoPreset() {
            if (appliedVibratoPreset == null) {
                return;
            }
            NotePresets.Default.VibratoPresets.Remove(appliedVibratoPreset);
            NotePresets.Save();
            VibratoPresets = new ObservableCollection<NotePresets.VibratoPreset>(NotePresets.Default.VibratoPresets);
        }
    }

    public class NotePropertyExpViewModel : ViewModelBase {
        public string Name { get; set; }
        public bool IsNumerical { get; set; } = false;
        public bool IsOptions { get; set; } = false;
        public float Min { get; set; }
        public float Max { get; set; }
        public ObservableCollection<string> Options { get; set; } = new ObservableCollection<string>();
        public string abbr;
        public float defaultValue;

        [Reactive] public bool IsNoteSelected { get; set; } = false;
        [Reactive] public float Value { get; set; }
        [Reactive] public int SelectedOption { get; set; }
        [Reactive] public bool DropDownOpen { get; set; }
        [Reactive] public bool HasValue { get; set; } = false;
        [Reactive] public FontWeight NameFontWeight { get; set; }

        private NotePropertiesViewModel parentViewmodel;

        public NotePropertyExpViewModel(UExpressionDescriptor descriptor, NotePropertiesViewModel parent) {
            Name = descriptor.name;
            defaultValue = descriptor.CustomDefaultValue;
            abbr = descriptor.abbr;
            if (descriptor.type == UExpressionType.Numerical) {
                IsNumerical = true;
                Max = descriptor.max;
                Min = descriptor.min;
                Value = defaultValue;
            } else if (descriptor.type == UExpressionType.Options) {
                IsOptions = true;
                descriptor.options.ForEach(opt => Options.Add(opt));
                SelectedOption = (int)defaultValue;
            }

            parentViewmodel = parent;

            if (IsOptions) {
                this.WhenAnyValue(vm => vm.SelectedOption)
                    .Subscribe(value => {
                        if (value >= 0 && DropDownOpen) {
                            parentViewmodel.SetOptionalExpressionsChanges(abbr, value);
                        }
                    });
            }

            this.WhenAnyValue(vm => vm.HasValue)
                .Subscribe(value => {
                    if (value) {
                        NameFontWeight = FontWeight.Bold;
                    } else {
                        NameFontWeight = FontWeight.Normal;
                    }
                });
        }

        public void SetNumericalExpressions(object? obj) {
            float? value = null;
            if (obj != null && (obj is float f || float.TryParse(obj.ToString(), out f))) {
                if (f < Min && f > Max) {
                    return;
                }
                value = f;
            }
            parentViewmodel.SetNumericalExpressionsChanges(abbr, value);
            this.RaisePropertyChanged(nameof(Value));
        }

        public override string ToString() {
            return Name;
        }
    }
}
