using BackgroundJobs.Api.Features.Audit;
using BackgroundJobs.Infrastructure.Persistence;

namespace BackgroundJobs.Api.Features.Dashboard;

/// <summary>
/// Records every change made from the dashboard (requeue, delete, trigger a recurring job) with the
/// signed-in admin, so actions taken outside the API end up in the same audit trail.
/// </summary>
internal sealed class DashboardAuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AuditTrail audit, JobsDbContext db)
    {
        var request = context.Request;
        var action = PathString.Empty;
        var isAction = HttpMethods.IsPost(request.Method)
            && request.Path.StartsWithSegments(DashboardAuth.Path, out action)
            && !action.StartsWithSegments("/stats", StringComparison.OrdinalIgnoreCase);

        string[] jobs = [];
        if (isAction && request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(context.RequestAborted);
            jobs = form["jobs[]"].Where(j => !string.IsNullOrEmpty(j)).Select(j => j!).ToArray();
        }

        await next(context);

        if (isAction && context.Response.StatusCode < 400 && context.User.Identity?.IsAuthenticated == true)
        {
            audit.Record("dashboard.action", "hangfire", null, new { action = action.Value, jobs });
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
