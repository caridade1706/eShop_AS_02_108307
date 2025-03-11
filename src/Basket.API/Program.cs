using eShop.Basket.API.Repositories;
using OpenTelemetry.Metrics;
using System.Diagnostics.Metrics;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddBasicServiceDefaults();
builder.AddApplicationServices();

builder.Services.AddGrpc();

var meter = new Meter("Basket.API");
builder.Services.AddSingleton(meter); // Registra o Meter no container de serviços

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("Basket.API") // Registra o Meter corretamente
            .AddOtlpExporter(Options =>
                Options.Endpoint = new Uri("http://localhost:4317"));
    });

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint();
app.MapDefaultEndpoints();
app.MapGrpcService<BasketService>();

app.Run();
