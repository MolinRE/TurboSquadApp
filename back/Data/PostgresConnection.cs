using Npgsql;

namespace TurboSquadApp.Data;

public static class PostgresConnection
{
    public static string Build(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres must be configured before using PostgreSQL.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var user = configuration["POSTGRES_USER"];
        var password = configuration["POSTGRES_PASSWORD"];

        if (!string.IsNullOrWhiteSpace(user))
        {
            builder.Username = user;
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            builder.Password = password;
        }

        return builder.ConnectionString;
    }
}
