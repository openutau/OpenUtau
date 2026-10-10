using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    public class GpuInfo {
        public int deviceId;
        public string description = "";

        override public string ToString() {
            return $"[{deviceId}] {description}";
        }
    }

    public enum OnnxRunnerChoice {
        Default,
        CPU,
        CPUForCoreML,
    }

    public class Onnx {

        private static bool cudaAvailable = OS.IsLinux() && CudaGpuDetector.IsCudaAvailable() && CudaGpuDetector.IsCuDnnAvailable();

        private static readonly Dictionary<int, OrtEpDevice> devices = initializeDevices();

        private static Dictionary<int, OrtEpDevice> initializeDevices() {
            var env = OrtEnv.Instance();
            var ortDevices = env.GetEpDevices();

            return ortDevices
                .Where(device => device.EpName.ToLower().Contains("dml"))
                .Select((device, index) => new { index, device })
                .ToDictionary(x => x.index, x => x.device);
        }

        public static List<string> getRunnerOptions() {
            if (OS.IsWindows()) {
                return new List<string> {
                "CPU",
                "DirectML"
                };
            } else if (OS.IsMacOS()) {
                return new List<string> {
                "CPU",
                "CoreML"
                };
            } else if (cudaAvailable) {
                return new List<string> {
                "CPU",
                "CUDA"
                };
            } else if (OS.IsAndroid()) {
                return new List<string> {
                "CPU",
                "NNAPI"
                };
            }
            return new List<string> {
                "CPU"        
            };
        }

        public static List<GpuInfo> getGpuInfo() {
            if (cudaAvailable) {
                return CudaGpuDetector.GetCudaDevices();
            }         
     
            if (OS.IsAndroid()) {
                return new List<GpuInfo>{new GpuInfo {
                    deviceId = 0, // eliminate exception of taking OnnxGpuOptions[0]
                }};
            }

            List<GpuInfo> gpuList = new List<GpuInfo>();
            var env = OrtEnv.Instance();
            var ortDevices = env.GetEpDevices();

            var i = 0;
            foreach (var device in ortDevices.Where(device => device.EpName.ToLower().Contains("dml"))) {
                var description = "";
                foreach (var item in device.HardwareDevice.Metadata.Entries) {
                    if (item.Key.ToLower() == "description") {
                        description = $"{item.Value} ({device.HardwareDevice.Type})";
                        break;
                    }
                }
                if (string.IsNullOrEmpty(description)) { // fallback
                    description = $"{device.EpName} {device.HardwareDevice.Vendor} ({device.HardwareDevice.Type})";
                }
                devices[i] = device;
                gpuList.Add(new GpuInfo {
                    deviceId = i++,
                    description = description
                });
            }
            return gpuList;
        }

        /// <summary>The runner the preference resolves to, always one of <see cref="getRunnerOptions"/>.</summary>
        private static string getRunner() {
            List<string> runnerOptions = getRunnerOptions();
            string runner = Preferences.Default.OnnxRunner;
            if (String.IsNullOrEmpty(runner)) {
                runner = runnerOptions[0];
            }
            if (!runnerOptions.Contains(runner)) {
                runner = "CPU";
            }
            return runner;
        }

        /// <summary>Whether a session created now runs on CPU, and so allows concurrent inference calls.</summary>
        public static bool IsCpuRunner() {
            return getRunner() == "CPU";
        }

        /// <summary>Serializes DirectML session creation, inference and disposal. The Windows
        /// DirectML build of ONNX Runtime kills the process natively when those overlap, and the
        /// package is frozen at 1.24.4, so the ORT-side fixes will never ship as an upgrade.</summary>
        public static readonly object DmlLock = new object();

        /// <summary>Whether sessions created now run on the DirectML execution provider.</summary>
        public static bool IsDmlRunner() {
            return getRunner() == "DirectML";
        }

        private readonly struct DmlScope : IDisposable {
            private readonly bool engaged;
            public DmlScope(bool engaged) { this.engaged = engaged; }
            public void Dispose() {
                if (engaged) {
                    System.Threading.Monitor.Exit(DmlLock);
                }
            }
        }

        private static readonly DmlScope NoDmlScope = new DmlScope(false);

        /// <summary>Set when DirectML is or was in use in this process. Sessions are cached and
        /// stay on DirectML even after the preference moves to CPU, so the lock has to stay
        /// engaged for them; a process that never used DirectML keeps the no-op scope.</summary>
        private static volatile bool dmlInUse;

        /// <summary>Takes <see cref="DmlLock"/> while DirectML session creation, Run or disposal
        /// can happen, which is while the DirectML runner is selected or once a DirectML session
        /// exists. DirectML session creation, Run and disposal must stay inside it.</summary>
        public static IDisposable EnterDmlScope() {
            // Lock first, then re-check the runner: a CPU->DirectML switch can land while this
            // thread is waiting on the lock, and a scope handed out as a no-op cannot be upgraded
            // afterwards, which would leave the following DirectML work outside the lock.
            System.Threading.Monitor.Enter(DmlLock);
            try {
                if (!IsDmlRunner() && !dmlInUse) {
                    System.Threading.Monitor.Exit(DmlLock);
                    return NoDmlScope;
                }
                dmlInUse = true;
                return new DmlScope(true);
            } catch {
                System.Threading.Monitor.Exit(DmlLock);
                throw;
            }
        }

        /// <summary>Creates a session with the selected execution provider. A DirectML failure
        /// falls back to CPU: the DML graph compiler rejects some models it cannot run.</summary>
        private static InferenceSession createSession(Func<InferenceSession> withProvider, Func<InferenceSession> cpu) {
            if (!IsDmlRunner()) {
                return withProvider();
            }
            // Before creating: whatever later runs on that session must keep the lock even if the
            // preference switches to CPU while the session is cached.
            dmlInUse = true;
            lock (DmlLock) {
                try {
                    return withProvider();
                } catch (Exception e) {
                    Log.Warning(e, "Failed to create a DirectML inference session, falling back to CPU.");
                    return cpu();
                }
            }
        }

        private static SessionOptions getOnnxSessionOptions(bool coremlEnableOnSubgraphs = false) {
            SessionOptions options = new SessionOptions();
            string runner = getRunner();
            switch (runner) {
                case "DirectML":
                    var d = devices[Preferences.Default.OnnxGpu];
                    options.AppendExecutionProvider(
                        OrtEnv.Instance(),
                        new List<OrtEpDevice> { d },
                        new Dictionary<string, string> { }
                     );
                    break;
                case "CoreML":
                    // Note: MLProgram format has stricter validation and may fail with complex DiffSinger models
                    // that have topological sorting issues (e.g., variance_predictor with diffusion embeddings)
                    // so we always use NeuralNetwork format (default) as MLProgram fails with complex models.
                    options.AppendExecutionProvider("CoreML", new Dictionary<string, string> {
                        { "MLComputeUnits", "ALL" },
                        { "RequireStaticInputShapes", "1"},
                        { "ModelFormat", "NeuralNetwork"},
                        { "EnableOnSubgraphs", coremlEnableOnSubgraphs ? "1" : "0" }  // Disable subgraph processing to avoid complex control flow issues
                    });
                    break;
                case "CUDA":
                    options.AppendExecutionProvider_CUDA(Preferences.Default.OnnxGpu);
                    break;
                case "NNAPI":
                    options.AppendExecutionProvider_Nnapi();
                    break;
            }
            return options;
        }

        public static InferenceSession getInferenceSession(byte[] model, OnnxRunnerChoice runnerChoice = OnnxRunnerChoice.Default) {
            if (runnerChoice == OnnxRunnerChoice.CPU ||
                (runnerChoice == OnnxRunnerChoice.CPUForCoreML && Preferences.Default.OnnxRunner == "CoreML")) {
                return new InferenceSession(model);
            } else {
                // Try with CoreML subgraphs enabled first, fallback to default if it fails
                if (OS.IsMacOS() && Preferences.Default.OnnxRunner == "CoreML") {
                    try {
                        using var subgraphOptions = getOnnxSessionOptions(coremlEnableOnSubgraphs: true);
                        return new InferenceSession(model, subgraphOptions);
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to create session with CoreML subgraphs enabled, falling back to default settings");
                    }
                }
                // The usings hold the SessionOptions alive across the native session creation: the
                // binding passes options.Handle as a raw pointer and never looks at options again,
                // so a GC could otherwise finalize the managed options and free the native ones
                // while the native call is still running.
                return createSession(
                    () => {
                        using var options = getOnnxSessionOptions();
                        return new InferenceSession(model, options);
                    },
                    () => new InferenceSession(model));
            }
        }

        public static InferenceSession getInferenceSession(string modelPath, OnnxRunnerChoice runnerChoice = OnnxRunnerChoice.Default) {
            if (runnerChoice == OnnxRunnerChoice.CPU ||
                (runnerChoice == OnnxRunnerChoice.CPUForCoreML && Preferences.Default.OnnxRunner == "CoreML")) {
                return new InferenceSession(modelPath);
            } else {
                // Try with CoreML subgraphs enabled first, fallback to default if it fails
                if (OS.IsMacOS() && Preferences.Default.OnnxRunner == "CoreML") {
                    try {
                        using var subgraphOptions = getOnnxSessionOptions(coremlEnableOnSubgraphs: true);
                        return new InferenceSession(modelPath, subgraphOptions);
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to create session with CoreML subgraphs enabled, falling back to default settings");
                    }
                }
                return createSession(
                    () => {
                        using var options = getOnnxSessionOptions();
                        return new InferenceSession(modelPath, options);
                    },
                    () => new InferenceSession(modelPath));
            }
        }

        public static void VerifyInputNames(InferenceSession session, IEnumerable<NamedOnnxValue> inputs) {
            var sessionInputNames = session.InputNames.ToHashSet();
            var givenInputNames = inputs.Select(v => v.Name).ToHashSet();
            var missing = sessionInputNames
                .Except(givenInputNames)
                .OrderBy(s => s, StringComparer.InvariantCulture)
                .ToArray();
            if (missing.Length > 0) {
                throw new ArgumentException("Missing input(s) for the inference session: " + string.Join(", ", missing));
            }
            var unexpected = givenInputNames
                .Except(sessionInputNames)
                .OrderBy(s => s, StringComparer.InvariantCulture)
                .ToArray();
            if (unexpected.Length > 0) {
                throw new ArgumentException("Unexpected input(s) for the inference session: " + string.Join(", ", unexpected));
            }
        }
    }
}
