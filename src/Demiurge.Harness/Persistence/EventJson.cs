using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;

namespace Demiurge.Harness.Persistence;

/// <summary>
/// JSON format of events, snapshots and configs in the store. Lives in the harness so the engine
/// stays free of any storage format. Polymorphic types (WorldEvent, AgentAction, Location) are
/// written with a "$type" discriminator equal to the class name:
/// <c>{"$type":"Worked","agentId":5,"earned":10,"money":48,"energy":74,"tick":1}</c>.
/// Concept: Event sourcing — docs/CONCEPTS.md#event-sourcing
/// </summary>
public static class EventJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static string Serialize(WorldEvent e) => JsonSerializer.Serialize(e, Options);

    public static WorldEvent DeserializeEvent(string json) =>
        JsonSerializer.Deserialize<WorldEvent>(json, Options)
        ?? throw new JsonException("Event payload deserialized to null.");

    private static JsonSerializerOptions Create()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(AddPolymorphism<WorldEvent>);
        resolver.Modifiers.Add(AddPolymorphism<AgentAction>);
        resolver.Modifiers.Add(AddPolymorphism<Location>);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = resolver,
            Converters =
            {
                new JsonStringEnumConverter(),
                new AgentIdConverter(),
                new PlaceIdConverter(),
                new PlacesGraphConverter(),
            },
        };
        options.MakeReadOnly();
        return options;
    }

    /// <summary>Registers every concrete subclass of TBase (found by reflection) under its class name.</summary>
    private static void AddPolymorphism<TBase>(JsonTypeInfo info)
    {
        if (info.Type != typeof(TBase))
            return;

        var options = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$type",
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var type in typeof(TBase).Assembly.GetTypes()
                     .Where(t => t.IsSubclassOf(typeof(TBase)) && !t.IsAbstract)
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            options.DerivedTypes.Add(new JsonDerivedType(type, type.Name));
        }

        info.PolymorphismOptions = options;
    }

    private sealed class AgentIdConverter : JsonConverter<AgentId>
    {
        public override AgentId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetInt32());

        public override void Write(Utf8JsonWriter writer, AgentId value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }

    private sealed class PlaceIdConverter : JsonConverter<PlaceId>
    {
        public override PlaceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetString() ?? throw new JsonException("PlaceId cannot be null."));

        public override void Write(Utf8JsonWriter writer, PlaceId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    /// <summary>PlacesGraph validates itself in its constructor, so it is rebuilt from (places, edges).</summary>
    private sealed class PlacesGraphConverter : JsonConverter<PlacesGraph>
    {
        private sealed record GraphDto(List<Place> Places, List<Edge> Edges);

        public override PlacesGraph Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = JsonSerializer.Deserialize<GraphDto>(ref reader, options)
                      ?? throw new JsonException("PlacesGraph cannot be null.");
            return new PlacesGraph(dto.Places, dto.Edges);
        }

        public override void Write(Utf8JsonWriter writer, PlacesGraph value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new GraphDto(value.Places.ToList(), value.Edges.ToList()), options);
    }
}
