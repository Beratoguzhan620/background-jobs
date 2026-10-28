using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BackgroundJobs.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Background Jobs",
            Version = "1.0",
            Description = """
                The background work of a small shop's back office: order imports, sales reports and housekeeping,
                run by Hangfire workers on PostgreSQL.

                **Getting started**

                1. `POST /api/auth/login` with the admin account from configuration and paste the `accessToken`
                   into *Authentication*.
                2. Start work: upload an order file to `POST /api/imports` or ask for a report at `POST /api/reports`.
                3. Follow it at `GET /api/jobs/{id}`: status, progress, attempts and the result.

                **Roles:** admins start, cancel and retry jobs and manage users; viewers read status and results.
                Admins can also sign in to the Hangfire dashboard at `/hangfire`.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `duplicate_job`
                or `invalid_state`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/background-jobs/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}
