namespace OpenUtau.Core.SignalChain {
    public interface ISignalSource {
        bool IsReady(int position, int count);
        /// <summary>
        /// Add float audio samples to existing buffer values.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="buffer"></param>
        /// <param name="index"></param>
        /// <param name="count"></param>
        /// <returns>End position after read.</returns>
        int Mix(int position, float[] buffer, int index, int count);
    }
}
