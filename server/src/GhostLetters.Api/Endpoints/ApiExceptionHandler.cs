using GhostLetters.Application;
using GhostLetters.Domain.Game;
using Microsoft.AspNetCore.Diagnostics;

namespace GhostLetters.Api.Endpoints;

/// <summary>Ошибки приложения и правил → RFC 7807 с кодом из контракта в title и code.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        (int status, string code)? mapped = exception switch
        {
            AppException app => (app.Status, app.Code),
            GameRuleException rule => (rule.Code == GameRuleException.Codes.UnknownPlayer ? 403 : 409, rule.Code),
            BadHttpRequestException => (400, AppException.Codes.Validation),
            _ => null,
        };

        if (mapped is not { } m)
        {
            return false;
        }

        http.Response.StatusCode = m.status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails =
            {
                Status = m.status,
                Title = m.code,
                Detail = exception.Message,
                Extensions = { ["code"] = m.code },
            },
        });
    }
}
