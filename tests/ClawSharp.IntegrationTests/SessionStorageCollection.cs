// TS origin: ./utils/envUtils.ts, ./utils/sessionStorage.ts, ./utils/sessionStoragePortable.ts
using Xunit;

namespace ClawSharp.IntegrationTests;

[CollectionDefinition("SessionStorage", DisableParallelization = true)]
public sealed class SessionStorageCollection : ICollectionFixture<SessionStorageCollectionFixture>
{
}

public sealed class SessionStorageCollectionFixture
{
}
