using Infrastructure;
using Web.Exceptions;
using Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCustomLogging();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Type = null;
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseAutoMigrations();
    app.UseDeveloperExceptionPage();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.UseHttpLogging();

app.MapGet("/hello", () => Results.Json(new {message = "world!"}));
app.MapGet("/exception", () =>
{
    throw new Exception("An error occurred.");
});

app.Run();