using OpenTelemetry.Metrics;
using System.Diagnostics.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

var withApiVersioning = builder.Services.AddApiVersioning();
builder.AddDefaultOpenApi(withApiVersioning);

// 🔹 Configuração OpenTelemetry com Prometheus
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("Catalog.API") // Registra o Meter corretamente
            .AddOtlpExporter(Options =>
            Options.Endpoint = new Uri("http://localhost:4317"));
    });

var app = builder.Build();

// 🔹 Garante que o Prometheus pode coletar as métricas
app.UseOpenTelemetryPrometheusScrapingEndpoint(); // 🔥 Expor o endpoint de métricas do Prometheus

// 🔹 Criando manualmente um contador
var meter = new Meter("Catalog.API");
var requestCounter = meter.CreateCounter<long>("catalog_requests_total", description: "Número total de requisições ao Catalog.API.");

// Middleware para contar requisições
app.Use(async (context, next) =>
{
    requestCounter.Add(1, new KeyValuePair<string, object>("method", context.Request.Method));
    await next();
});

// Configuração de rotas e middlewares
app.MapDefaultEndpoints();
app.UseStatusCodePages();
app.MapCatalogApi();
app.UseDefaultOpenApi();
app.Run();
