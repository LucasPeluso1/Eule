using EuleApi.Clients;
using EuleApi.Indicadores;
using EuleApi.Infrastructure;
using EuleApi.Options;
using EuleApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FontesOptions>(builder.Configuration.GetSection(FontesOptions.Section));
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.Section));
builder.Services.Configure<ScoreOptions>(builder.Configuration.GetSection(ScoreOptions.Section));
var cors = builder.Configuration.GetSection(EuleApi.Options.CorsOptions.Section).Get<EuleApi.Options.CorsOptions>() ?? new();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddCors(o => o.AddPolicy(EuleApi.Options.CorsOptions.PolicyName, p =>
    p.WithOrigins(cors.AllowedOrigins).WithMethods("GET").AllowAnyHeader()));

// Clientes HTTP tipados com resiliência (timeout, retentativas com backoff, circuit breaker).
void Resiliencia(IHttpClientBuilder b, TimeSpan tentativa, TimeSpan total, bool limitePorTempo = false)
{
    b.ConfigureHttpClient(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("EuleApi/1.0 (NUCCIBER-MPBA; consulta-publica)"));
    b.AddStandardResilienceHandler(r =>
    {
        r.Retry.MaxRetryAttempts = 2;
        r.AttemptTimeout.Timeout = tentativa;
        r.TotalRequestTimeout.Timeout = total;
        r.CircuitBreaker.SamplingDuration = tentativa * 2;

        // Fonte com limite de requisições (HTTP 429): não adianta retentar nem abrir o circuito;
        // o cliente converte o 429 em "fonte indisponível" com mensagem clara.
        if (limitePorTempo)
        {
            r.Retry.ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is HttpRequestException ||
                args.Outcome.Result is { StatusCode: >= System.Net.HttpStatusCode.InternalServerError or System.Net.HttpStatusCode.RequestTimeout });
            r.CircuitBreaker.ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is HttpRequestException ||
                args.Outcome.Result is { StatusCode: >= System.Net.HttpStatusCode.InternalServerError });
        }
    });
}

Resiliencia(builder.Services.AddHttpClient<IReceitaClient, ReceitaClient>(), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(50), limitePorTempo: true);
Resiliencia(builder.Services.AddHttpClient<IBacenAutorizacaoClient, BacenAutorizacaoClient>(), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(50));
Resiliencia(builder.Services.AddHttpClient<IRankingReclamacoesClient, RankingReclamacoesClient>(), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(100));

builder.Services.AddSingleton<IIndicador, ConstituicaoRecenteCapitalIncompativel>();
builder.Services.AddSingleton<IIndicador, AusenciaAutorizacaoBacen>();
builder.Services.AddSingleton<IIndicador, InconsistenciaCnae>();
builder.Services.AddSingleton<IIndicador, CruzamentoSocietario>();
builder.Services.AddSingleton<IIndicador, AusenciaRanking>();
builder.Services.AddSingleton<IIndicador, InconsistenciaGeografica>();
builder.Services.AddScoped<IEmpresaService, EmpresaService>();

var app = builder.Build();

app.UseMiddleware<TratamentoErrosMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(EuleApi.Options.CorsOptions.PolicyName);
app.MapControllers();

app.Run();

public partial class Program;
