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
        private DateTime mixUnlockTime = DateTime.MinValue;
        private bool wasRendering = false;

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
                        
                        if (OpenUtau.Core.PlaybackManager.Inst.IsWaveformBlanked) {
                            // sampleData is already empty, so the screen draws a perfect flat line.
                        }
                        else if (OpenUtau.Core.PlaybackManager.Inst.StartingToPlay || part.Mix == null) {
                            foreach (var cacheItem in PlaybackManager.Inst.LiveWaveformCache.Values) {
                                if (cacheItem.trackNo != part.trackNo) continue;
                                
                                double phraseStartMs = cacheItem.posMs;
                                float[] phraseSamples = cacheItem.samples;
                                int phraseStartSampleIdx = (int)((phraseStartMs - leftMs) * 44100 / 1000);
                                
                                double ageMs = (DateTime.Now - cacheItem.renderTime).TotalMilliseconds;
                                double animProgress = Math.Clamp(ageMs / 300.0, 0.0, 1.0); 
                                
                                if (animProgress < 1.0) needsAnotherFrame = true; 
                                
                                float ease = 1.0f - (float)Math.Pow(1.0 - animProgress, 3);
                                float visualScale = 1.0f * ease; 
                                
                                int startJ = Math.Max(0, -phraseStartSampleIdx);
                                int endJ = Math.Min(phraseSamples.Length, (sampleCount / 2) - phraseStartSampleIdx);
                                
                                for (int j = startJ; j < endJ; j++) {
                                    int targetIdx = (phraseStartSampleIdx + j) * 2; 
                                    float scaledSample = phraseSamples[j] * visualScale;
                                    sampleData[targetIdx] += scaledSample;     
                                    sampleData[targetIdx + 1] += scaledSample; 
                                }
                            }
                        }
                        // THE FINAL MIX 
                        else {
                            part.Mix.Mix(samplePos, sampleData, 0, sampleCount);
                        }

                        bool isRendering = PlaybackManager.Inst.StartingToPlay;
                        if (wasRendering && !isRendering) {
                            mixUnlockTime = DateTime.Now;
                        }
                        wasRendering = isRendering;
                        
                        double snapAgeMs = (DateTime.Now - mixUnlockTime).TotalMilliseconds;
                        double snapProgress = Math.Clamp(snapAgeMs / 300.0, 0.0, 1.0);
                        float snapEase = 1.0f - (float)Math.Pow(1.0 - snapProgress, 3);

                        if (snapProgress < 1.0) needsAnotherFrame = true;

                        // Phrase audio ranges as [startMs, endMs] pairs, matching
                        // the WaveSource layout of the mix, so that time ranges
                        // without any phrase are left blank instead of drawing a
                        // zero-volume line. Silence inside a phrase still draws.
                        double[]? phraseRanges = null;
                        if (part.renderPhrases.Count > 0) {
                            phraseRanges = new double[part.renderPhrases.Count * 2];
                            for (int p = 0; p < part.renderPhrases.Count; ++p) {
                                (double rangeStartMs, double rangeEndMs) = part.renderPhrases[p].AudioRange;
                                phraseRanges[p * 2] = rangeStartMs;
                                phraseRanges[p * 2 + 1] = rangeEndMs;
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
                                rawMin *= snapEase;
                                rawMax *= snapEase;
                                float min = 0.5f + rawMin * 0.5f;
                                float max = 0.5f + rawMax * 0.5f;
                                float yMax = Math.Clamp(max * bitmap.PixelSize.Height, 0, bitmap.PixelSize.Height - 1);
                                float yMin = Math.Clamp(min * bitmap.PixelSize.Height, 0, bitmap.PixelSize.Height - 1);
                                DrawPeak(bitmapData, bitmap.PixelSize.Width, i, (int)Math.Round(yMin), (int)Math.Round(yMax));
                            }
                            startSample = endSample;
                            columnStartMs = endMs;
                        }

                        if (needsAnotherFrame) {
                            Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual, Avalonia.Threading.DispatcherPriority.Background);
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
