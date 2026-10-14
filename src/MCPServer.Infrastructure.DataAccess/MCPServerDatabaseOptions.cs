namespace MCPServer.Infrastructure.Data;

/// <summary>
/// Database connection / behaviour options.
/// Bound from the <c>"Database"</c> configuration section.
/// </summary>
public sealed class MCPServerDatabaseOptions
{
    public static string SectionName => "Database";

    /// <summary>Returns the configuration section name used for binding.</summary>
    public string GetSectionName() => SectionName;

    /// <summary>PostgreSQL connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Enables EF Core sensitive-data logging (development only).</summary>
    public bool EnableSensitiveDataLogging { get; set; }

    /// <summary>Enables EF Core detailed error messages (development only).</summary>
    public bool EnableDetailedErrors { get; set; }
}
