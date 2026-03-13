using Microsoft.EntityFrameworkCore;

namespace SmartKb.Data.Tests;

public static class TestDbContextFactory
{
    public static SmartKbDbContext Create()
    {
        var options = new DbContextOptionsBuilder<SmartKbDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new SmartKbDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }
}
