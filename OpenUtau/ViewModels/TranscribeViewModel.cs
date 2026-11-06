using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public enum TranscribeAlgorithm {
        SOME,
        GAME,
        /// <summary>Forced alignment of an existing part's phonemes against a
        /// recording (tifa.cpp). Unlike SOME/GAME it does not create notes; it
        /// rewrites the phoneme timing of the selected track.</summary>
        TIFA,
    }

    public partial class TranscribeViewModel : ViewModelBase {
        // --- Availability ---
        public bool SomeAvailable { get; }
        public bool GameAvailable { get; }
        public bool RmvpeAvailable { get; }
        public bool TifaAvailable { get; }

        // --- Algorithm selection ---
        [Reactive] public partial TranscribeAlgorithm SelectedAlgorithm { get; set; }

        // Convenience bool bindings for RadioButtons
        public bool UseSome {
            get => SelectedAlgorithm == TranscribeAlgorithm.SOME;
            set { if (value) SelectedAlgorithm = TranscribeAlgorithm.SOME; }
        }
        public bool UseGame {
            get => SelectedAlgorithm == TranscribeAlgorithm.GAME;
            set { if (value) SelectedAlgorithm = TranscribeAlgorithm.GAME; }
        }
        public bool UseTifa {
            get => SelectedAlgorithm == TranscribeAlgorithm.TIFA;
            set { if (value) SelectedAlgorithm = TranscribeAlgorithm.TIFA; }
        }

        // Tooltip messages — null when available so no tooltip pops up
        public string? SomeNotFoundTip => SomeAvailable ? null : ThemeManager.GetString("dialogs.transcribe.some.notfound");
        public string? GameNotFoundTip => GameAvailable ? null : ThemeManager.GetString("dialogs.transcribe.game.notfound");
        public string? RmvpeNotFoundTip => RmvpeAvailable ? null : ThemeManager.GetString("dialogs.transcribe.rmvpe.notfound");
        public string? TifaNotFoundTip => TifaAvailable ? null : ThemeManager.GetString("dialogs.transcribe.tifa.notfound");

        // True when no algorithm is installed
        public bool NoneAvailable => !SomeAvailable && !GameAvailable && !TifaAvailable;

        // Whether to show the GAME options box
        public bool GameOptionsVisible => SelectedAlgorithm == TranscribeAlgorithm.GAME && GameAvailable;

        // Whether to show the TIFA options box
        public bool TifaOptionsVisible => SelectedAlgorithm == TranscribeAlgorithm.TIFA && TifaAvailable;

        // Whether the run button can be clicked
        public bool CanRun =>
            (SelectedAlgorithm == TranscribeAlgorithm.SOME && SomeAvailable) ||
            (SelectedAlgorithm == TranscribeAlgorithm.GAME && GameAvailable) ||
            (SelectedAlgorithm == TranscribeAlgorithm.TIFA && TifaAvailable && TifaTargetTrackIndex >= 0);

        // Whether RMVPE pitch extraction is offered. It only applies when a new
        // note part is created from the audio.
        public bool RmvpeVisible => SelectedAlgorithm != TranscribeAlgorithm.TIFA;

        [Reactive] public partial bool PredictPitd { get; set; } = false;

        // --- GAME options ---
        public List<int> SamplingStepsOptions { get; } = new List<int> { 1, 2, 4, 8, 16 };

        [Reactive] public partial int SamplingStepsIndex { get; set; } = 3;

        public int SamplingSteps => SamplingStepsIndex >= 0 && SamplingStepsIndex < SamplingStepsOptions.Count
            ? SamplingStepsOptions[SamplingStepsIndex]
            : 1;

        [Reactive] public partial float BoundaryThreshold { get; set; } = 0.2f;
        [Reactive] public partial int BoundaryRadius { get; set; } = 2;
        [Reactive] public partial float ScoreThreshold { get; set; } = 0.2f;

        // --- GAME batch inference ---
        /// <summary>Maximum number of audio chunks per batch. 1 = no batching.</summary>
        [Reactive] public partial int BatchSize { get; set; } = 1;

        /// <summary>Maximum total padded audio duration per batch in seconds (0 = unlimited).</summary>
        [Reactive] public partial float MaxBatchDuration { get; set; } = 60f;

        // --- TIFA options: align the phoneme timing of an existing part ---
        readonly List<UTrack> tifaTracks = new List<UTrack>();

        /// <summary>Tracks that already have notes, as "n. name".</summary>
        public List<string> TifaTrackOptions { get; } = new List<string>();

        /// <summary>Index 0 = auto (from the track's phonemizer language),
        /// then the aligner's four languages.</summary>
        public List<string> TifaLanguageOptions { get; } = new List<string>();

        [Reactive] public partial int TifaTargetTrackIndex { get; set; } = -1;
        [Reactive] public partial int TifaLanguageIndex { get; set; } = 0;

        public UTrack? TifaTargetTrack =>
            TifaTargetTrackIndex >= 0 && TifaTargetTrackIndex < tifaTracks.Count
                ? tifaTracks[TifaTargetTrackIndex]
                : null;

        public TifaLanguage TifaResolvedLanguage =>
            TifaLanguageIndex >= 1 && TifaLanguageIndex <= Tifa.SupportedLanguages.Length
                ? Tifa.SupportedLanguages[TifaLanguageIndex - 1]
                : DetectLanguage(TifaTargetTrack) ?? TifaLanguage.Zh;

        /// <summary>Language the aligner should expect, taken from the track's
        /// phonemizer when it is one of the four supported ones.</summary>
        public static TifaLanguage? DetectLanguage(UTrack? track) {
            if (track?.Phonemizer == null) {
                return null;
            }
            var factory = PhonemizerFactory.GetAll()
                .FirstOrDefault(f => f?.name == track.Phonemizer.Name);
            return factory != null && TifaPhonemeData.TryParseLanguage(factory.language, out var language)
                ? language
                : null;
        }

        void BuildTifaTracks() {
            var project = DocManager.Inst.Project;
            foreach (var track in project.tracks) {
                bool hasNotes = project.parts
                    .OfType<UVoicePart>()
                    .Any(p => p.trackNo == track.TrackNo && p.notes.Count > 0);
                if (hasNotes) {
                    tifaTracks.Add(track);
                    TifaTrackOptions.Add($"{track.TrackNo + 1}. {track.TrackName}");
                }
            }
            TifaTargetTrackIndex = tifaTracks.Count > 0 ? 0 : -1;
        }

        // Internal language code list (null = Auto); parallel to LanguageDisplayOptions
        private readonly List<string?> languageCodes;

        /// <summary>Display strings shown in the language ComboBox ("Auto", "en", "zh", …).</summary>
        public List<string> LanguageDisplayOptions { get; }

        public bool GameHasLanguages { get; }

        [Reactive] public partial int LanguageDisplayIndex { get; set; } = 0;

        /// <summary>The selected language code (null = Auto/universal).</summary>
        public string? LanguageCode => LanguageDisplayIndex >= 1 && LanguageDisplayIndex < languageCodes.Count
            ? languageCodes[LanguageDisplayIndex]
            : null;

        public TranscribeViewModel() {
            // Check SOME availability
            SomeAvailable = Some.IsInstalled();

            // Check GAME availability
            GameAvailable = Game.IsInstalled();

            // Check RMVPE availability
            RmvpeAvailable = RmvpeTranscriber.IsInstalled();

            // Check TIFA availability
            TifaAvailable = Tifa.IsInstalled();
            TifaLanguageOptions.Add(ThemeManager.GetString("dialogs.transcribe.tifa.language.auto"));
            foreach (var language in Tifa.SupportedLanguages) {
                TifaLanguageOptions.Add(TifaPhonemeData.Code(language));
            }
            BuildTifaTracks();

            // Default to GAME if available, otherwise fall back to SOME, then
            // to phoneme alignment.
            if (GameAvailable) {
                SelectedAlgorithm = TranscribeAlgorithm.GAME;
            } else if (SomeAvailable) {
                SelectedAlgorithm = TranscribeAlgorithm.SOME;
            } else if (TifaAvailable) {
                SelectedAlgorithm = TranscribeAlgorithm.TIFA;
            }

            // Load config from the backend that will actually run (no model sessions).
            // In particular, GGML-only installs must not probe the ONNX package.
            GameConfig? gameConfig = null;
            if (GameAvailable) {
                try {
                    gameConfig = GameBackendFactory.LoadResolvedConfig();
                } catch {
                    GameAvailable = false;
                }
            }

            GameHasLanguages = (gameConfig?.Languages?.Count ?? 0) > 0;

            // Build parallel language code + display lists
            languageCodes = new List<string?> { null }; // index 0 = Auto
            LanguageDisplayOptions = new List<string> {
                ThemeManager.GetString("dialogs.transcribe.game.language.universal")
            };
            if (gameConfig?.Languages != null) {
                foreach (var key in gameConfig.Languages.Keys.OrderBy(k => k)) {
                    languageCodes.Add(key);
                    LanguageDisplayOptions.Add(key);
                }
            }

            // Propagate SelectedAlgorithm changes to derived properties
            this.WhenAnyValue(vm => vm.SelectedAlgorithm)
                .Subscribe(_ => {
                    this.RaisePropertyChanged(nameof(UseSome));
                    this.RaisePropertyChanged(nameof(UseGame));
                    this.RaisePropertyChanged(nameof(UseTifa));
                    this.RaisePropertyChanged(nameof(GameOptionsVisible));
                    this.RaisePropertyChanged(nameof(TifaOptionsVisible));
                    this.RaisePropertyChanged(nameof(RmvpeVisible));
                    this.RaisePropertyChanged(nameof(CanRun));
                });
            this.WhenAnyValue(vm => vm.TifaTargetTrackIndex)
                .Subscribe(_ => {
                    this.RaisePropertyChanged(nameof(TifaTargetTrack));
                    this.RaisePropertyChanged(nameof(TifaResolvedLanguage));
                    this.RaisePropertyChanged(nameof(CanRun));
                });
            this.WhenAnyValue(vm => vm.TifaLanguageIndex)
                .Subscribe(_ => this.RaisePropertyChanged(nameof(TifaResolvedLanguage)));
        }

        /// <summary>Build a GameOptions instance from the current ViewModel state.</summary>
        public GameOptions BuildGameOptions() {
            return new GameOptions {
                SamplingSteps = SamplingSteps,
                BoundaryThreshold = BoundaryThreshold,
                BoundaryRadius = BoundaryRadius,
                ScoreThreshold = ScoreThreshold,
                LanguageCode = LanguageCode,
            };
        }

        /// <summary>
        /// Returns a batching strategy based on the current BatchSize and MaxBatchDuration.
        /// BatchSize=1 effectively disables batching.
        /// </summary>
        public MidiExtractor<GameOptions>.BatchingStrategy? BuildBatchingStrategy() {
            return new MidiExtractor<GameOptions>.BatchingStrategy {
                max_batch_size = BatchSize,
                max_batch_duration = MaxBatchDuration,
            };
        }
    }
}
