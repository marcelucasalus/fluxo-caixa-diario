using Command.LancamentoRegistrar;
using CommandStore.FluxoCaixa;
using FluxoCaixa.LancamentoRegistrar.Interface;
using FluxoCaixa.LancamentoRegistrar.Service;
using FluxoCaixaApi.Configurations;
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
using Query.ConsolidadoDiario;
using QueryStore;
using QueryStore.Interface;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;
using Store;
using Store.Identity;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

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
/*
builder.Services.AddDbContext<FluxoCaixaContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
*/
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
        DispatchConsumersAsync = true
    };

    IConnection connection = null;
    var retryCount = 20;
    var retryDelay = TimeSpan.FromSeconds(20);

    // Retry de conexão com RabbitMQ
    for (int attempt = 0; attempt < retryCount; attempt++)
    {
        try
        {
            connection = factory.CreateConnection();
            break; // Conexão bem-sucedida, sai do loop
        }
        catch (Exception ex)
        {
            if (attempt == retryCount - 1)
            {
                throw new InvalidOperationException("Não foi possível conectar ao RabbitMQ após várias tentativas.", ex);
            }

            // Aguarda antes de tentar novamente
            Console.WriteLine($"Attempt {attempt + 1} falhou, tentando novamente em {retryDelay.TotalSeconds} seconds...");
            Task.Delay(retryDelay).Wait();
        }
    }

    // Declarar exchange e fila aqui, na inicialização
    using (var channel = connection.CreateModel())
    {
        channel.ExchangeDeclare("lancamentos", ExchangeType.Direct, durable: true);
        channel.QueueDeclare("consolidado_queue", durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind("consolidado_queue", "lancamentos", "lancamento.registrado");
    }

    return connection;
});

builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>(); // Publisher que só publica mensagens
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

// 🔍 Adiciona o Swagger (CONSOLIDADO)
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

builder.Services.AddHttpClient();
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
builder.Services.AddTransient<IFluxoCaixaCommandStore, FluxoCaixaCommandStore>();
builder.Services.AddTransient<ILancamentoRegistrarService, LancamentoRegistrarService>();
builder.Services.AddTransient<IConsolidadoQueryStore, ConsolidadoQueryStore>();
builder.Services.AddTransient<ILancamentoQueryStore, LancamentoQueryStore>();

// Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<FluxoCaixaContext>()
    .AddDefaultTokenProviders();

// JWT Authentication
var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = false; // em prod deixe true
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

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
    options.AddPolicy("ConsultaOnly", policy => policy.RequireRole("consulta", "admin"));
});

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console() // logs no container
    .WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(new Uri("http://elasticsearch:9200"))
    {
        AutoRegisterTemplate = true,
        IndexFormat = "fluxocaixa-logs-{0:yyyy.MM.dd}"
    })
    .CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();

// Aplicar migrations e inicializar roles
using (var scope = app.Services.CreateScope())
{
    // Aplicar migrations do EF Core
    var context = scope.ServiceProvider.GetRequiredService<FluxoCaixaContext>();
    context.Database.Migrate();

    // Inicializar roles de Identity
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await RoleInitializer.InitializeAsync(roleManager);
}

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

//app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
