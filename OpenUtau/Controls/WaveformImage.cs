using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Util;
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
            get => tickOffset;
            set => SetAndRaise(TickOffsetProperty, ref tickOffset, value);
        }
        public bool ShowWaveform {
            get => showWaveform;
            set => SetAndRaise(ShowWaveformProperty, ref showWaveform, value);
        }

        private double tickWidth;
        private double tickOffset;
        private bool showWaveform;

        private WriteableBitmap? bitmap;
        private float[] sampleData = new float[0];
        private int sampleCount;
        private int[] bitmapData = new int[0];
        private const double AnimDurationMs = 280.0;
        private bool isFrameRequested = false;

        public WaveformImage() {
            MessageBus.Current.Listen<WaveformRefreshEvent>()
                .Subscribe(e => {
                    InvalidateVisual();
                });
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
            isFrameRequested = false;

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

                        bool needsAnotherFrame = false;
                        Array.Clear(sampleData, 0, sampleData.Length);

                        bool animate = Preferences.Default.AnimateWaveform;

                        if (OpenUtau.Core.PlaybackManager.Inst.IsWaveformBlanked) {
                            // Leave clean
                        }
                        else {
                            if (part.Mix != null) {
                                part.Mix.Mix(samplePos, sampleData, 0, sampleCount);
                            }

                            var now = DateTime.Now;
                            var liveItems = PlaybackManager.Inst.LiveWaveformCache.Values
                                .Where(c => c.trackNo == part.trackNo);

                            foreach (var cacheItem in liveItems) {
                                double ageMs = (now - cacheItem.renderTime).TotalMilliseconds;

                                double phraseStartMs = cacheItem.posMs;
                                float[] phraseSamples = cacheItem.samples;
                                int phraseStartSampleIdx = (int)((phraseStartMs - leftMs) * 44100 / 1000);

                                float visualScale = 1.0f;
                                if (animate && ageMs < AnimDurationMs) {
                                    needsAnotherFrame = true;
                                    double progress = Math.Clamp(ageMs / AnimDurationMs, 0.0, 1.0);
                                    // Smooth exponential ease-out
                                    visualScale = (float)(1.0 - Math.Pow(1.0 - progress, 3));
                                }

                                int startJ = Math.Max(0, -phraseStartSampleIdx);
                                int endJ = Math.Min(phraseSamples.Length, (sampleCount / 2) - phraseStartSampleIdx);

                                for (int j = startJ; j < endJ; j++) {
                                    int targetIdx = (phraseStartSampleIdx + j) * 2;
                                    float val = phraseSamples[j] * visualScale;

                                    // If part.Mix already rendered this audio and the phrase is finished animating,
                                    // we do not need to rewrite it.
                                    if (part.Mix != null && ageMs >= AnimDurationMs) {
                                        continue;
                                    }

                                    sampleData[targetIdx] = val;
                                    sampleData[targetIdx + 1] = val;
                                }
                            }
                        }

                        int startSample = 0;
                        int pixelWidth = bitmap.PixelSize.Width;
                        int pixelHeight = bitmap.PixelSize.Height;

                        for (int i = 0; i < pixelWidth; ++i) {
                            double endTick = viewModel.TickOrigin + viewModel.TickOffset + (i + 1.0) / viewModel.TickWidth;
                            double endMs = project.timeAxis.TickPosToMsPos(endTick);
                            int endSample = Math.Clamp((int)((endMs - leftMs) * 44100 / 1000) * 2, 0, sampleCount);

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
                                float yMax = Math.Clamp(max * pixelHeight, 0, pixelHeight - 1);
                                float yMin = Math.Clamp(min * pixelHeight, 0, pixelHeight - 1);
                                DrawPeak(bitmapData, pixelWidth, i, (int)Math.Round(yMin), (int)Math.Round(yMax));
                            }
                            startSample = endSample;
                        }

                        if (needsAnotherFrame && !isFrameRequested) {
                            isFrameRequested = true;
                            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
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
