using Microsoft.EntityFrameworkCore;

namespace TurboSquadApp.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
