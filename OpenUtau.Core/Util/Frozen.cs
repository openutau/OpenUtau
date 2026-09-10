using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using K4os.Hash.xxHash;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// An immutable numeric buffer. After <see cref="Freeze{T}(T[], int)"/>, the producing side
    /// must never write <see cref="Buffer"/> again: the buffer is shared across threads by
    /// reference (audio callback, UI, render workers) and only the immutability contract, not a
    /// lock, makes that safe.
    ///
    /// In DEBUG the content is hashed at construction and <see cref="Verify"/> can re-check it.
    /// Verification is deliberately NOT done on every read: PCM buffers are re-read on the audio
    /// callback at real time, and re-hashing a megabyte buffer per <c>Read</c> would distort the
    /// very latency budget the check exists to protect. Call <see cref="Verify"/> at the points
    /// where ownership changes hands (publish) and in tests.
    /// </summary>
    public sealed class Frozen<T> where T : struct {
        /// <summary>The owned buffer. Never write to it after Freeze.</summary>
        public readonly T[] Buffer;

        /// <summary>Authoritative element count; may be smaller than <c>Buffer.Length</c>.</summary>
        public readonly int Length;

#if DEBUG
        private readonly ulong hash;
#endif

        public Frozen(T[] buffer, int length) {
            if (buffer == null) {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (length < 0 || length > buffer.Length) {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            Buffer = buffer;
            Length = length;
#if DEBUG
            hash = ComputeHash();
#endif
        }

        public ReadOnlySpan<T> Span => Buffer.AsSpan(0, Length);

#if DEBUG
        private ulong ComputeHash() {
            return Length == 0 ? 0 : XXH64.DigestOf(MemoryMarshal.AsBytes(Span));
        }

        /// <summary>
        /// Re-hashes the buffer and asserts it still matches the construction-time hash.
        /// Throws <see cref="InvalidOperationException"/> on mismatch (i.e. somebody mutated a
        /// "frozen" buffer). Call at publish / hand-off points, not in the audio callback.
        /// </summary>
        public void Verify() {
            if (ComputeHash() != hash) {
                throw new InvalidOperationException($"Frozen<{typeof(T).Name}> buffer was mutated after Freeze.");
            }
        }

        public ulong DebugHash => hash;
#endif
    }

    public static class FrozenExtensions {
        /// <summary>
        /// Wraps <paramref name="array"/> as a frozen buffer, taking OWNERSHIP of it.
        /// <paramref name="length"/> defaults to the whole array; pass a smaller value only when
        /// the array is a scratch buffer with a live prefix.
        /// </summary>
        public static Frozen<T> Freeze<T>(this T[] array, int length = -1) where T : struct {
            return new Frozen<T>(array, length < 0 ? array.Length : length);
        }
    }
}
