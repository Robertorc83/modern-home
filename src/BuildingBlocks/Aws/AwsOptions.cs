namespace HomeShowing.BuildingBlocks.Aws;

/// <summary>
/// AWS settings. Locally everything points at LocalStack through <see cref="ServiceUrl"/>; in real AWS the URL is
/// left empty and the SDK uses the regional endpoints and the task role. Only configuration changes.
/// </summary>
public sealed class AwsOptions
{
    public bool Enabled { get; set; }
    public string Region { get; set; } = "us-east-1";
    /// <summary>Endpoint the services use (e.g. http://localstack:4566). Empty in real AWS.</summary>
    public string? ServiceUrl { get; set; }
    /// <summary>Endpoint the browser can reach, used only to sign URLs (e.g. http://localhost:4566).</summary>
    public string? PublicServiceUrl { get; set; }
    public string PhotoBucket { get; set; } = "listing-photos";
    public string NotificationsQueue { get; set; } = "showing-notifications";
    public string SenderEmail { get; set; } = "no-reply@showings.local";
}
