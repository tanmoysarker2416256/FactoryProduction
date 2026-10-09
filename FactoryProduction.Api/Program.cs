using System.Text.Json;
using FactoryProduction.Api;
using FactoryProduction.Application;
using FactoryProduction.Infrastructure;

EnvFile.Load(); // reads .env (if present) into environment variables before configuration is built

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Default is not set. Copy .env.example to .env and fill it in.");

builder.Services
    .AddApplication()
    .AddPersistence(connectionString)
    .AddMqttWorker(builder.Configuration);

var app = builder.Build();

// Never leak stack traces or SQL details to API callers.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ErrorResponse("internal_error", "An unexpected error occurred."));
}));

// Migration / initialization command:  dotnet run --project src/FactoryProduction.Api -- --migrate
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
    Console.WriteLine("Database is up to date.");
    return;
}

if (builder.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapEventsEndpoints();
app.MapAckEndpoints();
app.MapStateEndpoints();
app.MapMqttEndpoints(app.Environment.IsDevelopment());

app.Run();
