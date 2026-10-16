using Microsoft.AspNetCore.HttpLogging;

namespace Web.Extensions;

public static class LoggingExtensions
{
    public static async void AddCustomLogging(
        this IServiceCollection services)
    {
        services.AddHttpLogging(
            options => { options.LoggingFields = HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody; }
        );
    }
}