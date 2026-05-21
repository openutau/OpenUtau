using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Analysis;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public enum TranscribeAlgorithm {
        SOME,
        GAME,
    }

    public class TranscribeViewModel : ViewModelBase {
        // --- Availability ---
        public bool SomeAvailable { get; }
        public bool GameAvailable { get; }
        public bool RmvpeAvailable { get; }

        // --- Algorithm selection ---
        [Reactive] public TranscribeAlgorithm SelectedAlgorithm { get; set; }

        // Convenience bool bindings for RadioButtons
        public bool UseSome {
            get => SelectedAlgorithm == TranscribeAlgorithm.SOME;
            set { if (value) SelectedAlgorithm = TranscribeAlgorithm.SOME; }
        }
        public bool UseGame {
            get => SelectedAlgorithm == TranscribeAlgorithm.GAME;
            set { if (value) SelectedAlgorithm = TranscribeAlgorithm.GAME; }
        }

        // Tooltip messages — null when available so no tooltip pops up
        public string? SomeNotFoundTip => SomeAvailable ? null : ThemeManager.GetString("dialogs.transcribe.some.notfound");
        public string? GameNotFoundTip => GameAvailable ? null : ThemeManager.GetString("dialogs.transcribe.game.notfound");
        public string? RmvpeNotFoundTip => RmvpeAvailable ? null : ThemeManager.GetString("dialogs.transcribe.rmvpe.notfound");

        // True when neither algorithm is installed
        public bool NoneAvailable => !SomeAvailable && !GameAvailable;

        // Whether to show the GAME options box
        public bool GameOptionsVisible => SelectedAlgorithm == TranscribeAlgorithm.GAME && GameAvailable;

        // Whether the run button can be clicked
        public bool CanRun =>
            (SelectedAlgorithm == TranscribeAlgorithm.SOME && SomeAvailable) ||
            (SelectedAlgorithm == TranscribeAlgorithm.GAME && GameAvailable);

        [Reactive] public bool PredictPitd { get; set; } = false;

        // --- GAME options ---
        public List<int> SamplingStepsOptions { get; } = new List<int> { 1, 2, 4, 8, 16 };

        [Reactive] public int SamplingStepsIndex { get; set; } = 3;

        public int SamplingSteps => SamplingStepsIndex >= 0 && SamplingStepsIndex < SamplingStepsOptions.Count
            ? SamplingStepsOptions[SamplingStepsIndex]
            : 1;

        [Reactive] public float BoundaryThreshold { get; set; } = 0.2f;
        [Reactive] public int BoundaryRadius { get; set; } = 2;
        [Reactive] public float ScoreThreshold { get; set; } = 0.2f;

        // --- GAME batch inference ---
        /// <summary>Maximum number of audio chunks per batch. 1 = no batching.</summary>
        [Reactive] public int BatchSize { get; set; } = 1;

        /// <summary>Maximum total padded audio duration per batch in seconds (0 = unlimited).</summary>
        [Reactive] public float MaxBatchDuration { get; set; } = 60f;

        // Internal language code list (null = Auto); parallel to LanguageDisplayOptions
        private readonly List<string?> languageCodes;

        /// <summary>Display strings shown in the language ComboBox ("Auto", "en", "zh", …).</summary>
        public List<string> LanguageDisplayOptions { get; }

        public bool GameHasLanguages { get; }

        [Reactive] public int LanguageDisplayIndex { get; set; } = 0;

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

            // Default to GAME if available, otherwise fall back to SOME
            if (GameAvailable) {
                SelectedAlgorithm = TranscribeAlgorithm.GAME;
            } else if (SomeAvailable) {
                SelectedAlgorithm = TranscribeAlgorithm.SOME;
            }

            // Load GAME config (no model sessions) to populate options
            GameConfig? gameConfig = null;
            if (GameAvailable) {
                gameConfig = Game.LoadConfig();
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
                    this.RaisePropertyChanged(nameof(GameOptionsVisible));
                    this.RaisePropertyChanged(nameof(CanRun));
                });
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
