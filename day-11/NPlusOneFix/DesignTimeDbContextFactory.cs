using Microsoft.EntityFrameworkCore.Design;

namespace NPlusOneFix;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new AppDbContext(Db.ConnectionString);
}
