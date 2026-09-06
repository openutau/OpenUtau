using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Core.Util;
using Serilog;
using SDL3;

namespace OpenUtau.Audio {
    public class SDL3AudioOutput : IAudioOutput, IDisposable {
        const int channels = 2;
        const int sampleRate = 44100;

        public PlaybackState PlaybackState { get; private set; }
        public int DeviceNumber { get; private set; }

        private ISampleProvider? sampleProvider;
        private double currentTimeMs;
        private bool eof;

        private List<AudioOutputDevice> devices = new List<AudioOutputDevice>();
        private Guid selectedDevice = Guid.Empty;
        private IntPtr stream = IntPtr.Zero;
        private readonly SDL.AudioStreamCallback callback;
        private bool initializedSdl;

        public SDL3AudioOutput() {
            callback = DataCallback;
            // Ensure audio subsystem is initialized
            var audioFlag = SDL.InitFlags.Audio;
            if ((SDL.WasInit(audioFlag) & audioFlag) == 0) {
                if (!SDL.Init(audioFlag)) {
                    Log.Error($"Failed to initialize SDL audio: {SDL.GetError()}");
                }
                initializedSdl = true;
            }

            UpdateDeviceList();
            if (Preferences.Default.UseSystemDefaultAudioDevice) {
                OpenStream(SDL.AudioDeviceDefaultPlayback);
                return;
            }
            
            if (Guid.TryParse(Preferences.Default.PlaybackDevice, out var guid)
                && devices.Any(d => d.guid == guid)) {
                SelectDevice(guid, Preferences.Default.PlaybackDeviceNumber);
                return;
            }

            bool foundDevice = false;
            foreach (var dev in devices) {
                try {
                    SelectDevice(dev.guid, dev.deviceNumber);
                    foundDevice = true;
                    break;
                } catch (Exception e) {
                    Log.Warning(e, $"Failed to init audio device {dev}");
                }
            }

            if (!foundDevice) {
                // Fall back to whatever SDL considers the default device.
                OpenStream(SDL.AudioDeviceDefaultPlayback);
            }
        }

        private void UpdateDeviceList() {
            devices.Clear();
            int count;
            var arr = SDL.GetAudioPlaybackDevices(out count);
            if (arr == null) {
                Log.Error($"Failed to get SDL audio playback devices: {SDL.GetError()}");
            }

            for (int i = 0; i < arr.Length; i++) {
                uint devid = arr[i];
                string name = SDL.GetAudioDeviceName(devid) ?? $"Device {devid}";
                devices.Add(new AudioOutputDevice {
                    name = name,
                    api = "SDL3",
                    deviceNumber = i,
                    guid = ToGuid(devid),
                });
            }
        }

        public void Init(ISampleProvider sampleProvider) {
            PlaybackState = PlaybackState.Stopped;
            eof = false;
            currentTimeMs = 0;
            if (sampleRate != sampleProvider.WaveFormat.SampleRate) {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, sampleRate);
            }
            this.sampleProvider = sampleProvider.ToStereo();
        }

        public void Play() {
            if (stream == IntPtr.Zero) {
                return;
            }
            if (PlaybackState != PlaybackState.Playing) {
                if (!SDL.ResumeAudioStreamDevice(stream)) {
                    Log.Warning($"Failed to resume SDL audio device: {SDL.GetError()}");
                }
            }
            if (PlaybackState != PlaybackState.Paused) {
                currentTimeMs = 0;
            }
            PlaybackState = PlaybackState.Playing;
            eof = false;
        }

        public void Pause() {
            if (stream != IntPtr.Zero && PlaybackState == PlaybackState.Playing) {
                SDL.PauseAudioStreamDevice(stream);
            }
            PlaybackState = PlaybackState.Paused;
        }

        public void Stop() {
            if (stream != IntPtr.Zero && PlaybackState == PlaybackState.Playing) {
                SDL.PauseAudioStreamDevice(stream);
            }
            PlaybackState = PlaybackState.Stopped;
        }

        float[] temp = new float[0];

