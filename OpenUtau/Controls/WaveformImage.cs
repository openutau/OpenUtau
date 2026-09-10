using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using ReactiveUI;
using ReactiveUI.Primitives;
using Serilog;

namespace OpenUtau.App.Controls {
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

        private double tickWidth;
        private double tickOffset;
        private bool showWaveform;

        private WriteableBitmap? bitmap;
        private float[] sampleData = new float[0];
        private int sampleCount;
        private int[] bitmapData = new int[0];

        public WaveformImage() {
            // The projection payload is not read here; deliveries are repaint signals.
            OpenUtau.Core.Render.RenderView.Inst.Observe(_ => InvalidateVisual());
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DataContextProperty ||
                change.Property == TickWidthProperty ||
                change.Property == TickOffsetProperty ||
                change.Property == ShowWaveformProperty) {
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context) {
            if (DataContext == null || double.IsNaN(((NotesViewModel)DataContext).TickOffset)) {
                return;
            }
            var bitmap = GetBitmap();
            if (bitmap != null) {
                Array.Clear(bitmapData, 0, bitmapData.Length);
                var viewModel = (NotesViewModel?)DataContext;
                if (viewModel != null && ShowWaveform &&
                    viewModel.TickWidth > ViewConstants.PianoRollTickWidthShowDetails) {
                    var project = viewModel.Project;
                    var part = viewModel.Part;
                    if (project != null && part != null) {
                        double leftMs = project.timeAxis.TickPosToMsPos(viewModel.TickOrigin + viewModel.TickOffset);
                        double rightMs = project.timeAxis.TickPosToMsPos(viewModel.TickOrigin + viewModel.TickOffset + viewModel.ViewportTicks);
                        int samplePos = (int)(leftMs * 44100 / 1000) * 2;
                        sampleCount = (int)((rightMs - leftMs) * 44100 / 1000) * 2;
                        
                        if (sampleData.Length < sampleCount) {
                            Array.Resize(ref sampleData, sampleCount);
                        }
                        
                        Array.Clear(sampleData, 0, sampleData.Length);

                        // The part's current projection: phrase hashes and
                        // precomputed layouts, shared by the placement list below
                        // and the draw coverage further down.
                        var projection = OpenUtau.Core.Render.RenderView.Inst.Current(part);
                        var phraseView = new (ulong hash, double startMs, double endMs)[projection.Phrases.Count];
                        for (int p = 0; p < projection.Phrases.Count; ++p) {
                            var view = projection.Phrases[p];
                            phraseView[p] = (view.Hash, view.Layout.StartMs, view.Layout.EndMs);
                        }

                        // Only phrases whose pcm has rendered appear, so a part still
                        // rendering draws only what has finished.
                        var planner = OpenUtau.Core.PlaybackManager.Inst.MixPlanner;
                        if (MixPlanner.TryGetPartPlacements(planner, part, phraseView, out var pcmList)) {
                            var slots = new OpenUtau.Core.SignalChain.SampleSlot[pcmList.Count];
                            for (int i = 0; i < pcmList.Count; ++i) {
                                var p = pcmList[i];
                                slots[i] = new OpenUtau.Core.SignalChain.SampleSlot(
                                    p.posMs, p.durMs, 0, p.channels, p.pcm,
                                    OpenUtau.Core.SignalChain.SlotState.Ready);
                            }
                            var source = new OpenUtau.Core.SignalChain.SlotMixSource();
                            source.SetSlots(slots);
                            source.Mix(samplePos, sampleData, 0, sampleCount);
                        }

                        // Phrase audio ranges as [startMs, endMs] pairs, matching
                        // the slot layout of the mix, so that time ranges
                        // without any phrase are left blank instead of drawing a
                        // zero-volume line. Silence inside a phrase still draws.
                        double[]? phraseRanges = null;
                        if (phraseView.Length > 0) {
                            phraseRanges = new double[phraseView.Length * 2];
                            for (int p = 0; p < phraseView.Length; ++p) {
                                phraseRanges[p * 2] = phraseView[p].startMs;
                                phraseRanges[p * 2 + 1] = phraseView[p].endMs;
                            }
                        }

                        int startSample = 0;
                        double columnStartMs = leftMs;
                        for (int i = 0; i < bitmap.PixelSize.Width; ++i) {
                            double endTick = viewModel.TickOrigin + viewModel.TickOffset + (i + 1.0) / viewModel.TickWidth;
                            double endMs = project.timeAxis.TickPosToMsPos(endTick);
                            int endSample = Math.Clamp((int)((endMs - leftMs) * 44100 / 1000) * 2, 0, sampleCount);

                            // Skip drawing where no phrase has audio.
                            bool covered = false;
                            if (phraseRanges != null) {
                                for (int p = 0; p < phraseRanges.Length; p += 2) {
                                    if (phraseRanges[p + 1] > columnStartMs && phraseRanges[p] < endMs) {
                                        covered = true;
                                        break;
                                    }
                                }
                            }
                            if (!covered) {
                                startSample = endSample;
                                columnStartMs = endMs;
                                continue;
                            }

                            if (endSample > startSample) {
                                float rawMin = float.MaxValue;
                                float rawMax = float.MinValue;
                                for (int s = startSample; s < endSample; s++) {
                                    float val = sampleData[s];
                                    if (val < rawMin) rawMin = val;
                                    if (val > rawMax) rawMax = val;
                                }
                                if (rawMin == float.MaxValue) rawMin = 0;
                                if (rawMax == float.MinValue) rawMax = 0;
                                float min = 0.5f + rawMin * 0.5f;
                                float max = 0.5f + rawMax * 0.5f;
                                float yMax = Math.Clamp(max * bitmap.PixelSize.Height, 0, bitmap.PixelSize.Height - 1);
                                float yMin = Math.Clamp(min * bitmap.PixelSize.Height, 0, bitmap.PixelSize.Height - 1);
                                DrawPeak(bitmapData, bitmap.PixelSize.Width, i, (int)Math.Round(yMin), (int)Math.Round(yMax));
                            }
                            startSample = endSample;
                            columnStartMs = endMs;
                        }
                    }
                }
                using (var frameBuffer = bitmap.Lock()) {
                    Marshal.Copy(bitmapData, 0, frameBuffer.Address, bitmapData.Length);
                }
            }
            base.Render(context);
            if (bitmap != null) {
                var rect = Bounds.WithX(0).WithY(0);
                context.DrawImage(bitmap, rect, rect);
            }
        }

        private WriteableBitmap? GetBitmap() {
            int desiredWidth = (int)Bounds.Width;
            int desiredHeight = (int)Bounds.Height;
            if (desiredWidth == 0 || desiredHeight == 0) {
                return null;
            }
            if (bitmap == null || bitmap.Size.Width < desiredWidth) {
                bitmap?.Dispose();
                var size = new PixelSize(desiredWidth, desiredHeight);
                bitmap = new WriteableBitmap(
                    size, new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Rgba8888,
                    Avalonia.Platform.AlphaFormat.Unpremul);
                Log.Information($"Created bitmap {size}");
                bitmapData = new int[size.Width * size.Height];
            }
            return bitmap;
        }

        private void DrawPeak(int[] data, int width, int x, int y1, int y2) {
            const int color = 0x7F7F7F7F;
            if (y1 > y2) {
                int temp = y2;
                y2 = y1;
                y1 = temp;
            }
            for (var y = y1; y <= y2; ++y) {
                data[x + width * y] = color;
            }
        }
    }
}
