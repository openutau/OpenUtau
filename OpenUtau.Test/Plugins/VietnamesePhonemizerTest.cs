using System;
using System.Globalization;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins;

public class VietnamesePhonemizerTest {
    [Theory]
    [InlineData(typeof(VietnameseCVVCPhonemizer), false, 75)]
    [InlineData(typeof(VietnameseCVVCPhonemizer), true, 63)]
    [InlineData(typeof(VietnameseVCVPhonemizer), false, 75)]
    [InlineData(typeof(VietnameseVCVPhonemizer), true, 63)]
    [InlineData(typeof(VietnameseVINAPhonemizer), false, 75)]
    [InlineData(typeof(VietnameseVINAPhonemizer), true, 63)]
    public void LeadingPhonemeUsesResolvedPitch(Type phonemizerType, bool hasPreviousNote, int expectedPitch) {
        var phonemizer = (Phonemizer)Activator.CreateInstance(phonemizerType)!;
        phonemizer.SetSinger(new PitchSinger());
        var timeAxis = new TimeAxis();
        timeAxis.BuildSegments(new UProject());
        phonemizer.SetTiming(timeAxis);

        var note = new Phonemizer.Note {
            lyric = "a",
            tone = 72,
            position = 480,
            duration = 480,
            phonemeAttributes = [new Phonemizer.PhonemeAttributes { index = 0, toneShift = 3 }],
        };
        Phonemizer.Note? previous = hasPreviousNote
            ? new Phonemizer.Note { lyric = "a", tone = 60, position = 0, duration = 480 }
            : null;
        var result = phonemizer.Process([note], previous, null, previous, null,
            previous.HasValue ? [previous.Value] : null);

        Assert.NotEmpty(result.phonemes);
        Assert.Equal("pitch-" + expectedPitch.ToString(CultureInfo.InvariantCulture), result.phonemes[0].phoneme);
    }

    [Theory]
    [InlineData(typeof(VietnameseCVVCPhonemizer))]
    [InlineData(typeof(VietnameseVCVPhonemizer))]
    [InlineData(typeof(VietnameseVINAPhonemizer))]
    public void EndingPhonemeUsesExtendedNotePitch(Type phonemizerType) {
        var phonemizer = (Phonemizer)Activator.CreateInstance(phonemizerType)!;
        phonemizer.SetSinger(new PitchSinger());
        var timeAxis = new TimeAxis();
        timeAxis.BuildSegments(new UProject());
        phonemizer.SetTiming(timeAxis);

        var note = new Phonemizer.Note { lyric = "a", tone = 72, position = 0, duration = 480 };
        var extension = new Phonemizer.Note { lyric = "+", tone = 84, position = 480, duration = 480 };
        var result = phonemizer.Process([note, extension], null, null, null, null, null);

        Assert.NotEmpty(result.phonemes);
        Assert.Equal("pitch-84", result.phonemes[^1].phoneme);
    }

    private sealed class PitchSinger : USinger {
        public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
            var set = new UOtoSet(new OtoSet(), string.Empty);
            oto = new UOto(new Oto { Alias = "pitch-" + tone.ToString(CultureInfo.InvariantCulture) }, set, []);
            return true;
        }
    }
}
