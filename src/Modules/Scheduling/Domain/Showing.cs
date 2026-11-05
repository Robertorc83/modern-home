using HomeShowing.BuildingBlocks.Persistence;
using NodaTime;
using NodaTime.Text;

namespace HomeShowing.Scheduling.Domain;

public enum ShowingStatus { Requested, Confirmed, Declined, Cancelled }

public sealed class Showing : TenantDocument
{
    public string ListingId { get; set; } = "";
    public string ListingAddress { get; set; } = "";
    public string ListingCity { get; set; } = "";
    public string ListingTimeZone { get; set; } = "";
    public string BuyerId { get; set; } = "";
    public string BuyerName { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public Instant SlotStartUtc { get; set; }
    public Instant SlotEndUtc { get; set; }
    public ShowingStatus Status { get; set; }
    public string? Source { get; set; }

    /// <summary>
    /// Present only while the showing holds the slot (Requested or Confirmed). A unique partial index on
    /// (tenantId, activeSlotKey) is what prevents double booking: the database, not a read-then-write check.
    /// </summary>
    public string? ActiveSlotKey { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }

    public static string SlotKey(string listingId, Instant slotStartUtc) => $"{listingId}:{InstantPattern.ExtendedIso.Format(slotStartUtc)}";

    public bool IsActive => Status is ShowingStatus.Requested or ShowingStatus.Confirmed;
}

/// <summary>A short lived claim on a slot while a buyer is on the confirmation screen. MongoDB deletes it
/// at <see cref="ExpiresAt"/> through a TTL index; no cleanup job.</summary>
public sealed class SlotHold : TenantDocument
{
    public string ListingId { get; set; } = "";
    public string ActiveSlotKey { get; set; } = "";
    public string BuyerId { get; set; } = "";
    public Instant SlotStartUtc { get; set; }
    public Instant SlotEndUtc { get; set; }
    public Instant ExpiresAt { get; set; }
}
