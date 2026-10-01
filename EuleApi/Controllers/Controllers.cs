using EuleApi.Models;
using EuleApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace EuleApi.Controllers;

[ApiController]
[Route("api/empresa")]
public class EmpresaController(IEmpresaService service) : ControllerBase
{
    /// <summary>Consulta cadastral e score de risco de uma empresa pelo CNPJ (com ou sem máscara).</summary>
    /// <param name="cnpj">CNPJ com 14 dígitos, com ou sem máscara (a barra da máscara é aceita no caminho).</param>
    /// <param name="forcarAtualizacao">Ignora o cache de 24h e refaz as consultas às fontes.</param>
    [HttpGet("{**cnpj}")]
    [ProducesResponseType<EmpresaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<EmpresaResponse>> Get(string cnpj, [FromQuery] bool forcarAtualizacao, CancellationToken ct) =>
        Ok(await service.ConsultarAsync(cnpj, forcarAtualizacao, ct));
}

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", versaoAlgoritmo = "1.0.0", horario = DateTime.UtcNow });
}
