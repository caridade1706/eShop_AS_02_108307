using eShop.Basket.API.Repositories;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.AddBasicServiceDefaults();
builder.AddApplicationServices();

builder.Services.AddGrpc();

var meter = new Meter("Basket.API");
builder.Services.AddSingleton(meter); // Registra o Meter no container de serviços

builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Basket.API"))
            .AddAspNetCoreInstrumentation()
            .AddGrpcClientInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Basket.API")
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri("http://localhost:4317");
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            });
    })
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
