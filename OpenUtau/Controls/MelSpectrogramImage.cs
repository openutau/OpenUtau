using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// Which wave part, if any, the piano roll draws as a mel spectrogram. Chosen from
    /// the part's context menu; not saved with the project.
    /// </summary>
    public sealed class MelOverlay {
        public static readonly MelOverlay Inst = new MelOverlay();

        public UWavePart? Part { get; private set; }

        /// <summary>Raised on the UI thread when the part or the display settings change.</summary>
        public event Action? Changed;

        public bool IsShown(UPart part) => ReferenceEquals(part, Part);

        public void Toggle(UWavePart part) {
            Part = IsShown(part) ? null : part;
            Changed?.Invoke();
        }

        public void NotifySettingsChanged() => Changed?.Invoke();

        /// <summary>Drops the part without raising Changed, once it left the project.</summary>
        public void Forget() => Part = null;
    }

    /// <summary>
    /// The mel spectrogram of the wave part chosen in <see cref="MelOverlay"/>, drawn
    /// behind the notes. Time follows the project's tempo map, and rows follow the
    /// piano roll's pitch axis, so a harmonic sits on the row of its pitch.
    /// </summary>
    class MelSpectrogramImage : Control {
        public static readonly StyledProperty<double> TickWidthProperty =
            AvaloniaProperty.Register<MelSpectrogramImage, double>(nameof(TickWidth));
        public static readonly StyledProperty<double> TickOffsetProperty =
            AvaloniaProperty.Register<MelSpectrogramImage, double>(nameof(TickOffset));
        public static readonly StyledProperty<double> TrackHeightProperty =
            AvaloniaProperty.Register<MelSpectrogramImage, double>(nameof(TrackHeight));
        public static readonly StyledProperty<double> TrackOffsetProperty =
            AvaloniaProperty.Register<MelSpectrogramImage, double>(nameof(TrackOffset));
        public static readonly StyledProperty<bool> ShowMelProperty =
            AvaloniaProperty.Register<MelSpectrogramImage, bool>(nameof(ShowMel));

        public double TickWidth {
            get => GetValue(TickWidthProperty);
            set => SetValue(TickWidthProperty, value);
        }
        public double TickOffset {
            get => GetValue(TickOffsetProperty);
            set => SetValue(TickOffsetProperty, value);
        }
        public double TrackHeight {
            get => GetValue(TrackHeightProperty);
            set => SetValue(TrackHeightProperty, value);
        }
        public double TrackOffset {
            get => GetValue(TrackOffsetProperty);
            set => SetValue(TrackOffsetProperty, value);
        }
        public bool ShowMel {
            get => GetValue(ShowMelProperty);
            set => SetValue(ShowMelProperty, value);
        }

        private const int MaxFramesPerColumn = 64;

        private MelSpectrogram? mel;
        private UWavePart? melPart;
        private float[]? melSamples;
        private (int fft, int hop, int bands) melSettings;
        private UWavePart? hookedPart;

        private WriteableBitmap? bitmap;
        private int[] pixels = Array.Empty<int>();
        private byte[] frames = Array.Empty<byte>();
        private byte[] columns = Array.Empty<byte>();

        private uint[] table = Array.Empty<uint>();
        private (ColormapKind map, double min, double max, double opacity) tableKey;

        static MelSpectrogramImage() {
            AffectsRender<MelSpectrogramImage>(TickWidthProperty, TickOffsetProperty,
                TrackHeightProperty, TrackOffsetProperty, ShowMelProperty);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            MelOverlay.Inst.Changed += OnOverlayChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            MelOverlay.Inst.Changed -= OnOverlayChanged;
            ReleaseMel();
            bitmap?.Dispose();
            bitmap = null;
        }

        private void OnOverlayChanged() => InvalidateVisual();

        private void ReleaseMel() {
            mel?.Dispose();
            mel = null;
            melPart = null;
            melSamples = null;
        }

        // The spectrogram computes in the background and calls back on its own thread.
        private void OnMelChanged() => Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);

        private bool EnsureMel(UWavePart part) {
            var samples = part.Samples;
            if (samples == null) {
                // Still loading: draw once it is.
                if (!ReferenceEquals(hookedPart, part) && part.Peaks != null) {
                    hookedPart = part;
                    part.Peaks.ContinueWith(_ => Dispatcher.UIThread.Post(InvalidateVisual));
                }
                return false;
            }
            var prefs = Preferences.Default;
            var settings = (prefs.MelFftSize, prefs.MelHop, prefs.MelBands);
            if (mel == null || !ReferenceEquals(melPart, part) || !ReferenceEquals(melSamples, samples) ||
                !settings.Equals(melSettings)) {
                ReleaseMel();
                mel = new MelSpectrogram(samples, part.channels, settings.MelFftSize, settings.MelHop, settings.MelBands);
                mel.Changed += OnMelChanged;
                melPart = part;
                melSamples = samples;
                melSettings = settings;
            }
            return true;
        }

        public override void Render(DrawingContext context) {
            base.Render(context);
            if (DataContext is not NotesViewModel vm || double.IsNaN(vm.TickOffset) || !ShowMel ||
                vm.TickWidth <= ViewConstants.PianoRollTickWidthShowDetails) {
                return;
            }
            var project = vm.Project;
            var part = MelOverlay.Inst.Part;
            if (project == null || part == null) {
                ReleaseMel();
                return;
            }
            if (!project.parts.Contains(part)) {
                // Deleted, or another project was opened: let go of its audio.
                MelOverlay.Inst.Forget();
                ReleaseMel();
                return;
            }
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int width = (int)Math.Ceiling(Bounds.Width * scale);
            int height = (int)Math.Ceiling(Bounds.Height * scale);
            if (width <= 0 || height <= 0 || !EnsureMel(part) || mel == null) {
                return;
            }

            int bands = mel.Bands;
            var timeAxis = project.timeAxis;
            double pxPerTick = vm.TickWidth * scale;
            double firstTick = vm.TickOrigin + vm.TickOffset;
            double fileStartMs = timeAxis.TickPosToMsPos(part.position - part.skip);

            // Fractional frame at each column edge.
            var edge = new double[width + 1];
            for (int x = 0; x <= width; ++x) {
                double ms = timeAxis.TickPosToMsPos(firstTick + x / pxPerTick);
                edge[x] = (ms - fileStartMs) / 1000 * mel.FrameRate;
            }
            int minFrame = (int)Math.Floor(edge[0]);
            int count = (int)Math.Ceiling(edge[width]) - minFrame + 1;
            if (frames.Length < count * bands) {
                frames = new byte[count * bands];
            }
            mel.Read(minFrame, count, frames);

            // Per column, the loudest value of each band over the frames it covers.
            if (columns.Length < width * bands) {
                columns = new byte[width * bands];
            }
            for (int x = 0; x < width; ++x) {
                int o = x * bands;
                // Only the part's own span sounds; the skipped head and trimmed tail stay empty.
                double tick = firstTick + (x + 0.5) / pxPerTick;
                if (tick < part.position || tick >= part.End) {
                    Array.Clear(columns, o, bands);
                    continue;
                }
                int a = (int)Math.Floor(edge[x]) - minFrame;
                int b = Math.Min(Math.Max(a, (int)Math.Ceiling(edge[x + 1]) - 1 - minFrame),
                    a + MaxFramesPerColumn - 1);
                Buffer.BlockCopy(frames, a * bands, columns, o, bands);
                for (int f = a + 1; f <= b; ++f) {
                    int src = f * bands;
                    for (int k = 0; k < bands; ++k) {
                        if (frames[src + k] > columns[o + k]) {
                            columns[o + k] = frames[src + k];
                        }
                    }
                }
            }

            // Per row, the band position of its pitch.
            var rowBand = new int[height];
            var rowFrac = new float[height];
            for (int y = 0; y < height; ++y) {
                double tone = ViewConstants.MaxTone - 1 - (y / scale / vm.TrackHeight + vm.TrackOffset) + 0.5;
                double hz = 440 * Math.Pow(2, (tone - 69) / 12);
                double pos = MelSpectrogram.HzToBand(hz, bands);
                if (pos > bands - 1) {
                    rowBand[y] = -1;
                } else {
                    rowBand[y] = Math.Min((int)pos, bands - 2);
                    rowFrac[y] = (float)(pos - rowBand[y]);
                }
            }

            var key = ((ColormapKind)Preferences.Default.MelColormap, Preferences.Default.MelMinDb,
                Preferences.Default.MelMaxDb, Preferences.Default.MelOpacity);
            if (!key.Equals(tableKey)) {
                table = Colormap.BuildTable(key.Item1, key.Item2, key.Item3, key.Item4);
                tableKey = key;
            }

            if (pixels.Length < width * height) {
                pixels = new int[width * height];
            }
            for (int y = 0; y < height; ++y) {
                int band = rowBand[y];
                int row = y * width;
                if (band < 0) {
                    Array.Clear(pixels, row, width);
                    continue;
                }
                float frac = rowFrac[y];
                for (int x = 0; x < width; ++x) {
                    int o = x * bands + band;
                    int v0 = columns[o];
                    int v1 = columns[o + 1];
                    int v = v0 == 0 ? v1 : v1 == 0 ? v0 : (int)(v0 + (v1 - v0) * frac + 0.5f);
                    pixels[row + x] = (int)table[v];
                }
            }

            if (bitmap == null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height) {
                bitmap?.Dispose();
                bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
            }
            using (var fb = bitmap.Lock()) {
                for (int y = 0; y < height; ++y) {
                    Marshal.Copy(pixels, y * width, fb.Address + y * fb.RowBytes, width);
                }
            }
            context.DrawImage(bitmap, new Rect(0, 0, width, height), new Rect(Bounds.Size));
        }
    }
}
