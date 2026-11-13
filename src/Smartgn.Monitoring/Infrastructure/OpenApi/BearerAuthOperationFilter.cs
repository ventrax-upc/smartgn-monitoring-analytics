using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Com.Ventrax.Monitoring.Infrastructure.OpenApi;

public sealed class BearerAuthOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("bearerAuth")] = [],
        });
    }
}
