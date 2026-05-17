using Orders.Realtime.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Register the event buffer
builder.Services.AddSingleton<OrderStatusHub>();

builder.Services.AddCors();

builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxConcurrentConnections = 50;
    o.Limits.MaxConcurrentUpgradedConnections = 50;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

app.UseHttpsRedirection();

// Map the SSE endpoints
app.MapOrdersEndpoints();

app.Run();
