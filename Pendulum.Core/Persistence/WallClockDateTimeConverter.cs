using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pendulum.Core.Persistence;

/// Saves a reminder's times as wall-clock times, with no time zone, whatever kind of DateTime they
/// were in memory, and reads them back as wall-clock times. A time saved with an offset (by
/// earlier versions, for a snooze or a Quick Reminder) is turned into this PC's clock time first,
/// as it always was on reading, and then kept as a wall-clock time.
public sealed class WallClockDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind switch
        {
            DateTimeKind.Utc => DateTime.SpecifyKind(value.ToLocalTime(), DateTimeKind.Unspecified),
            DateTimeKind.Local => DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
            _ => value,
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var wall = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
        writer.WriteStringValue(DateTime.SpecifyKind(wall, DateTimeKind.Unspecified));
    }
}

/// <see cref="WallClockDateTimeConverter"/> for a time that may be missing.
public sealed class WallClockNullableDateTimeConverter : JsonConverter<DateTime?>
{
    private static readonly WallClockDateTimeConverter Inner = new();

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : Inner.Read(ref reader, typeof(DateTime), options);

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is { } v)
            Inner.Write(writer, v, options);
        else
            writer.WriteNullValue();
    }
}
