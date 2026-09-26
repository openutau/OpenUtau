namespace OpenUtau.Core.Headless {
    public class RenderJob {
        public string InputPath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public string? Singer { get; set; }
        public string? Renderer { get; set; }
        public string? Phonemizer { get; set; }
        public string? Resampler { get; set; }
        public string? Wavtool { get; set; }
    }

    public class HeadlessOpenUtauOptions {
        public string? SingersPath { get; set; }
        public string? OnnxRunner { get; set; }
        public int? OnnxGpu { get; set; }
        public double? DiffSingerDepth { get; set; }
        public int? DiffSingerSteps { get; set; }
        public int? DiffSingerVarianceSteps { get; set; }
        public int? DiffSingerPitchSteps { get; set; }
        public bool? DiffSingerTensorCache { get; set; }
    }
}