        private unsafe void DataCallback(IntPtr userdata, IntPtr streamPtr, int additionalAmount, int totalAmount) {
            if (additionalAmount <= 0) {
                return;
            }
            int samples = additionalAmount / sizeof(float);
            if (temp.Length < samples) {
                temp = new float[samples];
            }
            int n = 0;
            if (sampleProvider != null) {
                n = sampleProvider.Read(temp, 0, samples);
            }

            // If fewer samples read than requested, leave the remainder as zeros
            if (n < samples) {
                Array.Clear(temp, n, samples - n);
            }
            if (n == 0) {
                eof = true;
            }

            // Convert float[] to byte[] (SDL expects native float bytes for AudioF32)
            int bytesLen = samples * sizeof(float);
            var bytes = new byte[bytesLen];
            Buffer.BlockCopy(temp, 0, bytes, 0, Math.Min(n * sizeof(float), bytesLen));

            if (!SDL.PutAudioStreamData(streamPtr, bytes, bytesLen)) {
                Log.Warning($"Failed to put SDL audio stream data: {SDL.GetError()}");
            }

            currentTimeMs += (double)n / channels * 1000.0 / sampleRate;
        }

        public long GetPosition() {
            if (eof && PlaybackState == PlaybackState.Playing) {
                Stop();
            }
            // Return bytes position in the same convention the original used:
            return (long)(Math.Max(0, currentTimeMs) / 1000 * sampleRate * 2 /* 16 bit */ * channels);
        }

        public void SelectDevice(Guid guid, int deviceNumber) {
            if (Preferences.Default.UseSystemDefaultAudioDevice) {
                return;
            }
            if (selectedDevice != Guid.Empty && selectedDevice == guid) {
                return;
            }
            for (int i = 0; i < devices.Count; i++) {
                if (devices[i].guid == guid) {
                    deviceNumber = i;
                    break;
                }
                if (i == devices.Count - 1 && devices.Count > 0) {
                    guid = devices[0].guid;
                    deviceNumber = devices[0].deviceNumber;
                }
            }
            bool wasPlaying = PlaybackState == PlaybackState.Playing;
            OpenStream(FromGuid(guid));
            if (wasPlaying) {
                SDL.ResumeAudioStreamDevice(stream);
            }
            selectedDevice = guid;
            DeviceNumber = deviceNumber;
            if (Preferences.Default.PlaybackDevice != guid.ToString()) {
                Preferences.Default.PlaybackDevice = guid.ToString();
                Preferences.Default.PlaybackDeviceNumber = deviceNumber;
                Preferences.Save();
            }
        }

        public List<AudioOutputDevice> GetOutputDevices() {
            return devices;
        }

        private void OpenStream(uint devid) {
            CloseStream();

            var spec = new SDL.AudioSpec {
                Format = BitConverter.IsLittleEndian ? SDL.AudioFormat.AudioF32LE : SDL.AudioFormat.AudioF32BE,
                Channels = channels,
                Freq = sampleRate,
            };

            stream = SDL.OpenAudioDeviceStream(devid, in spec, callback, IntPtr.Zero);
            if (stream == IntPtr.Zero) {
                Log.Error($"Failed to open SDL audio device: {SDL.GetError()}");
            }
        }

        private void CloseStream() {
            if (stream != IntPtr.Zero) {
                SDL.DestroyAudioStream(stream);
                stream = IntPtr.Zero;
            }
        }

        private static Guid ToGuid(uint devid) {
            var bytes = new byte[16];
            BitConverter.GetBytes(devid).CopyTo(bytes, 0);
            return new Guid(bytes);
        }

        private static uint FromGuid(Guid guid) {
            var bytes = guid.ToByteArray();
            return BitConverter.ToUInt32(bytes, 0);
        }
        

        #region disposable

        private bool disposedValue;

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // dispose managed state (managed objects)
                }
                CloseStream();
                if (initializedSdl) {
                    SDL.QuitSubSystem(SDL.InitFlags.Audio);
                    initializedSdl = false;
                }
                disposedValue = true;
            }
        }

        ~SDL3AudioOutput() {
            Dispose(disposing: false);
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
