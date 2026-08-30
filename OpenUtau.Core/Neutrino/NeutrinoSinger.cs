using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Neutrino {
    public class NeutrinoSinger : USinger {
        public override string Id => voicebank.Id;
        public override string Name => voicebank.Name;
        public override Dictionary<string, string> LocalizedNames => voicebank.LocalizedNames;
        public override USingerType SingerType => USingerType.Neutrino;
        public override string BasePath => voicebank.BasePath;
        public override string Author => voicebank.Author;
        public override string Voice => voicebank.Voice;
        public override string Location => Path.GetDirectoryName(voicebank.File);
        public override string Web => voicebank.Web;
        public override string Version => voicebank.Version;
        public override string OtherInfo => voicebank.OtherInfo;
        public override IList<string> Errors => errors;
        public override string Avatar => voicebank.Image == null ? null : Path.Combine(Location, voicebank.Image);
        public override byte[] AvatarData => avatarData;
        public override string Portrait => voicebank.Portrait == null ? null : Path.Combine(Location, voicebank.Portrait);
        public override float PortraitOpacity => voicebank.PortraitOpacity;
        public override int PortraitHeight => voicebank.PortraitHeight;
        public override string Sample => voicebank.Sample == null ? null : Path.Combine(Location, voicebank.Sample);
        public override string DefaultPhonemizer =>
            voicebank.DefaultPhonemizer ?? "OpenUtau.Core.Neutrino.NeutrinoPhonemizer";
        public override Encoding TextFileEncoding => voicebank.TextFileEncoding;
        public override IList<USubbank> Subbanks => subbanks;
        public override IList<UOto> Otos => otos;

        readonly object sessionLock = new object();
        readonly List<string> errors = new List<string>();
        readonly List<USubbank> subbanks = new List<USubbank>();
        readonly List<UOto> otos = new List<UOto>();
        readonly Dictionary<string, UOto> otoMap = new Dictionary<string, UOto>();

        Voicebank voicebank;
        byte[] avatarData;
        InferenceSession timingSession;
        InferenceSession pitchSession;
        InferenceSession melspecSession;
        InferenceSession vocoderSession;
        string timingModelPath = string.Empty;
        string pitchModelPath = string.Empty;
        string melspecModelPath = string.Empty;
        string vocoderModelPath = string.Empty;

        public NeutrinoSinger(Voicebank voicebank) {
            this.voicebank = voicebank;
            found = true;
        }

        public override void EnsureLoaded() {
            if (!Loaded) {
                Reload();
            }
        }

        public override void Reload() {
            if (!Found) {
                return;
            }
            lock (sessionLock) {
                loaded = false;
                try {
                    voicebank.Reload();
                    Load();
                    loaded = true;
                } catch (Exception e) {
                    Log.Error(e, "Failed to load NEUTRINO singer {SingerPath}", voicebank.File);
                }
            }
        }

        void Load() {
            FreeSessions();
            errors.Clear();
            subbanks.Clear();
            otos.Clear();
            otoMap.Clear();

            string modelDirectory = ResolveModelDirectory();
            if (!IsV3ModelDirectory(modelDirectory)) {
                errors.Add($"NEUTRINO v3 model files were not found in {modelDirectory}");
            }

            subbanks.Add(new USubbank(new Subbank() {
                Prefix = string.Empty,
                Suffix = string.Empty,
                ToneRanges = new[] { "C1-B7" },
            }));
            foreach (string phoneme in NeutrinoPhoneme.AllPhonemes) {
                var oto = UOto.OfDummy(phoneme);
                if (otoMap.TryAdd(oto.Alias, oto)) {
                    otos.Add(oto);
                }
            }

            avatarData = null;
            if (Avatar != null && File.Exists(Avatar)) {
                try {
                    avatarData = File.ReadAllBytes(Avatar);
                } catch (Exception e) {
                    Log.Error(e, "Failed to load NEUTRINO avatar");
                }
            }
        }

        public void EnsureSessions() {
            if (timingSession != null
                && pitchSession != null
                && melspecSession != null
                && vocoderSession != null) {
                return;
            }
            lock (sessionLock) {
                EnsureModelPaths();
                timingSession ??= LoadSession(timingModelPath, OnnxRunnerChoice.Default);
                pitchSession ??= LoadSession(pitchModelPath, OnnxRunnerChoice.Default);
                melspecSession ??= LoadSession(melspecModelPath, OnnxRunnerChoice.Default);
                vocoderSession ??= LoadSession(vocoderModelPath, OnnxRunnerChoice.Default);
            }
        }

        public void EnsureTimingSession() {
            if (timingSession != null) {
                return;
            }
            lock (sessionLock) {
                EnsureModelPaths();
                timingSession ??= LoadSession(timingModelPath, OnnxRunnerChoice.Default);
            }
        }

        public void EnsurePitchSession() {
            if (pitchSession != null) {
                return;
            }
            lock (sessionLock) {
                EnsureModelPaths();
                pitchSession ??= LoadSession(pitchModelPath, OnnxRunnerChoice.Default);
            }
        }

        public void EnsureMelspecSession() {
            if (melspecSession != null) {
                return;
            }
            lock (sessionLock) {
                EnsureModelPaths();
                melspecSession ??= LoadSession(melspecModelPath, OnnxRunnerChoice.Default);
            }
        }

        public void EnsureVocoderSession() {
            if (vocoderSession != null) {
                return;
            }
            lock (sessionLock) {
                EnsureModelPaths();
                vocoderSession ??= LoadSession(vocoderModelPath, OnnxRunnerChoice.Default);
            }
        }

        void EnsureModelPaths() {
            if (!string.IsNullOrEmpty(timingModelPath)) {
                return;
            }
            string modelDirectory = ResolveModelDirectory();
            timingModelPath = RequireModel(modelDirectory, "t.bin");
            pitchModelPath = RequireModel(modelDirectory, "p.bin");
            melspecModelPath = RequireModel(modelDirectory, "s.bin");
            vocoderModelPath = RequireModel(modelDirectory, "v.bin");
        }

        string ResolveModelDirectory() {
            string nested = Path.Combine(Location, "model");
            if (IsV3ModelDirectory(nested)) {
                return nested;
            }
            if (IsV3ModelDirectory(Location)) {
                return Location;
            }
            return Directory.Exists(nested) ? nested : Location;
        }

        internal static bool IsV3ModelDirectory(string directory) {
            return !string.IsNullOrEmpty(directory)
                && File.Exists(Path.Combine(directory, "t.bin"))
                && File.Exists(Path.Combine(directory, "p.bin"))
                && File.Exists(Path.Combine(directory, "s.bin"))
                && File.Exists(Path.Combine(directory, "v.bin"));
        }

        static string RequireModel(string directory, string fileName) {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) {
                throw new FileNotFoundException($"NEUTRINO v3 model was not found: {path}", path);
            }
            return path;
        }

        static InferenceSession LoadSession(string path, OnnxRunnerChoice runnerChoice) {
            return Onnx.getInferenceSession(path, runnerChoice);
        }

        public float[] RunTiming(IReadOnlyCollection<NamedOnnxValue> inputs) {
            lock (sessionLock) {
                EnsureTimingSession();
                return RunWithCpuFallback(ref timingSession, timingModelPath, inputs, "gluon", "timing");
            }
        }

        public float[] RunPitch(IReadOnlyCollection<NamedOnnxValue> inputs) {
            lock (sessionLock) {
                EnsurePitchSession();
                return RunWithCpuFallback(ref pitchSession, pitchModelPath, inputs, "photon", "pitch");
            }
        }

        public float[] RunMelspec(IReadOnlyCollection<NamedOnnxValue> inputs) {
            lock (sessionLock) {
                EnsureMelspecSession();
                return RunWithCpuFallback(ref melspecSession, melspecModelPath, inputs, "higgs", "melspec");
            }
        }

        public float[] RunVocoder(IReadOnlyCollection<NamedOnnxValue> inputs) {
            lock (sessionLock) {
                EnsureVocoderSession();
                return RunWithCpuFallback(ref vocoderSession, vocoderModelPath, inputs, "output", "vocoder");
            }
        }

        float[] RunWithCpuFallback(
            ref InferenceSession session,
            string path,
            IReadOnlyCollection<NamedOnnxValue> inputs,
            string outputName,
            string modelName) {

            lock (sessionLock) {
                try {
                    return RunOutput(session, inputs, outputName);
                } catch (OnnxRuntimeException e) when (Preferences.Default.OnnxRunner == "DirectML") {
                    Log.Warning(e, "NEUTRINO {ModelName} failed on DirectML; retrying on CPU", modelName);
                    session?.Dispose();
                    session = LoadSession(path, OnnxRunnerChoice.CPU);
                    return RunOutput(session, inputs, outputName);
                }
            }
        }

        static float[] RunOutput(
            InferenceSession session,
            IReadOnlyCollection<NamedOnnxValue> inputs,
            string outputName) {

            using var outputs = session.Run(inputs, new[] { outputName });
            return outputs.Single().AsTensor<float>().ToArray();
        }

        public void FreeSessions() {
            lock (sessionLock) {
                timingSession?.Dispose();
                pitchSession?.Dispose();
                melspecSession?.Dispose();
                vocoderSession?.Dispose();
                timingSession = null;
                pitchSession = null;
                melspecSession = null;
                vocoderSession = null;
                timingModelPath = string.Empty;
                pitchModelPath = string.Empty;
                melspecModelPath = string.Empty;
                vocoderModelPath = string.Empty;
            }
        }

        public override void FreeMemory() {
            FreeSessions();
        }

        public override bool TryGetOto(string phoneme, out UOto oto) {
            oto = UOto.OfDummy(phoneme);
            return true;
        }

        public override IEnumerable<UOto> GetSuggestions(string text) {
            if (text != null) {
                text = text.Replace(" ", "");
            }
            bool all = string.IsNullOrEmpty(text);
            return otos.Where(oto => all || oto.Alias.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        public override byte[] LoadPortrait() {
            return string.IsNullOrEmpty(Portrait) ? null : File.ReadAllBytes(Portrait);
        }

        public override byte[] LoadSample() {
            return string.IsNullOrEmpty(Sample) ? null : File.ReadAllBytes(Sample);
        }
    }
}
