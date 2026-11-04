using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OpenUtau.Core.Util {
    internal static partial class WindowsGpuDetector {
        public static unsafe bool IsIndirectDisplayAdapter(IReadOnlyDictionary<string, string> metadata) {
            if (!OS.IsWindows() ||
                !metadata.TryGetValue("LUID", out var luidText) ||
                !ulong.TryParse(luidText, out var luid)) {
                return false;
            }

            var adapter = new OpenAdapterFromLuid {
                adapterIdLow = (uint)luid,
                adapterIdHigh = (int)(luid >> 32),
            };
            if (D3DKMTOpenAdapterFromLuid(ref adapter) < 0) {
                return false;
            }
            try {
                uint adapterType = 0;
                var query = new QueryAdapterInfo {
                    adapterHandle = adapter.adapterHandle,
                    type = 15,
                    data = &adapterType,
                    size = sizeof(uint),
                };
                return D3DKMTQueryAdapterInfo(ref query) >= 0 && IsIndirectDisplayAdapter(adapterType);
            } finally {
                D3DKMTCloseAdapter(ref adapter.adapterHandle);
            }
        }

        internal static bool IsIndirectDisplayAdapter(uint adapterType) {
            const uint indirectDisplayDevice = 1u << 6;
            return (adapterType & indirectDisplayDevice) != 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OpenAdapterFromLuid {
            public uint adapterIdLow;
            public int adapterIdHigh;
            public uint adapterHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct QueryAdapterInfo {
            public uint adapterHandle;
            public uint type;
            public void* data;
            public uint size;
        }

        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid request);

        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo request);

        [LibraryImport("gdi32.dll")]
        private static partial int D3DKMTCloseAdapter(ref uint adapterHandle);
    }
}
