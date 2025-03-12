using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

var withApiVersioning = builder.Services.AddApiVersioning();
builder.AddDefaultOpenApi(withApiVersioning);


var meter = new Meter("Catalog.API");
builder.Services.AddSingleton(meter); 


builder.Services.AddOpenTelemetry()

    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Catalog.API"))
            .AddAspNetCoreInstrumentation()
            .AddGrpcClientInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Catalog.API")
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
            .AddMeter("Catalog.API") // Certifica-se que o Meter está registrado
            .AddOtlpExporter(Options =>
                Options.Endpoint = new Uri("http://localhost:4317"));
    });

var app = builder.Build();

var requestCounter = meter.CreateCounter<long>("catalog_requests_total",
    description: "Número total de requisições ao Catalog.API.");

app.Use(async (context, next) =>
{
    requestCounter.Add(1);
    await next();
});


app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.MapDefaultEndpoints();
app.UseStatusCodePages();
app.MapCatalogApi();
app.UseDefaultOpenApi();
app.Run();
