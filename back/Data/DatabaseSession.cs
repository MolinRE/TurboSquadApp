namespace TurboSquadApp.Data;

public sealed class DatabaseSession(AppDbContext context)
{
    public AppDbContext Context => context;
}
