using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using BackgroundJobs.Core;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Api.Common;

/// <summary>
/// Turns business rule violations into RFC 9457 problem responses with a stable <c>code</c> field.
/// Anything else falls through to the default handler (500 without details).
/// </summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (code, detail) = exception switch
        {
            DomainException domain => (domain.Code, domain.Message),
            DbUpdateConcurrencyException => (ErrorCodes.ConcurrentUpdate, "The run changed while you were acting on it. Read it again and retry."),
            _ => (null, null),
        };

        if (code is null)
        {
            return false;
        }

        var status = ErrorStatuses.StatusFor(code);
        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail,
            Extensions = { ["code"] = code },
        };

        if (exception is DomainException { Details.Count: > 0 } withDetails)
        {
            foreach (var (key, value) in withDetails.Details)
            {
                problem.Extensions[key] = value;
            }
        }


        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
