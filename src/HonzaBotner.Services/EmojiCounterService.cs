using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using HonzaBotner.Database;
using HonzaBotner.Services.Contract;
using Microsoft.EntityFrameworkCore;
using CountedEmoji = HonzaBotner.Services.Contract.Dto.CountedEmoji;

namespace HonzaBotner.Services;

public class EmojiCounterService : IEmojiCounterService
{
    private readonly HonzaBotnerDbContext _dbContext;

    public EmojiCounterService(HonzaBotnerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<CountedEmoji>> ListAsync() =>
        (await _dbContext.CountedEmojis.ToListAsync().ConfigureAwait(false))
        .Select(GetDto).ToImmutableList();

    public async Task IncrementAsync(ulong emojiId)
    {
        int updated = await _dbContext.CountedEmojis
            .Where(emoji => emoji.Id == emojiId)
            .ExecuteUpdateAsync(update => update.SetProperty(emoji => emoji.Times, emoji => emoji.Times + 1));

        if (updated != 0) return;

        try
        {
            _dbContext.CountedEmojis.Add(new Database.CountedEmoji { Id = emojiId, Times = 1 });
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            await _dbContext.CountedEmojis
                .Where(emoji => emoji.Id == emojiId)
                .ExecuteUpdateAsync(update => update.SetProperty(emoji => emoji.Times, emoji => emoji.Times + 1));
        }
    }

    public async Task DecrementAsync(ulong emojiId)
    {
        await _dbContext.CountedEmojis
            .Where(emoji => emoji.Id == emojiId && emoji.Times > 0)
            .ExecuteUpdateAsync(update => update.SetProperty(emoji => emoji.Times, emoji => emoji.Times - 1));
    }


    private static CountedEmoji GetDto(Database.CountedEmoji emoji) =>
        new(emoji.Id, emoji.Times, emoji.FirstUsedAt);
}
