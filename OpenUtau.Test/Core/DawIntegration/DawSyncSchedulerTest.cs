using System;
using Xunit;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// The debounce contract from PROTOCOL.md §7. The scheduler takes time as an argument, so
    /// none of this sleeps.
    /// </summary>
    [Collection(DawIntegrationCollection.Name)]
    public class DawSyncSchedulerTest {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void FastStreamsWaitOneSecond() {
            var scheduler = new DawSyncScheduler();

            scheduler.Touch(DawSyncKind.Ustx, T0);

            Assert.Empty(scheduler.TryTake(T0.AddMilliseconds(999)));
            Assert.Equal(new[] { DawSyncKind.Ustx }, scheduler.TryTake(T0.AddSeconds(1)));
        }

        [Fact]
        public void PartLayoutWaitsFiveSeconds() {
            var scheduler = new DawSyncScheduler();

            scheduler.Touch(DawSyncKind.PartLayout, T0);

            Assert.Empty(scheduler.TryTake(T0.AddSeconds(4)));
            Assert.Equal(new[] { DawSyncKind.PartLayout }, scheduler.TryTake(T0.AddSeconds(5)));
        }

        [Fact]
        public void TakingAStreamClearsIt() {
            var scheduler = new DawSyncScheduler();

            scheduler.Touch(DawSyncKind.Tracks, T0);
            Assert.Single(scheduler.TryTake(T0.AddSeconds(1)));

            Assert.Empty(scheduler.TryTake(T0.AddSeconds(10)));
            Assert.False(scheduler.HasPending);
        }

        [Fact]
        public void EachEditPushesTheDueTimeOut() {
            var scheduler = new DawSyncScheduler();

            // A user editing continuously: the sync must land after they stop, not during.
            scheduler.Touch(DawSyncKind.Ustx, T0);
            scheduler.Touch(DawSyncKind.Ustx, T0.AddMilliseconds(500));
            scheduler.Touch(DawSyncKind.Ustx, T0.AddMilliseconds(900));

            Assert.Empty(scheduler.TryTake(T0.AddMilliseconds(1800)));
            Assert.Single(scheduler.TryTake(T0.AddMilliseconds(1900)));
        }

        [Fact]
        public void TakeIsOrderedUstxThenTracksThenLayout() {
            var scheduler = new DawSyncScheduler();

            scheduler.Touch(DawSyncKind.PartLayout, T0);
            scheduler.Touch(DawSyncKind.Tracks, T0);
            scheduler.Touch(DawSyncKind.Ustx, T0);

            Assert.Equal(
                new[] { DawSyncKind.Ustx, DawSyncKind.Tracks, DawSyncKind.PartLayout },
                scheduler.TryTake(T0.AddSeconds(5)));
        }

        [Fact]
        public void PlaybackFlushMakesPendingStreamsDueAtOnce() {
            var scheduler = new DawSyncScheduler();
            scheduler.Touch(DawSyncKind.Ustx, T0);
            scheduler.Touch(DawSyncKind.PartLayout, T0);

            // playbackStarted: the DAW is about to play, so it must not hear stale audio.
            scheduler.FlushPending(T0.AddMilliseconds(10));

            Assert.Equal(
                new[] { DawSyncKind.Ustx, DawSyncKind.PartLayout },
                scheduler.TryTake(T0.AddMilliseconds(10)));
        }

        [Fact]
        public void PlaybackFlushInventsNothingWhenIdle() {
            var scheduler = new DawSyncScheduler();

            scheduler.FlushPending(T0);

            Assert.False(scheduler.HasPending);
            Assert.Empty(scheduler.TryTake(T0));
        }

        [Fact]
        public void FullSyncMakesEveryStreamDue() {
            var scheduler = new DawSyncScheduler();

            scheduler.RequestFullSync(T0);

            Assert.Equal(
                new[] { DawSyncKind.Ustx, DawSyncKind.Tracks, DawSyncKind.PartLayout, DawSyncKind.ProjectInfo },
                scheduler.TryTake(T0));
        }

        [Fact]
        public void MakeDueBypassesTheDebounce() {
            var scheduler = new DawSyncScheduler();

            scheduler.MakeDue(DawSyncKind.PartLayout, T0);

            Assert.Equal(new[] { DawSyncKind.PartLayout }, scheduler.TryTake(T0));
        }

        [Fact]
        public void ClearDropsEverythingPending() {
            var scheduler = new DawSyncScheduler();
            scheduler.RequestFullSync(T0);

            scheduler.Clear();

            Assert.False(scheduler.HasPending);
            Assert.Empty(scheduler.TryTake(T0.AddMinutes(1)));
        }

        [Fact]
        public void DebounceWindowsAreInjectable() {
            var scheduler = new DawSyncScheduler(
                fastDebounce: TimeSpan.FromMilliseconds(10), slowDebounce: TimeSpan.FromMilliseconds(20));

            Assert.Equal(TimeSpan.FromMilliseconds(10), scheduler.DebounceFor(DawSyncKind.Ustx));
            Assert.Equal(TimeSpan.FromMilliseconds(10), scheduler.DebounceFor(DawSyncKind.Tracks));
            Assert.Equal(TimeSpan.FromMilliseconds(20), scheduler.DebounceFor(DawSyncKind.PartLayout));
        }
    }
}
