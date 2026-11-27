namespace Nordstein.Core.Common.Security;

/// <summary>
/// What <see cref="IMasterKeyRingInitializer.InitializeAsync"/> created, so that
/// <see cref="IMasterKeyRingInitializer.Remove"/> undoes exactly that and nothing else.
/// </summary>
/// <param name="Path">The full path of the ring file.</param>
/// <param name="RingId">The ring id written into it.</param>
/// <param name="CreatedDirectory">Whether that call created the key directory.</param>
public sealed record MasterKeyRingInitialized(string Path, Guid RingId, bool CreatedDirectory);
