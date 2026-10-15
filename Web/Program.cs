using Infrastructure;
using Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCustomLogging();

var app = builder.Build();

app.UseAutoMigrations();
app.UseHttpLogging();

app.MapGet("/hello", () => Results.Json(new {message = "world!"}));

app.Run();