using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.App.ViewModels {
    /// <summary>
    /// Session-wide cache of decoded singer avatars, shared by the track header and the singer flyout.
    /// Avatars are read and decoded on a single background worker, never on the UI thread.
    /// Singers are not loaded for this: the image comes from AvatarData when the singer is already
    /// loaded, otherwise straight from the avatar file.
    /// </summary>
    public static class SingerAvatarCache {
        const int Size = 100;

        class Entry {
            // The AvatarData array or avatar file path the bitmap was decoded from.
            public object Source = null!;
            public Bitmap? Bitmap;
            public bool Pending;
            public List<Action<Bitmap?>>? Callbacks;
        }

        // Keyed by instance: a singer rescan creates new USinger objects and the old entries are collected with them.
        static readonly ConditionalWeakTable<USinger, Entry> entries = new ConditionalWeakTable<USinger, Entry>();
        static readonly ConcurrentQueue<(Entry entry, object source, string name)> queue =
            new ConcurrentQueue<(Entry, object, string)>();
        static int draining;

        /// <summary>
        /// Returns the cached avatar of the singer. If it is not decoded yet, returns null, queues the
        /// decoding and later calls onLoaded on the UI thread (with null if there is no usable image).
        /// Must be called on the UI thread. Returned bitmaps are shared and must not be disposed.
        /// </summary>
        public static Bitmap? Get(USinger singer, Action<Bitmap?>? onLoaded = null) {
            Dispatcher.UIThread.VerifyAccess();
            if (!singer.Found) {
                return null;
            }
            object? source = singer.AvatarData;
            source ??= singer.Avatar;
            if (source == null) {
                return null;
            }
            if (entries.TryGetValue(singer, out var entry)) {
                if (!Equals(entry.Source, source) && !entry.Pending &&
                    entry.Source is string path && source is byte[] && path == singer.Avatar) {
                    // The singer got loaded since its avatar was read from file. Same image, keep it.
                    entry.Source = source;
                }
                if (Equals(entry.Source, source)) {
                    if (!entry.Pending) {
                        return entry.Bitmap;
                    }
                    if (onLoaded != null) {
                        entry.Callbacks!.Add(onLoaded);
                    }
                    return null;
                }
            }
            entry = new Entry {
                Source = source,
                Pending = true,
                Callbacks = new List<Action<Bitmap?>>(),
            };
            if (onLoaded != null) {
                entry.Callbacks.Add(onLoaded);
            }
            entries.AddOrUpdate(singer, entry);
            queue.Enqueue((entry, source, singer.Name));
            if (Interlocked.CompareExchange(ref draining, 1, 0) == 0) {
                Task.Run(Drain);
            }
            return null;
        }

        static void Drain() {
            while (true) {
                while (queue.TryDequeue(out var item)) {
                    var bitmap = Decode(item.source, item.name);
                    var entry = item.entry;
                    Dispatcher.UIThread.Post(() => Complete(entry, bitmap));
                }
                Interlocked.Exchange(ref draining, 0);
                // Something may have been queued after the last dequeue but before draining was reset.
                if (queue.IsEmpty || Interlocked.CompareExchange(ref draining, 1, 0) != 0) {
                    return;
                }
            }
        }

        static void Complete(Entry entry, Bitmap? bitmap) {
            entry.Bitmap = bitmap;
            entry.Pending = false;
            var callbacks = entry.Callbacks;
            entry.Callbacks = null;
            if (callbacks == null) {
                return;
            }
            foreach (var callback in callbacks) {
                try {
                    callback(bitmap);
                } catch (Exception e) {
                    Log.Error(e, "Failed to apply avatar.");
                }
            }
        }

        static Bitmap? Decode(object source, string name) {
            try {
                var data = source as byte[];
                if (data == null && source is string path && Path.IsPathRooted(path) && File.Exists(path)) {
                    data = File.ReadAllBytes(path);
                }
                if (data == null) {
                    return null;
                }
                using var stream = new MemoryStream(data);
                using var bitmap = new Bitmap(stream);
                return bitmap.CreateScaledBitmap(new PixelSize(Size, Size));
            } catch (Exception e) {
                Log.Error(e, $"Failed to decode avatar of {name}.");
                return null;
            }
        }
    }
}
