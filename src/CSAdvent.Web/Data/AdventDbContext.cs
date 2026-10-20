using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Data;

public sealed class AdventDbContext(DbContextOptions<AdventDbContext> options) : DbContext(options)
{
}
