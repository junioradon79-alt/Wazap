using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wazap.API.Services;

namespace Wazap.API.Controllers;

/// <summary>
/// Avis des clients sur les livreurs (réservé admin) : liste détaillée (numéro client
/// masqué) + synthèse moyenne par livreur. La réponse du livreur y est visible.
/// </summary>
[ApiController]
[Route("api/admin/ratings")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public class RatingsController : ControllerBase
{
    private readonly RiderRatingService _riderRatings;

    public RatingsController(RiderRatingService riderRatings)
    {
        _riderRatings = riderRatings;
    }

    // GET: api/admin/ratings
    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _riderRatings.ListForAdminAsync());
}