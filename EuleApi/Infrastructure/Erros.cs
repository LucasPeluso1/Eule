using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace EuleApi.Infrastructure;

public class CnpjInvalidoException(string mensagem) : Exception(mensagem);

public class CnpjNaoEncontradoException(string cnpj)
    : Exception($"CNPJ {cnpj} não encontrado na base da Receita Federal.");

public class FonteIndisponivelException(string fonte, string detalhe, Exception? inner = null)
    : Exception($"Fonte externa indisponível: {fonte}. {detalhe}", inner)
{
    public string Fonte { get; } = fonte;
}

/// <summary>Converte exceções de domínio em ProblemDetails (400/404/503) e evita vazar stack trace no 500.</summary>
public class TratamentoErrosMiddleware(RequestDelegate next, ILogger<TratamentoErrosMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499; // cliente encerrou a requisição
        }
        catch (Exception ex)
        {
            var (status, titulo) = ex switch
            {
                CnpjInvalidoException => (HttpStatusCode.BadRequest, "CNPJ inválido"),
                CnpjNaoEncontradoException => (HttpStatusCode.NotFound, "CNPJ não encontrado"),
                FonteIndisponivelException => (HttpStatusCode.ServiceUnavailable, "Fonte externa indisponível"),
                _ => (HttpStatusCode.InternalServerError, "Erro interno"),
            };

            if (status == HttpStatusCode.InternalServerError)
                logger.LogError(ex, "Erro não tratado em {Path}", context.Request.Path);
            else
                logger.LogWarning("{Titulo}: {Mensagem}", titulo, ex.Message);

            var problem = new ProblemDetails
            {
                Status = (int)status,
                Title = titulo,
                Detail = status == HttpStatusCode.InternalServerError
                    ? "Ocorreu um erro inesperado ao processar a consulta."
                    : ex.Message,
            };
            if (ex is FonteIndisponivelException fonte)
                problem.Extensions["fonte"] = fonte.Fonte;

            context.Response.StatusCode = problem.Status.Value;
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
        }
    }
}
