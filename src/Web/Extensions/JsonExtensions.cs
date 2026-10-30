using System.Text.Json.Serialization;

namespace Web.Extensions;

public static class JsonExtensions
{
    public static void ConfigureJsonSerialization(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options => 
        {
            options.SerializerOptions.AllowTrailingCommas = true;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
    }
}