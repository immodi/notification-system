using Application.DTOs;
using Application.Interfaces;
using Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Web.Endpoints;

public static class NotificationEndpoints
{
    private record NotificationId(Guid Id);
    private static async Task<Results<Ok<Notification>, NotFound>> Get(INotificationService service, Guid id)
    {
        var notification = await service.GetByIdAsync(id);
        return notification is null ? TypedResults.NotFound() : TypedResults.Ok(notification);
    }

    private static async Task<IResult> Post(HttpContext context, INotificationService service, NotificationDto dto)
    {
        var id = await service.CreateAsync(dto);
        return TypedResults.Accepted($"{context.Request.Path}/{id}", new NotificationId(id));
    }

    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/notifications")
            .WithTags("Notifications");
        
        group.MapGet("/{id:Guid}", Get)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("Get Notification");
        group.MapPost("/", Post)
            .Produces<NotificationId>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .WithName("Create Notification");
    }
}