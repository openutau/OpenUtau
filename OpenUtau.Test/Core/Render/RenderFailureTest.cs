using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Audio;
using Xunit;

namespace OpenUtau.Core.Render {
    [Collection(RenderSingletonCollection.Name)]
    public class RenderFailureTest : IDisposable {
        private sealed class TestOutput : IAudioOutput {
            public int StopCount { get; private set; }
            public PlaybackState PlaybackState => PlaybackState.Playing;
            public int DeviceNumber => 0;
            public void Stop() => StopCount++;
            public void Play() { }
            public void Pause() { }
            public void Init(ISampleProvider provider) { }
            public void SelectDevice(Guid guid, int deviceNumber) { }
            public long GetPosition() => 0;
            public List<AudioOutputDevice> GetOutputDevices() => new();
        }

        private readonly IAudioOutput originalOutput = PlaybackManager.Inst.AudioOutput;
        private readonly TestOutput output = new();
        private readonly List<ErrorMessageNotification> errors = new();

        public RenderFailureTest() {
            PlaybackManager.Inst.AudioOutput = output;
            DocManager.Inst.CommandSink = cmd => {
                if (cmd is ErrorMessageNotification error) {
                    errors.Add(error);
                }
            };
        }

        public void Dispose() {
            DocManager.Inst.CommandSink = null;
            PlaybackManager.Inst.AudioOutput = originalOutput;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SupersededPassDoesNotReportOrStopPlayback(bool cancellationError) {
            var source = new CancellationTokenSource();
            var token = source.Token;
            Exception error = cancellationError
                ? new TaskCanceledException()
                : new InvalidOperationException("Old renderer failed");
            // The failure can precede cancellation, but its UI callback runs later.
            var exception = new AggregateException(new AggregateException(error));
            source.Cancel();
            source.Dispose();

            RenderEngine.HandleRenderFailure(exception, token);

            Assert.Empty(errors);
            Assert.Equal(0, output.StopCount);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CurrentPassStillReportsFailure(bool cancellationError) {
            using var source = new CancellationTokenSource();
            Exception error = cancellationError
                ? new TaskCanceledException()
                : new InvalidOperationException("Current renderer failed");

            // A plugin cancelling independently of the host is still a failure.
            RenderEngine.HandleRenderFailure(new AggregateException(error), source.Token);

            Assert.Single(errors);
            Assert.Equal(1, output.StopCount);
        }
    }
}
