using System;

namespace OpenUtau.Core.Headless {
    public class HeadlessRenderException : Exception {
        public HeadlessRenderException(string message) : base(message) {
        }

        public HeadlessRenderException(string message, Exception innerException) : base(message, innerException) {
        }
    }
}
