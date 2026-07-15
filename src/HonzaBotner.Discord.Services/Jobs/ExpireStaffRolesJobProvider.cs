using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HonzaBotner.Database;
using HonzaBotner.Scheduler.Contract;
using HonzaBotner.Services.Contract;
using HonzaBotner.Services.Contract.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HonzaBotner.Discord.Services.Jobs;

[Cron("0 0 * * * *")]
public sealed class ExpireStaffRolesJobProvider : IJob
{
    private static readonly TimeSpan StaffVerificationLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan AuthVerificationLifetime = TimeSpan.FromDays(365);
    private readonly HonzaBotnerDbContext _dbContext;
    private readonly IDiscordRoleManager _roleManager;
    private readonly ILogger<ExpireStaffRolesJobProvider> _logger;

    public ExpireStaffRolesJobProvider(HonzaBotnerDbContext dbContext, IDiscordRoleManager roleManager,
        ILogger<ExpireStaffRolesJobProvider> logger)
    {
        _dbContext = dbContext;
        _roleManager = roleManager;
        _logger = logger;
    }

    public string Name => "expire-staff-roles";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.UtcNow;
        DateTime staffCutoff = now.Subtract(StaffVerificationLifetime);
        DateTime authCutoff = now.Subtract(AuthVerificationLifetime);
        Verification[] stale = await _dbContext.Verifications
            .Where(verification => verification.StaffVerifiedAt < staffCutoff ||
                                   verification.LastVerifiedAt < authCutoff)
            .ToArrayAsync(cancellationToken);

        foreach (Verification verification in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (verification.LastVerifiedAt < authCutoff)
                {
                    if (!await _roleManager.RevokeRolesPoolAsync(verification.UserId, RolesPool.Staff) ||
                        !await _roleManager.RevokeRolesPoolAsync(verification.UserId, RolesPool.Auth, true))
                        continue;

                    _dbContext.Verifications.Remove(verification);
                }
                else
                {
                    if (!await _roleManager.RevokeRolesPoolAsync(verification.UserId, RolesPool.Staff)) continue;
                    verification.StaffVerifiedAt = null;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Expiring staff roles for user {UserId} failed", verification.UserId);
            }
        }
    }
}
