using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using HomeShowing.BuildingBlocks.Aws;

namespace HomeShowing.Listings.Infrastructure;

public sealed record PhotoUpload(string UploadUrl, string Key, string DisplayUrl, DateTime ExpiresAt);

/// <summary>
/// Listing photos live in S3. The browser uploads straight to the bucket with a presigned PUT, the API never
/// proxies bytes. Listings store S3 keys; responses carry presigned GET URLs, and a URL the API handed out is
/// turned back into its key when the client saves the listing.
/// </summary>
public interface IPhotoStorage
{
    bool Enabled { get; }
    PhotoUpload CreateUpload(string tenantId, string listingId, string contentType);
    string ToDisplayUrl(string stored);
    /// <summary>Returns the S3 key for one of our signed URLs, the value itself otherwise.</summary>
    string ToStoredValue(string value);
    bool IsOwnKey(string tenantId, string listingId, string value);
}

public sealed class DisabledPhotoStorage : IPhotoStorage
{
    public bool Enabled => false;
    public PhotoUpload CreateUpload(string tenantId, string listingId, string contentType) => throw new InvalidOperationException("Photo uploads need AWS (Aws:Enabled).");
    public string ToDisplayUrl(string stored) => stored;
    public string ToStoredValue(string value) => value;
    public bool IsOwnKey(string tenantId, string listingId, string value) => false;
}

public sealed class S3PhotoStorage : IPhotoStorage
{
    public const string KeyPrefix = "listings/";
    private static readonly HashSet<string> Types = ["image/jpeg", "image/png", "image/webp"];
    private readonly AwsOptions _options;
    private readonly AmazonS3Client _signer;

    public S3PhotoStorage(AwsOptions options)
    {
        _options = options;
        var config = new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region) };
        var publicUrl = options.PublicServiceUrl ?? options.ServiceUrl;
        if (!string.IsNullOrWhiteSpace(publicUrl)) { config.ServiceURL = publicUrl; config.ForcePathStyle = true; }
        // Signing is offline; credentials come from the default chain (AWS_ACCESS_KEY_ID locally, the task role in ECS).
        _signer = new AmazonS3Client(FallbackCredentialsFactory.GetCredentials(), config);
    }

    public bool Enabled => true;

    public PhotoUpload CreateUpload(string tenantId, string listingId, string contentType)
    {
        if (!Types.Contains(contentType)) throw new ArgumentException("Upload a JPEG, PNG or WebP image.");
        var ext = contentType.Split('/')[1].Replace("jpeg", "jpg");
        var key = $"{KeyPrefix}{tenantId}/{listingId}/{Guid.NewGuid():N}.{ext}";
        var expires = DateTime.UtcNow.AddMinutes(10);
        var put = _signer.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.PhotoBucket, Key = key, Verb = HttpVerb.PUT, Expires = expires, ContentType = contentType,
            Protocol = Protocol(),
        });
        return new PhotoUpload(put, key, ToDisplayUrl(key), expires);
    }

    public string ToDisplayUrl(string stored) =>
        !stored.StartsWith(KeyPrefix, StringComparison.Ordinal) ? stored : _signer.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.PhotoBucket, Key = stored, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.AddHours(12), Protocol = Protocol(),
        });

    public string ToStoredValue(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        var path = uri.AbsolutePath.TrimStart('/');
        var bucketPrefix = _options.PhotoBucket + "/";
        if (path.StartsWith(bucketPrefix + KeyPrefix, StringComparison.Ordinal)) return path[bucketPrefix.Length..];      // path style
        if (uri.Host.StartsWith(_options.PhotoBucket + ".", StringComparison.Ordinal) && path.StartsWith(KeyPrefix)) return path; // virtual host
        return value;
    }

    public bool IsOwnKey(string tenantId, string listingId, string value) =>
        value.StartsWith($"{KeyPrefix}{tenantId}/{listingId}/", StringComparison.Ordinal);

    private Amazon.S3.Protocol Protocol() =>
        (_options.PublicServiceUrl ?? _options.ServiceUrl)?.StartsWith("http://") == true ? Amazon.S3.Protocol.HTTP : Amazon.S3.Protocol.HTTPS;
}
