using Xunit;

namespace ClawSharp.UnitTests;

[CollectionDefinition("SessionStorage", DisableParallelization = true)]
public sealed class SessionStorageCollection : ICollectionFixture<SessionStorageCollectionFixture>
{
}

public sealed class SessionStorageCollectionFixture
{
}
