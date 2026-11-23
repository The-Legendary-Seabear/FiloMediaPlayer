using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Filo.Api.Infrastructure
{
    /// <summary>
    /// Hides a diagnostic endpoint outside the Development environment.
    /// It responds with 404 as if the endpoint did not exist.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class DevelopmentOnlyAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            var environment = context.HttpContext.RequestServices
                .GetRequiredService<IWebHostEnvironment>();

            if (!environment.IsDevelopment())
                context.Result = new NotFoundResult();
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
