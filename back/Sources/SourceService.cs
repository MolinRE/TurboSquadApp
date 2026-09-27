using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;

namespace TurboSquadApp.Sources;

public sealed record CreateSourceInput(string Title, string Text);
public sealed record SourceSummary(Guid Id, string Title, DateTimeOffset CreatedAt);
public sealed record SourceView(Guid Id, string Title, string Text, DateTimeOffset CreatedAt);

public sealed class SourceService(AppDbContext db, TimeProvider clock)
{
    public async Task<IResult> ListAsync(CancellationToken cancellationToken)
    {
        var sources = await db.Sources.AsNoTracking().OrderByDescending(source => source.CreatedAt)
            .Select(source => new SourceSummary(source.Id, source.Title, source.CreatedAt))
            .ToListAsync(cancellationToken);
        return Results.Ok(sources);
    }

    public async Task<IResult> CreateAsync(CreateSourceInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Text))
            return Results.BadRequest(new { message = "Укажите название и текст Источника" });
        if (input.Title.Length > 200 || input.Text.Length > 100_000)
            return Results.BadRequest(new { message = "Название или текст Источника слишком длинные" });
        var source = new SourceRecord
        {
            Id = Guid.NewGuid(), Title = input.Title.Trim(), Text = input.Text.Trim(),
            CreatedAt = clock.GetUtcNow(),
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/cms/sources/{source.Id}", View(source));
    }

    public async Task<IResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return source is null ? Results.NotFound() : Results.Ok(View(source));
    }

    private static SourceView View(SourceRecord source) =>
        new(source.Id, source.Title, source.Text, source.CreatedAt);
}
