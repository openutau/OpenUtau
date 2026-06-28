using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Audio {
    public class MiniAudioOutput : IAudioOutput, IDisposable {
        const int channels = 2;
        const int sampleRate = 44100;

        public PlaybackState PlaybackState { get; private set; }
        public int DeviceNumber { get; private set; }


        private ISampleProvider? sampleProvider;
        private double currentTimeMs;
        private bool eof;

        private List<AudioOutputDevice> devices = new List<AudioOutputDevice>();
        private IntPtr callbackPtr = IntPtr.Zero;
        private IntPtr nativeContext = IntPtr.Zero;
        private Guid selectedDevice = Guid.Empty;

        public MiniAudioOutput() {
            unsafe {
                var f = (ou_audio_data_callback_t)DataCallback;
                GCHandle.Alloc(f);
                callbackPtr = Marshal.GetFunctionPointerForDelegate(f);
            }
            if (Preferences.Default.UseSystemDefaultAudioDevice) {
                nativeContext = ou_init_audio_device_auto(callbackPtr);
                if (nativeContext != IntPtr.Zero) {
                    return;
                }
                Log.Warning("Failed to init default audio device, falling back to enumerated audio devices.");
            }
            UpdateDeviceList();
            if (Guid.TryParse(Preferences.Default.PlaybackDevice, out var guid)) {
                SelectDevice(guid, Preferences.Default.PlaybackDeviceNumber);
            } else {
                bool foundDevice = false;
                foreach (AudioOutputDevice dev in devices) {
                    try {
                        SelectDevice(dev.guid, dev.deviceNumber);
                        foundDevice = true;
                        break;
                    } catch (Exception e) {
                        Log.Warning(e, $"Failed to init audio device {dev}");
                    }
                }
                if (!foundDevice) {
                    throw new Exception("Failed to init any audio device");
                }
            }
        }

        private void UpdateDeviceList() {
            devices.Clear();
            unsafe {
                const int kMaxCount = 128;
                ou_audio_device_info_t* device_infos = stackalloc ou_audio_device_info_t[kMaxCount];
                int count = ou_get_audio_device_infos(device_infos, kMaxCount);
                if (count == 0) {
                    throw new Exception("Failed to get any audio device info");
                }
                if (count > kMaxCount) {
                    Log.Warning($"More than {kMaxCount} audio devices found, only the first {kMaxCount} will be listed.");
                    count = kMaxCount;
                }
                for (int i = 0; i < count; i++) {
                    var guidData = new byte[16];
                    fixed (byte* guidPtr = guidData) {
                        *(ulong*)guidPtr = device_infos[i].api_id;
                        *(ulong*)(guidPtr + 8) = device_infos[i].id;
                    }
                    string api = Marshal.PtrToStringUTF8(device_infos[i].api); // Should be ascii.
                    string name = (OS.IsWindows() && api != "WASAPI")
                        ? Marshal.PtrToStringAnsi(device_infos[i].name)
                        : Marshal.PtrToStringUTF8(device_infos[i].name);
                    devices.Add(new AudioOutputDevice {
                        name = name,
                        api = api,
                        deviceNumber = i,
                        guid = new Guid(guidData),
                    });
                }
                ou_free_audio_device_infos(device_infos, count);
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
            if (PlaybackState != PlaybackState.Playing) {
                CheckError(ou_audio_device_start(nativeContext));
            }
            if (PlaybackState != PlaybackState.Paused) { 
                currentTimeMs = 0;
            }
            PlaybackState = PlaybackState.Playing;
            eof = false;
        }

        public void Pause() {
            if (PlaybackState == PlaybackState.Playing) {
                CheckError(ou_audio_device_stop(nativeContext));
            }
            PlaybackState = PlaybackState.Paused;
        }

        public void Stop() {
            if (PlaybackState == PlaybackState.Playing) {
                CheckError(ou_audio_device_stop(nativeContext));
            }
            PlaybackState = PlaybackState.Stopped;
        }

        float[] temp = new float[0];
        private unsafe void DataCallback(float* buffer, uint channels, uint frame_count) {
            int samples = (int)(channels * frame_count);
            if (temp.Length < samples) {
                temp = new float[samples];
            }
            int n = 0;
            if (sampleProvider != null) {
                n = sampleProvider.Read(temp, 0, samples);
            }
            if (n < samples) {
                Array.Fill(temp, 0, n, samples - n);
            }
            if (n == 0) {
                eof = true;
            }
            Marshal.Copy(temp, 0, (IntPtr)buffer, samples);
            currentTimeMs += n / channels * 1000.0 / sampleRate;
        }

        public long GetPosition() {
            if (eof && PlaybackState == PlaybackState.Playing) {
                Stop();
            }
            return (long)(Math.Max(0, currentTimeMs) / 1000 * sampleRate * 2 /* 16 bit */ * channels);
        }

        public void SelectDevice(Guid guid, int deviceNumber) {
            if (Preferences.Default.UseSystemDefaultAudioDevice) {
                return;
            }
            if (selectedDevice != Guid.Empty && selectedDevice == guid) {
                return;
            }
            if (nativeContext != IntPtr.Zero) {
                CheckError(ou_free_audio_device(nativeContext));
                nativeContext = IntPtr.Zero;
                selectedDevice = Guid.Empty;
            }
            for (int i = 0; i < devices.Count; i++) {
                if (devices[i].guid == guid) {
                    deviceNumber = i;
                    break;
                }
                if (i == devices.Count - 1) {
                    guid = devices[0].guid;
                    deviceNumber = devices[0].deviceNumber;
                }
            }
            uint api_id;
            ulong id;
            unsafe {
                fixed (byte* guidPtr = guid.ToByteArray()) {
                    api_id = (uint)*(ulong*)guidPtr;
                    id = *(ulong*)(guidPtr + 8);
                }
            }
            unsafe {
                nativeContext = ou_init_audio_device(api_id, id, callbackPtr);
                if (nativeContext == IntPtr.Zero) {
                    throw new Exception("Failed to init audio device");
                }
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
            if (Preferences.Default.UseSystemDefaultAudioDevice) {
                return new List<AudioOutputDevice>();
            }
            return devices;
        }

        #region binding

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct ou_audio_device_info_t {
            public IntPtr name;
            public ulong id;
            public IntPtr api;
            public uint api_id;
        }

        [UnmanagedFunctionPointer(callingConvention: CallingConvention.Cdecl)]
        private unsafe delegate void ou_audio_data_callback_t(float* buffer, uint channels, uint frame_count);

        [DllImport("worldline")] private static extern unsafe int ou_get_audio_device_infos(ou_audio_device_info_t* device_infos, int max_count);
        [DllImport("worldline")] private static extern unsafe void ou_free_audio_device_infos(ou_audio_device_info_t* device_infos, int count);
        [DllImport("worldline")] private static extern IntPtr ou_init_audio_device(uint api_id, ulong id, IntPtr callback);
        [DllImport("worldline")] private static extern IntPtr ou_init_audio_device_auto(IntPtr callback);
        [DllImport("worldline")] private static extern int ou_free_audio_device(IntPtr context);
        [DllImport("worldline")] private static extern int ou_audio_device_start(IntPtr context);
        [DllImport("worldline")] private static extern int ou_audio_device_stop(IntPtr context);
        [DllImport("worldline")] private static extern IntPtr ou_audio_get_error_message(int error_code);

        private static void CheckError(int errorCode) {
            if (errorCode == 0) {
                return;
            }
            IntPtr ptr = ou_audio_get_error_message(errorCode);
            throw new Exception(Marshal.PtrToStringUTF8(ptr));
        }

        #endregion

        #region disposable

        private bool disposedValue;

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // dispose managed state (managed objects)
                }

                // free unmanaged resources (unmanaged objects) and override finalizer
                if (nativeContext != IntPtr.Zero) {
                    ou_free_audio_device(nativeContext);
                    nativeContext = IntPtr.Zero;
                }

                // set large fields to null

                disposedValue = true;
            }
        }

        ~MiniAudioOutput() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
