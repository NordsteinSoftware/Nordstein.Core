using System.Collections.Concurrent;
using JetBrains.Annotations;
using Nordstein.Core.Domain;

namespace Nordstein.Core.Storage;

/// <summary>
/// Default <see cref="IEntityCache{TDomainEntity}"/>: a scope-local entry store whose validity is
/// decided process-wide by the shared <see cref="EntityCacheVersions{TDomainEntity}"/>, with a TTL
/// safety net against missed invalidations.
/// </summary>
[UsedImplicitly]
public sealed class EntityCache<TDomainEntity> : IEntityCache<TDomainEntity>
    where TDomainEntity : IDomainEntity
{
    // Background safety net against missed invalidations from out-of-band writes
    // (e.g. a SQL migration, another process). Write-through invalidation is the
    // primary correctness mechanism; TTL just bounds staleness if that ever fails.
    private readonly TimeSpan defaultTtl = TimeSpan.FromMinutes(5);

    private readonly TimeSpan ttl;
    private readonly TimeProvider clock;

    // Shared across every lifetime scope, so a write in one scope invalidates the copies held by
    // all the others — including the root-scope cache a singleton read path reads through.
    // See EntityCacheVersions for why the entries themselves must stay scope-local.
    private readonly EntityCacheVersions<TDomainEntity> versions;

    private readonly ConcurrentDictionary<Guid, Entry> entries = new();
    private Snapshot? allSnapshot;

    /// <summary>
    /// Creates a scope-local cache backed by the shared <paramref name="versions"/> registry.
    /// </summary>
    /// <param name="versions">
    /// The process-wide version counters that coordinate invalidations across DI lifetime scopes.
    /// </param>
    /// <param name="clock">
    /// The time provider used for TTL calculations. Defaults to <see cref="TimeProvider.System"/>
    /// when <see langword="null"/>.
    /// </param>
    /// <param name="ttl">
    /// The safety-net TTL. Defaults to five minutes when <see langword="null"/>. Write-through
    /// invalidation via <see cref="Invalidate"/> and <see cref="InvalidateAll"/> is the primary
    /// correctness mechanism; the TTL only bounds staleness when an out-of-band write bypasses the
    /// application (e.g. a SQL migration or another process).
    /// </param>
    public EntityCache(
        EntityCacheVersions<TDomainEntity> versions,
        TimeProvider? clock = null,
        TimeSpan? ttl = null)
    {
        this.versions = versions;
        this.clock = clock ?? TimeProvider.System;
        this.ttl = ttl ?? defaultTtl;
    }

    /// <summary>
    /// Returns the cached entity for <paramref name="id"/>, or <see langword="null"/> if the entry
    /// is absent, expired, or stale relative to the shared version.
    /// </summary>
    /// <param name="id">The entity identifier to look up.</param>
    /// <returns>
    /// The cached <typeparamref name="TDomainEntity"/>, or <see langword="null"/> on a cache miss.
    /// Stale entries are removed automatically before returning <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Thread-safe for concurrent reads. A version mismatch (caused by a write in any other scope)
    /// evicts the entry and returns <see langword="null"/>, forcing the caller to reload from the
    /// database.
    /// </remarks>
    public TDomainEntity? TryGet(Guid id)
    {
        if (!entries.TryGetValue(id, out Entry? entry))
        {
            return default;
        }

        // A write in any scope bumps the shared version, so a version mismatch means this copy is
        // stale even though our own scope never saw the invalidation.
        if (IsExpired(entry.CachedAt) || entry.Version != versions.VersionOf(id))
        {
            entries.TryRemove(id, out _);
            return default;
        }

        return entry.Entity;
    }

    /// <summary>
    /// Stores <paramref name="entity"/> in the cache under its <see cref="IDomainEntity.Id"/>,
    /// replacing any existing entry for that id.
    /// </summary>
    /// <param name="entity">The entity to cache.</param>
    /// <remarks>
    /// Thread-safe. The entry is stamped with the current time and the entity's current shared
    /// version so that a later <see cref="TryGet"/> call can detect staleness.
    /// </remarks>
    public void Set(TDomainEntity entity)
        => entries[entity.Id] = new Entry(entity, clock.GetUtcNow(), versions.VersionOf(entity.Id));

    /// <summary>
    /// Removes the per-id entry from this scope's cache and bumps the shared version counter for
    /// <paramref name="id"/> so that copies held by other scopes are also treated as stale on their
    /// next read. Also clears the all-entities snapshot.
    /// </summary>
    /// <param name="id">The identifier of the entity to invalidate.</param>
    /// <remarks>
    /// Thread-safe. Call after any write (add, update, remove) to keep the cache consistent.
    /// </remarks>
    public void Invalidate(Guid id)
    {
        versions.Invalidate(id);
        entries.TryRemove(id, out _);
        Volatile.Write(ref allSnapshot, null);
    }

    /// <summary>
    /// Returns the cached all-entities snapshot, or <see langword="null"/> if the snapshot is
    /// absent, expired, or stale relative to the shared all-entities version.
    /// </summary>
    /// <returns>
    /// The complete list of cached entities, or <see langword="null"/> on a snapshot miss. The
    /// stale snapshot is evicted automatically before returning <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Thread-safe via <see cref="Volatile"/> reads. A version mismatch (caused by any write in any
    /// scope that calls <see cref="Invalidate"/> or <see cref="InvalidateAll"/>) invalidates the
    /// snapshot.
    /// </remarks>
    public IReadOnlyList<TDomainEntity>? TryGetAll()
    {
        Snapshot? snap = Volatile.Read(ref allSnapshot);
        if (snap is null)
        {
            return null;
        }

        if (IsExpired(snap.CachedAt) || snap.AllVersion != versions.AllVersion)
        {
            Volatile.Write(ref allSnapshot, null);
            return null;
        }

        return snap.Entities;
    }

    /// <summary>
    /// Stores the all-entities snapshot and refreshes the per-id entry for every entity in
    /// <paramref name="entities"/>.
    /// </summary>
    /// <param name="entities">The complete set of entities to cache.</param>
    /// <remarks>
    /// Thread-safe. The snapshot is stamped with the current time and the current all-entities
    /// version so that a later <see cref="TryGetAll"/> call can detect staleness. Per-id entries
    /// are also refreshed so <see cref="TryGet"/> can serve individual lookups from the warm cache.
    /// </remarks>
    public void SetAll(IReadOnlyList<TDomainEntity> entities)
    {
        DateTimeOffset now = clock.GetUtcNow();
        long allVersion = versions.AllVersion;
        foreach (TDomainEntity entity in entities)
        {
            entries[entity.Id] = new Entry(entity, now, versions.VersionOf(entity.Id));
        }
        Volatile.Write(ref allSnapshot, new Snapshot(entities, now, allVersion));
    }

    /// <summary>
    /// Bumps the shared all-entities version counter and clears the local all-entities snapshot so
    /// that copies in all scopes are treated as stale on their next <see cref="TryGetAll"/> call.
    /// Per-id entries are <em>not</em> cleared.
    /// </summary>
    /// <remarks>
    /// Thread-safe. Call after bulk operations (e.g. <c>RemoveAllAsync</c>) where per-entity
    /// invalidation would be expensive. Individual entity reads continue to be served from the
    /// per-id cache until their own entries expire or are explicitly invalidated.
    /// </remarks>
    public void InvalidateAll()
    {
        versions.InvalidateAll();
        Volatile.Write(ref allSnapshot, null);
    }

    private bool IsExpired(DateTimeOffset cachedAt)
        => clock.GetUtcNow() - cachedAt > ttl;

    private sealed record Entry(TDomainEntity Entity, DateTimeOffset CachedAt, long Version);
    private sealed record Snapshot(IReadOnlyList<TDomainEntity> Entities, DateTimeOffset CachedAt, long AllVersion);
}
