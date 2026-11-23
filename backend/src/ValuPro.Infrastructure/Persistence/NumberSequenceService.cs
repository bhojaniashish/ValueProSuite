using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Clients;
using ValuPro.Domain.Common;

namespace ValuPro.Infrastructure.Persistence;

/// <summary>
/// Hands out numbers with optimistic concurrency: if two requests take the same number, one save fails
/// and retries with the next. Saves immediately, so call it before adding the record that uses the number.
/// </summary>
public sealed class NumberSequenceService(AppDbContext db) : INumberSequence
{
    private const int Attempts = 8;

    public async Task<long> NextAsync(string key, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var seq = await db.Set<NumberSequence>().FirstOrDefaultAsync(s => s.Key == key, ct);
            if (seq is null)
            {
                seq = new NumberSequence { Key = key, Last = 1 };
                db.Add(seq);
            }
            else
            {
                seq.Last++;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return seq.Last;
            }
            catch (DbUpdateException) when (attempt < Attempts)
            {
                // Someone else took it (concurrency token) or created the row first (unique index): re-read and retry.
                db.Entry(seq).State = EntityState.Detached;
            }
        }
    }
}
