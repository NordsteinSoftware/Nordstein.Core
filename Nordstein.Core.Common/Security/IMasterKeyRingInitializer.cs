namespace Nordstein.Core.Common.Security;

/// <summary>
/// Creates the master key ring file once: a fresh 32-byte key from the system CSPRNG as the one active key
/// (version 1), its canary, and the given ring id, in the key directory of <see cref="MasterKeyRingOptions"/>.
/// It never replaces or changes an existing ring.
/// </summary>
/// <remarks>
/// <para>
/// <b>Custody.</b> A missing key directory is created with mode <c>0700</c> (its parent must exist); an existing
/// one must be empty and is set to <c>0700</c>. The file is opened create-new (atomic: a ring that appears
/// between the check and the open is never overwritten), set to mode <c>0600</c> on its open handle whatever
/// the umask, written unbuffered, flushed to disk, and then the key directory is flushed
/// (<see cref="Io.IDirectorySync"/>) so the entry survives a crash; a created directory's parent is flushed
/// after its creation. On Windows no mode is set; the file inherits the directory's access control.
/// </para>
/// <para>
/// <b>Failure.</b> A refusal (<see cref="MasterKeyRingFailure.AlreadyInitialized"/>,
/// <see cref="MasterKeyRingFailure.PathInExcludedRoot"/>, <see cref="MasterKeyRingFailure.FileRejected"/>)
/// changes nothing. A failure after the open deletes the partial or unflushed ring this call created, and the
/// key directory if this call created it, before it propagates. A ring that already existed, or appeared, is
/// never touched. A crash during the write can leave a partial ring under the final name: it fails to load
/// and blocks a second initialization until an operator removes it, and nothing was ever wrapped under it.
/// </para>
/// <para>
/// <b>Placement.</b> The excluded-root check runs on the configured paths before the write and again after it:
/// a key directory swapped for a link into an excluded root in between is refused, and the ring written there
/// is deleted when it still is the one this call wrote.
/// </para>
/// <para>Stateless and thread-safe; <see cref="Module"/> registers one instance per container where the host
/// registered <see cref="MasterKeyRingOptions"/>.</para>
/// </remarks>
public interface IMasterKeyRingInitializer
{
    /// <summary>
    /// Runs every refusal of <see cref="InitializeAsync"/> without writing anything, so a host can refuse before
    /// it starts other first-run work.
    /// </summary>
    /// <exception cref="MasterKeyRingException">
    /// <see cref="MasterKeyRingFailure.AlreadyInitialized"/>: a file (or a link) exists at the ring path, or the
    /// key directory is not empty. <see cref="MasterKeyRingFailure.PathInExcludedRoot"/>: the ring path may lie
    /// inside an excluded root. <see cref="MasterKeyRingFailure.FileRejected"/>: a path cannot be resolved.
    /// </exception>
    void Precheck();

    /// <summary>
    /// Runs <see cref="Precheck"/> again and writes the ring.
    /// </summary>
    /// <param name="ringId">The ring's identifier; not <see cref="Guid.Empty"/>.</param>
    /// <param name="cancellationToken">
    /// Observed before anything is created and during the write; once the ring is written the call completes
    /// its flushes. A cancelled write removes what this call created.
    /// </param>
    /// <returns>What was created.</returns>
    /// <exception cref="ArgumentException"><paramref name="ringId"/> is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="MasterKeyRingException">
    /// A refusal of <see cref="Precheck"/>; <see cref="MasterKeyRingFailure.AlreadyInitialized"/> also when a ring
    /// appeared between the check and the open (with the open's <see cref="IOException"/> as the inner exception).
    /// </exception>
    /// <exception cref="IOException">
    /// The key directory's parent is missing, or creating, writing or flushing failed; the inner state is rolled
    /// back as described on the type.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<MasterKeyRingInitialized> InitializeAsync(Guid ringId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a completed <see cref="InitializeAsync"/>, for a host whose later first-run step failed: deletes
    /// the ring file, and the key directory when <paramref name="created"/> says that call created it and it is
    /// empty. Not cancellable: a rollback that stops half-way would leave the ring behind.
    /// </summary>
    /// <param name="created">What <see cref="InitializeAsync"/> returned.</param>
    /// <exception cref="ArgumentNullException"><paramref name="created"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The file at the path is not the ring with <see cref="MasterKeyRingInitialized.RingId"/> (replaced since, or
    /// unreadable); nothing is deleted.
    /// </exception>
    /// <exception cref="IOException">Deleting failed.</exception>
    void Remove(MasterKeyRingInitialized created);
}
