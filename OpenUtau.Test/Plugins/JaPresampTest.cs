using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;
using Xunit;
using Xunit.Abstractions;

namespace OpenUtau.Plugins {
    public class JaPresampTest : PhonemizerTestBase {
        public JaPresampTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() => new JapanesePresampPhonemizer();

        private NoteParams Note(string lyric, string color = "", string tone = "C4", string hint = "") {
            return new NoteParams {
                lyric = lyric,
                hint = hint,
                tone = tone,
                phonemes = SamePhonemeParams(1, 0, 0, color)
            };
        }

        // General
        [Fact]
        [Trait("Category", "General")]
        public void JaPlusMinusTest() {
            RunPhonemizeTest("ja_vcv_integration",
                [Note("あ"), Note("+"), Note("あ"), Note("+~"), Note("R")],
                ["- あ", "a あ", "a R"]);
        }
        [Fact]
        [Trait("Category", "General")]
        public void JaUnicordTest() { // が, が, ヴ, ヴ
            RunPhonemizeTest("ja_vcv_integration",
                [Note("\u304c"), Note("\u304b\u3099"), Note("\u30f4"), Note("\u30a6\u3099")],
                ["- が", "a が", "a ヴ", "u ヴ"]);
        }
        [Fact]
        [Trait("Category", "General")]
        public void JaPriorityTest() { // [PRIORITY] p
            RunPhonemizeTest("ja_cvvc_integration",
                [Note("ri"), Note("p"), Note("re"), Note("i"), Note("s")],
                ["り", "i p", "p", "れ", "e い", "i s"]);
        }
        [Fact]
        [Trait("Category", "General")]
        public void PhoneticHintTest() {
            RunPhonemizeTest("ja_vcv_integration",
                [Note("あ", "cvvc"), Note("", "cvvc", "C4", "か"), Note("さ"), Note("し", "vcv", "C4", "- た"), Note("な", "vcv"), Note("R")],
                ["- あ_CVVC_C4", "か_CVVC_C4", "a さ", "- た_VCV_D4", "a な_VCV_D4", "a R"]);
        }
        [Fact]
        [Trait("Category", "General")]
        public void NonExistentAliasTest() {
            RunPhonemizeTest("ja_vcv_integration",
                [Note("あ", "cvvc"), Note("しぁ", "cvvc"), Note("さ"), Note("ぬぃ", "vcv"), Note("な", "vcv")],
                ["- あ_CVVC_C4", "しぁ", "a さ", "a ぬぃ_VCV_D4", "i な_VCV_D4"]);
        }
        [Fact]
        [Trait("Category", "General")]
        public void ToneShiftAltTest() {
            RunPhonemizeTest("ja_vcv_integration", [
                new NoteParams { lyric = "あ", hint = "", tone = "G4",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            color = "vcv",
                            shift = - 1,
                            alt = 0
                        }
                    }
                },
                new NoteParams { lyric = "か", hint = "", tone = "G4",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            color = "vcv",
                            shift = - 1,
                            alt = 0
                        }
                    }
                },
                Note("さ", "vcv", "G4"),
                new NoteParams { lyric = "た", hint = "", tone = "G4",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            color = "vcv",
                            shift = 0,
                            alt = 2 // fallback
                        }
                    }
                },
                new NoteParams {
                    lyric = "ん", hint = "", tone = "G4",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            color = "vcv",
                            shift = 0,
                            alt = 2
                        }
                    }
                }],
                ["- あ_VCV_D4", "a か_VCV_D4", "a さ_VCV_G4", "a た_VCV_G4", "a ん2_VCV_G4"]);
        }

        // CV
        [Fact]
        [Trait("Category", "CV")]
        public void CvTest() {
            RunPhonemizeTest("ja_cv_integration",
                [Note("あ"), Note("か"), Note("さ"), Note("た"), Note("な")],
                ["あ", "か", "さ", "た", "な"]);
        }
        [Fact]
        [Trait("Category", "CV")]
        public void CvColorTest() {
            RunPhonemizeTest("ja_cv_integration",
                [Note("あ"), Note("か"), Note("さ", "cv"), Note("た", "cv", "G4"), Note("な", "cv", "G4")],
                ["あ", "か", "さ_CV_C4", "た_CV_G4", "な_CV_G4"]);
        }
        [Fact]
        [Trait("Category", "CV")]
        public void CvIntegrationTest() {
            RunPhonemizeTest("ja_cv_integration",
                [Note("あ"), Note("か", "cvvc"), Note("さ", "cvvc"), Note("た", "vcv"), Note("な", "cv"), Note("R", "cv")],
                ["あ", "か_CVVC_C4", "a s_CVVC_C4", "さ_CVVC_C4", "a た_VCV_D4", "な_CV_C4", "R"]);
        }
        [Fact]
        [Trait("Category", "CV")]
        public void CvGlottalTest() {
            RunPhonemizeTest("ja_cv_integration",
                [Note("あ"), Note("あ・"), Note("か", "cvvc"), Note("あ・", "cvvc"), Note("か", "cvvc"), Note("あ・", "vcv"), Note("た", "vcv"), Note("あ・", "vcv"), Note("R", "vcv")],
                ["あ", "あ", "か_CVVC_C4", "a ・_CVVC_C4", "・ あ_CVVC_C4", "a k_CVVC_C4", "か_CVVC_C4", "a あ・_VCV_D4", "a た_VCV_D4", "a あ・_VCV_D4", "a R_VCV_D4"]);
        }

        // CVVC
        [Fact]
        [Trait("Category", "CVVC")]
        public void CvvcTest() {
            RunPhonemizeTest("ja_cvvc_integration",
                [Note("あ"), Note("あ"), Note("っ"), Note("か"), Note("さ"), Note("a t"), Note("た"), Note("な")],
                ["- あ", "a あ", "a k", "か", "a s", "さ", "a t", "た", "a n", "な"]);
        }
        [Fact]
        [Trait("Category", "CVVC")]
        public void CvvcPreCTest() {
            RunPhonemizeTest("ja_cvvc_integration",
                [Note("さ"), Note("さ")],
                ["- s", "さ", "a s", "さ"]);
        }
        [Fact]
        [Trait("Category", "CVVC")]
        public void CvvcColorTest() {
            RunPhonemizeTest("ja_cvvc_integration",
                [Note("あ"),
                new NoteParams { lyric = "か", hint = "", tone = "C4",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            color = "",
                            shift = 0,
                            alt = 0
                        },
                        new PhonemeParams {
                            color = "cvvc",
                            shift = 0,
                            alt = 0
                        }
                    }
                },
                Note("さ", "cvvc", "F4"),
                new NoteParams { lyric = "た", hint = "", tone = "C4",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            color = "cvvc",
                            shift = 0,
                            alt = 0
                        },
                        new PhonemeParams {
                            color = "",
                            shift = 0,
                            alt = 0
                        }
                    }
                },
                Note("な", "cvvc")],
            ["- あ", "a k", "か", "a s_CVVC_C4", "さ_CVVC_F4", "a t_CVVC_F4", "た_CVVC_C4", "a n", "な_CVVC_C4"]);
        }
        [Fact]
        [Trait("Category", "CVVC")]
        public void CvvcIntegrationTest() {
            RunPhonemizeTest("ja_cvvc_integration",
                [Note("あ"), Note("か", "cvvc"), Note("さ", "cvvc"), Note("た", "vcv"), Note("な", "cv"), Note("R")],
                ["- あ", "a k", "か_CVVC_C4", "a s_CVVC_C4", "さ_CVVC_C4", "a た_VCV_D4", "な_CV_C4", "a R"]);
        }
        [Fact]
        [Trait("Category", "CVVC")]
        public void CvvcDirectSpecification() {
            RunPhonemizeTest("ja_cvvc_integration",
                [Note("あ"), Note("か"), Note("n s"), Note("さ"), Note("n t"), Note("た", "cvvc"), Note("な", "cvvc"), Note("i R")],
                ["- あ", "a k", "か", "n s", "さ", "n t", "た_CVVC_C4", "a n_CVVC_C4", "な_CVVC_C4", "i R"]);
        }
        [Fact]
        [Trait("Category", "CVVC")]
        public void CvvcGlottalTest() {
            RunPhonemizeTest("ja_cvvc_integration",
                [Note("あ"), Note("あ・"), Note("あ"), Note("・"), Note("あ"), Note("か", "cvvc"), Note("あ・", "cvvc"), Note("か", "cvvc"), Note("あ・", "vcv"), Note("た", "vcv"), Note("あ・", "vcv"), Note("R", "vcv")],
                ["- あ", "a ・", "・ あ", "a あ", "a ・", "・ あ", "a k", "か_CVVC_C4", "a ・_CVVC_C4", "・ あ_CVVC_C4", "a k_CVVC_C4", "か_CVVC_C4", "a あ・_VCV_D4", "a た_VCV_D4", "a あ・_VCV_D4", "a R_VCV_D4"]);
        }

        // VCV
        [Fact]
        [Trait("Category", "VCV")]
        public void VcvTest() {
            RunPhonemizeTest("ja_vcv_integration",
                [Note("あ"), Note("あ"), Note("っ"), Note("か"), Note("さ"), Note("た"), Note("R")],
                ["- あ", "a あ", "っ", "- か", "a さ", "a た", "a R"]);
        }
        [Fact]
        [Trait("Category", "VCV")]
        public void VcvColorTest() {
            RunPhonemizeTest("ja_vcv_integration",
                [Note("あ"),
                new NoteParams { lyric = "か", hint = "", tone = "C4",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            color = "vcv",
                            shift = 0,
                            alt = 0
                        },
                        new PhonemeParams { // Second phoneme params are ignored here
                            color = "cvvc",
                            shift = 0,
                            alt = 0
                        }
                    }
                },
                Note("さ", "vcv", "F4"),
                new NoteParams { lyric = "た", hint = "", tone = "C4",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            color = "",
                            shift = 0,
                            alt = 0
                        },
                        new PhonemeParams {
                            color = "cvvc",
                            shift = 0,
                            alt = 0
                        }
                    }
                },
                Note("な", "vcv")],
                ["- あ", "a か_VCV_D4", "a さ_VCV_D4", "a た", "a な_VCV_D4"]);
        }
        [Fact]
        [Trait("Category", "VCV")]
        public void VcvIntegrationTest() {
            RunPhonemizeTest("ja_vcv_integration",
                [Note("あ"), Note("か", "cvvc"), Note("さ"), Note("た", "vcv"), Note("な", "cv"), Note("R")],
                ["- あ", "か_CVVC_C4", "a さ", "a た_VCV_D4", "な_CV_C4", "a R"]);
        }
        [Fact]
        [Trait("Category", "VCV")]
        public void VcvDirectSpecification() {
            RunPhonemizeTest("ja_vcv_integration",
                [Note("あ"), Note("i か"), Note("i さ"), Note("i た"), Note("- な"), Note("u R")],
                ["- あ", "i か", "i さ", "i た", "- な", "u R"]);
        }
        [Fact]
        [Trait("Category", "VCV")]
        public void VcvGlottalTest() {
            RunPhonemizeTest("ja_vcv_integration",
                [Note("あ"), Note("あ・"), Note("か", "cvvc"), Note("あ・", "cvvc"), Note("た", "vcv"), Note("あ・", "vcv"), Note("R")],
                ["- あ", "a あ・", "か_CVVC_C4", "a ・_CVVC_C4", "・ あ_CVVC_C4", "a た_VCV_D4", "a あ・_VCV_D4", "a R"]);
        }

        // X-SAMPA
        [Fact]
        [Trait("Category", "X-SAMPA")]
        public void CvvcXsampaTest() {
            RunPhonemizeTest("ja_presamp",
                [Note("あ"), Note("あ"), Note("っ"), Note("ひゃ"), Note("さ"), Note("ちゃ"), Note("にゃ")],
                ["- あ_XS", "a あ_XS", "a C_XS", "ひゃ_XS", "a s_XS", "さ_XS", "a tS_XS", "ちゃ_XS", "a J_XS", "にゃ_XS"]);
        }
    }
}
