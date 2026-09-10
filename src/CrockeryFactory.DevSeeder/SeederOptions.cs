namespace CrockeryFactory.DevSeeder;

/// <summary>Command line for the seeder. Deliberately small and deliberately awkward to run.</summary>
public sealed record SeederOptions(
    int Months,
    bool Acknowledged,
    string? ConnectionString)
{
    public const string AcknowledgeFlag = "--i-understand";

    public static SeederOptions Parse(string[] args)
    {
        var months = 3;
        var acknowledged = false;
        string? connectionString = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--months" when i + 1 < args.Length && int.TryParse(args[i + 1], out var m):
                    months = m;
                    i++;
                    break;

                case "--years" when i + 1 < args.Length && int.TryParse(args[i + 1], out var y):
                    months = y * 12;
                    i++;
                    break;

                case AcknowledgeFlag:
                    acknowledged = true;
                    break;

                case "--connection" when i + 1 < args.Length:
                    connectionString = args[i + 1];
                    i++;
                    break;
            }
        }

        return new SeederOptions(Math.Clamp(months, 1, 120), acknowledged, connectionString);
    }

    public static string Usage =>
        """
        Generates believable development data. Never run this against a factory database.

          dotnet run -- --months 3  --i-understand
          dotnet run -- --years  5  --i-understand      (the PF-14 performance dataset)

        Options:
          --months <n>      Months of trading to generate (default 3)
          --years <n>       Shorthand for --months (n * 12)
          --connection <cs> Override the connection string
          --i-understand    Required. Confirms this is not a factory database.

        Refuses to run unless DOTNET_ENVIRONMENT is Development, the flag above is
        present, and the database contains no dispatches.
        """;
}
