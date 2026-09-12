namespace CrockeryFactory.Persistence;

/// <summary>
/// Database schema per module. Kept as constants so the module a table belongs to is
/// visible in one file rather than spread across fifteen ToTable calls.
/// </summary>
public static class ModuleSchemas
{
    public const string Catalogue = "catalogue";
    public const string Stock = "stock";
    public const string Production = "production";
    public const string Sales = "sales";
    public const string Shared = "shared";
    /// <summary>Not "identity": that is a reserved word in T-SQL and would force
    /// brackets on every hand-written query against these tables.</summary>
    public const string Identity = "auth";
}

/// <summary>
/// Namespace fragments used to apply each module's EF configurations separately.
///
/// The specification's sample applies configurations one assembly per module. This
/// solution keeps modules in one assembly and separates them by namespace instead, so
/// the same per-module explicitness is preserved without twelve project files. Splitting
/// into real assemblies later is a project-file change that touches no entity and no
/// configuration class.
/// </summary>
public static class ModuleNamespaces
{
    public const string Catalogue = ".Catalogue.";
    public const string Stock = ".Stock.";
    public const string Production = ".Production.";
    public const string Sales = ".Sales.";
    public const string Staff = ".Staff.";
    public const string Shared = ".Shared.";
}
