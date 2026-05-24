using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;
#if WINDOWS
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
#endif
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App {
#if !WINDOWS
    public class NAudioOutput : DummyAudioOutput { }
#else
    public class NAudioOutput : IAudioOutput {
        const int Channels = 2;

        private readonly object lockObj = new object();
        private WaveOutEvent? waveOutEvent;
        private WasapiOut? wasapiOut;
        private int deviceNumber;

        // Auto-mode fields
        private MMDeviceEnumerator? mmEnumerator;
        private DefaultDeviceNotificationClient? notificationClient;

        public NAudioOutput() {
            if (Preferences.Default.UseSystemDefaultAudioDevice) {
                mmEnumerator = new MMDeviceEnumerator();
                notificationClient = new DefaultDeviceNotificationClient(() => Task.Run(() => PlaybackManager.Inst.StopPlayback()));
                mmEnumerator.RegisterEndpointNotificationCallback(notificationClient);
                return;
            }
            if (Guid.TryParse(Preferences.Default.PlaybackDevice, out var guid)) {
                SelectDevice(guid, Preferences.Default.PlaybackDeviceNumber);
            } else {
                SelectDevice(new Guid(), 0);
            }
        }

        // Fired when WasapiOut stops unexpectedly (e.g. USB headset unplugged).
        private void OnWasapiPlaybackStopped(object? sender, StoppedEventArgs e) {
            if (e.Exception != null) {
                Log.Warning(e.Exception, "WasapiOut stopped unexpectedly.");
                Task.Run(() => PlaybackManager.Inst.StopPlayback());
            }
        }

        public PlaybackState PlaybackState {
            get {
                lock (lockObj) {
                    if (Preferences.Default.UseSystemDefaultAudioDevice) {
                        return wasapiOut == null ? PlaybackState.Stopped : wasapiOut.PlaybackState;
                    }
                    return waveOutEvent == null ? PlaybackState.Stopped : waveOutEvent.PlaybackState;
                }
            }
        }

        public int DeviceNumber => deviceNumber;

        public long GetPosition() {
            lock (lockObj) {
                if (Preferences.Default.UseSystemDefaultAudioDevice) {
                    return wasapiOut == null ? 0 : wasapiOut.GetPosition() / Channels;
                }
                return waveOutEvent == null ? 0 : waveOutEvent.GetPosition() / Channels;
            }
        }

        public void Init(ISampleProvider sampleProvider) {
            lock (lockObj) {
                if (Preferences.Default.UseSystemDefaultAudioDevice) {
                    // Re-register notification client if Stop() previously unregistered it.
                    if (notificationClient == null && mmEnumerator != null) {
                        notificationClient = new DefaultDeviceNotificationClient(() => Task.Run(() => PlaybackManager.Inst.StopPlayback()));
                        mmEnumerator.RegisterEndpointNotificationCallback(notificationClient);
                    }
                    if (wasapiOut != null) {
                        wasapiOut.PlaybackStopped -= OnWasapiPlaybackStopped;
                        wasapiOut.Stop();
                        wasapiOut.Dispose();
                    }
                    wasapiOut = new WasapiOut(AudioClientShareMode.Shared, 200);
                    wasapiOut.PlaybackStopped += OnWasapiPlaybackStopped;
                    wasapiOut.Init(sampleProvider);
                } else {
                    if (waveOutEvent != null) {
                        waveOutEvent.Stop();
                        waveOutEvent.Dispose();
                    }
                    waveOutEvent = new WaveOutEvent() {
                        DeviceNumber = deviceNumber,
                    };
                    waveOutEvent.Init(sampleProvider);
                }
            }
        }

        public void Pause() {
            lock (lockObj) {
                if (Preferences.Default.UseSystemDefaultAudioDevice) {
                    wasapiOut?.Pause();
                } else {
                    waveOutEvent?.Pause();
                }
            }
        }

        public void Play() {
            lock (lockObj) {
                if (Preferences.Default.UseSystemDefaultAudioDevice) {
                    wasapiOut?.Play();
                } else {
                    waveOutEvent?.Play();
                }
            }
        }

        public void Stop() {
            lock (lockObj) {
                if (Preferences.Default.UseSystemDefaultAudioDevice) {
                    if (notificationClient != null && mmEnumerator != null) {
                        mmEnumerator.UnregisterEndpointNotificationCallback(notificationClient);
                        notificationClient = null;
                    }
                    if (wasapiOut != null) {
                        wasapiOut.PlaybackStopped -= OnWasapiPlaybackStopped;
                        wasapiOut.Stop();
                        wasapiOut.Dispose();
                        wasapiOut = null;
                    }
                } else {
                    if (waveOutEvent != null) {
                        waveOutEvent.Stop();
                        waveOutEvent.Dispose();
                        waveOutEvent = null;
                    }
                }
            }
        }

        public void SelectDevice(Guid guid, int deviceNumber) {
            if (Preferences.Default.UseSystemDefaultAudioDevice) {
                return;
            }
            Preferences.Default.PlaybackDevice = guid.ToString();
            Preferences.Default.PlaybackDeviceNumber = deviceNumber;
            Preferences.Save();
            // Product guid may not be unique. Use device number first.
            if (deviceNumber < WaveOut.DeviceCount && WaveOut.GetCapabilities(deviceNumber).ProductGuid == guid) {
                this.deviceNumber = deviceNumber;
                return;
            }
            // If guid does not match, device number may have changed. Search guid instead.
            this.deviceNumber = 0;
            for (int i = 0; i < WaveOut.DeviceCount; ++i) {
                if (WaveOut.GetCapabilities(i).ProductGuid == guid) {
                    this.deviceNumber = i;
                    break;
                }
            }
        }

        public List<AudioOutputDevice> GetOutputDevices() {
            if (Preferences.Default.UseSystemDefaultAudioDevice) {
                return new List<AudioOutputDevice>();
            }
            var outDevices = new List<AudioOutputDevice>();
            for (int i = 0; i < WaveOut.DeviceCount; ++i) {
                var capability = WaveOut.GetCapabilities(i);
                outDevices.Add(new AudioOutputDevice {
                    api = "WaveOut",
                    name = capability.ProductName,
                    deviceNumber = i,
                    guid = capability.ProductGuid,
                });
            }
            return outDevices;
        }

        // Implements IMMNotificationClient; only acts on default render device changes.
        private class DefaultDeviceNotificationClient : IMMNotificationClient {
            private readonly Action onDefaultRenderDeviceChanged;

            public DefaultDeviceNotificationClient(Action onDefaultRenderDeviceChanged) {
                this.onDefaultRenderDeviceChanged = onDefaultRenderDeviceChanged;
            }

            public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) {
                if (flow == DataFlow.Render && role == Role.Multimedia) {
                    onDefaultRenderDeviceChanged();
                }
            }

            public void OnDeviceAdded(string pwstrDeviceId) { }
            public void OnDeviceRemoved(string deviceId) { }
            public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
            public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        }
    }
#endif
}
