using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.App.ViewModels;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.App {
    public class SingerFlyoutOrderTest {
        class TestSinger : USinger {
            private readonly string id;
            private readonly USingerType type;
            public TestSinger(string id, USingerType type) {
                this.id = id;
                this.type = type;
                found = true;
            }
            public override string Id => id;
            public override string Name => id;
            public override USingerType SingerType => type;
        }

        static List<List<string>> Sections(IEnumerable<string> recents, IEnumerable<string> favorites) {
            var singers = new USinger[] {
                new TestSinger("c-classic", USingerType.Classic),
                new TestSinger("a-classic", USingerType.Classic),
                new TestSinger("b-classic", USingerType.Classic),
                new TestSinger("z-enunu", USingerType.Enunu),
                new TestSinger("y-diffsinger", USingerType.DiffSinger),
            };
            var byId = singers.ToDictionary(s => s.Id);
            var groups = singers
                .GroupBy(s => s.SingerType)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Name).ToList());
            return SingerFlyoutViewModel.OrderSingers(byId, groups, recents, favorites)
                .Select(section => section.Select(s => s.Id).ToList())
                .ToList();
        }

        static List<string> Order(IEnumerable<string> recents, IEnumerable<string> favorites) {
            return Sections(recents, favorites).SelectMany(section => section).ToList();
        }

        [Fact]
        public void FavoritesThenRecentsThenGroups() {
            var order = Order(
                new[] { "b-classic", "missing", "y-diffsinger" },
                new[] { "z-enunu", "c-classic", "y-diffsinger" });
            Assert.Equal(new[] {
                // Recent favorites in recent order.
                "y-diffsinger",
                // Remaining favorites alphabetically.
                "c-classic", "z-enunu",
                // Recent non-favorites, unknown ids skipped.
                "b-classic",
                // Everything else by group name, then singer name.
                "a-classic",
            }, order);
        }

        [Fact]
        public void NoNonFavoriteBeforeFavorite() {
            var favorites = new[] { "y-diffsinger", "a-classic" };
            // Recent non-favorites must still come after every favorite.
            var order = Order(new[] { "z-enunu", "b-classic", "a-classic" }, favorites);
            int lastFavorite = order.FindLastIndex(favorites.Contains);
            int firstOther = order.FindIndex(id => !favorites.Contains(id));
            Assert.True(lastFavorite < firstOther);
            Assert.Equal(new[] { "a-classic", "y-diffsinger", "z-enunu", "b-classic", "c-classic" }, order);
        }

        [Fact]
        public void GroupsFollowGroupNameOrder() {
            var order = Order(new string[0], new string[0]);
            // Classic, DiffSinger, Enunu: by name, not by USingerType value.
            Assert.Equal(new[] { "a-classic", "b-classic", "c-classic", "y-diffsinger", "z-enunu" }, order);
        }

        [Fact]
        public void SplitsFavoritesRecentsAndRest() {
            var sections = Sections(new[] { "b-classic", "y-diffsinger" }, new[] { "y-diffsinger" });
            Assert.Equal(3, sections.Count);
            Assert.Equal(new[] { "y-diffsinger" }, sections[0]);
            Assert.Equal(new[] { "b-classic" }, sections[1]);
            Assert.Equal(new[] { "a-classic", "c-classic", "z-enunu" }, sections[2]);
        }

        [Fact]
        public void EmptySectionsAreKept() {
            var sections = Sections(new string[0], new string[0]);
            Assert.Equal(3, sections.Count);
            Assert.Empty(sections[0]);
            Assert.Empty(sections[1]);
            Assert.Equal(5, sections[2].Count);
        }

        [Theory]
        [InlineData("Kasane Teto", "", true)]
        [InlineData("Kasane Teto", "teto", true)]
        [InlineData("Kasane Teto", "TETO", true)]
        [InlineData("Kasane Teto", "miku", false)]
        // Hiragana matches katakana, and half width matches full width.
        [InlineData("重音テト", "てと", true)]
        [InlineData("ＵＴＡＵ", "utau", true)]
        public void SearchMatches(string term, string query, bool expected) {
            Assert.Equal(expected, SingerTileViewModel.Matches(new[] { term }, query));
        }

        class NamedSinger : USinger {
            public NamedSinger() {
                found = true;
            }
            public override string Id => "Teto\\TetoCV";
            public override string Name => "重音テト";
            public override Dictionary<string, string> LocalizedNames => new Dictionary<string, string> {
                { "en-US", "Kasane Teto" },
                { "ja-JP", "重音テト" },
            };
            public override string Location => Path.Combine("Singers", "Teto", "TetoCV");
            public override IList<string> SearchTerms => new[] { "kasane", " teto " };
        }

        [Fact]
        public void SearchTermsIncludeNamesFolderAndAuthorTerms() {
            var terms = SingerTileViewModel.BuildSearchTerms(new NamedSinger());
            Assert.Contains("重音テト", terms);
            Assert.Contains("Kasane Teto", terms);
            Assert.Contains("Teto\\TetoCV", terms);
            Assert.Contains("TetoCV", terms);
            Assert.Contains("kasane", terms);
            Assert.Contains("teto", terms);
            Assert.Equal(terms.Distinct(), terms);
            Assert.True(SingerTileViewModel.Matches(terms, "kasane te"));
        }

        [Fact]
        public void SplitSearchTermsOnAnyComma() {
            Assert.Equal(new[] { "kasane", "teto", "テト", "tet" },
                SingersViewModel.SplitSearchTerms(" kasane, teto，テト、 ;tet;teto "));
        }

        class FolderSinger : USinger {
            readonly string location;
            public FolderSinger(string location) {
                this.location = location;
                found = true;
            }
            public override string Location => location;
            public override IList<string> SearchTerms { get; } = new List<string>();
        }

        [Fact]
        public void SetSearchTermsWritesCharacterYaml() {
            var dir = Directory.CreateTempSubdirectory().FullName;
            try {
                var yamlFile = Path.Combine(dir, "character.yaml");
                File.WriteAllText(yamlFile, "name: Teto\n");
                var singer = new FolderSinger(dir);

                SingersViewModel.SetSearchTerms(singer, "kasane, teto");
                Assert.Equal(new[] { "kasane", "teto" }, singer.SearchTerms);
                using (var stream = File.OpenRead(yamlFile)) {
                    var config = VoicebankConfig.Load(stream);
                    Assert.Equal("Teto", config.Name);
                    Assert.Equal(new[] { "kasane", "teto" }, config.SearchTerms);
                }

                SingersViewModel.SetSearchTerms(singer, " ");
                Assert.Empty(singer.SearchTerms);
                Assert.DoesNotContain("search_terms", File.ReadAllText(yamlFile));
            } finally {
                Directory.Delete(dir, true);
            }
        }
    }
}
