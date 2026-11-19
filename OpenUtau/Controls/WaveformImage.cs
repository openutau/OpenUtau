using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// The rendered audio of the open part, drawn behind the notes as a
    /// <see cref="WaveformEnvelope"/> on a grid from TickOrigin, rebuilt on newly
    /// rendered audio.
    /// </summary>
    class WaveformImage : Control {
        public static readonly DirectProperty<WaveformImage, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<WaveformImage, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<WaveformImage, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<WaveformImage, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<WaveformImage, bool> ShowWaveformProperty =
            AvaloniaProperty.RegisterDirect<WaveformImage, bool>(
                nameof(ShowWaveform),
                o => o.ShowWaveform,
                (o, v) => o.ShowWaveform = v);

        public double TickWidth {
            get => tickWidth;
            set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TickOffset {
            get { return tickOffset; }
            set { SetAndRaise(TickOffsetProperty, ref tickOffset, value); }
        }
        public bool ShowWaveform {
            get { return showWaveform; }
            set { SetAndRaise(ShowWaveformProperty, ref showWaveform, value); }
        }

        private const int SampleRate = 44100;
        private const int Channels = 2;
        private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x7F, 0x7F, 0x7F, 0x7F));

        private double tickWidth;
        private double tickOffset;
        private bool showWaveform;

        private readonly WaveformEnvelope envelope = new WaveformEnvelope();
        private float[] sampleData = new float[0];

        public WaveformImage() {
            // The projection payload is not read here; deliveries mean new audio.
            OpenUtau.Core.Render.RenderView.Inst.Observe(_ => {
                envelope.Invalidate();
                InvalidateVisual();
            });
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DataContextProperty ||
                change.Property == TickWidthProperty ||
                change.Property == TickOffsetProperty ||
                change.Property == ShowWaveformProperty ||
                change.Property == BoundsProperty) {
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context) {
            base.Render(context);
            if (DataContext is not NotesViewModel viewModel || double.IsNaN(viewModel.TickOffset) ||
                !ShowWaveform || viewModel.TickWidth <= ViewConstants.PianoRollTickWidthShowDetails) {
                return;
            }
            var project = viewModel.Project;
            var part = viewModel.Part;
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            // Everything below is in device pixels.
            int width = (int)Math.Ceiling(Bounds.Width * scale);
            double height = Math.Round(Bounds.Height * scale);
            if (project == null || part == null || width <= 0 || height <= 0) {
                return;
            }
            double offsetPx = viewModel.TickOffset * viewModel.TickWidth * scale;
            int firstColumn = (int)Math.Floor(offsetPx);
            envelope.Update(project.timeAxis, part, viewModel.TickOrigin, viewModel.TickWidth * scale,
                new[] { (0.0, height) }, firstColumn, firstColumn + width + 1, int.MinValue, int.MaxValue,
                (edges, min, max) => FillColumns(part, edges, min[0], max[0]));
            envelope.Draw(context, Bounds.Size, scale, Math.Round(-offsetPx), Fill);
        }

        // The mixed audio of both channels, as one lane.
        private bool FillColumns(UPart part, double[] edges, float[] columnMin, float[] columnMax) {
            int columns = edges.Length - 1;
            int firstSample = Math.Max(0, SampleIndex(edges[0]));
            int sampleCount = Math.Max(0, SampleIndex(edges[columns]) - firstSample);
            if (sampleCount == 0) {
                return false;
            }
            if (sampleData.Length < sampleCount) {
                sampleData = new float[sampleCount];
            }
            Array.Clear(sampleData, 0, sampleCount);

            var projection = OpenUtau.Core.Render.RenderView.Inst.Current(part);
            var phraseView = new (ulong hash, double startMs, double endMs)[projection.Phrases.Count];
            for (int p = 0; p < projection.Phrases.Count; ++p) {
                var view = projection.Phrases[p];
                phraseView[p] = (view.Hash, view.Layout.StartMs, view.Layout.EndMs);
            }
            // Only phrases whose pcm has rendered appear, so a part still
            // rendering draws only what has finished.
            var planner = OpenUtau.Core.PlaybackManager.Inst.MixPlanner;
            if (!MixPlanner.TryGetPartPlacements(planner, part, phraseView, out var pcmList)) {
                return false;
            }
            var slots = new OpenUtau.Core.SignalChain.SampleSlot[pcmList.Count];
            for (int i = 0; i < pcmList.Count; ++i) {
                var p = pcmList[i];
                slots[i] = new OpenUtau.Core.SignalChain.SampleSlot(
                    p.posMs, p.durMs, 0, p.channels, p.pcm,
                    OpenUtau.Core.SignalChain.SlotState.Ready);
            }
            var source = new OpenUtau.Core.SignalChain.SlotMixSource();
            source.SetSlots(slots);
            source.Mix(firstSample, sampleData, 0, sampleCount);

            // NaN where no phrase has audio. Silence inside a phrase still draws.
            float lastValue = 0;
            for (int i = 0; i < columns; ++i) {
                double fromMs = edges[i], toMs = edges[i + 1];
                bool covered = false;
                foreach (var phrase in phraseView) {
                    if (phrase.endMs > fromMs && phrase.startMs < toMs) {
                        covered = true;
                        break;
                    }
                }
                int s0 = Math.Clamp(SampleIndex(fromMs) - firstSample, 0, sampleCount);
                int s1 = Math.Clamp(SampleIndex(toMs) - firstSample, 0, sampleCount);
                if (!covered || fromMs < 0) {
                    columnMin[i] = columnMax[i] = float.NaN;
                    if (s1 > 0) {
                        lastValue = sampleData[s1 - 1];
                    }
                    continue;
                }
                float min, max;
                if (s1 > s0) {
                    min = float.MaxValue;
                    max = float.MinValue;
                    for (int s = s0; s < s1; ++s) {
                        float v = sampleData[s];
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                    lastValue = sampleData[s1 - 1];
                } else {
                    // Zoomed in past one sample per column: hold the last sample.
                    min = max = lastValue;
                }
                columnMin[i] = min;
                columnMax[i] = max;
            }
            return true;
        }

        // Index of the interleaved sample at a song time, as the mix lays them out.
        private static int SampleIndex(double ms) {
            return (int)(ms * SampleRate / 1000) * Channels;
        }
    }
}
