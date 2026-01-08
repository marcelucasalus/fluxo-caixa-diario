using Xunit;

namespace TestesIntegracao.Fixtures
{
    [CollectionDefinition("IntegrationTest Collection")]
    public class IntegrationTestCollection : ICollectionFixture<FluxoCaixaWebApplicationFactory>
    {
        // Esta classe não contém testes, apenas define a collection
    }
}