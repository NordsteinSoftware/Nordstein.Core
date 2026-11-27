using Autofac;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Conversion;
using Nordstein.Core.Common.Conversion.Internal;
using Nordstein.Core.Common.Cryptography;
using Nordstein.Core.Common.Cryptography.Internal;
using Nordstein.Core.Common.Hosting;
using Nordstein.Core.Common.Hosting.Internal;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Io.Internal;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Common.Lifecycle.Internal;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Common.Random.Internal;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Common.Security.Internal;
using Nordstein.Core.Common.Serialization;
using Nordstein.Core.Common.Serialization.Internal;
using Nordstein.Core.Common.Settings;
using Nordstein.Core.Common.Settings.Internal;
using Nordstein.Core.Common.Tabular;
using Nordstein.Core.Common.Tabular.Internal;
using Nordstein.Core.Common.Text;
using Nordstein.Core.Common.Text.Internal;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Common.Time.Internal;

namespace Nordstein.Core.Common;

public class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder
            .RegisterType<TypeConverter>()
            .As<ITypeConverter>()
            .SingleInstance();

        builder.RegisterType<SeededRandom>()
            .AsSelf();

        builder
            .Register(c => c.Resolve<SeededRandom.Factory>()(seed: 420))
            .As<IRandom>()
            .SingleInstance();

        builder.RegisterType<JsonSerializer>()
            .As<ISerializer>()
            .SingleInstance();

        builder.RegisterType<AsyncLock>().As<IAsyncLock>().SingleInstance();

        builder.RegisterType<SystemClock>().As<IClock>().SingleInstance();

        builder.RegisterType<AppVersion>().As<IAppVersion>().SingleInstance();

        builder.RegisterType<NullHostedService>().AsSelf();

        builder.RegisterType<TempDirectory>()
            .As<ITempDirectory>()
            .OwnedByLifetimeScope();

        builder.RegisterType<AeadStreamCodec>().As<IAeadStreamCodec>().SingleInstance();

        builder.RegisterType<AeadKeyWrap>().As<IAeadKeyWrap>().SingleInstance();

        builder.RegisterType<Pbkdf2PasswordHasher>().As<IPasswordHasher>().SingleInstance();

        // The default secret hasher yields to a product's own: a registration made before this
        // module keeps the default slot (and, made after it, wins as the last registration).
        builder.RegisterType<Sha256SecretHasher>()
            .As<ISecretHasher>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<DirectorySync>().As<IDirectorySync>().SingleInstance();

        builder.RegisterType<ExclusiveFileLock>().As<IExclusiveFileLock>().SingleInstance();

        builder.RegisterType<FilesystemProbe>().As<IFilesystemProbe>().SingleInstance();

        builder.RegisterType<DurableFilePublisher>().As<IDurableFilePublisher>().SingleInstance();

        builder.RegisterType<PrivateFileSpool>().As<IPrivateFileSpool>().SingleInstance();

        builder.RegisterType<SecretFileLoader>().As<ISecretFileLoader>().SingleInstance();

        builder.RegisterType<PathContainment>().As<IPathContainment>().SingleInstance();

        builder.RegisterType<CsvReader>().As<ICsvReader>().SingleInstance();

        builder.RegisterType<CsvRowWriter>().As<ICsvRowWriter>().SingleInstance();

        builder.RegisterType<TextNormalizer>().As<ITextNormalizer>().SingleInstance();

        // Built once from every registered SettingRoot and the product's one SettingNaming; it
        // resolves only in a container that registered a SettingNaming.
        builder.RegisterType<SettingRegistry>().As<ISettingRegistry>().SingleInstance();

        // The wall clock for scheduling. A host or test that registered its own TimeProvider first
        // keeps it; IClock stays the seam for "what time is it" in feature code.
        builder.RegisterInstance(TimeProvider.System).As<TimeProvider>().IfNotRegistered(typeof(TimeProvider));
    }
}
