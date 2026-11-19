using System;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    // Smoke tests for the built-in phonemizers. Each type must be constructible,
    // tolerate a missing/empty singer, and phonemize a dummy note with a dummy
    // singer without throwing.
    //
    // One test case per phonemizer (previously three: CreationTest, SetSingerTest
    // and DummySingerPhonemizeTest were declared on an abstract base class and
    // re-run for every derived class, i.e. 3 x 6 = 18 cases).
    public class PhonemizerTest {
        static USinger GetDummySinger() {
            var voicebank = new Voicebank {
                BasePath = "null",
                File = "null",
                Name = "Dummy",
            };
            var otoSet = new OtoSet {
                File = "null",
                Name = "",
            };
            otoSet.Otos.Add(new Oto {
                Alias = "a",
                Wav = "a.wav",
                Phonetic = "a",
            });
            voicebank.OtoSets.Add(otoSet);
            return new ClassicSinger(voicebank);
        }

        [Theory]
        [InlineData(typeof(DefaultPhonemizer))]
        [InlineData(typeof(ArpasingPhonemizer))]
        [InlineData(typeof(JapaneseCVVCPhonemizer))]
        [InlineData(typeof(JapaneseVCVPhonemizer))]
        [InlineData(typeof(KoreanCVCPhonemizer))]
        [InlineData(typeof(KoreanCVVCPhonemizer))]
        public void SmokeTest(Type phonemizerType) {
            // must be constructible
            var phonemizer = Activator.CreateInstance(phonemizerType) as Phonemizer;
            Assert.NotNull(phonemizer);

            // must tolerate a missing singer and a null singer
            phonemizer.SetSinger(USinger.CreateMissing("Unloaded"));
            phonemizer.SetSinger(null);

            // must phonemize a dummy note with a dummy singer
            phonemizer.SetSinger(GetDummySinger());
            phonemizer.Process(new Phonemizer.Note[] {
                new Phonemizer.Note {
                    lyric = "a",
                    duration = 480,
                    position = 240,
                    tone = 60
                }
            }, null, null, null, null, new Phonemizer.Note[0]);
        }
    }
}
