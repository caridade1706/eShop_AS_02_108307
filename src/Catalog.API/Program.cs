using OpenTelemetry.Metrics;
using System.Diagnostics.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

var withApiVersioning = builder.Services.AddApiVersioning();
builder.AddDefaultOpenApi(withApiVersioning);

// 🔹 Criar um único Meter para toda a API
var meter = new Meter("Catalog.API");
builder.Services.AddSingleton(meter); // Registra o Meter no container de serviços

// 🔹 Configuração OpenTelemetry com Prometheus
builder.Services.AddOpenTelemetry()
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

// 🔹 Middleware para contar todas as requisições à API
app.Use(async (context, next) =>
{
    requestCounter.Add(1);
    await next();
});

// 🔹 Expor o endpoint de métricas do Prometheus
app.UseOpenTelemetryPrometheusScrapingEndpoint();

// 🔹 Configuração de rotas e middlewares
app.MapDefaultEndpoints();
app.UseStatusCodePages();
app.MapCatalogApi();
app.UseDefaultOpenApi();
app.Run();
