using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Vamoose.Api.Infrastructure;

public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class NotFoundException(string message = "Not found.") : AppException(message, StatusCodes.Status404NotFound);

public class ForbiddenException(string message = "You can't do that on this trip.") : AppException(message, StatusCodes.Status403Forbidden);

public class ConflictException(string message) : AppException(message, StatusCodes.Status409Conflict);

public class BadRequestException(string message) : AppException(message, StatusCodes.Status400BadRequest);

public class AgentUnavailableException(string message) : AppException(message, StatusCodes.Status503ServiceUnavailable);

public class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, detail) = exception switch
        {
            AppException app => (app.StatusCode, app.Message),
            _ => (StatusCodes.Status500InternalServerError, "Something went wrong. Please try again."),
        };
        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled error on {Path}", context.Request.Path);
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Detail = detail },
        });
    }
}
