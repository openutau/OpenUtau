using System.IO;
using OpenUtau.Core.Ustx;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenUtau.Core {
    public class Yaml {
        public static Yaml DefaultSerializer => instance;
        public static Yaml DefaultDeserializer => instance;

        private static readonly Yaml instance = new Yaml();

        private readonly ISerializer serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .WithEventEmitter(next => new FlowEmitter(next))
            .DisableAliases()
            .WithQuotingNecessaryStrings()
            .Build();

        private readonly IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        private readonly object serializerLock = new object();
        private readonly object deserializerLock = new object();

        public string Serialize(object? graph) {
            lock (serializerLock) {
                return serializer.Serialize(graph);
            }
        }

        public void Serialize(TextWriter writer, object? graph) {
            lock (serializerLock) {
                serializer.Serialize(writer, graph);
            }
        }

        public T Deserialize<T>(string input) {
            lock (deserializerLock) {
                return deserializer.Deserialize<T>(input);
            }
        }

        public T Deserialize<T>(TextReader input) {
            lock (deserializerLock) {
                return deserializer.Deserialize<T>(input);
            }
        }
    }

    public class FlowEmitter : ChainedEventEmitter {
        public FlowEmitter(IEventEmitter nextEmitter) : base(nextEmitter) { }
        public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter) {
            if (eventInfo.Source.Type == typeof(PitchPoint) ||
                eventInfo.Source.Type == typeof(UVibrato) ||
                eventInfo.Source.Type == typeof(UExpression)) {
                eventInfo.Style = MappingStyle.Flow;
            }
            base.Emit(eventInfo, emitter);
        }
        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter) {
            base.Emit(eventInfo, emitter);
        }
    }
}
