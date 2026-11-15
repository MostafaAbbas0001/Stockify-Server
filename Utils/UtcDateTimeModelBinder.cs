using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Stockify.Utils;

/// <summary>
/// Binds every DateTime coming from the route or query string as UTC. A plain date such as
/// <c>2026-11-01</c> means midnight UTC; a value with an offset or <c>Z</c> is converted to UTC.
/// Without this, plain dates bind as <see cref="DateTimeKind.Unspecified"/> and Npgsql refuses to
/// compare them with <c>timestamptz</c> columns (every list filter returned 400).
/// </summary>
public sealed class UtcDateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var raw = context.ValueProvider.GetValue(context.ModelName).FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
            return Task.CompletedTask;

        context.ModelState.SetModelValue(context.ModelName, context.ValueProvider.GetValue(context.ModelName));
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
            context.Result = ModelBindingResult.Success(value);
        else
            context.ModelState.TryAddModelError(context.ModelName, $"'{raw}' is not a valid date.");

        return Task.CompletedTask;
    }
}

public sealed class UtcDateTimeModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = context.Metadata.UnderlyingOrModelType;
        var fromBody = context.BindingInfo.BindingSource == BindingSource.Body;
        return type == typeof(DateTime) && !fromBody ? new UtcDateTimeModelBinder() : null;
    }
}

/// <summary>
/// The same rule for JSON bodies: a plain date (<c>2026-11-01</c>) is midnight UTC and a value with an
/// offset is converted to UTC. Without it, expense periods and other body dates arrived as
/// <see cref="DateTimeKind.Unspecified"/> and saving them failed with a 500. Writing is unchanged.
/// </summary>
public sealed class UtcDateTimeJsonConverter : System.Text.Json.Serialization.JsonConverter<DateTime>
{
    public override DateTime Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
            return value;
        throw new System.Text.Json.JsonException($"'{raw}' is not a valid date.");
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, DateTime value,
        System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(value);
}
