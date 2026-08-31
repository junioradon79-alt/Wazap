using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Wazap.Application.Exceptions;

namespace Wazap.API.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Exception non gérée : {Message}", exception.Message);

        var problemDetails = exception switch
        {
            ArgumentException =>
                Build(StatusCodes.Status400BadRequest, "Requête invalide", exception.Message),
            UnauthorizedAccessException =>
                Build(StatusCodes.Status401Unauthorized, "Non autorisé", exception.Message),
            ForbiddenException =>
                Build(StatusCodes.Status403Forbidden, "Accès refusé", exception.Message),
            InvalidOperationException =>
                Build(StatusCodes.Status409Conflict, "État de commande invalide", exception.Message),
            KeyNotFoundException =>
                Build(StatusCodes.Status404NotFound, "Ressource introuvable", exception.Message),
            _ =>
                Build(StatusCodes.Status500InternalServerError, "Erreur interne du serveur",
                     "Une erreur interne est survenue.")
        };

        problemDetails.Instance = httpContext.Request.Path;

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static ProblemDetails Build(int status, string title, string detail) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = detail
        };
}
