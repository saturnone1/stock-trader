using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockTrader.Data;

/// <summary>Creates model metadata for repeatable local EF migration commands without starting the host.</summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=stocktrader-design.db")
            .Options;
        return new AppDbContext(options);
    }
}
