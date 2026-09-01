using Microsoft.AspNetCore.Mvc;
using Wazap.Domain.Configuration;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PacksController : ControllerBase
{
    private readonly IReadOnlyList<PackConfiguration> _packs;

    public PacksController(IReadOnlyList<PackConfiguration> packs) => _packs = packs;

    /// <summary>
    /// Catalogue des packs prépayés (payé à l'usage, sans abonnement).
    /// </summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(_packs);
}