namespace Orders.Realtime.Api;

public enum OrderStatus
{
    Unknown = 0,
    Pending = 1,
    Processing = 2,
    Ok = 3,
    ProcessedWithErrors = 4
}
