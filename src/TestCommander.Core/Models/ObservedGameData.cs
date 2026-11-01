namespace TestCommander.Core.Models;

public sealed record ResourceSnapshot(
    string VillageName,
    int? Wood,
    int? Clay,
    int? Iron,
    int? Crop,
    int? WarehouseCapacity,
    int? GranaryCapacity);

public sealed record QueueObservation(
    string QueueType,
    string ItemName,
    int? Level,
    DateTimeOffset? CompletesAt);

public sealed record TroopMovementObservation(
    string MovementType,
    string Origin,
    string Destination,
    DateTimeOffset? ArrivesAt,
    string Description);

public sealed record ParsedGameData(
    ResourceSnapshot? Resources,
    IReadOnlyList<QueueObservation> Queues,
    IReadOnlyList<TroopMovementObservation> TroopMovements);
