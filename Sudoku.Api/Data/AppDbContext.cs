using Microsoft.EntityFrameworkCore;
using Sudoku.Api.Models;

namespace Sudoku.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<OtpCode> OtpCodes { get; set; }
    }
}