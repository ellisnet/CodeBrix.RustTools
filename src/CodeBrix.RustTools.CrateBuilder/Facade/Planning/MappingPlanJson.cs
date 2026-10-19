using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// The JSON form of a <see cref="MappingPlan"/> (the wrap report's twin, for agents):
/// System.Text.Json, camelCase names, enum values as camelCase strings, null values left out,
/// indented, <c>\n</c> line endings. The plan's object graph has back references (a handle lists
/// its methods, a method names its handle, a type mapping names its mirror), so every handle,
/// mirror, enum or function that appears INSIDE another object is written as its C name; the
/// full objects appear once, in the plan's top-level lists.
/// </summary>
internal static class MappingPlanJson
{
    /// <summary>The serializer settings.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    /// <summary>The plan as JSON text (ends with <c>\n</c>).</summary>
    public static string Serialize(MappingPlan plan)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        return JsonSerializer.Serialize(plan, SerializerOptions).Replace("\r\n", "\n") + "\n";
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(ReferenceByName);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = resolver,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly();
        return options;
    }

    private static void ReferenceByName(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || info.Type == typeof(MappingPlan))
        {
            return;
        }

        foreach (var property in info.Properties)
        {
            var type = property.PropertyType;
            if (type == typeof(HandleType) || type == typeof(MirrorStruct) || type == typeof(EnumMapping) || type == typeof(ExportedFunction))
            {
                property.CustomConverter = (JsonConverter)Activator.CreateInstance(typeof(NameConverter<>).MakeGenericType(type));
            }
            else if (type == typeof(IList<ExportedFunction>))
            {
                property.CustomConverter = new NameListConverter();
            }
        }
    }

    private static string NameOf(object value) => value switch
    {
        HandleType h => h.CName,
        MirrorStruct m => m.CName,
        EnumMapping e => e.CName,
        ExportedFunction f => f.CName,
        _ => null,
    };

    private sealed class NameConverter<T> : JsonConverter<T>
        where T : class
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("The mapping plan JSON is written, never read back.");

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WriteStringValue(NameOf(value));
    }

    private sealed class NameListConverter : JsonConverter<IList<ExportedFunction>>
    {
        public override IList<ExportedFunction> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("The mapping plan JSON is written, never read back.");

        public override void Write(Utf8JsonWriter writer, IList<ExportedFunction> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var f in value)
            {
                writer.WriteStringValue(f.CName);
            }

            writer.WriteEndArray();
        }
    }
}
