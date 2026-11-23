using System;
using System.Collections.Generic;
using Xunit;

namespace OpenUtau.Core.SignalChain {
    public class ExportAdapterTest {
        class ConstantSource : ISignalSource {
            readonly int length;

            public ConstantSource(int length) {
                this.length = length;
            }

            public bool IsReady(int position, int count) => true;

            public int Mix(int position, float[] buffer, int index, int count) {
                int n = System.Math.Clamp(length - position, 0, count);
                for (int i = 0; i < n; ++i) {
                    buffer[index + i] += 1f;
                }
                return position + n;
            }
        }

        static List<float> ReadAll(ExportAdapter adapter) {
            var result = new List<float>();
            var buffer = new float[1024];
            int n;
            while ((n = adapter.Read(buffer, 0, buffer.Length)) > 0) {
                result.AddRange(new ArraySegment<float>(buffer, 0, n));
            }
            return result;
        }

        [Fact]
        public void FullExportIsUntouched() {
            var samples = ReadAll(new ExportAdapter(new ConstantSource(4000)));

            Assert.Equal(4000, samples.Count);
            Assert.All(samples, sample => Assert.Equal(1f, sample));
        }

        [Fact]
        public void RangeExportReturnsOnlyTheRange() {
            var samples = ReadAll(new ExportAdapter(new ConstantSource(200000), 100, 300));

            Assert.Equal(17640, samples.Count);
        }

        [Fact]
        public void RangeExportFadesBothEdgesAndKeepsTheMiddle() {
            var samples = ReadAll(new ExportAdapter(new ConstantSource(200000), 100, 300));

            Assert.Equal(0f, samples[0]);
            Assert.Equal(0f, samples[samples.Count - 1]);
            Assert.Equal(1f, samples[samples.Count / 2]);
            Assert.True(samples[200] > 0f && samples[200] < 1f);
        }

        [Fact]
        public void RangeBeyondTheSourceIsPaddedWithSilence() {
            var samples = ReadAll(new ExportAdapter(new ConstantSource(1000), 100, 300));

            Assert.Equal(17640, samples.Count);
            Assert.All(samples, sample => Assert.Equal(0f, sample));
        }

        [Fact]
        public void RangeStartsAtTheRequestedPosition() {
            var source = new PositionRecorder();

            ReadAll(new ExportAdapter(source, 100, 110));

            Assert.Equal(8820, source.FirstPosition);
        }

        class PositionRecorder : ISignalSource {
            public int FirstPosition = -1;

            public bool IsReady(int position, int count) => true;

            public int Mix(int position, float[] buffer, int index, int count) {
                if (FirstPosition < 0) {
                    FirstPosition = position;
                }
                return position + count;
            }
        }
    }
}
