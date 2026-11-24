using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    /// <summary>
    /// Track ids: stable across save and load, unique within a project, and never shared by
    /// copies, imports or projects started from the same template.
    /// </summary>
    // ImportTracks publishes a LoadProjectNotification, swallowed here through DocManager's
    // command sink, which the render tests also own.
    [Collection(RenderSingletonCollection.Name)]
    public class UTrackIdTest : System.IDisposable {
        public void Dispose() => DocManager.Inst.CommandSink = null;

        static UProject RoundTrip(UProject project) {
            project.BeforeSave();
            string yaml = Yaml.DefaultSerializer.Serialize(project);
            project.AfterSave();
            var loaded = Yaml.DefaultDeserializer.Deserialize<UProject>(yaml);
            loaded.AfterLoad();
            return loaded;
        }

        static UProject ProjectWithTracks(params string[] names) {
            var project = new UProject();
            project.tracks.Clear();
            foreach (var name in names) {
                project.tracks.Add(new UTrack(name) { TrackNo = project.tracks.Count });
            }
            return project;
        }

        [Fact]
        public void NewTracksGetDistinctWellFormedIds() {
            var ids = Enumerable.Range(0, 200).Select(_ => new UTrack("x").Id).ToList();

            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.Matches("^t[a-z2-7]{10}$", id));
        }

        [Fact]
        public void IdsSurviveSaveAndLoad() {
            var project = ProjectWithTracks("Lead", "Harmony");
            var ids = project.tracks.Select(t => t.Id).ToList();

            var loaded = RoundTrip(project);

            Assert.Equal(ids, loaded.tracks.Select(t => t.Id));
            Assert.False(loaded.TrackIdsUnsaved);
        }

        [Fact]
        public void AProjectSavedWithoutIdsGetsThemOnLoadAndIsFlagged() {
            var project = ProjectWithTracks("Lead", "Harmony");
            project.BeforeSave();
            string yaml = Yaml.DefaultSerializer.Serialize(project);
            project.AfterSave();
            // What a project saved by an older OpenUtau looks like.
            string legacy = string.Join("\n", yaml.Replace("\r\n", "\n").Split('\n')
                .Where(line => !line.TrimStart(' ', '-').StartsWith("id: ")));
            Assert.DoesNotContain("id: t", legacy);

            var loaded = Yaml.DefaultDeserializer.Deserialize<UProject>(legacy);
            loaded.AfterLoad();

            Assert.All(loaded.tracks, t => Assert.True(t.HasId));
            Assert.Equal(2, loaded.tracks.Select(t => t.Id).Distinct().Count());
            Assert.True(loaded.TrackIdsUnsaved);
        }

        [Fact]
        public void DuplicateIdsAreResolvedOnLoadKeepingTheFirst() {
            var project = ProjectWithTracks("Lead", "Copy");
            project.tracks[1].Id = project.tracks[0].Id;

            var loaded = RoundTrip(project);

            Assert.Equal(project.tracks[0].Id, loaded.tracks[0].Id);
            Assert.NotEqual(loaded.tracks[0].Id, loaded.tracks[1].Id);
            Assert.True(loaded.TrackIdsUnsaved);
        }

        [Fact]
        public void ImportingTheSameProjectTwiceKeepsIdsUnique() {
            var target = ProjectWithTracks("Lead");
            string leadId = target.tracks[0].Id;
            var source = ProjectWithTracks("Imported");
            var first = RoundTrip(source);
            var second = RoundTrip(source);

            DocManager.Inst.CommandSink = _ => { };
            Formats.ImportTracks(target, new[] { first, second }, importTempo: false);

            Assert.Equal(3, target.tracks.Count);
            Assert.Equal(leadId, target.tracks[0].Id);
            Assert.Equal(3, target.tracks.Select(t => t.Id).Distinct().Count());
        }

        [Fact]
        public void ATrackAddedWithACopiedIdGetsItsOwn() {
            var project = ProjectWithTracks("Lead");
            var copy = new UTrack("Lead_copy") { Id = project.tracks[0].Id, TrackNo = 1 };

            new AddTrackCommand(project, copy).Execute();

            Assert.NotEqual(project.tracks[0].Id, copy.Id);
        }

        [Fact]
        public void UndoingATrackRemovalBringsBackTheSameId() {
            var project = ProjectWithTracks("Lead", "Harmony");
            var harmony = project.tracks[1];
            string id = harmony.Id;
            var remove = new RemoveTrackCommand(project, harmony);

            remove.Execute();
            remove.Unexecute();

            Assert.Same(harmony, project.tracks[1]);
            Assert.Equal(id, project.tracks[1].Id);
        }

        [Fact]
        public void ATemplateGetsNewIds() {
            var project = ProjectWithTracks("Lead", "Harmony");
            var before = new HashSet<string>(project.tracks.Select(t => t.Id));

            project.RegenerateTrackIds();

            Assert.DoesNotContain(project.tracks, t => before.Contains(t.Id));
            Assert.Equal(2, project.tracks.Select(t => t.Id).Distinct().Count());
        }
    }
}
