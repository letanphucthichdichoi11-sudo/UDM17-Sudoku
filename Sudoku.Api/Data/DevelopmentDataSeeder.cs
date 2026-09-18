using Microsoft.EntityFrameworkCore;
using Sudoku.Api.Models;

namespace Sudoku.Api.Data;

internal static class DevelopmentDataSeeder
{
    internal const string TestPassword = "SudokuTest!2026";

    public static async Task SeedAsync(AppDbContext context)
    {
        await EnsurePlayerAsync(
            context,
            "test_player_a",
            "test_player_a@sudoku.test");
        await EnsurePlayerAsync(
            context,
            "test_player_b",
            "test_player_b@sudoku.test");
        await context.SaveChangesAsync();
    }

    private static async Task EnsurePlayerAsync(
        AppDbContext context,
        string username,
        string email)
    {
        bool exists = await context.Users.AnyAsync(
            user => user.Username == username || user.Email == email);
        if (exists)
            return;

        context.Users.Add(new User
        {
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword),
            Status = "Active"
        });
    }
}
