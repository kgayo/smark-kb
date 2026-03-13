using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartKb.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SmartKbDbContext>
{
    public SmartKbDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SmartKbDbContext>();
        optionsBuilder.UseSqlServer("Server=(local);Database=SmartKb;Trusted_Connection=True;");
        return new SmartKbDbContext(optionsBuilder.Options);
    }
}
