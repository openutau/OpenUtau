using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace OpenUtau.Audio {
    public class DummyAudioOutput : IAudioOutput {
        public PlaybackState PlaybackState => PlaybackState.Stopped;
        public int DeviceNumber => 0;
        public List<AudioOutputDevice> GetOutputDevices() => new List<AudioOutputDevice>();
        public long GetPosition() => 0;
        public void Init(ISampleProvider sampleProvider) { }
        public void Pause() { }
        public void Play() { }
        public void SelectDevice(Guid guid, int deviceNumber) { }
        public void Stop() { }
    }
}
