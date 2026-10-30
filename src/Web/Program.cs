using Infrastructure;
using Web.Endpoints;
using Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCustomLogging();
builder.Services.AddValidation();
builder.Services.ConfigureJsonSerialization();
builder.Services.AddGlobalExceptionHandler();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.UseAutoMigrationsAsync();
}

app.UseExceptionHandler();
app.UseHttpLogging();
app.MapNotificationEndpoints();

app.Run();

public partial class Program { }
