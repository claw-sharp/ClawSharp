using Xunit;

namespace ClawSharp.IntegrationTests;

[CollectionDefinition("SessionStorage", DisableParallelization = true)]
public sealed class SessionStorageCollection : ICollectionFixture<SessionStorageCollectionFixture>
{
}

public sealed class SessionStorageCollectionFixture
{
}
