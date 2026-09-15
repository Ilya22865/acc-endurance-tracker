using AccEndurance.Server.Hubs;
using AccEndurance.Server.Services;
using AccEndurance.Domain;
using AccEndurance.Server.Controllers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.AddDbContextFactory<AccEnduranceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("AccEndurance")));

builder.Services.AddSingleton<SessionStateService>();
builder.Services.AddHostedService<TelemetryPersistService>();
builder.Services.AddSingleton<AccBroadcastService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AccBroadcastService>());
builder.Services.AddSingleton<SessionStateBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionStateBroadcaster>());

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(_ => true);
    });
});

var app = builder.Build();

app.UseCors();
app.MapControllers();
app.MapHub<SessionHub>("/hub/session");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccEnduranceDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();