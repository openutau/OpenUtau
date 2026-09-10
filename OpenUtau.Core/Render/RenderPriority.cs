using System;

namespace OpenUtau.Core.Render {
    internal static class RenderPriority {
        internal static int PlaybackBucket(double sourceStartMs, double sourceEndMs, double playbackStartMs) {
            if (sourceStartMs <= playbackStartMs && sourceEndMs > playbackStartMs) {
                return 0;
            }
            return sourceStartMs >= playbackStartMs ? 1 : 2;
        }

        internal static double PlaybackDistance(double sourceStartMs, double sourceEndMs, double playbackStartMs) {
            if (sourceStartMs <= playbackStartMs && sourceEndMs > playbackStartMs) {
                return Math.Max(0, playbackStartMs - sourceStartMs);
            }
            if (sourceStartMs >= playbackStartMs) {
                return sourceStartMs - playbackStartMs;
            }
            return playbackStartMs - sourceEndMs;
        }

        internal static int PreRenderBucket(
            bool isPriorityPart,
            bool overlapsPriority,
            bool isAfterPriorityStart) {
            if (isPriorityPart && overlapsPriority) {
                return 0;
            }
            if (isPriorityPart) {
                return 1;
            }
            if (isAfterPriorityStart) {
                return 2;
            }
            return 3;
        }

        internal static int PreRenderDistance(int phraseStartTick, int phraseEndTick, int priorityStartTick) {
            if (phraseStartTick <= priorityStartTick && phraseEndTick > priorityStartTick) {
                return 0;
            }
            if (phraseStartTick >= priorityStartTick) {
                return phraseStartTick - priorityStartTick;
            }
            return priorityStartTick - phraseEndTick;
        }
    }
}
