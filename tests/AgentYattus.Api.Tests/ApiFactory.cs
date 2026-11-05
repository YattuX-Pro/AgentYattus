using Microsoft.AspNetCore.Mvc.Testing;

namespace AgentYattus.Api.Tests;

// Démarre l'API en mémoire pour les tests : pas de port réseau, pas de serveur à lancer.
public sealed class ApiFactory : WebApplicationFactory<Program>;
