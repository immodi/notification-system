using Web.Exceptions;

namespace Web.Extensions;

public static class ExceptionsExtensions
{
    public static void AddGlobalExceptionHandler(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context => { context.ProblemDetails.Type = null; };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
    }
}