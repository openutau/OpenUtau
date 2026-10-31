using System.Collections.Generic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    public class PreservedRangeSerializationTest {
        [Fact]
        public void PreservedRangesSurviveYamlRoundTrip() {
            var project = new UProject();
            var part = new UVoicePart { trackNo = 0, position = 0 };
            part.preservedRanges = new List<UTickRange> { new UTickRange(120, 480) };
            project.parts.Add(part);

            project.BeforeSave();
            string yaml = Yaml.DefaultSerializer.Serialize(project);
            var loaded = Yaml.DefaultDeserializer.Deserialize<UProject>(yaml);
            loaded.AfterLoad();
            var loadedPart = Assert.IsType<UVoicePart>(Assert.Single(loaded.parts));

            Assert.NotNull(loadedPart.preservedRanges);
            var range = Assert.Single(loadedPart.preservedRanges!);
            Assert.Equal(120, range.start);
            Assert.Equal(480, range.end);
        }

        [Fact]
        public void PartsWithoutPreservedRangesDoNotGainTheField() {
            var project = new UProject();
            project.parts.Add(new UVoicePart { trackNo = 0, position = 0 });

            project.BeforeSave();
            string yaml = Yaml.DefaultSerializer.Serialize(project);

            Assert.DoesNotContain("preserved_ranges", yaml);
        }
    }
}
