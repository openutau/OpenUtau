using System;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.Core.Util {
    public class DebounceTest {
        [Fact]
        public async Task FlushRunsPendingCallbackOnce() {
            var debounce = new Debounce();
            var callbackCount = 0;
            debounce.Do(TimeSpan.FromSeconds(10), () => {
                Interlocked.Increment(ref callbackCount);
                return Task.CompletedTask;
            });

            await debounce.Flush();
            await Task.Delay(50);

            Assert.Equal(1, Volatile.Read(ref callbackCount));
        }
    }
}
