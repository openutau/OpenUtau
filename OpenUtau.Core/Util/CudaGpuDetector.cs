using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace OpenUtau.Core.Util {
    public static class CudaGpuDetector {
        // CUDA driver API
        [DllImport("nvcuda.dll", EntryPoint = "cuInit")]
        private static extern int cuInitWindows(uint flags);

        [DllImport("libcuda.so", EntryPoint = "cuInit")]
        private static extern int cuInitLinux(uint flags);

        [DllImport("nvcuda.dll", EntryPoint = "cuDriverGetVersion")]
        private static extern int cuDriverGetVersionWindows(out int driverVersion);

        [DllImport("libcuda.so", EntryPoint = "cuDriverGetVersion")]
        private static extern int cuDriverGetVersionLinux(out int driverVersion);

        [DllImport("nvcuda.dll", EntryPoint = "cuDeviceGetCount")]
        private static extern int cuDeviceGetCountWindows(out int count);

        [DllImport("libcuda.so", EntryPoint = "cuDeviceGetCount")]
        private static extern int cuDeviceGetCountLinux(out int count);

        [DllImport("nvcuda.dll", EntryPoint = "cuDeviceGetName")]
        private static extern int cuDeviceGetNameWindows(byte[] name, int len, int dev);

        [DllImport("libcuda.so", EntryPoint = "cuDeviceGetName")]
        private static extern int cuDeviceGetNameLinux(byte[] name, int len, int dev);

        // cuDNN
        [DllImport("cudnn64_9.dll", EntryPoint = "cudnnGetVersion", SetLastError = true)]
        private static extern long cudnnGetVersionWindows();

        [DllImport("libcudnn.so", EntryPoint = "cudnnGetVersion", SetLastError = true)]
        private static extern long cudnnGetVersionLinux();

        private static int CuInit(uint flags) => OS.IsWindows() ? cuInitWindows(flags) : cuInitLinux(flags);

        private static int CuDriverGetVersion(out int version) => OS.IsWindows()
            ? cuDriverGetVersionWindows(out version)
            : cuDriverGetVersionLinux(out version);

        private static int CuDeviceGetCount(out int count) => OS.IsWindows()
            ? cuDeviceGetCountWindows(out count)
            : cuDeviceGetCountLinux(out count);

        private static int CuDeviceGetName(byte[] name, int len, int device) => OS.IsWindows()
            ? cuDeviceGetNameWindows(name, len, device)
            : cuDeviceGetNameLinux(name, len, device);

        private static long CuDnnGetVersion() => OS.IsWindows() ? cudnnGetVersionWindows() : cudnnGetVersionLinux();

        public static bool IsCudaAvailable() {
            try {
                int res = CuInit(0);
                Log.Debug($"[CUDA DETECTOR] cuInit -> {res}");
                if (res != 0) return false;

                res = CuDriverGetVersion(out int version);
                Log.Debug($"[CUDA DETECTOR] cuDriverGetVersion -> {res}, version={version}");
                if (res != 0) return false;

                int major = version / 1000;
                int minor = (version % 1000) / 10;
                Log.Information($"[CUDA DETECTOR] Detected CUDA driver version {major}.{minor}");

                return major >= 12;
            } catch (DllNotFoundException ex) {
                Log.Error($"[CUDA DETECTOR] CUDA driver library not found: {ex.Message}");
                return false;
            } catch (Exception ex) {
                Log.Error($"[CUDA DETECTOR] Exception in IsCudaAvailable: {ex}");
                return false;
            }
        }

        public static bool IsCuDnnAvailable() {
            try {
                long version = CuDnnGetVersion();
                int major = (int)(version / 1000);
                int minor = (int)((version % 1000) / 100);
                Log.Information($"[CUDA DETECTOR] cuDNN version {major}.{minor} (raw {version})");

                return major >= 9;
            } catch (DllNotFoundException ex) {
                Log.Error($"[CUDA DETECTOR] cuDNN library not found: {ex.Message}");
                return false;
            } catch (Exception ex) {
                Log.Error($"[CUDA DETECTOR] Exception in IsCuDnnAvailable: {ex}");
                return false;
            }
        }

        public static List<GpuInfo> GetCudaDevices() {
            var list = new List<GpuInfo>();
            try {
                int res = CuDeviceGetCount(out int count);
                Log.Debug($"[CUDA DETECTOR] cuDeviceGetCount -> {res}, count={count}");
                if (res != 0) return list;

                for (int i = 0; i < count; i++) {
                    var nameBytes = new byte[256];
                    res = CuDeviceGetName(nameBytes, nameBytes.Length, i);
                    Log.Debug($"[CUDA DETECTOR] cuDeviceGetName(dev={i}) -> {res}");
                    if (res == 0) {
                        string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                        Log.Debug($"[CUDA DETECTOR] Device {i}: {name}");
                        list.Add(new GpuInfo {
                            deviceId = i,
                            description = name
                        });
                    }
                }
            } catch (Exception ex) {
                Log.Error($"[CUDA DETECTOR] Exception in GetCudaDevices: {ex}");
            }
            return list;
        }
    }
}

