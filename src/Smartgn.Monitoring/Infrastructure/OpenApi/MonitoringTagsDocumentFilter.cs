using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Com.Ventrax.Monitoring.Infrastructure.OpenApi;

public sealed class MonitoringTagsDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        document.Tags =
        new HashSet<OpenApiTag>
        {
            new OpenApiTag
            {
                Name = "Analytics",
                Description = "Authorized live readings, consumption history and Pro reports.",
            },
        };
    }
}
