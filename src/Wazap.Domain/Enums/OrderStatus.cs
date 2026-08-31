namespace Wazap.Domain.Enums;

public enum OrderStatus
{
    PendingVendorConfirmation = 1,
    VendorConfirmed = 2,
    AwaitingRiderAcceptance = 3,
    RiderAssigned = 4,
    ReadyForPickup = 5,
    PickedUp = 6,
    InTransit = 7,
    Delivered = 8,
    Cancelled = 9
}
