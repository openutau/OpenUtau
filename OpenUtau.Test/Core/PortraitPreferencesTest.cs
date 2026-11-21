using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    public class PortraitPreferencesTest {
        [Fact]
        public void OldAndCustomizedPortraitPreferencesRoundTrip() {
            var defaults = Json.Deserialize<Preferences.SerializablePreferences>("{}");
            Assert.True(defaults.AdvancedPortraitSettings && defaults.UseVoiceColorPortraits && defaults.DimPortraitDuringSilence);
            Assert.Equal(225, defaults.PortraitSilenceMs);
            Assert.Equal(150, defaults.PortraitFadeMs);
            defaults.AdvancedPortraitSettings = defaults.UseVoiceColorPortraits = defaults.DimPortraitDuringSilence = false;
            defaults.PortraitFadeMs = 340;
            defaults.PortraitSilenceMs = 735;
            var loaded = Json.Deserialize<Preferences.SerializablePreferences>(Json.Serialize(defaults));
            Assert.False(loaded.AdvancedPortraitSettings);
            Assert.False(loaded.UseVoiceColorPortraits);
            Assert.False(loaded.DimPortraitDuringSilence);
            Assert.Equal(735, loaded.PortraitSilenceMs);
            Assert.Equal(340, loaded.PortraitFadeMs);
        }
    }
}
