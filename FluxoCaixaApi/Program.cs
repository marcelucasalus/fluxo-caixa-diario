using Command.LancamentoRegistrar;
using CommandStore.FluxoCaixa;
using FluxoCaixa.LancamentoRegistrar.Interface;
using FluxoCaixa.LancamentoRegistrar.Service;
using FluxoCaixaApi.Configurations;
using Integration.Sub;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Query.ConsolidadoDiario;
using QueryStore;
using QueryStore.Interface;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(LancamentoRegistrarCommandHandler).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(ConsolidadoQueryHandler).Assembly);
});

builder.Services.AddDbContext<FluxoCaixaContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), options =>
{
    options.MigrationsAssembly("Store");
    options.EnableRetryOnFailure();
}));

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = builder.Configuration;
    var host = configuration["Redis:Host"];
    var port = configuration["Redis:Port"];
    var options = ConfigurationOptions.Parse($"{host}:{port}");
    return ConnectionMultiplexer.Connect(options);
});

builder.Services.AddSingleton(sp =>
{
    var config = builder.Configuration.GetSection("RabbitMQ");
    var factory = new ConnectionFactory()
    {
        HostName = config["Host"],
        UserName = config["Username"],
        Password = config["Password"],
        DispatchConsumersAsync = true // importante para consumers async
    };

    var connection = factory.CreateConnection();

    // Declarar exchange e fila aqui, na inicialização
    using var channel = connection.CreateModel();
    channel.ExchangeDeclare("lancamentos", ExchangeType.Direct, durable: true);
    channel.QueueDeclare("consolidado_queue", durable: true, exclusive: false, autoDelete: false);
    channel.QueueBind("consolidado_queue", "lancamentos", "lancamento.registrado");

    return connection;
});

builder.Services.AddSingleton<RabbitMqPublisher>(); // Publisher que só publica mensagens
builder.Services.AddHostedService<ConsolidadoWorker>(); // Worker que consome mensagens


// ✅ Registrar o versionamento de API
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// 🔍 Adiciona o Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions >, ConfigureSwaggerOptions>();
builder.Services.AddTransient<IFluxoCaixaCommandStore, FluxoCaixaCommandStore>();
builder.Services.AddTransient<ILancamentoRegistrarService, LancamentoRegistrarService>();
builder.Services.AddTransient<IConsolidadoQueryStore, ConsolidadoQueryStore>();
builder.Services.AddTransient<ILancamentoQueryStore, LancamentoQueryStore>();

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console() // logs no container
    .WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(new Uri("http://host.docker.internal:9200"))
    {
        AutoRegisterTemplate = true,
        IndexFormat = "fluxocaixa-logs-{0:yyyy.MM.dd}"
    })
    .CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();

using var scope = app.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<FluxoCaixaContext>();
context.Database.Migrate();

app.UseCors("AllowAll");

// Habilita o Swagger na aplicação
var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    // Cria uma aba para cada versão detectada
    foreach (var description in provider.ApiVersionDescriptions)
    {
        options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
            description.GroupName.ToUpperInvariant());
    }
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

//app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
