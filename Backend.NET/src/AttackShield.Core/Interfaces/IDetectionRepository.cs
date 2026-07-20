using AttackShield.Core.Entities;

namespace AttackShield.Core.Interfaces;

public interface IDetectionRepository : IRepository<Detection>
{
    /// <summary>
    /// Finds a recent detection matching type+location within the given window.
    /// Backs the de-dup gate in POST /api/detections/receive.
    /// </summary>
    Task<Detection?> FindRecentAsync(
        string weaponType,
        string location,
        DateTime since,
        string? detectionType = null,
        CancellationToken ct = default);

    Task<long> CountByTypeAsync(string detectionType, CancellationToken ct = default);

    Task<long> CountByTypeSinceAsync(string detectionType, DateTime since, CancellationToken ct = default);

    /// <summary>Recent detections for a user (or all when userId is null), newest first.</summary>
    Task<IReadOnlyList<Detection>> GetRecentAsync(string? userId, int limit, CancellationToken ct = default);
}
