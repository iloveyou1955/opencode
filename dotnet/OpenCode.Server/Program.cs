using OpenCode.Server;
using OpenCode.Core.Services;
using OpenCode.Core.Contracts;
using OpenCode.AgentFramework.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
#pragma warning disable CA1416
builder.Services.AddOpenCodeServices();
#pragma warning restore CA1416

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

app.MapOpenCodeRoutes();

app.Run();
