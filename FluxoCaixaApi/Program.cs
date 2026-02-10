using Command.LancamentoRegistrar;
using CommandStore.FluxoCaixa;
using FluxoCaixa.LancamentoRegistrar.Interface;
using FluxoCaixa.LancamentoRegistrar.Service;
using FluxoCaixaApi.Configurations;
using FluxoCaixaApi.Services;
using FluxoCaixaApi.Services.Interface;
using Integration.Sub;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Query.ConsolidadoDiario;
using QueryStore;
using QueryStore.Interface;
using RabbitMQ.Client;
using StackExchange.Redis;
using Store.Identity;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ========================================
// CONFIGURAÇÃO CONSOLIDADA DO OPENTELEMETRY
// ========================================
var serviceName = "fluxocaixaapi";
var serviceVersion = "1.0.0";

// Resource Builder compartilhado
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName, serviceVersion: serviceVersion)
    .AddAttributes(new[]
    {
        new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName),
        new KeyValuePair<string, object>("host.name", Environment.MachineName)
    });

// Configurar Logs
builder.Logging.ClearProviders();
builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(resourceBuilder);
    options.AddOtlpExporter(otlp =>
    {
        otlp.Endpoint = new Uri("http://otel-collector:4317");
        otlp.Protocol = OtlpExportProtocol.Grpc;
    });
    options.IncludeScopes = true;
    options.IncludeFormattedMessage = true;
});

// Configurar Tracing e Metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(res => res
        .AddService(serviceName, serviceVersion: serviceVersion)
        .AddAttributes(new[]
        {
            new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName),
            new KeyValuePair<string, object>("host.name", Environment.MachineName)
        }))
    .WithTracing(tracer => tracer
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(serviceName)
        .AddOtlpExporter(otlp =>
        {
            otlp.Endpoint = new Uri("http://otel-collector:4317");
            otlp.Protocol = OtlpExportProtocol.Grpc;
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("FluxoCaixaApi.Metrics")
        .AddOtlpExporter((otlp, readerOptions) =>
        {
            otlp.Endpoint = new Uri("http://otel-collector:4317");
            otlp.Protocol = OtlpExportProtocol.Grpc;
            readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10000;
        }));

// ========================================
// MEDIATR
// ========================================
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(LancamentoRegistrarCommandHandler).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(ConsolidadoQueryHandler).Assembly);
});

// ========================================
// DATABASE
// ========================================
builder.Services.AddDbContext<FluxoCaixaContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ========================================
// IDENTITY
// ========================================
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<FluxoCaixaContext>()
    .AddDefaultTokenProviders();

// ========================================
// REDIS
// ========================================
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = builder.Configuration;
    var host = configuration["Redis:Host"];
    var port = configuration["Redis:Port"];
    var options = ConfigurationOptions.Parse($"{host}:{port}");
    return ConnectionMultiplexer.Connect(options);
});

// ========================================
// RABBITMQ
// ========================================
builder.Services.AddSingleton(sp =>
{
    var config = builder.Configuration.GetSection("RabbitMQ");
    var factory = new ConnectionFactory()
    {
        HostName = config["Host"],
        UserName = config["Username"],
        Password = config["Password"],
        DispatchConsumersAsync = true
    };

    IConnection connection = null;
    var retryCount = 20;
    var retryDelay = TimeSpan.FromSeconds(20);

    for (int attempt = 0; attempt < retryCount; attempt++)
    {
        try
        {
            connection = factory.CreateConnection();
            break;
        }
        catch (Exception ex)
        {
            if (attempt == retryCount - 1)
            {
                throw new InvalidOperationException("Não foi possível conectar ao RabbitMQ após várias tentativas.", ex);
            }
            Console.WriteLine($"Attempt {attempt + 1} falhou, tentando novamente em {retryDelay.TotalSeconds} seconds...");
            Task.Delay(retryDelay).Wait();
        }
    }

    using (var channel = connection.CreateModel())
    {
        channel.ExchangeDeclare("lancamentos", ExchangeType.Direct, durable: true);
        channel.QueueDeclare("consolidado_queue", durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind("consolidado_queue", "lancamentos", "lancamento.registrado");
    }

    return connection;
});

builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
builder.Services.AddHostedService<ConsolidadoWorker>();

// ========================================
// API VERSIONING
// ========================================
builder.Services.AddApiVersioning(options =>
{
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
});

builder.Services.AddVersionedApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// ========================================
// CORS
// ========================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// ========================================
// SWAGGER
// ========================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Digite: Bearer {seu token JWT}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ========================================
// DEPENDENCY INJECTION
// ========================================
builder.Services.AddHttpClient();
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
builder.Services.AddTransient<IFluxoCaixaCommandStore, FluxoCaixaCommandStore>();
builder.Services.AddSingleton(new Meter("FluxoCaixaApi.Metrics", "1.0.0"));
builder.Services.AddSingleton(new ActivitySource("FluxoCaixaApi"));
builder.Services.AddTransient<ILancamentoRegistrarService, LancamentoRegistrarService>();
builder.Services.AddTransient<IConsolidadoQueryStore, ConsolidadoQueryStore>();
builder.Services.AddTransient<ILancamentoQueryStore, LancamentoQueryStore>();
builder.Services.AddTransient<IAuthService, AuthService>();

// ========================================
// JWT AUTHENTICATION
// ========================================
var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

// ========================================
// AUTHORIZATION
// ========================================
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
    options.AddPolicy("ConsultaOnly", policy => policy.RequireRole("consulta", "admin"));
});

// ========================================
// BUILD APP
// ========================================
var app = builder.Build();

app.UseCors("AllowAll");

// ========================================
// SWAGGER UI
// ========================================
var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    foreach (var description in provider.ApiVersionDescriptions)
    {
        options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
            description.GroupName.ToUpperInvariant());
    }
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();