using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OpenUtau.Core.Analysis;

class SomeConfig {
    public string model = "model.onnx";
    public int sample_rate = 44100;
}

public class SomeOptions {
}

public class Some : MidiExtractor<SomeOptions> {
    private const string PackageId = "some";

    InferenceSession session;
    RunOptions? runOptions;
    SomeConfig config;
    bool disposed = false;

    protected override int ExpectedSampleRate => config.sample_rate;

    /// <summary>
    /// Check if SOME is installed (some.yaml is present) without loading models.
    /// </summary>
    public static bool IsInstalled(string? location = null) {
        location ??= PackageManager.Inst.GetInstalledPath(PackageId);
        return location != null && File.Exists(Path.Combine(location, "some.yaml"));
    }

    public Some() : this(null) { }

    public Some(string? location) {
        location ??= PackageManager.Inst.GetInstalledPath(PackageId)!;
        string yamlpath = Path.Combine(location, "some.yaml");
        if (!File.Exists(yamlpath)) {
            throw new MessageCustomizableException("SOME not found", "<translate:errors.failed.transcribe.some>",
                new FileNotFoundException(), false,
                new string[] { "https://github.com/xunmengshe/OpenUtau/releases/0.0.0.0" });
        }

        config = Yaml.DefaultDeserializer.Deserialize<SomeConfig>(
            File.ReadAllText(yamlpath, System.Text.Encoding.UTF8));
        session = Onnx.getInferenceSession(Path.Combine(location, config.model));
        runOptions = new RunOptions();
    }

    protected override List<TranscribedNote> TranscribeWaveform(float[] samples, SomeOptions options) {
        var inputs = new List<NamedOnnxValue>();
        inputs.Add(NamedOnnxValue.CreateFromTensor("waveform",
            new DenseTensor<float>(samples, new int[] { samples.Length }, false)
                .Reshape(new int[] { 1, samples.Length })));
        try {
            using var outputs = session.Run(inputs, session.OutputNames, runOptions);
            float[] note_midi = outputs
                .Where(o => o.Name == "note_midi")
                .First()
                .AsTensor<float>()
                .ToArray();
            bool[] note_rest = outputs
                .Where(o => o.Name == "note_rest")
                .First()
                .AsTensor<bool>()
                .ToArray();
            float[] note_dur = outputs
                .Where(o => o.Name == "note_dur")
                .First()
                .AsTensor<float>()
                .ToArray();
            var notes = new List<TranscribedNote>(note_midi.Length);
            for (int i = 0; i < note_midi.Length; i++) {
                notes.Add(new TranscribedNote(note_dur[i], note_midi[i], !note_rest[i]));
            }

            return notes;
        } catch (OnnxRuntimeException) {
            if (runOptions != null && runOptions.Terminate) {
                throw new OperationCanceledException();
            }
            throw;
        }
    }

    public override void Interrupt() {
        if (!disposed && runOptions != null) {
            runOptions.Terminate = true;
        }
    }

    protected override void DisposeManaged() {
        if (disposed) return;
        disposed = true;
        runOptions?.Dispose();
        session.Dispose();
    }
}
