using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Scheduling.Domain;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HomeShowing.Scheduling.Infrastructure;

public sealed record ShowingChange(string ResumeToken, string Operation, string ShowingId, string Status, string ListingAddress, string BuyerName);

/// <summary>
/// Wraps a MongoDB change stream on <c>showings</c>, filtered server side to the caller's tenant and,
/// for agents and buyers, to their own showings. The resume token is handed to the client as the SSE
/// event id, so a reconnect continues exactly where it left off.
/// </summary>
public sealed class ShowingChangeFeed(MongoDb db, ITenantContext tenant)
{
    public async IAsyncEnumerable<ShowingChange?> WatchAsync(string? resumeToken, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var match = new BsonDocument
        {
            { "operationType", new BsonDocument("$in", new BsonArray { "insert", "update", "replace" }) },
            { "fullDocument.tenantId", tenant.TenantId },
        };
        if (tenant.IsInRole(Roles.Agent)) match.Add("fullDocument.agentId", tenant.UserId);
        else if (tenant.IsInRole(Roles.Buyer)) match.Add("fullDocument.buyerId", tenant.UserId);

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<Showing>>().Match(new BsonDocumentFilterDefinition<ChangeStreamDocument<Showing>>(match));
        var options = new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup, MaxAwaitTime = TimeSpan.FromSeconds(10) };
        if (!string.IsNullOrWhiteSpace(resumeToken) && resumeToken.All(Uri.IsHexDigit))
            options.ResumeAfter = new BsonDocument("_data", resumeToken);

        var col = db.Collection<Showing>(ShowingRepository.CollectionName);
        IChangeStreamCursor<ChangeStreamDocument<Showing>> cursor;
        try { cursor = await col.WatchAsync(pipeline, options, ct); }
        catch (MongoCommandException) when (options.ResumeAfter is not null)
        {
            options.ResumeAfter = null; // token too old (oplog rolled over): start fresh, the client refetches anyway
            cursor = await col.WatchAsync(pipeline, options, ct);
        }

        using (cursor)
        {
            while (!ct.IsCancellationRequested)
            {
                bool more;
                try { more = await cursor.MoveNextAsync(ct); }
                catch (OperationCanceledException) { yield break; }
                if (!more) yield break;
                var any = false;
                foreach (var c in cursor.Current)
                {
                    any = true;
                    var doc = c.FullDocument;
                    yield return new ShowingChange(c.ResumeToken["_data"].AsString, c.OperationType.ToString().ToLowerInvariant(),
                        doc?.Id ?? c.DocumentKey["_id"].ToString()!, doc?.Status.ToString() ?? "", doc?.ListingAddress ?? "", doc?.BuyerName ?? "");
                }
                if (!any) yield return null; // idle tick: lets the endpoint send a heartbeat
            }
        }
    }
}
