using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.App.Controls {
    public class SingerPortrait : Control {
        public static readonly StyledProperty<PortraitSelection?> SelectionProperty =
            AvaloniaProperty.Register<SingerPortrait, PortraitSelection?>(nameof(Selection));

        public PortraitSelection? Selection {
            get => GetValue(SelectionProperty);
            set => SetValue(SelectionProperty, value);
        }

        private sealed class Layer {
            public Bitmap? Bitmap;
            public bool Ready;
            public double StartOpacity;
        }

        private readonly Dictionary<string, Layer> layers = new();
        private PortraitSelection? current;
        private Layer? target;
        private long fadeStart;
        private bool frameRequested;
        private bool attached;
        private TimeSpan fadeDuration;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == SelectionProperty && attached) {
                SelectPortrait();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            attached = true;
            SelectPortrait();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            attached = false;
            ClearPortraits();
            base.OnDetachedFromVisualTree(e);
        }

        private void ClearPortraits() {
            foreach (var layer in layers.Values) {
                layer.Bitmap?.Dispose();
            }
            layers.Clear();
            target = null;
            current = null;
            fadeStart = 0;
            frameRequested = false;
            InvalidateVisual();
        }

        private void SelectPortrait() {
            var selection = Selection;
            if (selection == null || selection.Generation != current?.Generation ||
                !ReferenceEquals(selection.Singer, current?.Singer)) {
                ClearPortraits();
            }
            var previous = current;
            current = selection;
            if (selection == null) {
                return;
            }
            Opacity = Math.Clamp(selection.Singer.PortraitOpacity, 0, 1) * selection.OpacityFactor;
            if (selection.FadeMs == 0 && fadeStart != 0) {
                fadeStart = 0;
                InvalidateVisual();
            }
            if (previous != null && selection.ColorPath == previous.ColorPath) {
                return;
            }
            if (!string.IsNullOrEmpty(selection.Singer.Portrait)) {
                EnsureLoaded(selection.Singer, string.Empty);
            }
            if (selection.ColorPath != null) {
                EnsureLoaded(selection.Singer, selection.ColorPath);
            }
            ShowRequestedLayer();
        }

        private async void EnsureLoaded(USinger singer, string key) {
            if (layers.ContainsKey(key)) {
                return;
            }
            var layer = new Layer();
            layers.Add(key, layer);
            Bitmap? image = null;
            try {
                image = await Task.Run(() => LoadBitmap(singer, key));
            } catch (Exception e) {
                Log.Warning(e, "Failed to load portrait {Portrait} for {Singer}", key.Length == 0 ? singer.Portrait : key, singer.Id);
            }
            // A singer reload or closed window can invalidate an in-flight request.
            if (!layers.TryGetValue(key, out var cached) || cached != layer) {
                image?.Dispose();
                return;
            }
            layer.Bitmap = image;
            layer.Ready = true;
            ShowRequestedLayer();
        }

        private static Bitmap? LoadBitmap(USinger singer, string key) {
            byte[]? data;
            if (key.Length == 0) {
                data = singer.LoadPortrait();
            } else {
                if (Path.IsPathRooted(key)) {
                    throw new InvalidDataException("A color portrait path must be relative to the voicebank.");
                }
                var path = Path.GetFullPath(key, Path.GetFullPath(singer.Location));
                var relative = Path.GetRelativePath(singer.Location, path);
                if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) {
                    throw new InvalidDataException("The color portrait is outside the voicebank folder.");
                }
                data = File.ReadAllBytes(path);
            }
            if (data == null) {
                return null;
            }
            using var stream = new MemoryStream(data);
            var bitmap = new Bitmap(stream);
            var height = singer.PortraitHeight;
            if (height <= 0 && bitmap.Size.Height <= 800) {
                return bitmap;
            }
            using (bitmap) {
                height = height > 0 ? height : 800;
                int width = Math.Max(1, checked((int)Math.Round(height * bitmap.Size.Width / bitmap.Size.Height)));
                return bitmap.CreateScaledBitmap(new PixelSize(width, height));
            }
        }

        private void ShowRequestedLayer() {
            var requested = layers.GetValueOrDefault(current?.ColorPath ?? string.Empty);
            if (requested?.Ready == false && target != null) {
                return;
            }
            if (requested?.Bitmap == null) {
                requested = layers.GetValueOrDefault(string.Empty);
                if (requested?.Ready == false) {
                    return;
                }
                if (requested?.Bitmap == null) {
                    requested = null;
                }
            }
            if (requested == target) {
                return;
            }
            double progress = Progress();
            foreach (var layer in layers.Values) {
                layer.StartOpacity = Weight(layer, progress);
            }
            bool firstImage = target == null && progress >= 1;
            target = requested;
            fadeDuration = TimeSpan.FromMilliseconds(current?.FadeMs ?? 0);
            fadeStart = firstImage || fadeDuration == TimeSpan.Zero ? 0 : Stopwatch.GetTimestamp();
            InvalidateVisual();
        }

        private double Progress() => fadeStart == 0 ? 1 :
            Math.Min(1, Stopwatch.GetElapsedTime(fadeStart) / fadeDuration);

        private double Weight(Layer layer, double progress) =>
            layer.StartOpacity * (1 - progress) + (layer == target ? progress : 0);

        private Rect ImageBounds(Bitmap bitmap) =>
            new Rect(new Point(Bounds.Width - bitmap.Size.Width - 100, 0), bitmap.Size);

        public override void Render(DrawingContext context) {
            base.Render(context);
            double progress = Progress();
            if (progress >= 1) {
                if (target?.Bitmap is { } image) {
                    context.DrawImage(image, ImageBounds(image));
                }
                return;
            }
            var area = default(Rect);
            foreach (var layer in layers.Values) {
                if (layer.Bitmap is { } bitmap && (layer == target || layer.StartOpacity > 0)) {
                    area = area.Union(ImageBounds(bitmap));
                }
            }
            area = area.Intersect(new Rect(Bounds.Size));
            if (area.Width > 0 && area.Height > 0) {
                // Mix premultiplied colors and alpha on transparent pixels. Only the
                // finished frame receives the singer's opacity and the piano roll background.
                double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
                var size = PixelSize.FromSize(area.Size, scale);
                using var frame = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
                using (var mix = frame.CreateDrawingContext())
                using (mix.PushTransform(Matrix.CreateTranslation(-area.X, -area.Y)))
                using (mix.PushRenderOptions(new RenderOptions { BitmapBlendingMode = BitmapBlendingMode.Plus })) {
                    foreach (var layer in layers.Values) {
                        double weight = Weight(layer, progress);
                        if (layer.Bitmap is { } bitmap && weight > 0) {
                            using (mix.PushOpacity(weight)) {
                                mix.DrawImage(bitmap, ImageBounds(bitmap));
                            }
                        }
                    }
                }
                context.DrawImage(frame, new Rect(area.Size), area);
            }
            if (!frameRequested && TopLevel.GetTopLevel(this) is { } topLevel) {
                frameRequested = true;
                topLevel.RequestAnimationFrame(_ => {
                    frameRequested = false;
                    InvalidateVisual();
                });
            }
        }
    }
}
