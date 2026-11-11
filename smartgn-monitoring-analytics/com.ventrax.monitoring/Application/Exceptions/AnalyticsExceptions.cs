namespace Com.Ventrax.Monitoring.Application.Exceptions;

// 403: el usuario no tiene permiso vigente sobre ese medidor o función.
public class MeterAccessDeniedException(Guid meterId, string function)
    : Exception($"Access to meter {meterId} for function {function} is not granted.")
{
    public Guid MeterId { get; } = meterId;
}

// 403: la funcionalidad necesita plan Pro vigente.
public class ProSubscriptionRequiredException()
    : Exception("An active Pro subscription is required for this feature.");

// 503: una dependencia (IAM, Billing, Telemetry) no respondió.
// Nunca se interpreta como "Free" ni como "sin permiso".
public class DependencyUnavailableException(string dependency, Exception? inner = null)
    : Exception($"{dependency} is temporarily unavailable.", inner)
{
    public string Dependency { get; } = dependency;
}
